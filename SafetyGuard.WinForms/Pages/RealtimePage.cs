using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using SafetyGuard.WinForms.Controls;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.UI;

namespace SafetyGuard.WinForms.Pages;

public sealed class RealtimePage : UserControl
{
    private enum ViewMode { Single, Grid }

    private readonly AppBootstrap _app;

    // Offline identifiers (match OfflinePage camId/camName)
    private const string OfflineCameraId = "offline";
    private const string OfflineCameraName = "Offline Import";

    // Right: events list
    private readonly FlowLayoutPanel _events = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        BackColor = Color.Transparent
    };

    // Layout columns
    private readonly Panel _left = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
    private readonly Panel _right = new() { Dock = DockStyle.Right, Width = 360, BackColor = Color.Transparent };

    private const int MaxEventCards = 30;

    // Live host
    private readonly Panel _liveHost = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
    private readonly Panel _singleHost = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
    private readonly TableLayoutPanel _grid = new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 2,
        RowCount = 2,
        BackColor = Color.Transparent
    };

    // ✅ Single camera frame (giới hạn kích thước để "bé lại")
    private readonly Panel _singleFrame = new() { BackColor = Color.Black };
    private const int SingleMaxW = 920;  // chỉnh nhỏ hơn nếu muốn (vd 820)
    private const int SingleMaxH = 560;  // chỉnh nhỏ hơn nếu muốn (vd 480)

    // Views
    private readonly List<CameraViewControl> _gridViews = new();
    private CameraViewControl? _singleView;

    // Top controls
    private Guna2ComboBox _cboCam = null!;
    private Guna2Button _btnSingle = null!;
    private Guna2Button _btnGrid = null!;
    private Guna2Button _btnClearLive = null!;

    private ViewMode _mode = ViewMode.Single;
    private bool _detecting;

    public RealtimePage(AppBootstrap app)
    {
        _app = app;
        Dock = DockStyle.Fill;
        BackColor = AppColors.ContentBg;

        BuildUI();

        // live events from engine (✅ filter out Offline Import)
        _app.Engine.OnViolationCreated += v => this.SafeInvoke(() =>
        {
            if (!ShouldShowInLivePanel(v)) return;
            PushEventCard(v);
        });
    }

    private bool ShouldShowInLivePanel(ViolationRecord v)
    {
        // Hide offline imports from realtime "Live Events"
        if (!string.IsNullOrWhiteSpace(v.CameraId) &&
            v.CameraId.Equals(OfflineCameraId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(v.CameraName) &&
            v.CameraName.Equals(OfflineCameraName, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private void BuildUI()
    {
        var root = new Panel { Dock = DockStyle.Fill };
        Controls.Add(root);

        root.Controls.Add(_left);
        root.Controls.Add(_right);

        // ===== Top bar (left) =====
        var topCard = ControlFactory.Card();
        topCard.Dock = DockStyle.Top;
        topCard.Height = 100;
        _left.Controls.Add(topCard);

        var bar = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 14, 14, 14) };
        topCard.Controls.Add(bar);

        // Left group: Start / Stop
        var leftGroup = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent
        };
        bar.Controls.Add(leftGroup);

        var btnStart = new Guna2Button
        {
            Text = "Start Detection",
            BorderRadius = 10,
            FillColor = AppColors.PrimaryBlue,
            ForeColor = Color.White,
            Size = new Size(140, 36),
            Margin = new Padding(0, 0, 8, 0)
        };
        leftGroup.Controls.Add(btnStart);

        var btnStop = new Guna2Button
        {
            Text = "Stop",
            BorderRadius = 10,
            FillColor = AppColors.BadRed,
            ForeColor = Color.White,
            Size = new Size(90, 36),
            Margin = new Padding(0)
        };
        leftGroup.Controls.Add(btnStop);

        // Right group: mode + camera selector
        var rightGroup = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent
        };
        bar.Controls.Add(rightGroup);

        _btnSingle = new Guna2Button
        {
            Text = "Single",
            BorderRadius = 10,
            FillColor = AppColors.PrimaryBlue,
            ForeColor = Color.White,
            Size = new Size(90, 36),
            Margin = new Padding(0, 0, 8, 0)
        };
        rightGroup.Controls.Add(_btnSingle);

        _btnGrid = new Guna2Button
        {
            Text = "2×2 Grid",
            BorderRadius = 10,
            FillColor = Color.White,
            ForeColor = AppColors.TitleText,
            Size = new Size(100, 36),
            Margin = new Padding(0, 0, 8, 0)
        };
        rightGroup.Controls.Add(_btnGrid);

        _cboCam = new Guna2ComboBox
        {
            Width = 220,
            Height = 36,
            BorderRadius = 10,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0)
        };
        rightGroup.Controls.Add(_cboCam);

        btnStart.Click += (_, _) => SetDetecting(true);
        btnStop.Click += (_, _) => SetDetecting(false);

        _btnSingle.Click += (_, _) => SetMode(ViewMode.Single);
        _btnGrid.Click += (_, _) => SetMode(ViewMode.Grid);

        // ===== Live host =====
        _left.Controls.Add(_liveHost);
        _liveHost.BringToFront();

        _liveHost.Controls.Add(_singleHost);
        _liveHost.Controls.Add(_grid);

        // Grid setup (gọn hơn)
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _grid.Padding = new Padding(10); // ✅ giảm padding cho gọn

        // ✅ Single frame: camera bé lại + căn giữa
        _singleHost.Padding = new Padding(10);
        _singleFrame.Size = new Size(SingleMaxW, SingleMaxH);
        _singleFrame.Anchor = AnchorStyles.None;
        _singleHost.Controls.Add(_singleFrame);
        _singleHost.Resize += (_, _) => CenterSingleFrame();
        CenterSingleFrame();

        // ===== Build camera sources + selector =====
        var cams = _app.Settings.Current.Cameras.Where(c => c.Enabled).ToList();
        if (cams.Count == 0)
            cams.Add(new CameraConfig { Name = "No Camera", RtspUrl = "", Enabled = false });

        _cboCam.Items.Clear();
        foreach (var c in cams) _cboCam.Items.Add(c.Name);

        _cboCam.SelectedIndex = 0;
        _cboCam.SelectedIndexChanged += (_, _) =>
        {
            if (_mode != ViewMode.Single) return;
            var cam = cams[Math.Max(0, _cboCam.SelectedIndex)];
            ShowSingle(cam);
        };

        // Build grid (tối đa 4)
        BuildGridViews();

        // ===== Right events panel =====
        var eventCard = ControlFactory.Card();
        eventCard.Dock = DockStyle.Fill;
        _right.Padding = new Padding(10, 10, 20, 20);
        _right.Controls.Add(eventCard);

        // Header (no Location hacks -> aligned)
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(10, 8, 10, 8)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        eventCard.Controls.Add(header);

        var lbl = ControlFactory.Title("Live Events", 12, true);
        lbl.Dock = DockStyle.Fill;
        lbl.TextAlign = ContentAlignment.MiddleLeft;
        header.Controls.Add(lbl, 0, 0);

        _btnClearLive = new Guna2Button
        {
            Text = "Clear",
            BorderRadius = 10,
            FillColor = Color.FromArgb(238, 242, 248),
            ForeColor = Color.FromArgb(60, 70, 90),
            Size = new Size(90, 30),
            Margin = new Padding(0)
        };
        _btnClearLive.Click += (_, _) => _events.Controls.Clear();
        header.Controls.Add(_btnClearLive, 1, 0);

        eventCard.Controls.Add(_events);
        ControlPerf.EnableDoubleBuffer(_events);

        // Keep card widths in sync when panel resizes
        _events.SizeChanged += (_, _) => UpdateEventCardWidths();

        // Default: Single
        SetMode(ViewMode.Single);
        ShowSingle(cams[0]);
    }

    private void BuildGridViews()
    {
        _grid.Controls.Clear();
        _gridViews.Clear();

        var cams = _app.Settings.Current.Cameras.Where(c => c.Enabled).Take(4).ToList();
        while (cams.Count < 4) cams.Add(new CameraConfig { Name = "Disabled", RtspUrl = "", Enabled = false });

        for (int i = 0; i < 4; i++)
        {
            var cam = cams[i];
            var view = new CameraViewControl(_app, cam)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(8) // ✅ giảm "to" / tạo khoảng hở đẹp
            };

            _gridViews.Add(view);
            _grid.Controls.Add(view, i % 2, i / 2);

            // click tile -> switch to single
            view.Cursor = Cursors.Hand;
            view.Click += (_, _) =>
            {
                SetMode(ViewMode.Single);

                // set combo if found
                var all = _app.Settings.Current.Cameras.Where(c => c.Enabled).ToList();
                var idx = all.FindIndex(c => c.Name == cam.Name);
                if (idx >= 0 && idx < _cboCam.Items.Count) _cboCam.SelectedIndex = idx;

                ShowSingle(cam);
            };

            if (IsValidRtsp(cam)) view.Start();
            view.SetDetecting(_detecting);
        }
    }

    private void SetMode(ViewMode mode)
    {
        _mode = mode;

        _singleHost.Visible = (mode == ViewMode.Single);
        _grid.Visible = (mode == ViewMode.Grid);

        // show camera selector only on Single
        _cboCam.Visible = (mode == ViewMode.Single);

        // button highlight
        _btnSingle.FillColor = mode == ViewMode.Single ? AppColors.PrimaryBlue : Color.White;
        _btnSingle.ForeColor = mode == ViewMode.Single ? Color.White : AppColors.TitleText;

        _btnGrid.FillColor = mode == ViewMode.Grid ? AppColors.PrimaryBlue : Color.White;
        _btnGrid.ForeColor = mode == ViewMode.Grid ? Color.White : AppColors.TitleText;
    }

    private void ShowSingle(CameraConfig cam)
    {
        // dispose old view
        foreach (Control c in _singleFrame.Controls) c.Dispose();
        _singleFrame.Controls.Clear();

        var view = new CameraViewControl(_app, cam) { Dock = DockStyle.Fill };
        _singleView = view;
        _singleFrame.Controls.Add(view);

        CenterSingleFrame();

        if (IsValidRtsp(cam)) view.Start();
        view.SetDetecting(_detecting);
    }

    private void CenterSingleFrame()
    {
        // clamp khung theo size hiện tại (không tràn khi cửa sổ nhỏ)
        int w = Math.Min(SingleMaxW, Math.Max(260, _singleHost.ClientSize.Width - _singleHost.Padding.Horizontal));
        int h = Math.Min(SingleMaxH, Math.Max(200, _singleHost.ClientSize.Height - _singleHost.Padding.Vertical));

        _singleFrame.Size = new Size(w, h);

        int x = Math.Max(0, (_singleHost.ClientSize.Width - _singleFrame.Width) / 2);
        int y = Math.Max(0, (_singleHost.ClientSize.Height - _singleFrame.Height) / 2);
        _singleFrame.Location = new Point(x, y);
    }

    private static bool IsValidRtsp(CameraConfig cam)
    {
        return cam.Enabled &&
               !string.IsNullOrWhiteSpace(cam.RtspUrl) &&
               !cam.RtspUrl.Contains("your_rtsp_here", StringComparison.OrdinalIgnoreCase);
    }

    private void SetDetecting(bool on)
    {
        _detecting = on;

        foreach (var v in _gridViews) v.SetDetecting(_detecting);
        _singleView?.SetDetecting(_detecting);

        _app.Logs.Info(on ? "Detection started." : "Detection stopped.");
    }

    private int CalcEventCardWidth()
    {
        var w = _events.ClientSize.Width;
        if (w <= 0) w = _right.ClientSize.Width;
        return Math.Max(220, w - SystemInformation.VerticalScrollBarWidth - 18);
    }

    private void UpdateEventCardWidths()
    {
        var target = CalcEventCardWidth();
        foreach (Control c in _events.Controls)
        {
            if (c is Guna2Panel p) p.Width = target;
            else c.Width = target;
        }
    }

    private void PushEventCard(ViolationRecord v)
    {
        _events.SuspendLayout();

        var width = CalcEventCardWidth();

        var p = new Guna2Panel
        {
            BorderRadius = 12,
            FillColor = Color.FromArgb(255, 240, 240),
            Height = 90,
            Width = width,
            Margin = new Padding(6),
            Padding = new Padding(12)
        };

        // Layout inside card (avoid Location misalignment)
        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        p.Controls.Add(inner);

        var t = ControlFactory.Title($"{v.Type}", 11, true);
        t.Dock = DockStyle.Fill;
        t.TextAlign = ContentAlignment.MiddleLeft;
        inner.Controls.Add(t, 0, 0);
        inner.SetColumnSpan(t, 2);

        var s = ControlFactory.Muted($"{v.CameraName} • {v.TimeUtc.ToLocalTime():HH:mm:ss} • conf {v.Confidence:0.00}", 9);
        s.Dock = DockStyle.Fill;
        s.TextAlign = ContentAlignment.MiddleLeft;
        inner.Controls.Add(s, 0, 1);
        inner.SetColumnSpan(s, 2);

        var badge = new BadgeLabel(
            v.Level.ToString().ToUpperInvariant(),
            UiHelpers.SeverityColor(v.Level),
            Color.White)
        {
            Dock = DockStyle.Left,
            Margin = new Padding(0, 6, 0, 0)
        };
        inner.Controls.Add(badge, 0, 2);

        _events.Controls.Add(p);
        _events.Controls.SetChildIndex(p, 0);

        // ✅ giới hạn số card để không lag dần theo thời gian
        while (_events.Controls.Count > MaxEventCards)
        {
            var last = _events.Controls[_events.Controls.Count - 1];
            _events.Controls.RemoveAt(_events.Controls.Count - 1);
            last.Dispose();
        }

        _events.ResumeLayout(true);
    }
}
