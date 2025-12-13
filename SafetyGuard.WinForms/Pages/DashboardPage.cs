using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.Services;
using SafetyGuard.WinForms.UI;
using LiveChartsCore.SkiaSharpView.WinForms;


namespace SafetyGuard.WinForms.Pages;

public sealed class DashboardPage : UserControl
{
    private readonly AppBootstrap _app;

    private Label _kpiTotal = null!;
    private Label _kpiCritical = null!;
    private Label _kpiRate = null!;
    private Label _kpiCams = null!;

    private CartesianChart _trend = null!;
    private PieChart _pie = null!;

    public DashboardPage(AppBootstrap app)
    {
        _app = app;
        Dock = DockStyle.Fill;
        BackColor = AppColors.ContentBg;

        BuildUI();
        RefreshMetrics();

        _app.Violations.OnChanged += () => this.SafeInvoke(RefreshMetrics);
        _app.Settings.OnChanged += _ => this.SafeInvoke(RefreshMetrics);
    }

    private void BuildUI()
    {
        var tblMain = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        tblMain.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tblMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(tblMain);

        var tblKpi = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        tblKpi.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tblKpi.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tblKpi.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tblKpi.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tblMain.Controls.Add(tblKpi, 0, 0);

        tblKpi.Controls.Add(KpiCard("TOTAL VIOLATIONS (TODAY)", out _kpiTotal, "↓ demo", AppColors.BlueTint, "i"), 0, 0);
        tblKpi.Controls.Add(KpiCard("CRITICAL WARNINGS", out _kpiCritical, "↑ demo", AppColors.RedTint, "!"), 1, 0);
        tblKpi.Controls.Add(KpiCard("SAFETY RATE", out _kpiRate, "Target: 98%", AppColors.GreenTint, "✓"), 0, 1);
        tblKpi.Controls.Add(KpiCard("ACTIVE CAMERAS", out _kpiCams, "All Systems", AppColors.AmberTint, "🎥"), 1, 1);

        var tblCharts = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 6, 0, 0)
        };
        tblCharts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        tblCharts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        tblMain.Controls.Add(tblCharts, 0, 1);

        var cardTrend = ControlFactory.Card();
        cardTrend.Dock = DockStyle.Fill;
        tblCharts.Controls.Add(cardTrend, 0, 0);
        BuildTrendCard(cardTrend);

        var cardPie = ControlFactory.Card();
        cardPie.Dock = DockStyle.Fill;
        tblCharts.Controls.Add(cardPie, 1, 0);
        BuildPieCard(cardPie);
    }

    private Guna2ShadowPanel KpiCard(string title, out Label value, string sub, Color tint, string icon)
    {
        var card = ControlFactory.Card();
        card.Dock = DockStyle.Fill;

        var t = ControlFactory.Muted(title, 9, true);
        t.Location = new Point(4, 4);
        card.Controls.Add(t);

        value = new Label
        {
            Text = "0",
            AutoSize = true,
            ForeColor = AppColors.TitleText,
            Font = new Font("Segoe UI", 28, FontStyle.Bold),
            Location = new Point(2, 28)
        };
        card.Controls.Add(value);

        var s = new Label
        {
            Text = sub,
            AutoSize = true,
            ForeColor = AppColors.MutedText,
            Font = new Font("Segoe UI", 9),
            Location = new Point(4, 78)
        };
        card.Controls.Add(s);

        var ib = ControlFactory.IconBox(tint);
        ib.Location = new Point(card.Width - 60, 18);
        ib.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        card.Controls.Add(ib);

        ib.Controls.Add(new Label
        {
            Text = icon,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = AppColors.TitleText
        });

        return card;
    }

    private void BuildTrendCard(Guna2ShadowPanel card)
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 40 };
        card.Controls.Add(header);

        var lbl = ControlFactory.Title("7-Day Trends", 12, true);
        lbl.Location = new Point(4, 8);
        header.Controls.Add(lbl);

        var cb = new Guna2ComboBox
        {
            Width = 140,
            Height = 32,
            BorderRadius = 10,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            DrawMode = DrawMode.OwnerDrawFixed,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cb.Items.AddRange(new object[] { "Last 7 Days", "Last 30 Days" });
        cb.SelectedIndex = 0;
        header.Controls.Add(cb);
        header.Resize += (_, _) => cb.Location = new Point(header.Width - cb.Width - 6, 4);
        cb.Location = new Point(header.Width - cb.Width - 6, 4);

        _trend = new CartesianChart { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        card.Controls.Add(_trend);
    }

    private void BuildPieCard(Guna2ShadowPanel card)
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 40 };
        card.Controls.Add(header);

        var lbl = ControlFactory.Title("Violation Types", 12, true);
        lbl.Location = new Point(4, 8);
        header.Controls.Add(lbl);

        _pie = new PieChart { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        card.Controls.Add(_pie);
    }

    private void RefreshMetrics()
    {
        var all = _app.Violations.All();
        var today = DateTime.UtcNow.Date;

        var todayItems = all.Where(v => v.TimeUtc.Date == today).ToList();
        var critical = todayItems.Count(v => v.Level == ViolationLevel.Critical);

        var cams = _app.Settings.Current.Cameras.Count(c => c.Enabled);
        var totalToday = todayItems.Count;

        // Safety rate = 100 - normalized violations (demo logic)
        var safetyRate = Math.Max(0, 100 - totalToday * 2);

        _kpiTotal.Text = totalToday.ToString();
        _kpiCritical.Text = critical.ToString();
        _kpiRate.Text = $"{safetyRate}%";
        _kpiCams.Text = $"{cams}/{_app.Settings.Current.Cameras.Count}";

        // trend 7 days
        var days = Enumerable.Range(0, 7).Select(i => today.AddDays(-6 + i)).ToList();
        var counts = days.Select(d => all.Count(v => v.TimeUtc.Date == d)).Select(x => (double)x).ToArray();

        _trend.Series = new ISeries[]
        {
            new LineSeries<double> { Values = counts, GeometrySize = 10 }
        };

        // pie by type (last 7 days)
        var start = today.AddDays(-6);
        var last7 = all.Where(v => v.TimeUtc.Date >= start).ToList();
        var byType = Enum.GetValues(typeof(ViolationType)).Cast<ViolationType>()
            .Select(t => (t, c: last7.Count(v => v.Type == t)))
            .Where(x => x.c > 0)
            .ToList();

        if (byType.Count == 0) byType.Add((ViolationType.NoHelmet, 1));

        _pie.Series = byType.Select(x => new PieSeries<double>
        {
            Values = new double[] { x.c },
            Name = x.t.ToString(),
            InnerRadius = 60
        }).Cast<LiveChartsCore.ISeries>().ToArray();
    }
}
