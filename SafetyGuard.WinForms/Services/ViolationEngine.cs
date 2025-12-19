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

        var rules = s.Rules
            .Where(r => r.Enabled)
            .ToDictionary(r => r.Type, r => r);

        foreach (var t in tracks)
        {
            if (!states.TryGetValue(t.TrackId, out var st))
                continue;

            var dt = dtOverride ?? (now - st.LastUpdateUtc).TotalSeconds;
            if (dt < 0 || dt > 1.0) dt = 1.0 / 15.0;
            st.LastUpdateUtc = now;

            (st.NoHelmetSeconds, st.NoHelmetEpisodeLogged) = UpdateEpisode(st.NoHelmetSeconds, st.NoHelmetEpisodeLogged, hasItem: st.HasHelmet, dt);
            (st.NoVestSeconds, st.NoVestEpisodeLogged) = UpdateEpisode(st.NoVestSeconds, st.NoVestEpisodeLogged, hasItem: st.HasVest, dt);
            (st.NoGlovesSeconds, st.NoGlovesEpisodeLogged) = UpdateEpisode(st.NoGlovesSeconds, st.NoGlovesEpisodeLogged, hasItem: st.HasGloves, dt);
            (st.SmokingSeconds, st.SmokingEpisodeLogged) = UpdateEpisode(st.SmokingSeconds, st.SmokingEpisodeLogged, hasItem: !st.HasSmoke, dt);

            st.NoHelmetEpisodeLogged = TryCreateViolation(ViolationType.NoHelmet, s.NoHelmetSeconds, st.NoHelmetSeconds, st.NoHelmetEpisodeLogged);
            st.NoVestEpisodeLogged = TryCreateViolation(ViolationType.NoVest, s.NoVestSeconds, st.NoVestSeconds, st.NoVestEpisodeLogged);
            st.NoGlovesEpisodeLogged = TryCreateViolation(ViolationType.NoGloves, s.NoGlovesSeconds, st.NoGlovesSeconds, st.NoGlovesEpisodeLogged);
            st.SmokingEpisodeLogged = TryCreateSmoking(s.SmokingSeconds, st.SmokingSeconds, st.SmokingEpisodeLogged);

            bool TryCreateViolation(ViolationType type, double thresholdSec, double curSec, bool episodeLogged)
            {
                if (!rules.TryGetValue(type, out var rule))
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

            bool TryCreateSmoking(double thresholdSec, double curSec, bool episodeLogged)
            {
                if (!rules.TryGetValue(ViolationType.Smoking, out var rule))
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

    private static (double sec, bool logged) UpdateEpisode(double durationSec, bool episodeLogged, bool hasItem, double dt)
    {
        if (hasItem)
            return (0, episodeLogged);

        return (durationSec + dt, episodeLogged);
    }

    // ===== Compatibility: OfflineAnalyzer vẫn gọi ProcessDetections =====
    private readonly Dictionary<(string cam, ViolationType type), DateTime> _offlineCooldown = new();

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
        }

        // Nếu idle lâu (thường là người dùng chạy offline lần mới), reset session để TrackId không dính từ lần trước.
        if (session.LastSeenUtc != DateTime.MinValue && (now - session.LastSeenUtc).TotalSeconds > 5)
        {
            session = new _Session();
            _sessions[cameraId] = session;
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
}
