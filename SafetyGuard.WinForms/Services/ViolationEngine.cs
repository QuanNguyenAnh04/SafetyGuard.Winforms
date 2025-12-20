using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.Vision;

namespace SafetyGuard.WinForms.Services;

public sealed class ViolationEngine
{
    private readonly IAppSettingsService _settings;
    private readonly IViolationRepository _repo;
    private readonly EvidenceService _evidence;
    private readonly LogService _logs;

    public event Action<ViolationRecord>? OnViolationCreated;

    // ==========================================================
    // Anti-spam cooldown (chống "đổi TrackId"):
    // - Log theo vòng đời TrackId (PersonState.*EpisodeLogged)
    // - Nhưng tracker có thể mất track rồi tạo TrackId mới cho cùng 1 người
    //   => cooldown sẽ suppress log trùng trong khoảng CooldownSeconds,
    //      dựa trên bbox similarity (IoU/center-distance).
    // ==========================================================
    private readonly object _recentLock = new();
    private readonly Dictionary<(string camId, ViolationType type), List<_RecentEvent>> _recent = new();

    private sealed class _RecentEvent
    {
        public DateTime TimeUtc;
        public BoundingBox Box = new();
        public int TrackId;
    }

    // ==========================================================
    // MinConsecutiveFrames: đếm số frame liên tiếp "đang vi phạm"
    // (không cần sửa PersonState, lưu trong engine theo (cameraId, trackId)).
    // ==========================================================
    private readonly object _frameLock = new();
    private readonly Dictionary<(string camId, int trackId), _FrameCounters> _frames = new();

    private sealed class _FrameCounters
    {
        public int NoHelmet;
        public int NoVest;
        public int NoGloves;
        public int NoGlasses;
        public int NoBoots;
        public int Smoking;

        // "good" streaks: dùng để reset episodeLogged khi đã trở lại bình thường đủ ổn định
        public int OkHelmet;
        public int OkVest;
        public int OkGloves;
        public int OkGlasses;
        public int OkBoots;
        public int OkNoSmoke;
    }


    // ===== Session state for ProcessDetections (Offline/Image) =====
    // Mục tiêu: KHÔNG spam theo frame, và nếu là video offline thì vẫn có TrackId theo vòng đời người.
    private sealed class _Session
    {
        public readonly SortTracker Tracker = new();
        public readonly PpeMapper Mapper = new();
        public readonly Dictionary<int, PersonState> States = new();
        public int FrameIndex;
        public DateTime LastSeenUtc = DateTime.MinValue;
    }

    private readonly Dictionary<string, _Session> _sessions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reset tracking session cho cameraId (dùng khi bắt đầu 1 lần chạy offline mới)</summary>
    public void ResetSession(string cameraId)
    {
        if (string.IsNullOrWhiteSpace(cameraId)) return;
        _sessions.Remove(cameraId);

        ClearCameraCaches(cameraId);
    }

    public ViolationEngine(IAppSettingsService settings, IViolationRepository repo, EvidenceService evidence, LogService logs)
    {
        _settings = settings;
        _repo = repo;
        _evidence = evidence;
        _logs = logs;
    }

    public void ProcessTracks(
        string cameraId,
        string cameraName,
        Bitmap frameForEvidence,
        IReadOnlyList<SortTracker.Track> tracks,
        IReadOnlyDictionary<int, PersonState> states,
        bool forceCreate = false,
        double? dtOverride = null)
    {
        var now = DateTime.UtcNow;
        var s = _settings.Current;

        var minFrames = Math.Max(1, s.MinConsecutiveFrames);
        var cooldownSec = Math.Max(0, s.CooldownSeconds);

        CleanupDeadTrackCaches(cameraId, tracks);

        var rules = s.Rules
            .Where(r => r.Enabled)
            .ToDictionary(r => r.Type, r => r);

        foreach (var t in tracks)
        {
            if (!states.TryGetValue(t.TrackId, out var st))
                continue;

            // ---- MinConsecutiveFrames counters (per track) ----
            var fc = GetFrameCounters(cameraId, t.TrackId);
            fc.NoHelmet = st.HasHelmet ? 0 : (fc.NoHelmet + 1);
            fc.NoVest = st.HasVest ? 0 : (fc.NoVest + 1);
            fc.NoGloves = st.HasGloves ? 0 : (fc.NoGloves + 1);
            fc.NoGlasses = st.HasGlasses ? 0 : (fc.NoGlasses + 1);
            fc.NoBoots = st.HasBoots ? 0 : (fc.NoBoots + 1);
            fc.Smoking = st.HasSmoke ? (fc.Smoking + 1) : 0;

            fc.OkHelmet = st.HasHelmet ? (fc.OkHelmet + 1) : 0;
            fc.OkVest = st.HasVest ? (fc.OkVest + 1) : 0;
            fc.OkGloves = st.HasGloves ? (fc.OkGloves + 1) : 0;
            fc.OkGlasses = st.HasGlasses ? (fc.OkGlasses + 1) : 0;
            fc.OkBoots = st.HasBoots ? (fc.OkBoots + 1) : 0;
            fc.OkNoSmoke = !st.HasSmoke ? (fc.OkNoSmoke + 1) : 0;


            var dt = dtOverride ?? (now - st.LastUpdateUtc).TotalSeconds;
            if (dt < 0 || dt > 1.0) dt = 1.0 / 15.0;
            st.LastUpdateUtc = now;

            // Lưu ý: theo pipeline mới, EpisodeLogged là "đã log trong vòng đời track" (không reset khi PPE trở lại bình thường)
            (st.NoHelmetSeconds, st.NoHelmetEpisodeLogged) = UpdateEpisode(st.NoHelmetSeconds, st.NoHelmetEpisodeLogged, hasItem: st.HasHelmet, dt, okConsecFrames: fc.OkHelmet, resetFrames: minFrames);

            (st.NoVestSeconds, st.NoVestEpisodeLogged) = UpdateEpisode(st.NoVestSeconds, st.NoVestEpisodeLogged, hasItem: st.HasVest, dt, okConsecFrames: fc.OkVest, resetFrames: minFrames);

            (st.NoGlovesSeconds, st.NoGlovesEpisodeLogged) = UpdateEpisode(st.NoGlovesSeconds, st.NoGlovesEpisodeLogged, hasItem: st.HasGloves, dt, okConsecFrames: fc.OkGloves, resetFrames: minFrames);

            (st.NoGlassesSeconds, st.NoGlassesEpisodeLogged) = UpdateEpisode(st.NoGlassesSeconds, st.NoGlassesEpisodeLogged, hasItem: st.HasGlasses, dt, okConsecFrames: fc.OkGlasses, resetFrames: minFrames);

            (st.NoBootsSeconds, st.NoBootsEpisodeLogged) = UpdateEpisode(st.NoBootsSeconds, st.NoBootsEpisodeLogged, hasItem: st.HasBoots, dt, okConsecFrames: fc.OkBoots, resetFrames: minFrames);

            (st.SmokingSeconds, st.SmokingEpisodeLogged) = UpdateEpisode(st.SmokingSeconds, st.SmokingEpisodeLogged, hasItem: !st.HasSmoke, dt,okConsecFrames: fc.OkNoSmoke, resetFrames: minFrames);

            st.NoHelmetEpisodeLogged = TryCreateViolation(ViolationType.NoHelmet, s.NoHelmetSeconds, st.NoHelmetSeconds, st.NoHelmetEpisodeLogged, fc.NoHelmet);
            st.NoVestEpisodeLogged = TryCreateViolation(ViolationType.NoVest, s.NoVestSeconds, st.NoVestSeconds, st.NoVestEpisodeLogged, fc.NoVest);
            st.NoGlovesEpisodeLogged = TryCreateViolation(ViolationType.NoGloves, s.NoGlovesSeconds, st.NoGlovesSeconds, st.NoGlovesEpisodeLogged, fc.NoGloves);
            st.NoGlassesEpisodeLogged = TryCreateViolation(ViolationType.NoGlasses, s.NoGlassesSeconds, st.NoGlassesSeconds, st.NoGlassesEpisodeLogged, fc.NoGlasses);
            st.NoBootsEpisodeLogged = TryCreateViolation(ViolationType.NoBoots, s.NoBootsSeconds, st.NoBootsSeconds, st.NoBootsEpisodeLogged, fc.NoBoots);
            st.SmokingEpisodeLogged = TryCreateSmoking(s.SmokingSeconds, st.SmokingSeconds, st.SmokingEpisodeLogged, fc.Smoking);

            bool TryCreateViolation(ViolationType type, double thresholdSec, double curSec, bool episodeLogged, int consecFrames)
            {
                if (!rules.TryGetValue(type, out var rule))
                    return episodeLogged;

                if (consecFrames < minFrames)
                    return episodeLogged;

                if (forceCreate && !episodeLogged && curSec > 0)
                {
                    Create(type, rule, confidence: 1.0f);
                    return true;
                }

                if (!episodeLogged && curSec >= thresholdSec)
                {
                    Create(type, rule, confidence: 1.0f);
                    return true;
                }

                return episodeLogged;
            }

            bool TryCreateSmoking(double thresholdSec, double curSec, bool episodeLogged, int consecFrames)
            {
                if (!rules.TryGetValue(ViolationType.Smoking, out var rule))
                    return episodeLogged;

                if (consecFrames < minFrames)
                    return episodeLogged;

                if (forceCreate && !episodeLogged && st.HasSmoke)
                {
                    Create(ViolationType.Smoking, rule, confidence: Math.Max(0.01f, st.SmokeConfidence));
                    return true;
                }

                if (!episodeLogged && st.HasSmoke && curSec >= thresholdSec)
                {
                    Create(ViolationType.Smoking, rule, confidence: Math.Max(0.01f, st.SmokeConfidence));
                    return true;
                }

                return episodeLogged;
            }

            void Create(ViolationType type, DetectionRule rule, float confidence)
            {
                // ✅ Cooldown chống đổi TrackId (dùng setting "Anti-spam cooldown")
                if (!forceCreate && cooldownSec > 0 &&
                    ShouldSuppressDuplicate(cameraId, type, t.Box, t.TrackId, now, cooldownSec))
                {
                    return;
                }

                var v = new ViolationRecord
                {
                    TimeUtc = now,
                    CameraId = cameraId,
                    CameraName = cameraName,
                    Type = type,
                    Level = rule.Level,
                    Status = ViolationStatus.New,
                    Confidence = confidence,
                    TrackId = t.TrackId,
                    PersonBox = $"{t.Box.X:0},{t.Box.Y:0},{t.Box.W:0},{t.Box.H:0}"
                };

                using (var clone = (Bitmap)frameForEvidence.Clone())
                    v.SnapshotPath = _evidence.SavePersonSnapshot(clone, v, t.Box);

                v.ClipPath = _evidence.SaveClipPlaceholder(v);

                _repo.Add(v);
                _logs.Warn($"Violation created: {cameraName} track={t.TrackId} {type} conf={confidence:0.00}");
                OnViolationCreated?.Invoke(v);
            }
        }
    }

    private static (double sec, bool logged) UpdateEpisode(double durationSec, bool episodeLogged, bool hasItem, double dt, int okConsecFrames, int resetFrames)
    {
        // hasItem=true => đang bình thường => reset thời gian vi phạm.
        // Nếu bình thường đủ lâu => reset episodeLogged=false để vi phạm lần sau vẫn log được.
        if (hasItem)
        {
            if (resetFrames < 1) resetFrames = 1;
            var allowNewEpisode = okConsecFrames >= resetFrames;
            return (0, allowNewEpisode ? false : episodeLogged);
        }

        return (durationSec + dt, episodeLogged);
    }


    // ===== Compatibility: OfflineAnalyzer vẫn gọi ProcessDetections =====
    public void ProcessDetections(
        string cameraId,
        string cameraName,
        Bitmap frameForEvidence,
        DetectionResult[] detections,
        bool forceCreate = false)
    {
        // ✅ Offline/Image: thay vì log theo detection/frame -> chạy tracking + PersonState rồi log-once-per-track.
        var now = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(cameraId))
            cameraId = "offline";

        if (!_sessions.TryGetValue(cameraId, out var session))
        {
            session = new _Session();
            _sessions[cameraId] = session;

            ClearCameraCaches(cameraId);
        }

        // Nếu idle lâu (thường là người dùng chạy offline lần mới), reset session để TrackId không dính từ lần trước.
        if (session.LastSeenUtc != DateTime.MinValue && (now - session.LastSeenUtc).TotalSeconds > 5)
        {
            session = new _Session();
            _sessions[cameraId] = session;

            ClearCameraCaches(cameraId);
        }

        session.LastSeenUtc = now;

        var s = _settings.Current;

        // PERSON detections for tracking
        var dets = detections ?? Array.Empty<DetectionResult>();
        var persons = dets.Where(d => d.Class == ObjectClass.Person).ToList();

        // update tracker + states
        session.Tracker.Update(
            frameIndex: session.FrameIndex++,
            personDetections: persons,
            iouThreshold: s.TrackIouThreshold,
            maxMissed: s.TrackMaxMissedFrames);

        session.Mapper.UpdateStates(
            tracks: session.Tracker.Tracks,
            detections: dets,
            states: session.States,
            ppeIouThreshold: s.PpeIouThreshold,
            minConf: 0.30f);

        // cleanup states of dead tracks (prevent memory growth)
        var alive = session.Tracker.Tracks.Select(t => t.TrackId).ToHashSet();
        var dead = session.States.Keys.Where(id => !alive.Contains(id)).ToList();
        foreach (var id in dead) session.States.Remove(id);

        // process like realtime (log once per TrackId per violation type)
        ProcessTracks(
            cameraId,
            cameraName,
            frameForEvidence,
            session.Tracker.Tracks,
            session.States,
            forceCreate: forceCreate,
            dtOverride: null); // offline loop chạy nhanh -> dùng forceCreate nếu muốn log ngay
    }

    private _FrameCounters GetFrameCounters(string cameraId, int trackId)
    {
        cameraId ??= "";
        var key = (camId: cameraId, trackId);

        lock (_frameLock)
        {
            if (!_frames.TryGetValue(key, out var fc))
            {
                fc = new _FrameCounters();
                _frames[key] = fc;
            }

            return fc;
        }
    }

    private void CleanupDeadTrackCaches(string cameraId, IReadOnlyList<SortTracker.Track> tracks)
    {
        cameraId ??= "";
        var alive = tracks.Select(t => t.TrackId).ToHashSet();

        lock (_frameLock)
        {
            var keysToRemove = _frames.Keys
                .Where(k => string.Equals(k.camId, cameraId, StringComparison.OrdinalIgnoreCase) && !alive.Contains(k.trackId))
                .ToList();

            foreach (var k in keysToRemove)
                _frames.Remove(k);
        }
    }

    private void ClearCameraCaches(string cameraId)
    {
        cameraId ??= "";

        lock (_frameLock)
        {
            var removeKeys = _frames.Keys
                .Where(k => string.Equals(k.camId, cameraId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var k in removeKeys)
                _frames.Remove(k);
        }

        lock (_recentLock)
        {
            var removeKeys = _recent.Keys
                .Where(k => string.Equals(k.camId, cameraId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var k in removeKeys)
                _recent.Remove(k);
        }
    }

    private bool ShouldSuppressDuplicate(
        string cameraId,
        ViolationType type,
        BoundingBox box,
        int trackId,
        DateTime nowUtc,
        int cooldownSeconds)
    {
        if (cooldownSeconds <= 0) return false;

        cameraId ??= "";
        var key = (camId: cameraId, type);
        var cutoff = nowUtc.AddSeconds(-cooldownSeconds);

        lock (_recentLock)
        {
            if (!_recent.TryGetValue(key, out var list))
            {
                list = new List<_RecentEvent>(8);
                _recent[key] = list;
            }

            // drop old
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].TimeUtc < cutoff) list.RemoveAt(i);

            // match any recent event that "looks like same person"
            foreach (var e in list)
            {
                // Sau khi bật "episode reset" thì 1 TrackId có thể log nhiều lần.
                // => Cooldown cần chặn cả trường hợp cùng TrackId, và cả đổi TrackId.
                if (e.TrackId == trackId)
                    return true;

                if (IsSamePersonBox(e.Box, box))
                    return true;
            }


            // record
            list.Add(new _RecentEvent { TimeUtc = nowUtc, Box = box, TrackId = trackId });
            if (list.Count > 24) list.RemoveRange(0, list.Count - 24);
        }

        return false;
    }

    private static bool IsSamePersonBox(BoundingBox a, BoundingBox b)
    {
        // 2 tiêu chí: IoU đủ cao hoặc tâm gần nhau + kích thước tương tự
        var iou = IoU(a, b);
        if (iou >= 0.30f) return true;

        var acx = a.X + a.W / 2f;
        var acy = a.Y + a.H / 2f;
        var bcx = b.X + b.W / 2f;
        var bcy = b.Y + b.H / 2f;

        var dx = acx - bcx;
        var dy = acy - bcy;
        var dist = MathF.Sqrt(dx * dx + dy * dy);

        var diag = MathF.Sqrt(MathF.Max(a.W, b.W) * MathF.Max(a.W, b.W) + MathF.Max(a.H, b.H) * MathF.Max(a.H, b.H));
        if (diag <= 1e-3f) return false;

        var distNorm = dist / diag;

        var areaA = MathF.Max(1f, a.W * a.H);
        var areaB = MathF.Max(1f, b.W * b.H);
        var ratio = areaA > areaB ? areaA / areaB : areaB / areaA;

        return distNorm <= 0.25f && ratio <= 2.0f;
    }

    private static float IoU(BoundingBox a, BoundingBox b)
    {
        var ax1 = a.X;
        var ay1 = a.Y;
        var ax2 = a.X + a.W;
        var ay2 = a.Y + a.H;

        var bx1 = b.X;
        var by1 = b.Y;
        var bx2 = b.X + b.W;
        var by2 = b.Y + b.H;

        var x1 = MathF.Max(ax1, bx1);
        var y1 = MathF.Max(ay1, by1);
        var x2 = MathF.Min(ax2, bx2);
        var y2 = MathF.Min(ay2, by2);

        var inter = MathF.Max(0, x2 - x1) * MathF.Max(0, y2 - y1);
        if (inter <= 0) return 0;

        var areaA = MathF.Max(1f, a.W * a.H);
        var areaB = MathF.Max(1f, b.W * b.H);

        return inter / (areaA + areaB - inter);
    }
}
