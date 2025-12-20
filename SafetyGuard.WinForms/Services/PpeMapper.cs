using System;
using System.Collections.Generic;
using System.Linq;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.Vision;

namespace SafetyGuard.WinForms.Services;

public sealed class PpeMapper
{
    public void UpdateStates(
        IReadOnlyList<SortTracker.Track> tracks,
        IReadOnlyList<DetectionResult> detections,
        Dictionary<int, PersonState> states,
        float ppeIouThreshold,
        float minConf = 0.30f)
    {
        detections ??= Array.Empty<DetectionResult>();

        var helmets = detections.Where(d => d.Class == ObjectClass.Helmet && d.Confidence >= minConf).ToList();
        var vests = detections.Where(d => d.Class == ObjectClass.Vest && d.Confidence >= minConf).ToList();
        var gloves = detections.Where(d => d.Class == ObjectClass.Gloves && d.Confidence >= minConf).ToList();
        var glasses = detections.Where(d => d.Class == ObjectClass.Glasses && d.Confidence >= minConf).ToList();
        var boots = detections.Where(d => d.Class == ObjectClass.Boots && d.Confidence >= minConf).ToList();
        var smokes = detections.Where(d => d.Class == ObjectClass.Smoking && d.Confidence >= minConf).ToList();

        foreach (var t in tracks)
        {
            if (!states.TryGetValue(t.TrackId, out var st))
            {
                st = new PersonState { TrackId = t.TrackId };
                states[t.TrackId] = st;
            }

            (st.HasHelmet, st.HelmetConfidence) = PickBestAssigned(helmets, t.Box, ppeIouThreshold);
            (st.HasVest, st.VestConfidence) = PickBestAssigned(vests, t.Box, ppeIouThreshold);
            (st.HasGloves, st.GlovesConfidence) = PickBestAssigned(gloves, t.Box, ppeIouThreshold);
            (st.HasGlasses, st.GlassesConfidence) = PickBestAssigned(glasses, t.Box, ppeIouThreshold);
            (st.HasBoots, st.BootsConfidence) = PickBestAssigned(boots, t.Box, ppeIouThreshold);
            (st.HasSmoke, st.SmokeConfidence) = PickBestAssigned(smokes, t.Box, ppeIouThreshold);
        }
    }

    private static (bool ok, float conf) PickBestAssigned(
        List<DetectionResult> objs,
        BoundingBox person,
        float iouThres)
    {
        float best = 0f;
        foreach (var o in objs)
        {
            if (!IsAssigned(o.Box, person, iouThres)) continue;
            if (o.Confidence > best) best = o.Confidence;
        }
        return (best > 0, best);
    }

    // PPE thuộc về người nếu center nằm trong bbox người hoặc IoU > threshold
    private static bool IsAssigned(BoundingBox obj, BoundingBox person, float iouThres)
    {
        float cx = obj.X + obj.W * 0.5f;
        float cy = obj.Y + obj.H * 0.5f;

        bool inside =
            cx >= person.X && cx <= (person.X + person.W) &&
            cy >= person.Y && cy <= (person.Y + person.H);

        if (inside) return true;

        return IoU(obj, person) >= iouThres;
    }

    private static float IoU(BoundingBox a, BoundingBox b)
    {
        float ax1 = a.X, ay1 = a.Y, ax2 = a.X + a.W, ay2 = a.Y + a.H;
        float bx1 = b.X, by1 = b.Y, bx2 = b.X + b.W, by2 = b.Y + b.H;

        float x1 = Math.Max(ax1, bx1);
        float y1 = Math.Max(ay1, by1);
        float x2 = Math.Min(ax2, bx2);
        float y2 = Math.Min(ay2, by2);

        float inter = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        float union = a.W * a.H + b.W * b.H - inter;

        return union <= 0 ? 0 : inter / union;
    }
}
