using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.Services;

namespace SafetyGuard.WinForms.Vision;

public sealed class RtspFrameSource : FrameSourceBase
{
    private readonly LogService _logs;

    public RtspFrameSource(string cameraId, string cameraName, string rtspUrl, LogService logs)
        : base(cameraId, cameraName, rtspUrl)
    {
        _logs = logs;
    }

    protected override void Run(CancellationToken ct)
    {
        Task.Run(() =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    SetStatus(CameraStatus.Reconnecting);
                    _logs.Info($"Connecting RTSP: {CameraName}");

                    using var cap = new VideoCapture();
                    if (!cap.Open(SourceUrl))
                    {
                        _logs.Warn($"RTSP open failed: {CameraName}");
                        SetStatus(CameraStatus.Offline);
                        Thread.Sleep(1200);
                        continue;
                    }

                    SetStatus(CameraStatus.Connected);
                    using var mat = new Mat();

                    while (!ct.IsCancellationRequested)
                    {
                        if (!cap.Read(mat) || mat.Empty())
                        {
                            _logs.Warn($"RTSP read failed: {CameraName}");
                            break;
                        }

                        using var bmp = BitmapConverter.ToBitmap(mat);
                        EmitFrame((Bitmap)bmp.Clone());
                        Thread.Sleep(10);
                    }
                }
                catch (Exception ex)
                {
                    _logs.Error($"RTSP loop error {CameraName}: {ex.Message}");
                }

                SetStatus(CameraStatus.Reconnecting);
                Thread.Sleep(1000);
            }
        }, ct);
    }
}
