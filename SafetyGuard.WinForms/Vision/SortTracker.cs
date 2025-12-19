using System;
using System.Collections.Generic;
using System.Linq;
using SafetyGuard.WinForms.Models;

namespace SafetyGuard.WinForms.Vision;

public sealed class SortTracker
{
    public sealed class Track
    {
        public int TrackId { get; init; }
        public BoundingBox Box { get; set; } = new BoundingBox();
        public float Confidence { get; set; }

        public int Age { get; set; }
        public int Missed { get; set; }
        public int LastFrameIndex { get; set; }
    }

    private readonly List<Track> _tracks = new();
    private int _nextId = 1;

    public IReadOnlyList<Track> Tracks => _tracks;

    public void Predict(int frameIndex, int maxMissed)
    {
        foreach (var t in _tracks)
        {
            t.Age++;
            t.Missed++;
            t.LastFrameIndex = frameIndex;
        }
        Prune(maxMissed);
    }

    public IReadOnlyList<Track> Update(
        int frameIndex,
        IReadOnlyList<DetectionResult> personDetections,
        float iouThreshold,
        int maxMissed)
    {
        // advance 1 frame
        foreach (var t in _tracks)
        {
            t.Age++;
            t.Missed++;
            t.LastFrameIndex = frameIndex;
        }

        var dets = personDetections ?? Array.Empty<DetectionResult>();

        // match greedily by IoU
        var pairs = new List<(int ti, int di, float iou)>();
        for (int ti = 0; ti < _tracks.Count; ti++)
            for (int di = 0; di < dets.Count; di++)
            {
                var iou = IoU(_tracks[ti].Box, dets[di].Box);
                pairs.Add((ti, di, iou));
            }

        pairs = pairs.OrderByDescending(p => p.iou).ToList();

        var usedT = new bool[_tracks.Count];
        var usedD = new bool[dets.Count];

        foreach (var (ti, di, iou) in pairs)
        {
            if (iou < iouThreshold) break;
            if (usedT[ti] || usedD[di]) continue;

            usedT[ti] = true;
            usedD[di] = true;

            _tracks[ti].Box = dets[di].Box;
            _tracks[ti].Confidence = dets[di].Confidence;
            _tracks[ti].Missed = 0;
        }

        // new tracks for unmatched detections
        for (int di = 0; di < dets.Count; di++)
        {
            if (usedD[di]) continue;

            _tracks.Add(new Track
            {
                TrackId = _nextId++,
                Box = dets[di].Box,
                Confidence = dets[di].Confidence,
                Age = 1,
                Missed = 0,
                LastFrameIndex = frameIndex
            });
        }

        Prune(maxMissed);
        return _tracks;
    }

    private void Prune(int maxMissed)
    {
        for (int i = _tracks.Count - 1; i >= 0; i--)
            if (_tracks[i].Missed > maxMissed)
                _tracks.RemoveAt(i);
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
