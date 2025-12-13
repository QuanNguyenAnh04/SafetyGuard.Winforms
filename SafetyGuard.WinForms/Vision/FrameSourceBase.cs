using System;
using System.Drawing;
using System.Threading;
using SafetyGuard.WinForms.Models;

namespace SafetyGuard.WinForms.Vision;

public abstract class FrameSourceBase : IDisposable
{
    public string CameraId { get; }
    public string CameraName { get; }
    public string SourceUrl { get; }

    public CameraStatus Status { get; protected set; } = CameraStatus.Offline;

    public event Action<Bitmap>? OnFrame;
    public event Action<CameraStatus>? OnStatus;

    protected CancellationTokenSource? Cts;

    protected FrameSourceBase(string cameraId, string cameraName, string url)
    {
        CameraId = cameraId;
        CameraName = cameraName;
        SourceUrl = url;
    }

    public void Start()
    {
        if (Cts != null) return;
        Cts = new CancellationTokenSource();
        Run(Cts.Token);
    }

    public void Stop()
    {
        try { Cts?.Cancel(); } catch { }
        Cts = null;
        SetStatus(CameraStatus.Offline);
    }

    protected void EmitFrame(Bitmap bmp) => OnFrame?.Invoke(bmp);

    protected void SetStatus(CameraStatus st)
    {
        Status = st;
        OnStatus?.Invoke(st);
    }

    protected abstract void Run(CancellationToken ct);

    public void Dispose() => Stop();
}
