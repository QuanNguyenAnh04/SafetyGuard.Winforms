using System;
using System.Collections.Generic;
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

    private FlowLayoutPanel _root = null!;
    private Label _lblTotal = null!;
    private Label _lblCritical = null!;
    private Label _lblRate = null!;
    private Label _lblActive = null!;

    private ComboBox _cbRange = null!;
    private CartesianChart _trend = null!;
    private PieChart _pie = null!;
    private Label _trendNoData = null!;
    private Label _pieNoData = null!;

    private readonly Axis _trendXAxis = new();
    private readonly Axis _trendYAxis = new();

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
        _app.Violations.OnChanged += () =>
        {
            if (!IsHandleCreated) return;
            BeginInvoke(new Action(RefreshMetrics));
        };
    }

    private void BuildUi()
    {
        _root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(18),
            BackColor = AppColors.ContentBg
        };
        Controls.Add(_root);

        // KPI row
        var kpiRow = new TableLayoutPanel
        {
            ColumnCount = 4,
            RowCount = 1,
            Width = 1100,
            Height = 110
        };
        kpiRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        kpiRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        kpiRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        kpiRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        kpiRow.Controls.Add(BuildKpiCard("TOTAL VIOLATIONS (TODAY)", out _lblTotal), 0, 0);
        kpiRow.Controls.Add(BuildKpiCard("CRITICAL ALERTS", out _lblCritical), 1, 0);
        kpiRow.Controls.Add(BuildKpiCard("COMPLIANCE RATE", out _lblRate), 2, 0);
        kpiRow.Controls.Add(BuildKpiCard("ACTIVE CAMERAS", out _lblActive), 3, 0);

        _root.Controls.Add(kpiRow);

        // charts row
        var charts = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Width = 1100,
            Height = 360,
            Margin = new Padding(0, 14, 0, 0)
        };
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        charts.Controls.Add(BuildTrendCard(), 0, 0);
        charts.Controls.Add(BuildPieCard(), 1, 0);

        _root.Controls.Add(charts);

        ConfigureCharts();
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
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(16, 14, 16, 14)
        };

        card.Controls.Add(ControlFactory.Muted(title, 9, true));

        value = new Label
        {
            Text = "—",
            AutoSize = true,
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = AppColors.TitleText,
            Location = new Point(0, 34)
        };
        card.Controls.Add(value);

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
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(16)
        };

        var header = new Panel { Dock = DockStyle.Top, Height = 44 };
        card.Controls.Add(header);

        var title = ControlFactory.Muted("Violations Trend", 10, true);
        title.Location = new Point(6, 10);
        header.Controls.Add(title);

        _cbRange = new ComboBox
        {
            Width = 140,
            Height = 32,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cbRange.Items.AddRange(new object[] { "Last 7 Days", "Last 30 Days" });
        _cbRange.SelectedIndex = 0;
        _cbRange.SelectedIndexChanged += (_, _) => RefreshMetrics();
        header.Controls.Add(_cbRange);

        header.Resize += (_, _) => _cbRange.Location = new Point(header.Width - _cbRange.Width - 6, 8);
        _cbRange.Location = new Point(header.Width - _cbRange.Width - 6, 8);

        var body = new Panel { Dock = DockStyle.Fill };
        card.Controls.Add(body);

        _trend = new CartesianChart { Dock = DockStyle.Fill, BackColor = Color.Transparent };
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
            Margin = new Padding(0),
            Padding = new Padding(16)
        };

        var header = new Panel { Dock = DockStyle.Top, Height = 44 };
        card.Controls.Add(header);

        var title = ControlFactory.Muted("By Violation Type", 10, true);
        title.Location = new Point(6, 10);
        header.Controls.Add(title);

        var body = new Panel { Dock = DockStyle.Fill };
        card.Controls.Add(body);

        _pie = new PieChart { Dock = DockStyle.Fill, BackColor = Color.Transparent };
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
        _trendXAxis.LabelsRotation = 0;
        _trendYAxis.MinLimit = 0;

        _trend.XAxes = new[] { _trendXAxis };
        _trend.YAxes = new[] { _trendYAxis };

        _trend.Series = new ISeries[]
        {
            new LineSeries<double>
            {
                Values = new double[] { 0 },
                GeometrySize = 10
            }
        };

        _pie.Series = Array.Empty<ISeries>();
    }

    private void RefreshMetrics()
    {
        var days = _cbRange?.SelectedIndex == 1 ? 30 : 7;

        var todayUtc = DateTime.UtcNow.Date;
        var fromUtc = todayUtc.AddDays(-(days - 1));
        var toUtc = todayUtc.AddDays(1); // exclusive end for today

        // ✅ SQLite repo: dùng Query thay vì All()
        var items = _app.Violations.Query(fromUtc, toUtc, limit: 200000);

        var todayItems = items.Where(v => v.TimeUtc.ToUniversalTime().Date == todayUtc).ToList();

        _lblTotal.Text = todayItems.Count.ToString();

        var criticalToday = todayItems.Count(v => v.Level == ViolationLevel.Critical);
        _lblCritical.Text = criticalToday.ToString();

        // compliance rate demo: 100% - (violation/tổng giả lập)
        var totalChecks = Math.Max(1, todayItems.Count + 50);
        var compliance = Math.Max(0, 100 - (int)Math.Round(todayItems.Count * 100.0 / totalChecks));
        _lblRate.Text = compliance + "%";

        var camCount = _app.Settings.Current.Cameras?.Count ?? 0;
        _lblActive.Text = $"{camCount}/{camCount}";

        // trend counts by day
        var dayCounts = items
            .GroupBy(v => v.TimeUtc.ToUniversalTime().Date)
            .ToDictionary(g => g.Key, g => g.Count());

        var labels = Enumerable.Range(0, days)
            .Select(i => fromUtc.AddDays(i).Date)
            .ToList();

        var values = labels.Select(d => (double)(dayCounts.TryGetValue(d, out var c) ? c : 0)).ToArray();

        // Lấy series đầu tiên, ép kiểu an toàn
        var line = _trend.Series.FirstOrDefault() as LineSeries<double>;

        // Chỉ gán nếu lấy được series
        if (line != null)
        {
            line.Values = values;
        }

        _trendXAxis.Labels = labels.Select(d => d.ToString("MM/dd")).ToArray();

        _trendNoData.Visible = values.All(v => v <= 0);

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
    }
}
