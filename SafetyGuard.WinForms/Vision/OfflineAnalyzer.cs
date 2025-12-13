using System;
using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.Services;

namespace SafetyGuard.WinForms.Vision;

public sealed class OfflineAnalyzer
{
    private readonly IDetector _detector;
    private readonly ViolationEngine _engine;
    private readonly LogService _logs;

    public OfflineAnalyzer(IDetector detector, ViolationEngine engine, LogService logs)
    {
        _detector = detector;
        _engine = engine;
        _logs = logs;
    }

    public void AnalyzeImage(string path, string cameraId, string cameraName)
    {
        using var bmp = (Bitmap)Bitmap.FromFile(path);
        var detections = _detector.Detect(bmp);
        _engine.ProcessDetections(cameraId, cameraName, bmp, detections);
        _logs.Info($"Offline image analyzed: {path} det={detections.Length}");
    }

    public void AnalyzeVideo(string path, string cameraId, string cameraName, int sampleEveryNFrames = 10, Action<int, int>? progress = null)
    {
        using var cap = new VideoCapture(path);
        if (!cap.IsOpened())
        {
            _logs.Error("Offline video open failed: " + path);
            return;
        }

        var total = (int)cap.Get(VideoCaptureProperties.FrameCount);
        if (total <= 0) total = 1;

        using var mat = new Mat();
        int idx = 0;

        while (cap.Read(mat) && !mat.Empty())
        {
            idx++;
            if (idx % sampleEveryNFrames != 0) continue;

            using var bmp = BitmapConverter.ToBitmap(mat);
            var detections = _detector.Detect(bmp);
            _engine.ProcessDetections(cameraId, cameraName, bmp, detections);

            progress?.Invoke(idx, total);
        }

        _logs.Info($"Offline video analyzed: {path}");
    }
}
