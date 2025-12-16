using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WinForms;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.UI;

namespace SafetyGuard.WinForms.Pages;

public sealed class DashboardPage : UserControl
{
    private readonly AppBootstrap _app;

    // Root scrolling surface
    private Panel _scroll = null!;

    // Two centered rows
    private TableLayoutPanel _kpiRow = null!;
    private TableLayoutPanel _chartsRow = null!;

    // KPI labels
    private Label _lblTotal = null!;
    private Label _lblCritical = null!;
    private Label _lblRate = null!;
    private Label _lblActive = null!;

    // Charts
    private ComboBox _cbRange = null!;
    private CartesianChart _trend = null!;
    private PieChart _pie = null!;
    private Label _trendNoData = null!;
    private Label _pieNoData = null!;

    private readonly Axis _trendXAxis = new();
    private readonly Axis _trendYAxis = new();

    // ✅ rc6.1 FIX: ObservableCollection để clear/add
    private readonly ObservableCollection<double> _trendValues = new();
    private readonly ObservableCollection<string> _trendLabels = new();

    private LineSeries<double>? _trendLine;
    private int _lastDays = -1;

    private Action? _violationsChangedHandler;

    // Layout constants
    private const int MaxContentWidth = 1100;
    private const int RowGap = 14;

    public DashboardPage(AppBootstrap app)
    {
        _app = app;

        Dock = DockStyle.Fill;
        BackColor = AppColors.ContentBg;

        BuildUi();
        Hook();

        RefreshMetrics();
    }

    private void Hook()
    {
        // tránh subscribe lambda vô danh (không unsubscribe được)
        _violationsChangedHandler = () =>
        {
            if (!IsHandleCreated) return;
            BeginInvoke(new Action(RefreshMetrics));
        };
        _app.Violations.OnChanged += _violationsChangedHandler;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_violationsChangedHandler != null)
                _app.Violations.OnChanged -= _violationsChangedHandler;
        }
        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        Controls.Clear();

        _scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = AppColors.ContentBg,
            Padding = new Padding(18)
        };
        Controls.Add(_scroll);

        // KPI row (centered manually)
        _kpiRow = new TableLayoutPanel
        {
            ColumnCount = 4,
            RowCount = 1,
            Height = 110,
            BackColor = AppColors.ContentBg,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _kpiRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _kpiRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _kpiRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _kpiRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        var c0 = BuildKpiCard("TOTAL VIOLATIONS (TODAY)", out _lblTotal); c0.Margin = new Padding(0, 0, 12, 0);
        var c1 = BuildKpiCard("CRITICAL ALERTS", out _lblCritical); c1.Margin = new Padding(0, 0, 12, 0);
        var c2 = BuildKpiCard("COMPLIANCE RATE", out _lblRate); c2.Margin = new Padding(0, 0, 12, 0);
        var c3 = BuildKpiCard("ACTIVE CAMERAS", out _lblActive); c3.Margin = new Padding(0);

        _kpiRow.Controls.Add(c0, 0, 0);
        _kpiRow.Controls.Add(c1, 1, 0);
        _kpiRow.Controls.Add(c2, 2, 0);
        _kpiRow.Controls.Add(c3, 3, 0);

        // Charts row (centered manually)
        _chartsRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Height = 360,
            BackColor = AppColors.ContentBg,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _chartsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        _chartsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        var trendCard = BuildTrendCard(); trendCard.Margin = new Padding(0, 0, 12, 0);
        var pieCard = BuildPieCard(); pieCard.Margin = new Padding(0);

        _chartsRow.Controls.Add(trendCard, 0, 0);
        _chartsRow.Controls.Add(pieCard, 1, 0);

        _scroll.Controls.Add(_chartsRow);
        _scroll.Controls.Add(_kpiRow);

        _kpiRow.Top = _scroll.Padding.Top;
        _chartsRow.Top = _kpiRow.Bottom + RowGap;

        ConfigureCharts();

        _scroll.Resize += (_, _) => RelayoutCenteredRows();
        HandleCreated += (_, _) => BeginInvoke(new Action(RelayoutCenteredRows));
    }

    private void RelayoutCenteredRows()
    {
        if (_scroll == null) return;

        var avail = _scroll.ClientSize.Width - _scroll.Padding.Left - _scroll.Padding.Right;
        if (avail <= 0) return;

        var w = Math.Min(MaxContentWidth, avail);
        w = Math.Max(420, w);

        var left = _scroll.Padding.Left + Math.Max(0, (avail - w) / 2);

        _kpiRow.Width = w;
        _kpiRow.Left = left;

        _chartsRow.Width = w;
        _chartsRow.Left = left;

        _kpiRow.Top = _scroll.Padding.Top;
        _chartsRow.Top = _kpiRow.Bottom + RowGap;
    }

    private Control BuildKpiCard(string title, out Label value)
    {
        var card = new Guna2ShadowPanel
        {
            Radius = 16,
            FillColor = Color.White,
            ShadowColor = Color.FromArgb(210, 220, 235),
            ShadowDepth = 2,
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 12, 14, 12)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        var titleLbl = ControlFactory.Muted(title, 9, true);
        titleLbl.AutoSize = false;
        titleLbl.Dock = DockStyle.Fill;
        titleLbl.TextAlign = ContentAlignment.MiddleCenter;
        layout.Controls.Add(titleLbl, 0, 0);

        value = new Label
        {
            Text = "—",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = AppColors.TitleText
        };
        layout.Controls.Add(value, 0, 1);

        return card;
    }

    private Control BuildTrendCard()
    {
        var card = new Guna2ShadowPanel
        {
            Radius = 16,
            FillColor = Color.White,
            ShadowColor = Color.FromArgb(210, 220, 235),
            ShadowDepth = 2,
            Dock = DockStyle.Fill,
            Padding = new Padding(16)
        };

        var wrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        wrapper.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        wrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(wrapper);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        wrapper.Controls.Add(header, 0, 0);

        var title = ControlFactory.Muted("Violations Trend", 10, true);
        title.AutoSize = false;
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleLeft;
        header.Controls.Add(title, 0, 0);

        _cbRange = new ComboBox
        {
            Width = 140,
            Height = 32,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 8, 0, 0)
        };
        _cbRange.Items.AddRange(new object[] { "Last 7 Days", "Last 30 Days" });
        _cbRange.SelectedIndex = 0;

        // ✅ khi đổi range: rebuild series + refresh
        _cbRange.SelectedIndexChanged += (_, _) =>
        {
            _lastDays = -1; // ép rebuild geometry
            RefreshMetrics();
        };

        header.Controls.Add(_cbRange, 1, 0);

        // ✅ QUAN TRỌNG: nền phải trắng để Skia clear frame (rc6.1)
        var body = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.White
        };
        wrapper.Controls.Add(body, 0, 1);

        _trend = new CartesianChart
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,     // ✅ FIX GHOST LINE
            Margin = Padding.Empty
        };
        body.Controls.Add(_trend);

        _trendNoData = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "No data",
            ForeColor = AppColors.MutedText,
            Font = new Font("Segoe UI", 11, FontStyle.Italic),
            BackColor = Color.Transparent,
            Visible = false
        };
        body.Controls.Add(_trendNoData);
        _trendNoData.BringToFront();

        return card;
    }

    private Control BuildPieCard()
    {
        var card = new Guna2ShadowPanel
        {
            Radius = 16,
            FillColor = Color.White,
            ShadowColor = Color.FromArgb(210, 220, 235),
            ShadowDepth = 2,
            Dock = DockStyle.Fill,
            Padding = new Padding(16)
        };

        var wrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        wrapper.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        wrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(wrapper);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        wrapper.Controls.Add(header, 0, 0);

        var title = ControlFactory.Muted("By Violation Type", 10, true);
        title.AutoSize = false;
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleLeft;
        header.Controls.Add(title, 0, 0);

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.White
        };
        wrapper.Controls.Add(body, 0, 1);

        _pie = new PieChart
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White, // ✅ tránh ghost tương tự
            Margin = Padding.Empty
        };
        body.Controls.Add(_pie);

        _pieNoData = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "No data",
            ForeColor = AppColors.MutedText,
            Font = new Font("Segoe UI", 11, FontStyle.Italic),
            BackColor = Color.Transparent,
            Visible = false
        };
        body.Controls.Add(_pieNoData);
        _pieNoData.BringToFront();

        return card;
    }

    private void ConfigureCharts()
    {
        // ✅ rc6.1: tắt animation để tránh giữ frame cũ
        _trend.AnimationsSpeed = TimeSpan.Zero;
        _trend.EasingFunction = null;

        _pie.AnimationsSpeed = TimeSpan.Zero;
        _pie.EasingFunction = null;

        _trendXAxis.LabelsRotation = 0;
        _trendYAxis.MinLimit = 0;

        _trendXAxis.Labels = _trendLabels;

        _trend.XAxes = new[] { _trendXAxis };
        _trend.YAxes = new[] { _trendYAxis };

        _pie.Series = Array.Empty<ISeries>();
    }

    private void EnsureTrendSeries(int days)
    {
        if (_trendLine != null && _lastDays == days) return;

        _lastDays = days;

        // ✅ Rebuild series để bỏ hẳn geometry cũ (đổi 7/30)
        _trendLine = new LineSeries<double>
        {
            Values = _trendValues,
            GeometrySize = 10,
            LineSmoothness = 0, // giảm khả năng “uốn” lạ
            // Nếu bạn không muốn vùng tô xanh dưới đường line thì bật dòng này:
            // Fill = null
        };

        _trend.Series = new ISeries[] { _trendLine };
    }

    private void RefreshMetrics()
    {
        var days = _cbRange?.SelectedIndex == 1 ? 30 : 7;

        EnsureTrendSeries(days);

        var todayUtc = DateTime.UtcNow.Date;
        var fromUtc = todayUtc.AddDays(-(days - 1));
        var toUtc = todayUtc.AddDays(1); // exclusive end for today

        // ✅ materialize 1 lần để không query DB lại nhiều lần
        var items = _app.Violations.Query(fromUtc, toUtc, limit: 200000).ToList();

        var todayItems = items.Where(v => v.TimeUtc.ToUniversalTime().Date == todayUtc).ToList();

        _lblTotal.Text = todayItems.Count.ToString();

        var criticalToday = todayItems.Count(v => v.Level == ViolationLevel.Critical);
        _lblCritical.Text = criticalToday.ToString();

        var totalChecks = Math.Max(1, todayItems.Count + 50);
        var compliance = Math.Max(0, 100 - (int)Math.Round(todayItems.Count * 100.0 / totalChecks));
        _lblRate.Text = compliance + "%";

        var camCount = _app.Settings.Current.Cameras?.Count ?? 0;
        _lblActive.Text = $"{camCount}/{camCount}";

        // trend counts by day
        var dayCounts = items
            .GroupBy(v => v.TimeUtc.ToUniversalTime().Date)
            .ToDictionary(g => g.Key, g => g.Count());

        var daysList = Enumerable.Range(0, days)
            .Select(i => fromUtc.AddDays(i).Date)
            .ToList();

        // ✅ Clear/Add để chart không bị “dính” data cũ
        _trendValues.Clear();
        foreach (var d in daysList)
            _trendValues.Add(dayCounts.TryGetValue(d, out var c) ? c : 0);

        _trendLabels.Clear();
        foreach (var d in daysList)
            _trendLabels.Add(d.ToString("MM/dd"));

        _trendNoData.Visible = _trendValues.All(v => v <= 0);

        // ép repaint
        _trend.Refresh();

        // pie by type
        var byType = items.GroupBy(v => v.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        _pieNoData.Visible = byType.Count == 0;

        _pie.Series = byType.Select(x =>
            (ISeries)new PieSeries<double>
            {
                Values = new double[] { x.Count },
                Name = x.Type.ToString()
            }).ToArray();

        _pie.Refresh();
    }
}
