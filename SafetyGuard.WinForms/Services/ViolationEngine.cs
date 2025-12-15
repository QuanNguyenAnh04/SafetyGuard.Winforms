using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Linq;
using SafetyGuard.WinForms.Models;

namespace SafetyGuard.WinForms.Services;

public sealed class ViolationEngine
{
    private readonly IAppSettingsService _settings;
    private readonly IViolationRepository _repo;
    private readonly EvidenceService _evidence;
    private readonly LogService _logs;

    private sealed class State
    {
        public int Consecutive;
        public DateTime LastCreatedUtc = DateTime.MinValue;
    }

    private readonly ConcurrentDictionary<string, State> _state = new();

    public event Action<ViolationRecord>? OnViolationCreated;

    public ViolationEngine(IAppSettingsService settings, IViolationRepository repo, EvidenceService evidence, LogService logs)
    {
        _settings = settings;
        _repo = repo;
        _evidence = evidence;
        _logs = logs;
    }

    public void ProcessDetections(
        string cameraId,
        string cameraName,
        Bitmap currentFrameForEvidence,
        Detection[] detections)
    {
        var now = DateTime.UtcNow;
        var s = _settings.Current;

        var rules = s.Rules
            .Where(r => r.Enabled)
            .ToDictionary(r => r.Type, r => r);

        foreach (var rt in rules.Keys)
        {
            var key = $"{cameraId}:{rt}";
            _state.TryAdd(key, new State());
        }

        var byType = detections
            .Where(d => rules.TryGetValue(d.Type, out var rule) && d.Confidence >= rule.ConfidenceThreshold)
            .GroupBy(d => d.Type)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Confidence).First());

        foreach (var kv in rules)
        {
            var type = kv.Key;
            var key = $"{cameraId}:{type}";
            var st = _state.GetOrAdd(key, _ => new State());

            if (byType.TryGetValue(type, out var best))
            {
                st.Consecutive++;

                var ready = st.Consecutive >= s.MinConsecutiveFrames;
                var cooldownOk = (now - st.LastCreatedUtc).TotalSeconds >= s.CooldownSeconds;

                if (ready && cooldownOk)
                {
                    var rule = kv.Value;
                    var v = new ViolationRecord
                    {
                        TimeUtc = now,
                        CameraId = cameraId,
                        CameraName = cameraName,
                        Type = type,
                        Level = rule.Level,
                        Status = ViolationStatus.New,
                        Confidence = best.Confidence
                    };

                    using (var clone = (Bitmap)currentFrameForEvidence.Clone())
                        v.SnapshotPath = _evidence.SaveSnapshot(clone, v);

                    v.ClipPath = _evidence.SaveClipPlaceholder(v);

                    st.LastCreatedUtc = now;
                    st.Consecutive = 0;

                    _repo.Add(v);
                    _logs.Warn($"Violation created: {cameraName} {type} conf={best.Confidence:0.00}");
                    OnViolationCreated?.Invoke(v);
                }
            }
            else
            {
                st.Consecutive = Math.Max(0, st.Consecutive - 1);
            }
        }
    }
}
