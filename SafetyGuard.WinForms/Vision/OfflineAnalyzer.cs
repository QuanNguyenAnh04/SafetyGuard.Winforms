using System;
using System.Diagnostics;
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

    public void AnalyzeImage(
        string path,
        string cameraId,
        string cameraName,
        Action<Bitmap, DetectionResult[]>? onFrame,
        bool forceCreate = true)
    {
        using var bmp = (Bitmap)Bitmap.FromFile(path);

        var dets = _detector.Detect(bmp);
        _engine.ResetSession(cameraId);
        _engine.ProcessDetections(cameraId, cameraName, bmp, dets, forceCreate: forceCreate);

        _logs.Info($"[OFFLINE][IMG] {path} dets={dets.Length}");
        Debug.WriteLine($"[OFFLINE][IMG] dets={dets.Length}");

        onFrame?.Invoke((Bitmap)bmp.Clone(), dets);
    }

    public void AnalyzeVideo(
        string path,
        string cameraId,
        string cameraName,
        int sampleEveryNFrames,
        Action<int, int>? progress,
        Action<Bitmap, DetectionResult[]>? onFrame,
        bool forceCreate = true)
    {
        _logs.Info($"[OFFLINE][VID] Open: {path}");
        Debug.WriteLine($"[OFFLINE][VID] Open: {path}");

        using var cap = new VideoCapture(path);

        if (!cap.IsOpened())
        {
            var msg = "OpenCv VideoCapture cannot open this video. " +
                      "Bạn cần cài NuGet OpenCvSharp4.runtime.win (x64) hoặc thiếu codec/ffmpeg.";
            _logs.Error("[OFFLINE][VID] " + msg);
            throw new InvalidOperationException(msg);
        }

        int total = (int)cap.Get(VideoCaptureProperties.FrameCount);
        double fps = cap.Get(VideoCaptureProperties.Fps);
        _logs.Info($"[OFFLINE][VID] Opened OK. FrameCount={total}, FPS={fps:0.##}");
        Debug.WriteLine($"[OFFLINE][VID] Opened OK. FrameCount={total}, FPS={fps:0.##}");

        using var mat = new Mat();

        // ✅ đọc thử frame đầu để xác nhận decode OK
        if (!cap.Read(mat) || mat.Empty())
        {
            var msg = "Video opened but cannot read frames (decode fail). " +
                      "Rất thường do thiếu OpenCvSharp4.runtime.win hoặc thiếu ffmpeg.";
            _logs.Error("[OFFLINE][VID] " + msg);
            throw new InvalidOperationException(msg);
        }

        // rewind về đầu (vì đã đọc 1 frame)
        cap.Set(VideoCaptureProperties.PosFrames, 0);

        _engine.ResetSession(cameraId);

        int idx = 0;

        // throttle UI preview (tránh BeginInvoke queue làm RAM tăng)
        long lastUi = 0;
        long uiInterval = (long)(Stopwatch.Frequency / 6.0); // ~6 FPS preview max

        while (cap.Read(mat) && !mat.Empty())
        {
            idx++;

            if (sampleEveryNFrames > 1 && (idx % sampleEveryNFrames) != 0)
            {
                progress?.Invoke(idx, total);
                continue;
            }

            using var bmp = BitmapConverter.ToBitmap(mat);

            DetectionResult[] dets;
            try
            {
                dets = _detector.Detect(bmp);
            }
            catch (Exception ex)
            {
                _logs.Error("[OFFLINE][VID] Detect crash: " + ex.Message);
                throw;
            }

            // ✅ log ít nhưng chắc chắn có
            if (idx == 1 || idx % (sampleEveryNFrames * 20) == 0)
            {
                _logs.Info($"[OFFLINE][VID] frame={idx}/{total} dets={dets.Length}");
                Debug.WriteLine($"[OFFLINE][VID] frame={idx}/{total} dets={dets.Length}");
            }

            _engine.ProcessDetections(cameraId, cameraName, bmp, dets, forceCreate: forceCreate);

            if (onFrame != null)
            {
                var now = Stopwatch.GetTimestamp();
                if (idx == 1 || now - lastUi >= uiInterval) // ✅ frame đầu luôn show
                {
                    lastUi = now;
                    onFrame((Bitmap)bmp.Clone(), dets); // UI dispose clone
                }
            }

            progress?.Invoke(idx, total);
        }

        _logs.Info("[OFFLINE][VID] Done.");
        Debug.WriteLine("[OFFLINE][VID] Done.");
    }
}
