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
    private readonly CameraRuntimeState _state;
    private readonly System.Windows.Forms.Timer _watchdog = new() { Interval = 1000 };


    private int _fpsCount;
    private DateTime _fpsWindowStart = DateTime.MinValue;
    private bool CanDetect => _detecting && _state.Status == CameraStatus.Connected;



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
        _state = new CameraRuntimeState
        {
            CameraId = cam.Id,
            CameraName = cam.Name,
            Status = CameraStatus.Offline
        };

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
        _pic.Controls.Add(_lblCenter);
        _lblCenter.BringToFront();


        _lblOverlay.Text = $"{cam.Name}";
        _lblOverlay.Location = new Point(10, 10);
        _pic.Controls.Add(_lblOverlay);

        _statusBadge = new BadgeLabel("OFFLINE", AppColors.MutedText, Color.White)
        {
            Location = new Point(10, 42)
        };
        _pic.Controls.Add(_statusBadge);

        _watchdog.Tick += (_, _) =>
        {
            if (_source == null) return;

            // nếu hơn 3s không có frame => Offline (tùy bạn chỉnh 3-5s)
            if (_state.Status == CameraStatus.Connected &&
                _state.LastFrameUtc != DateTime.MinValue &&
                (DateTime.UtcNow - _state.LastFrameUtc).TotalSeconds > 3)
            {
                _state.Status = CameraStatus.Offline;
                UpdateStatusBadge();
                UpdateOverlayText();
            }
        };
        _watchdog.Start();


        // Double buffer giảm giật khi repaint
        ControlPerf.EnableDoubleBuffer(this);
        ControlPerf.EnableDoubleBuffer(frame);

        Resize += (_, _) => CenterStatusLabel();

        Disposed += (_, _) =>
        {
            _disposed = true;
            _watchdog.Stop();
            Stop();
        };
    }
    private void CenterStatusLabel()
    {
        _lblCenter.Left = (Width - _lblCenter.Width) / 2;
        _lblCenter.Top = (Height - _lblCenter.Height) / 2;
    }

    public void Start()
    {
        Stop();

        _source = new RtspFrameSource(_cam.Id, _cam.Name, _cam.RtspUrl, _app.Logs);
        _source.OnStatus += OnSourceStatus;


        // ❌ KHÔNG SafeInvoke(OnFrame) nữa
        _source.OnFrame += HandleFrame;

        _source.Start();
    }

    private void OnSourceStatus(CameraStatus st)
    {
        _state.Status = st;
        if (_disposed || IsDisposed) return;

        if (IsHandleCreated)
        {
            BeginInvoke((Action)(() =>
            {
                UpdateStatusBadge();
                UpdateOverlayText();
            }));
        }
    }

    private readonly Label _lblCenter = new()
    {
        AutoSize = true,
        ForeColor = Color.White,
        BackColor = Color.Transparent,
        Font = new Font("Segoe UI", 14, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleCenter
    };

    public void Stop()
    {
        try
        {
            if (_source != null)
            {
                _source.OnFrame -= HandleFrame;
                _source.OnStatus -= OnSourceStatus;
                _source.Dispose();
                _source = null;
            }
        }
        catch { /* ignore */ }

        _state.Status = CameraStatus.Offline;
        _state.Fps = 0;
        _state.LastFrameUtc = DateTime.MinValue;

        UpdateStatusBadge();
        UpdateOverlayText();

        // clear image
        if (!IsDisposed && _pic.Image != null)
        {
            var old = _pic.Image;
            _pic.Image = null;
            old.Dispose();
        }
    }

    public void SetDetecting(bool enabled) => _detecting = enabled;

    private void UpdateStatusBadge()
    {
        switch (_state.Status)
        {
            case CameraStatus.Connected:
                _statusBadge.Set(
                    $"CONNECTED • {_state.Fps:0.0} FPS",
                    AppColors.GoodGreen,
                    Color.White);
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
        var now = DateTime.UtcNow;
        _state.LastFrameUtc = now;
        _state.Status = CameraStatus.Connected;

        // FPS
        if (_fpsWindowStart == DateTime.MinValue)
            _fpsWindowStart = now;

        _fpsCount++;
        var elapsed = (now - _fpsWindowStart).TotalSeconds;
        if (elapsed >= 1)
        {
            _state.Fps = _fpsCount / elapsed;
            _fpsCount = 0;
            _fpsWindowStart = now;
        }
        if (IsHandleCreated && !_disposed && !IsDisposed)
        {
            BeginInvoke((Action)(() =>
            {
                UpdateStatusBadge();
                UpdateOverlayText();
            }));
        }


        if (elapsed >= 1 && IsHandleCreated && !_disposed && !IsDisposed)
        {
            BeginInvoke((Action)(() =>
            {
                UpdateStatusBadge();   // cập nhật text có FPS
            }));
        }

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
    private void UpdateOverlayText()
    {
        var statusText = _state.Status switch
        {
            CameraStatus.Connected => "", // không cần dòng phụ
            CameraStatus.Reconnecting => "\nRECONNECTING…",
            _ => "\nNO SIGNAL"
        };

        _lblOverlay.Text = $"{_cam.Name}{statusText}";

        // làm nổi khi lỗi
        _lblOverlay.BackColor = _state.Status == CameraStatus.Connected
            ? Color.FromArgb(120, 0, 0, 0)
            : Color.FromArgb(160, 200, 60, 60); // đỏ nhạt

        _lblCenter.Text = _state.Status switch
        {
            CameraStatus.Reconnecting => "RECONNECTING…",
            CameraStatus.Offline => "NO SIGNAL",
            _ => ""
        };

        _lblCenter.Visible = _state.Status != CameraStatus.Connected;
        CenterStatusLabel();

    }


    private async Task ProcessFrameAsync(Bitmap bmp)
    {
        var nowTick = Stopwatch.GetTimestamp();
        var canUiUpdate = (nowTick - Interlocked.Read(ref _lastUiTick)) >= UiIntervalTicks;

        Detection[] dets = Array.Empty<Detection>();
        Bitmap displayBmp = bmp; // mặc định dùng luôn bmp để tránh clone thừa

        if (CanDetect)
        {
            await InferenceGate.WaitAsync().ConfigureAwait(false);
            try
            {
                dets = _app.Detector.Detect(bmp);
                displayBmp = Render(bmp, dets);
                bmp.Dispose();

                if (dets.Length > 0)
                {
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
