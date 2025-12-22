using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
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

    private bool _warmedUp;

    // “Frame mới nhất chờ detect” (drop cũ)
    private Bitmap? _latestForDetect;
    private int _latestFrameId;
    private readonly object _detectLock = new object();

    // Kết quả detect gần nhất
    private DetectionResult[] _lastDets = Array.Empty<DetectionResult>();

    // Worker detect chạy 1 luồng duy nhất
    private Task? _detectWorker;
    private readonly SemaphoreSlim _detectSignal = new SemaphoreSlim(0, 1);

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
        if (sampleEveryNFrames < 1) sampleEveryNFrames = 1;

        using var cap = new VideoCapture(path);
        if (!cap.IsOpened())
            throw new InvalidOperationException("Cannot open video: " + path);

        int total = (int)cap.Get(VideoCaptureProperties.FrameCount);
        double fps = cap.Get(VideoCaptureProperties.Fps);
        if (fps <= 0 || double.IsNaN(fps) || double.IsInfinity(fps)) fps = 30;

        _logs.Info($"[OFFLINE][VID] Opened OK. FrameCount={total}, FPS={fps:0.##}");

        using var mat = new Mat();

        // đọc 1 frame để chắc decode OK
        if (!cap.Read(mat) || mat.Empty())
            throw new InvalidOperationException("Video opened but cannot read frames (decode fail).");

        // ✅ show frame đầu ngay (tránh đen lúc warm-up)
        if (onFrame != null)
        {
            using var firstBmp = BitmapConverter.ToBitmap(mat);
            onFrame((Bitmap)firstBmp.Clone(), Array.Empty<DetectionResult>());
        }

        // rewind về đầu
        cap.Set(VideoCaptureProperties.PosFrames, 0);

        // ✅ warm-up detector 1 lần
        if (!_warmedUp)
        {
            _warmedUp = true;
            try
            {
                using var warm = new Bitmap(640, 640);
                _ = _detector.Detect(warm);
                _logs.Info("[OFFLINE] Warm-up detector done.");
            }
            catch (Exception ex)
            {
                _logs.Warn("[OFFLINE] Warm-up detector failed: " + ex.Message);
            }
        }

        _engine.ResetSession(cameraId);

        // ✅ start detect worker nếu chưa có
        EnsureDetectWorker(cameraId, cameraName, forceCreate);

        // Throttle preview (đừng spam UI 30fps nếu máy yếu)
        double previewFps = Math.Min(30, Math.Max(15, fps));
        long uiInterval = (long)(Stopwatch.Frequency / previewFps);
        long lastUi = 0;

        // Throttle progress ~10Hz
        long progressInterval = (long)(Stopwatch.Frequency / 10.0);
        long lastProgress = 0;

        void ReportProgress(int idx)
        {
            if (progress == null) return;
            var now = Stopwatch.GetTimestamp();
            if (lastProgress == 0 || now - lastProgress >= progressInterval)
            {
                lastProgress = now;
                progress(idx, total);
            }
        }

        // Pace theo FPS video, nhưng nếu bị “tụt” thì không cố ngủ (tránh khựng)
        long frameIntervalTicks = (long)(Stopwatch.Frequency / fps);
        long nextDue = Stopwatch.GetTimestamp();

        int idx = 0;
        while (cap.Read(mat) && !mat.Empty())
        {
            idx++;

            using var bmp = BitmapConverter.ToBitmap(mat);

            // ✅ submit frame mới nhất cho detect (drop cũ) theo chu kỳ N frame
            // sampleEveryNFrames giờ là “detect mỗi N frame”, preview vẫn đều
            if (idx % sampleEveryNFrames == 0)
            {
                SubmitForDetect((Bitmap)bmp.Clone(), idx);
            }

            // ✅ preview: show frame + lastDets (dets gần nhất)
            if (onFrame != null)
            {
                var now = Stopwatch.GetTimestamp();
                if (idx == 1 || now - lastUi >= uiInterval)
                {
                    lastUi = now;
                    var detsSnapshot = _lastDets; // đọc nhanh
                    onFrame((Bitmap)bmp.Clone(), detsSnapshot);
                }
            }

            ReportProgress(idx);

            // pacing
            nextDue += frameIntervalTicks;
            var now2 = Stopwatch.GetTimestamp();
            var remaining = nextDue - now2;

            // nếu tụt quá nhiều (CPU bận) -> reset để tránh “đợi bù” gây khựng
            if (remaining < -frameIntervalTicks * 2)
            {
                nextDue = now2;
                continue;
            }

            if (remaining > 0)
            {
                int ms = (int)(remaining * 1000 / Stopwatch.Frequency);
                if (ms > 0) Thread.Sleep(ms);
            }
        }

        // progress cuối
        progress?.Invoke(Math.Min(idx, total), total);

        _logs.Info("[OFFLINE][VID] Done.");
    }

    private void EnsureDetectWorker(string cameraId, string cameraName, bool forceCreate)
    {
        if (_detectWorker != null) return;

        _detectWorker = Task.Run(() =>
        {
            while (true)
            {
                // chờ có frame mới
                _detectSignal.Wait();

                Bitmap? bmp;
                int frameId;

                lock (_detectLock)
                {
                    bmp = _latestForDetect;
                    frameId = _latestFrameId;
                    _latestForDetect = null; // lấy xong
                }

                if (bmp == null) continue;

                try
                {
                    // ✅ detect + engine chạy ở worker (KHÔNG block preview)
                    var dets = _detector.Detect(bmp);

                    _engine.ProcessDetections(cameraId, cameraName, bmp, dets, forceCreate: forceCreate);

                    // cập nhật last dets
                    _lastDets = dets;

                    if (frameId % 100 == 0)
                        _logs.Info($"[OFFLINE][DET] frame={frameId}, dets={dets.Length}");
                }
                catch (Exception ex)
                {
                    _logs.Error("[OFFLINE][DET] " + ex);
                }
                finally
                {
                    bmp.Dispose();
                }
            }
        });
    }

    private void SubmitForDetect(Bitmap bmpClone, int frameId)
    {
        lock (_detectLock)
        {
            // drop frame cũ chưa detect
            _latestForDetect?.Dispose();
            _latestForDetect = bmpClone;
            _latestFrameId = frameId;
        }

        // báo worker có frame mới (nếu semaphore đang 0)
        if (_detectSignal.CurrentCount == 0)
            _detectSignal.Release();
    }
}
