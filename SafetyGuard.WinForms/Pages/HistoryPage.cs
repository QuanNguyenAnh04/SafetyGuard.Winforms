using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using SafetyGuard.WinForms.Dialogs;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.Services;
using SafetyGuard.WinForms.UI;

namespace SafetyGuard.WinForms.Pages;

public sealed class HistoryPage : UserControl
{
    private readonly AppBootstrap _app;

    private readonly Guna2DataGridView _grid = new() { Dock = DockStyle.Fill };
    private List<ViolationRecord> _current = new();

    private readonly Guna2TextBox _search = new() { BorderRadius = 10, PlaceholderText = "Search camera/type..." };
    private readonly Guna2ComboBox _cbType = new() { BorderRadius = 10, DrawMode = DrawMode.OwnerDrawFixed, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Guna2ComboBox _cbStatus = new() { BorderRadius = 10, DrawMode = DrawMode.OwnerDrawFixed, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Guna2DateTimePicker _from = new() { BorderRadius = 10, Format = DateTimePickerFormat.Short };
    private readonly Guna2DateTimePicker _to = new() { BorderRadius = 10, Format = DateTimePickerFormat.Short };

    public HistoryPage(AppBootstrap app)
    {
        _app = app;
        Dock = DockStyle.Fill;
        BackColor = AppColors.ContentBg;

        BuildUI();
        RefreshData();

        _app.Violations.OnChanged += () => this.SafeInvoke(RefreshData);
    }

    private void BuildUI()
    {
        var root = new Panel { Dock = DockStyle.Fill };
        Controls.Add(root);

        var card = ControlFactory.Card();
        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(5);
        
        root.Controls.Add(card);

        SetupGrid();
        card.Controls.Add(_grid);

        // Filter bar
        var bar = new Panel { Dock = DockStyle.Top, Height = 50, };
        card.Controls.Add(bar);

        _search.Location = new Point(6, 8);
        _search.Size = new Size(220, 38);
        bar.Controls.Add(_search);

        _cbType.Location = new Point(236, 8);
        _cbType.Size = new Size(160, 38);
        _cbType.Items.Add("All Types");
        _cbType.Items.AddRange(Enum.GetNames(typeof(ViolationType)));
        _cbType.SelectedIndex = 0;
        bar.Controls.Add(_cbType);

        _cbStatus.Location = new Point(404, 8);
        _cbStatus.Size = new Size(160, 38);
        _cbStatus.Items.Add("All Status");
        _cbStatus.Items.AddRange(Enum.GetNames(typeof(ViolationStatus)));
        _cbStatus.SelectedIndex = 0;
        bar.Controls.Add(_cbStatus);

        _from.Location = new Point(572, 8);
        _from.Size = new Size(120, 38);
        _from.Value = DateTime.Now.AddDays(-7);
        bar.Controls.Add(_from);

        _to.Location = new Point(700, 8);
        _to.Size = new Size(120, 38);
        _to.Value = DateTime.Now;
        bar.Controls.Add(_to);

        var btnApply = new Guna2Button { Text = "Apply", BorderRadius = 10, FillColor = AppColors.PrimaryBlue, ForeColor = Color.White };
        btnApply.Location = new Point(828, 8);
        btnApply.Size = new Size(88, 38);
        btnApply.Click += (_, _) => RefreshData();
        bar.Controls.Add(btnApply);

        var btnCsv = new Guna2Button { Text = "Export CSV", BorderRadius = 10, FillColor = Color.White, ForeColor = AppColors.TitleText };
        btnCsv.Location = new Point(924, 8);
        btnCsv.Size = new Size(110, 38);
        btnCsv.Click += (_, _) => Export(false);
        bar.Controls.Add(btnCsv);

        var btnXlsx = new Guna2Button { Text = "Export Excel", BorderRadius = 10, FillColor = Color.White, ForeColor = AppColors.TitleText };
        btnXlsx.Location = new Point(1040, 8);
        btnXlsx.Size = new Size(120, 38);
        btnXlsx.Click += (_, _) => Export(true);
        bar.Controls.Add(btnXlsx);

        // Grid
        


        _search.TextChanged += (_, _) => RefreshData();
        _cbType.SelectedIndexChanged += (_, _) => RefreshData();
        _cbStatus.SelectedIndexChanged += (_, _) => RefreshData();
    }

    private void SetupGrid()
    {
        _grid.ThemeStyle.AlternatingRowsStyle.BackColor = Color.White;
        _grid.ThemeStyle.BackColor = Color.White;
        _grid.ThemeStyle.GridColor = Color.FromArgb(235, 238, 245);
        _grid.ThemeStyle.HeaderStyle.BackColor = Color.White;
        _grid.ThemeStyle.HeaderStyle.ForeColor = AppColors.TitleText;
        _grid.ThemeStyle.HeaderStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _grid.ThemeStyle.RowsStyle.ForeColor = AppColors.TitleText;
        _grid.ThemeStyle.RowsStyle.Font = new Font("Segoe UI", 9);
        _grid.ThemeStyle.RowsStyle.Height = 48;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoGenerateColumns = false;

        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Time", DataPropertyName = "TimeUtc", Width = 180 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Camera", DataPropertyName = "CameraName", Width = 170 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Violation Type", DataPropertyName = "Type", Width = 140 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Level", DataPropertyName = "Level", Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Status", DataPropertyName = "Status", Width = 130 });
        _grid.Columns.Add(new DataGridViewButtonColumn { HeaderText = "Action", Text = "View Evidence", UseColumnTextForButtonValue = true, Width = 130 });

        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var row = _grid.Rows[e.RowIndex].DataBoundItem as ViolationRecord;
            if (row == null) return;

            if (_grid.Columns[e.ColumnIndex].HeaderText == "Time")
            {
                e.Value = row.TimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                e.FormattingApplied = true;
            }
        };

        _grid.CellContentClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            if (_grid.Columns[e.ColumnIndex] is DataGridViewButtonColumn)
            {
                var v = _grid.Rows[e.RowIndex].DataBoundItem as ViolationRecord;
                if (v == null) return;

                using var dlg = new ViolationEvidenceDialog(v);
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.NewStatus.HasValue)
                {
                    _app.Violations.UpdateStatus(v.Id, dlg.NewStatus.Value);
                }
            }
        };
    }

    private void RefreshData()
    {
        var all = _app.Violations.All();

        var q = all.AsEnumerable();

        var from = _from.Value.Date.ToUniversalTime();
        var to = _to.Value.Date.AddDays(1).ToUniversalTime();

        q = q.Where(v => v.TimeUtc >= from && v.TimeUtc < to);

        var s = _search.Text.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(s))
            q = q.Where(v => v.CameraName.ToLowerInvariant().Contains(s) || v.Type.ToString().ToLowerInvariant().Contains(s));

        if (_cbType.SelectedIndex > 0 && Enum.TryParse<ViolationType>(_cbType.SelectedItem!.ToString(), out var t))
            q = q.Where(v => v.Type == t);

        if (_cbStatus.SelectedIndex > 0 && Enum.TryParse<ViolationStatus>(_cbStatus.SelectedItem!.ToString(), out var st))
            q = q.Where(v => v.Status == st);

        _current = q.OrderByDescending(v => v.TimeUtc).ToList();
        _grid.DataSource = _current;
    }

    private void Export(bool excel)
    {
        using var sfd = new SaveFileDialog
        {
            Filter = excel ? "Excel (*.xlsx)|*.xlsx" : "CSV (*.csv)|*.csv",
            FileName = excel ? "violations.xlsx" : "violations.csv"
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        if (excel) _app.Export.ExportExcel(sfd.FileName, _current);
        else _app.Export.ExportCsv(sfd.FileName, _current);

        MessageBox.Show(this, "Export done!", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
