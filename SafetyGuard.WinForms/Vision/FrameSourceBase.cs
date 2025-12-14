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

    public CameraStatus Status { get; private set; } = CameraStatus.Offline;

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
        SetStatus(CameraStatus.Reconnecting);   // 🔹 trạng thái khởi động
        Run(Cts.Token);
    }

    public void Stop()
    {
        if (Cts == null) return;

        try { Cts.Cancel(); } catch { }
        Cts = null;

        SetStatus(CameraStatus.Offline);        // 🔹 chỉ phát nếu khác trạng thái trước
    }

    protected void EmitFrame(Bitmap bmp)
        => OnFrame?.Invoke(bmp);

    /// <summary>
    /// Update camera status – only raise event when changed
    /// </summary>
    protected void SetStatus(CameraStatus st)
    {
        if (Status == st) return;   // ✅ chặn spam event

        Status = st;
        OnStatus?.Invoke(st);
    }

    protected abstract void Run(CancellationToken ct);

    public void Dispose() => Stop();
}
