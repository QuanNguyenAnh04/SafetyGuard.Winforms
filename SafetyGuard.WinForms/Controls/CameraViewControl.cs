using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.Services;
using SafetyGuard.WinForms.UI;
using SafetyGuard.WinForms.Vision;

namespace SafetyGuard.WinForms.Controls;

public sealed class CameraViewControl : UserControl
{
    private readonly AppBootstrap _app;
    private readonly CameraConfig _cam;

    private FrameSourceBase? _source;

    private readonly PictureBox _pic = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.Black,
        Margin = Padding.Empty,
        Padding = Padding.Empty
    };

    private readonly Label _lblOverlay = new()
    {
        AutoSize = true,
        ForeColor = Color.White,
        BackColor = Color.FromArgb(120, 0, 0, 0),
        Padding = new Padding(6)
    };

    private readonly BadgeLabel _statusBadge;

    private bool _detecting;
    private bool _disposed;

    // ===== PERF =====
    private int _processing; // 0/1 để drop frame khi bận
    private long _lastUiTick;
    private const int UiFps = 15;
    private static readonly long UiIntervalTicks =
        (long)(Stopwatch.Frequency / (double)UiFps);

    // nếu muốn giới hạn số luồng detect chạy song song (đa camera)
    private static readonly SemaphoreSlim InferenceGate = new(initialCount: 2, maxCount: 2);

    public CameraViewControl(AppBootstrap app, CameraConfig cam)
    {
        _app = app;
        _cam = cam;

        Dock = DockStyle.Fill;
        BackColor = Color.Black;
        Margin = Padding.Empty;
        Padding = Padding.Empty;

        var frame = new Guna2Panel
        {
            Dock = DockStyle.Fill,
            BorderRadius = 14,
            FillColor = Color.Black,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            BorderThickness = 0,
        };
        frame.ShadowDecoration.Enabled = false;
        Controls.Add(frame);

        frame.Controls.Add(_pic);

        _lblOverlay.Text = $"{cam.Name}";
        _lblOverlay.Location = new Point(10, 10);
        _pic.Controls.Add(_lblOverlay);

        _statusBadge = new BadgeLabel("OFFLINE", AppColors.MutedText, Color.White)
        {
            Location = new Point(10, 42)
        };
        _pic.Controls.Add(_statusBadge);

        // Double buffer giảm giật khi repaint
        ControlPerf.EnableDoubleBuffer(this);
        ControlPerf.EnableDoubleBuffer(frame);

        Disposed += (_, _) =>
        {
            _disposed = true;
            Stop();
        };
    }

    public void Start()
    {
        Stop();

        _source = new RtspFrameSource(_cam.Id, _cam.Name, _cam.RtspUrl, _app.Logs);
        _source.OnStatus += st => this.SafeInvoke(() => UpdateStatus(st));

        // ❌ KHÔNG SafeInvoke(OnFrame) nữa
        _source.OnFrame += HandleFrame;

        _source.Start();
    }

    public void Stop()
    {
        try
        {
            if (_source != null)
            {
                _source.OnFrame -= HandleFrame;
                _source.Dispose();
                _source = null;
            }
        }
        catch { /* ignore */ }

        UpdateStatus(CameraStatus.Offline);

        // clear image
        if (!IsDisposed && _pic.Image != null)
        {
            var old = _pic.Image;
            _pic.Image = null;
            old.Dispose();
        }
    }

    public void SetDetecting(bool enabled) => _detecting = enabled;

    private void UpdateStatus(CameraStatus st)
    {
        switch (st)
        {
            case CameraStatus.Connected:
                _statusBadge.Set("CONNECTED", AppColors.GoodGreen, Color.White);
                break;
            case CameraStatus.Reconnecting:
                _statusBadge.Set("RECONNECTING", AppColors.WarnAmber, Color.White);
                break;
            default:
                _statusBadge.Set("OFFLINE", AppColors.MutedText, Color.White);
                break;
        }
    }

    private void HandleFrame(Bitmap bmp)
    {
        if (_disposed || IsDisposed)
        {
            bmp.Dispose();
            return;
        }

        // Drop frame nếu đang xử lý frame trước (giảm lag)
        if (Interlocked.Exchange(ref _processing, 1) == 1)
        {
            bmp.Dispose();
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessFrameAsync(bmp);
            }
            catch
            {
                bmp.Dispose();
            }
            finally
            {
                Interlocked.Exchange(ref _processing, 0);
            }
        });
    }

    private async Task ProcessFrameAsync(Bitmap bmp)
    {
        var nowTick = Stopwatch.GetTimestamp();
        var canUiUpdate = (nowTick - Interlocked.Read(ref _lastUiTick)) >= UiIntervalTicks;

        Detection[] dets = Array.Empty<Detection>();
        Bitmap displayBmp = bmp; // mặc định dùng luôn bmp để tránh clone thừa

        if (_detecting)
        {
            await InferenceGate.WaitAsync().ConfigureAwait(false);
            try
            {
                dets = _app.Detector.Detect(bmp);
                // Render bbox lên bitmap mới
                displayBmp = Render(bmp, dets);
                // bmp không dùng cho UI nữa → dispose
                bmp.Dispose();

                if (dets.Length > 0)
                {
                    // Engine có thể lưu snapshot/clip → chạy luôn ở background (đang ở background rồi)
                    _app.Engine.ProcessDetections(_cam.Id, _cam.Name, displayBmp, dets);
                }
            }
            finally
            {
                InferenceGate.Release();
            }
        }

        if (!canUiUpdate)
        {
            // Không update UI thì phải dispose bitmap giữ
            if (!ReferenceEquals(displayBmp, bmp))
                displayBmp.Dispose();
            else
                bmp.Dispose();

            return;
        }

        Interlocked.Exchange(ref _lastUiTick, nowTick);

        // Update UI (nhẹ): chỉ set Image
        this.BeginInvoke((Action)(() =>
        {
            if (_disposed || IsDisposed)
            {
                displayBmp.Dispose();
                return;
            }

            var old = _pic.Image;
            _pic.Image = displayBmp;
            old?.Dispose();
        }));
    }

    private static Bitmap Render(Bitmap src, Detection[] dets)
    {
        var bmp = (Bitmap)src.Clone();
        using var g = Graphics.FromImage(bmp);
        using var pen = new Pen(Color.Lime, 2);
        using var font = new Font("Segoe UI", 10, FontStyle.Bold);
        using var brush = new SolidBrush(Color.FromArgb(160, 0, 0, 0));

        foreach (var d in dets)
        {
            var r = new Rectangle((int)d.Box.X, (int)d.Box.Y, (int)d.Box.W, (int)d.Box.H);
            g.DrawRectangle(pen, r);

            var text = $"{d.Type} {d.Confidence:0.00}";
            var size = g.MeasureString(text, font);
            var bg = new RectangleF(r.X, Math.Max(0, r.Y - size.Height - 2), size.Width + 8, size.Height + 4);

            g.FillRectangle(brush, bg);
            g.DrawString(text, font, Brushes.White, bg.X + 4, bg.Y + 2);
        }

        return bmp;
    }
}
