using System;
using System.Drawing;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.Services;

namespace SafetyGuard.WinForms.Vision;

public sealed class RealTimeCameraRunner : IDisposable
{
    private readonly FrameSourceBase _source;
    private readonly IDetector _detector;
    private readonly ViolationEngine _engine;
    private readonly LogService _logs;

    private int _frameIdx = 0;
    private readonly int _inferEveryN;

    public event Action<Bitmap, Detection[]>? OnPreview;
    public event Action<CameraStatus>? OnStatus;

    public RealTimeCameraRunner(
        FrameSourceBase source,
        IDetector detector,
        ViolationEngine engine,
        LogService logs,
        int inferEveryN = 3)
    {
        _source = source;
        _detector = detector;
        _engine = engine;
        _logs = logs;
        _inferEveryN = Math.Max(1, inferEveryN);

        _source.OnFrame += HandleFrame;
        _source.OnStatus += st => OnStatus?.Invoke(st);
    }

    public void Start() => _source.Start();
    public void Stop() => _source.Stop();

    private void HandleFrame(Bitmap frame)
    {
        try
        {
            _frameIdx++;

            Detection[] dets = Array.Empty<Detection>();
            if (_frameIdx % _inferEveryN == 0)
            {
                dets = _detector.Detect(frame);
                _engine.ProcessDetections(_source.CameraId, _source.CameraName, frame, dets);
                // ProcessDetections sẽ tự rule N-frames + cooldown + evidence + repo :contentReference[oaicite:1]{index=1}
            }

            using var annotated = DrawOverlay(frame, dets);
            OnPreview?.Invoke((Bitmap)annotated.Clone(), dets);
        }
        catch (Exception ex)
        {
            _logs.Error($"Runner error {_source.CameraName}: {ex.Message}");
        }
        finally
        {
            // QUAN TRỌNG: frame clone từ RtspFrameSource phải Dispose để không leak RAM :contentReference[oaicite:2]{index=2}
            frame.Dispose();
        }
    }

    private static Bitmap DrawOverlay(Bitmap frame, Detection[] dets)
    {
        var bmp = (Bitmap)frame.Clone();
        if (dets.Length == 0) return bmp;

        using var g = Graphics.FromImage(bmp);
        using var pen = new Pen(Color.Lime, 2);
        using var font = new Font("Segoe UI", 10, FontStyle.Bold);

        foreach (var d in dets)
        {
            var r = d.Box.ToRectClamped(bmp.Width, bmp.Height); // đã có sẵn trong BoundingBox :contentReference[oaicite:3]{index=3}
            g.DrawRectangle(pen, r);
            g.DrawString($"{d.Type} {d.Confidence:0.00}", font, Brushes.Yellow,
                r.X, Math.Max(0, r.Y - 18));
        }
        return bmp;
    }

    public void Dispose()
    {
        Stop();
        _source.OnFrame -= HandleFrame;
    }
}
