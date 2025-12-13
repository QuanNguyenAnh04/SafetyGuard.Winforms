using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Linq;
using SafetyGuard.WinForms.Models;

namespace SafetyGuard.WinForms.Services;

public sealed class ViolationEngine
{
    private readonly AppSettingsService _settings;
    private readonly ViolationRepository _repo;
    private readonly EvidenceService _evidence;
    private readonly LogService _logs;

    private sealed class State
    {
        public int Consecutive;
        public DateTime LastCreatedUtc = DateTime.MinValue;
    }

    // key = cameraId + type
    private readonly ConcurrentDictionary<string, State> _state = new();

    public event Action<ViolationRecord>? OnViolationCreated;

    public ViolationEngine(AppSettingsService settings, ViolationRepository repo, EvidenceService evidence, LogService logs)
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
        var s = _settings.Current;

        // filter by rules thresholds
        var rules = s.Rules.Where(r => r.Enabled).ToDictionary(r => r.Type, r => r);

        // reset counters for types not present (helps reduce spam)
        foreach (var rt in rules.Keys)
        {
            var key = $"{cameraId}:{rt}";
            _state.TryAdd(key, new State());
        }

        // group by type (anti-spam by camera+type)
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
                var cooldownOk = (DateTime.UtcNow - st.LastCreatedUtc).TotalSeconds >= s.CooldownSeconds;

                if (ready && cooldownOk)
                {
                    var rule = kv.Value;
                    var v = new ViolationRecord
                    {
                        TimeUtc = DateTime.UtcNow,
                        CameraId = cameraId,
                        CameraName = cameraName,
                        Type = type,
                        Level = rule.Level,
                        Status = ViolationStatus.New,
                        Confidence = best.Confidence
                    };

                    // evidence
                    v.SnapshotPath = _evidence.SaveSnapshot((Bitmap)currentFrameForEvidence.Clone(), v);
                    v.ClipPath = _evidence.SaveClipPlaceholder(v);

                    st.LastCreatedUtc = DateTime.UtcNow;
                    st.Consecutive = 0;

                    _repo.Add(v);
                    _logs.Warn($"Violation created: {cameraName} {type} conf={best.Confidence:0.00}");
                    OnViolationCreated?.Invoke(v);
                }
            }
            else
            {
                // decay / reset
                st.Consecutive = Math.Max(0, st.Consecutive - 1);
            }
        }
    }
}
