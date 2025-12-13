using System;
using System.Drawing;
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

    // ✅ image fills the whole rounded frame
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

    public event Action<ViolationRecord>? OnViolation;

    public CameraViewControl(AppBootstrap app, CameraConfig cam)
    {
        _app = app;
        _cam = cam;

        Dock = DockStyle.Fill;
        BackColor = Color.Black;
        Margin = Padding.Empty;
        Padding = Padding.Empty;

        // ✅ frame has NO padding -> removes the "border/white frame" feeling
        var frame = new Guna2Panel
        {
            Dock = DockStyle.Fill,
            BorderRadius = 14,
            FillColor = Color.Black,
            Padding = Padding.Empty,        // ✅ was Padding(2)
            Margin = Padding.Empty,
            BorderThickness = 0,
        };
        frame.ShadowDecoration.Enabled = false;
        Controls.Add(frame);

        frame.Controls.Add(_pic);

        // Overlay labels on top of picture
        _lblOverlay.Text = $"{cam.Name}";
        _lblOverlay.Location = new Point(10, 10);
        _pic.Controls.Add(_lblOverlay);

        _statusBadge = new BadgeLabel("OFFLINE", AppColors.MutedText, Color.White)
        {
            Location = new Point(10, 42)
        };
        _pic.Controls.Add(_statusBadge);

        Disposed += (_, _) => Stop();
    }

    public void Start()
    {
        Stop();

        _source = new RtspFrameSource(_cam.Id, _cam.Name, _cam.RtspUrl, _app.Logs);
        _source.OnStatus += st => this.SafeInvoke(() => UpdateStatus(st));
        _source.OnFrame += bmp => this.SafeInvoke(() => OnFrame(bmp));
        _source.Start();
    }

    public void Stop()
    {
        _source?.Dispose();
        _source = null;
        UpdateStatus(CameraStatus.Offline);
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

    private void OnFrame(Bitmap bmp)
    {
        var detections = _detecting ? _app.Detector.Detect(bmp) : Array.Empty<Detection>();

        using var rendered = Render(bmp, detections);
        _pic.Image?.Dispose();
        _pic.Image = (Bitmap)rendered.Clone();

        if (_detecting && detections.Length > 0)
        {
            _app.Engine.ProcessDetections(_cam.Id, _cam.Name, bmp, detections);
        }

        bmp.Dispose();
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
