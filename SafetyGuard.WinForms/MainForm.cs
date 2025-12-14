using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using Guna.UI2.WinForms.Enums;
using SafetyGuard.WinForms.Pages;
using SafetyGuard.WinForms.UI;
using Timer = System.Windows.Forms.Timer;

namespace SafetyGuard.WinForms;

public partial class MainForm : Form
{
    private readonly AppBootstrap _app;

    private readonly Guna2BorderlessForm _borderless;
    private readonly Guna2ShadowForm _shadow;
    private readonly Guna2DragControl _drag; // ✅ kéo form bằng topbar

    private Guna2Panel pnlSidebar = null!;
    private Guna2Panel pnlContent = null!;
    private Guna2Panel pnlTopbar = null!;
    private Guna2Panel pnlHost = null!;

    private Label lblTitle = null!;
    private Label lblSub = null!;
    private Label lblDate = null!;
    private Label lblTime = null!;

    private readonly Dictionary<string, UserControl> _pages = new();
    private readonly Dictionary<string, Guna2Button> _nav = new();

    private readonly Timer _clock = new() { Interval = 1000 };

    // ✅ độ dày vùng resize (borderless)
    private const int ResizeGrip = 8;

    public MainForm(AppBootstrap app)
    {
        _app = app;

        Text = "SafetyGuard";
        MinimumSize = new Size(1280, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppColors.ContentBg;

        // Borderless + shadow
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = true;
        MinimizeBox = true;
        DoubleBuffered = true;

        _borderless = new Guna2BorderlessForm
        {
            ContainerControl = this,
            BorderRadius = 14,
            TransparentWhileDrag = true
            // ⚠️ Không dùng SetDragForm/ResizeForm để tránh lệch version
        };

        _shadow = new Guna2ShadowForm
        {
            TargetForm = this,
            ShadowColor = Color.Black
        };

        // ✅ layout đúng thứ tự dock (không đè)
        BuildLayout();

        // ✅ build sidebar/topbar trước để có lblDate/lblTime
        BuildSidebar();
        BuildTopbar();
        BuildWindowButtons();

        // ✅ kéo form bằng topbar (tương thích mọi version Guna)
        _drag = new Guna2DragControl
        {
            TargetControl = pnlTopbar,
            DockIndicatorTransparencyValue = 0.6,
            UseTransparentDrag = true
        };

        // pages
        BuildPages();

        // clock
        _clock.Tick += (_, _) =>
        {
            lblDate.Text = DateTime.Now.ToString("MMM dd, yyyy");
            lblTime.Text = DateTime.Now.ToString("hh:mm:ss tt");
        };
        _clock.Start();

        _app.Logs.Info("App started.");
    }

    private void BuildLayout()
    {
        SuspendLayout();

        pnlSidebar = new Guna2Panel
        {
            Dock = DockStyle.Left,
            Width = 260,
            FillColor = AppColors.SidebarBg
        };

        pnlContent = new Guna2Panel
        {
            Dock = DockStyle.Fill,
            FillColor = AppColors.ContentBg
        };

        pnlTopbar = new Guna2Panel
        {
            Dock = DockStyle.Top,
            Height = 64,
            FillColor = Color.White
        };

        pnlHost = new Guna2Panel
        {
            Dock = DockStyle.Fill,
            FillColor = AppColors.ContentBg,
            Padding = new Padding(24, 16, 24, 24)
        };

        // ✅ thứ tự add để không đè
        Controls.Add(pnlContent);
        Controls.Add(pnlSidebar);

        pnlContent.Controls.Add(pnlHost);
        pnlContent.Controls.Add(pnlTopbar);

        ResumeLayout();
    }

    private void BuildSidebar()
    {
        pnlSidebar.Controls.Clear();

        // logo/top
        var pnlLogo = new Guna2Panel { Dock = DockStyle.Top, Height = 92, FillColor = AppColors.SidebarBg };
        pnlSidebar.Controls.Add(pnlLogo);

        var logoIcon = new Guna2CircleButton
        {
            Size = new Size(44, 44),
            Location = new Point(18, 22),
            FillColor = AppColors.PrimaryBlue,
            Text = "S",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            DisabledState = { FillColor = AppColors.PrimaryBlue },
            BorderThickness = 2,
            BorderColor = Color.FromArgb(60, 255, 255, 255),
        };
        pnlLogo.Controls.Add(logoIcon);

        var lblApp = new Guna2HtmlLabel
        {
            Text = "SafetyGuard",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            BackColor = Color.Transparent,
            Location = new Point(70, 22),
            AutoSize = true
        };
        pnlLogo.Controls.Add(lblApp);

        var lblVer = new Guna2HtmlLabel
        {
            Text = "PRO EDITION v2.1",
            ForeColor = Color.FromArgb(150, 160, 180),
            Font = new Font("Segoe UI", 8),
            BackColor = Color.Transparent,
            Location = new Point(70, 48),
            AutoSize = true
        };
        pnlLogo.Controls.Add(lblVer);
        
        // menu scroll
        var pnlMenu = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.Transparent };
        pnlSidebar.Controls.Add(pnlMenu);

        // ✅ Dock=Top: add bottom-most first, top-most last
        AddNav(pnlMenu, "System Settings", "settings");
        AddNav(pnlMenu, "Offline Analysis", "offline");
        AddNav(pnlMenu, "History & Evidence", "history");
        AddNav(pnlMenu, "Real-time Monitor", "realtime");
        AddNav(pnlMenu, "Dashboard", "dashboard");

        // ✅ add header LAST so it stays on top
        pnlMenu.Controls.Add(new Label
        {
            Text = "MAIN MENU",
            ForeColor = Color.FromArgb(120, 130, 150),
            AutoSize = true,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            Padding = new Padding(0, 78, 0, 6),
            Dock = DockStyle.Top
        });

        _nav["dashboard"].Checked = true;
    }

    private void AddNav(Control parent, string text, string key)
    {
        var btn = ControlFactory.NavButton(text);

        // ✅ ép nút thấp lại để không “đẩy” mất MAIN MENU / Dashboard
        btn.AutoSize = false;
        btn.Height = 44;                 // thử 42–46 tuỳ bạn
        btn.Margin = new Padding(12, 6, 12, 0);

        btn.Click += (_, _) => Open(key);
        parent.Controls.Add(btn);
        _nav[key] = btn;
    }


    private void BuildTopbar()
    {
        pnlTopbar.Controls.Clear();

        lblTitle = new Label
        {
            Text = "Overview Dashboard",
            AutoSize = true,
            ForeColor = AppColors.TitleText,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            Location = new Point(22, 14)
        };
        pnlTopbar.Controls.Add(lblTitle);

        lblSub = new Label
        {
            Text = "Connected: 4 Cameras | System Status: Optimal",
            AutoSize = true,
            ForeColor = AppColors.MutedText,
            Font = new Font("Segoe UI", 9),
            Location = new Point(24, 42)
        };
        pnlTopbar.Controls.Add(lblSub);

        // right area docked (không bị lệch khi resize)
        var right = new Panel
        {
            Dock = DockStyle.Right,
            Width = 260,
            BackColor = Color.Transparent
        };
        pnlTopbar.Controls.Add(right);

        /*
        var bell = new Guna2CircleButton
        {
            Size = new Size(36, 36),
            FillColor = AppColors.ContentBg,
            Text = "🔔",
            Font = new Font("Segoe UI", 11),
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(10, 14)
        };
        right.Controls.Add(bell);
        */

        lblDate = new Label
        {
            Text = DateTime.Now.ToString("MMM dd, yyyy"),
            AutoSize = true,
            ForeColor = AppColors.TitleText,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Location = new Point(60, 14)
        };
        right.Controls.Add(lblDate);

        lblTime = new Label
        {
            Text = DateTime.Now.ToString("hh:mm:ss tt"),
            AutoSize = true,
            ForeColor = AppColors.MutedText,
            Font = new Font("Segoe UI", 9),
            Location = new Point(60, 34)
        };
        right.Controls.Add(lblTime);

        // double click topbar to max/restore
        pnlTopbar.DoubleClick += (_, _) =>
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
        };
    }

    private void BuildWindowButtons()
    {
        // panel giữ 3 nút ở góc phải
        var pnlBtns = new Panel
        {
            Dock = DockStyle.Right,
            Width = 138,
            BackColor = Color.Transparent
        };
        pnlTopbar.Controls.Add(pnlBtns);
        pnlBtns.BringToFront();

        var btnClose = new Guna2ControlBox
        {
            Dock = DockStyle.Right,
            ControlBoxType = ControlBoxType.CloseBox,
            FillColor = Color.Transparent,
            IconColor = Color.FromArgb(40, 40, 40),
            HoverState = { FillColor = Color.FromArgb(240, 90, 90), IconColor = Color.White }
        };

        var btnMax = new Guna2ControlBox
        {
            Dock = DockStyle.Right,
            ControlBoxType = ControlBoxType.MaximizeBox,
            FillColor = Color.Transparent,
            IconColor = Color.FromArgb(40, 40, 40),
            HoverState = { FillColor = Color.FromArgb(230, 230, 230) }
        };

        var btnMin = new Guna2ControlBox
        {
            Dock = DockStyle.Right,
            ControlBoxType = ControlBoxType.MinimizeBox,
            FillColor = Color.Transparent,
            IconColor = Color.FromArgb(40, 40, 40),
            HoverState = { FillColor = Color.FromArgb(230, 230, 230) }
        };

        pnlBtns.Controls.Add(btnClose);
        pnlBtns.Controls.Add(btnMax);
        pnlBtns.Controls.Add(btnMin);
    }

    private void BuildPages()
    {
        _pages["dashboard"] = new DashboardPage(_app) { Dock = DockStyle.Fill, Visible = false };
        _pages["realtime"] = new RealtimePage(_app) { Dock = DockStyle.Fill, Visible = false };
        _pages["history"] = new HistoryPage(_app) { Dock = DockStyle.Fill, Visible = false };
        _pages["offline"] = new OfflinePage(_app) { Dock = DockStyle.Fill, Visible = false };
        _pages["settings"] = new SettingsPage(_app) { Dock = DockStyle.Fill, Visible = false };

        pnlHost.Controls.Clear();
        foreach (var p in _pages.Values) pnlHost.Controls.Add(p);

        Open("dashboard");
    }

    private void Open(string key)
    {
        foreach (var p in _pages.Values) p.Visible = false;

        var page = _pages[key];
        page.Visible = true;
        page.BringToFront();

        foreach (var b in _nav.Values) b.Checked = false;
        if (_nav.TryGetValue(key, out var btn)) btn.Checked = true;

        switch (key)
        {
            case "dashboard":
                lblTitle.Text = "Overview Dashboard";
                lblSub.Text = "Connected: 4 Cameras | System Status: Optimal";
                break;
            case "realtime":
                lblTitle.Text = "Real-time Monitoring";
                lblSub.Text = "Start/Stop Detection • Multi-camera 2×2";
                break;
            case "history":
                lblTitle.Text = "Violation History & Management";
                lblSub.Text = "Workflow: New → Acknowledged → Resolved / False Alarm";
                break;
            case "offline":
                lblTitle.Text = "Offline Analysis";
                lblSub.Text = "Upload video/image and run detection.";
                break;
            case "settings":
                lblTitle.Text = "System Configuration";
                lblSub.Text = "Cameras • Detection Rules • Storage • Logs";
                break;
        }
    }

    // ✅ Resize borderless bằng kéo viền (không phụ thuộc version Guna)
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        const int HTCLIENT = 1;
        const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
                  HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if ((int)m.Result == HTCLIENT)
            {
                var p = PointToClient(new Point(m.LParam.ToInt32()));
                bool left = p.X <= ResizeGrip;
                bool right = p.X >= ClientSize.Width - ResizeGrip;
                bool top = p.Y <= ResizeGrip;
                bool bottom = p.Y >= ClientSize.Height - ResizeGrip;

                if (left && top) m.Result = (IntPtr)HTTOPLEFT;
                else if (right && top) m.Result = (IntPtr)HTTOPRIGHT;
                else if (left && bottom) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (right && bottom) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (left) m.Result = (IntPtr)HTLEFT;
                else if (right) m.Result = (IntPtr)HTRIGHT;
                else if (top) m.Result = (IntPtr)HTTOP;
                else if (bottom) m.Result = (IntPtr)HTBOTTOM;
            }
            return;
        }

        base.WndProc(ref m);
    }
}
