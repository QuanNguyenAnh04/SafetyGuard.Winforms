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

    // ===== Backward compatible API (giữ cho code cũ) =====
    public void AnalyzeImage(string path, string cameraId, string cameraName)
        => AnalyzeImage(path, cameraId, cameraName, onFrame: null, forceCreate: true);

    public void AnalyzeVideo(string path, string cameraId, string cameraName, int sampleEveryNFrames = 10, Action<int, int>? progress = null)
        => AnalyzeVideo(path, cameraId, cameraName, sampleEveryNFrames, progress, onFrame: null, forceCreate: true);

    // ===== New API (dùng cho UI preview + event log) =====

    public void AnalyzeImage(
        string path,
        string cameraId,
        string cameraName,
        Action<Bitmap, Detection[]>? onFrame,
        bool forceCreate = true)
    {
        using var bmp = (Bitmap)Bitmap.FromFile(path);
        var detections = _detector.Detect(bmp);

        // ✅ Lưu event vào DB qua engine (forceCreate để ảnh đơn vẫn tạo record)
        _engine.ProcessDetections(cameraId, cameraName, bmp, detections, forceCreate: true);

        // ✅ push frame + detection ra UI
        if (onFrame != null)
        {
            using var clone = (Bitmap)bmp.Clone();
            onFrame(clone, detections);
        }

        _logs.Info($"Offline image analyzed: {path} det={detections.Length}");
    }

    public void AnalyzeVideo(
        string path,
        string cameraId,
        string cameraName,
        int sampleEveryNFrames,
        Action<int, int>? progress,
        Action<Bitmap, Detection[]>? onFrame,
        bool forceCreate = true)
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

            // sample frame
            if (sampleEveryNFrames > 1 && (idx % sampleEveryNFrames != 0))
            {
                progress?.Invoke(idx, total);
                continue;
            }

            using var bmp = BitmapConverter.ToBitmap(mat);
            var detections = _detector.Detect(bmp);
            _logs.Info($"[OFFLINE] dets={detections.Length}");
            if (detections.Length > 0)
            {
                var top = detections.OrderByDescending(x => x.Confidence).Take(3)
                    .Select(x => $"{x.Type}:{x.Confidence:0.00}");
                _logs.Info("[OFFLINE] top=" + string.Join(", ", top));
            }


            // ✅ Lưu event vào DB
            _engine.ProcessDetections(cameraId, cameraName, bmp, detections, forceCreate: forceCreate);

            // ✅ UI preview + event log
            if (onFrame != null)
            {
                using var clone = (Bitmap)bmp.Clone();
                onFrame(clone, detections);
            }

            progress?.Invoke(idx, total);
        }

        _logs.Info($"Offline video analyzed: {path}");
    }
}
