using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.Services;
using SafetyGuard.WinForms.UI;
using SafetyGuard.WinForms.Vision;
using Timer = System.Windows.Forms.Timer;

namespace SafetyGuard.WinForms.Controls;

public sealed class CameraViewControl : UserControl
{
    private readonly AppBootstrap _app;
    private readonly CameraConfig _cam;
    private readonly CameraRuntimeState _state;
    private readonly Timer _watchdog = new() { Interval = 1000 };

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

    private readonly Label _lblCenter = new()
    {
        AutoSize = true,
        ForeColor = Color.White,
        BackColor = Color.Transparent,
        Font = new Font("Segoe UI", 14, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleCenter
    };

    private readonly BadgeLabel _statusBadge;

    private int _fpsCount;
    private DateTime _fpsWindowStart = DateTime.MinValue;

    private bool _detecting;
    private bool _disposed;

    private bool CanDetect => _detecting && _state.Status == CameraStatus.Connected;

    // ===== PERF =====
    private int _processing; // 0/1
    private long _lastUiTick;
    private const int UiFps = 15;
    private static readonly long UiIntervalTicks =
        (long)(Stopwatch.Frequency / (double)UiFps);

    private static readonly SemaphoreSlim InferenceGate = new(initialCount: 2, maxCount: 2);

    // ===== NEW PIPELINE STATE =====
    private readonly SortTracker _tracker = new();
    private readonly PpeMapper _ppeMapper = new();
    private readonly Dictionary<int, PersonState> _personStates = new();
    private DetectionResult[] _lastDetections = Array.Empty<DetectionResult>();

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

        // ✅ Avoid long waits when camera is disabled or RTSP url is empty
        if (!_cam.Enabled || string.IsNullOrWhiteSpace(_cam.RtspUrl))
        {
            _state.Status = CameraStatus.Offline;
            if (IsHandleCreated)
            {
                BeginInvoke((Action)(() =>
                {
                    UpdateStatusBadge();
                    UpdateOverlayText();
                }));
            }
            return;
        }

        _source = new RtspFrameSource(_cam.Id, _cam.Name, _cam.RtspUrl, _app.Logs);
        _source.OnStatus += OnSourceStatus;
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

        if (!IsDisposed && _pic.Image != null)
        {
            var old = _pic.Image;
            _pic.Image = null;
            old.Dispose();
        }
    }

    public void SetDetecting(bool enabled) => _detecting = enabled;

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

    private void UpdateStatusBadge()
    {
        switch (_state.Status)
        {
            case CameraStatus.Connected:
                _statusBadge.Set($"CONNECTED • {_state.Fps:0.0} FPS", AppColors.GoodGreen, Color.White);
                break;
            case CameraStatus.Reconnecting:
                _statusBadge.Set("RECONNECTING", AppColors.WarnAmber, Color.White);
                break;
            default:
                _statusBadge.Set("OFFLINE", AppColors.MutedText, Color.White);
                break;
        }
    }

    private void UpdateOverlayText()
    {
        var statusText = _state.Status switch
        {
            CameraStatus.Connected => "",
            CameraStatus.Reconnecting => "\nRECONNECTING…",
            _ => "\nNO SIGNAL"
        };

        _lblOverlay.Text = $"{_cam.Name}{statusText}";

        _lblOverlay.BackColor = _state.Status == CameraStatus.Connected
            ? Color.FromArgb(120, 0, 0, 0)
            : Color.FromArgb(160, 200, 60, 60);

        _lblCenter.Text = _state.Status switch
        {
            CameraStatus.Reconnecting => "RECONNECTING…",
            CameraStatus.Offline => "NO SIGNAL",
            _ => ""
        };

        _lblCenter.Visible = _state.Status != CameraStatus.Connected;
        CenterStatusLabel();
    }

    private void HandleFrame(FramePacket pkt)
    {
        var bmp = pkt.Frame;
        var now = DateTime.UtcNow;

        _state.LastFrameUtc = now;
        _state.Status = CameraStatus.Connected;

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

        if (_disposed || IsDisposed)
        {
            bmp.Dispose();
            return;
        }

        // drop if busy
        if (Interlocked.Exchange(ref _processing, 1) == 1)
        {
            bmp.Dispose();
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessFrameAsync(pkt).ConfigureAwait(false);
            }
            catch
            {
                try { bmp.Dispose(); } catch { }
            }
            finally
            {
                Interlocked.Exchange(ref _processing, 0);
            }
        });
    }

    private async Task ProcessFrameAsync(FramePacket pkt)
    {
        long nowTick = Stopwatch.GetTimestamp();
        var canUiUpdate = (nowTick - Interlocked.Read(ref _lastUiTick)) >= UiIntervalTicks;

        var bmp = pkt.Frame;

        Bitmap displayBmp = bmp;
        bool ownsBmp = false;

        if (CanDetect)
        {
            var s = _app.Settings.Current;
            var everyN = Math.Max(1, s.DetectEveryNFrames);
            var doDetect = (pkt.FrameIndex % everyN) == 0;

            if (doDetect)
            {
                await InferenceGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    _lastDetections = _app.Detector.Detect(bmp);
                }
                finally
                {
                    InferenceGate.Release();
                }

                var persons = _lastDetections
                    .Where(d => d.Class == ObjectClass.Person)
                    .ToList();

                _tracker.Update(pkt.FrameIndex, persons, s.TrackIouThreshold, s.TrackMaxMissedFrames);
            }
            else
            {
                _tracker.Predict(pkt.FrameIndex, s.TrackMaxMissedFrames);
            }

            _ppeMapper.UpdateStates(_tracker.Tracks, _lastDetections, _personStates, s.PpeIouThreshold);

            // cleanup states không còn track
            var alive = _tracker.Tracks.Select(t => t.TrackId).ToHashSet();
            var keys = _personStates.Keys.ToList();
            foreach (var k in keys)
                if (!alive.Contains(k))
                    _personStates.Remove(k);

            // log/event + evidence crop (dùng frame gốc)
            if (_tracker.Tracks.Count > 0)
                _app.Engine.ProcessTracks(_cam.Id, _cam.Name, bmp, _tracker.Tracks, _personStates);

            displayBmp = RenderTracks(bmp, _tracker.Tracks, _personStates);
            ownsBmp = true;
            bmp.Dispose();
        }

        if (!canUiUpdate)
        {
            if (ownsBmp) displayBmp.Dispose();
            else bmp.Dispose();
            return;
        }

        Interlocked.Exchange(ref _lastUiTick, nowTick);

        BeginInvoke((Action)(() =>
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

    private static Bitmap RenderTracks(Bitmap src, IReadOnlyList<SortTracker.Track> tracks, IReadOnlyDictionary<int, PersonState> states)
    {
        var bmp = (Bitmap)src.Clone();

        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.HighSpeed;
        g.InterpolationMode = InterpolationMode.Low;

        using var font = new Font("Segoe UI", 10, FontStyle.Bold);

        foreach (var t in tracks)
        {
            var rect = t.Box.ToRectClamped(bmp.Width, bmp.Height);

            states.TryGetValue(t.TrackId, out var st);

            var color = ColorFromId(t.TrackId);
            using var pen = new Pen(color, 2);

            g.DrawRectangle(pen, rect);

            string ppe = st == null
                ? "?"
                : $"H:{(st.HasHelmet ? 1 : 0)} V:{(st.HasVest ? 1 : 0)} G:{(st.HasGloves ? 1 : 0)} S:{(st.HasSmoke ? 1 : 0)}";

            var text = $"ID:{t.TrackId}  {ppe}";
            var size = g.MeasureString(text, font);

            var bg = new RectangleF(
                rect.X,
                Math.Max(0, rect.Y - size.Height - 4),
                size.Width + 8,
                size.Height + 4);

            var bgColor = (st != null && (!st.HasHelmet || !st.HasVest || !st.HasGloves || st.HasSmoke))
                ? Color.FromArgb(180, 180, 40, 40)
                : Color.FromArgb(160, 0, 0, 0);

            using var brush = new SolidBrush(bgColor);
            g.FillRectangle(brush, bg);
            g.DrawString(text, font, Brushes.White, bg.X + 4, bg.Y + 2);
        }

        return bmp;
    }

    private static Color ColorFromId(int id)
    {
        unchecked
        {
            // ✅ tránh CS0266: dùng uint hằng số lớn
            uint h = (uint)id * 2654435761u;
            int r = 80 + (int)(h & 0x7Fu);
            int g = 80 + (int)((h >> 7) & 0x7Fu);
            int b = 80 + (int)((h >> 14) & 0x7Fu);
            return Color.FromArgb(r, g, b);
        }
    }
}
