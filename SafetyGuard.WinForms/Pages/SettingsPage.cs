using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using SafetyGuard.WinForms.Dialogs;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.UI;
using Timer = System.Windows.Forms.Timer;
namespace SafetyGuard.WinForms.Pages;

public sealed class SettingsPage : UserControl
{
    private readonly AppBootstrap _app;

    private readonly Panel _scroll = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        BackColor = Color.Transparent
    };

    private readonly FlowLayoutPanel _stack = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        WrapContents = false,
        FlowDirection = FlowDirection.TopDown,
        BackColor = Color.Transparent,
        Padding = new Padding(24, 16, 24, 24)
    };

    // sections
    private Guna2Panel _cardCameras = null!;
    private FlowLayoutPanel _cameraList = null!;

    private Guna2Panel _cardRules = null!;
    private TableLayoutPanel _rulesGrid = null!;

    private Guna2Panel _cardLogic = null!;
    private Guna2NumericUpDown _numMinFrames = null!;
    private Guna2NumericUpDown _numCooldown = null!;
    private Guna2ComboBox _cbRetention = null!;
    private Guna2TextBox _tbEvidenceRoot = null!;
    private Guna2ToggleSwitch _swSnapshot = null!;
    private Guna2ToggleSwitch _swClip = null!;
    private TableLayoutPanel _centerLayout = null!;


    // debounce save for sliders
    private readonly Timer _saveDebounce = new() { Interval = 350 };
    private Action? _pendingSave;

    public SettingsPage(AppBootstrap app)
    {
        _app = app;

        Dock = DockStyle.Fill;
        BackColor = AppColors.ContentBg;

        Controls.Add(_scroll);
        _centerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        // 50% - FIXED - 50%  (cột giữa sẽ set bằng FitCardsToWidth)
        _centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 980));
        _centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _centerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _scroll.Controls.Add(_centerLayout);

        // ✅ Quan trọng: stack KHÔNG được Dock=Top vào scroll nữa
        _stack.Dock = DockStyle.Top; // được, nhưng nằm trong cell giữa
        _stack.Margin = Padding.Empty;

        _centerLayout.Controls.Add(_stack, 1, 0);


        BuildHeader();
        BuildCameraManagement();
        BuildDetectionRules();
        BuildSystemLogic();

        // resize: keep cards centered + fixed max width like mock
        _scroll.Resize += (_, _) => FitCardsToWidth();
        FitCardsToWidth();

        EnsureDefaultsIfEmpty();
        ReloadAll();

        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            _pendingSave?.Invoke();
            _pendingSave = null;
        };
    }

    // =========================
    // Header (title + subtitle)
    // =========================
    private void BuildHeader()
    {
        var p = new Panel
        {
            BackColor = Color.Transparent,
            Height = 56,
            Width = 980,
            Margin = new Padding(0, 0, 0, 14)
        };

        var title = new Label
        {
            Text = "System Configuration",
            AutoSize = true,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = AppColors.TitleText,
            Location = new Point(0, 0)
        };

        var sub = new Label
        {
            Text = "Connected: 4 Cameras | System Status: Optimal",
            AutoSize = true,
            Font = new Font("Segoe UI", 10),
            ForeColor = AppColors.MutedText,
            Location = new Point(2, 32)
        };

        p.Controls.Add(title);
        p.Controls.Add(sub);

        _stack.Controls.Add(p);
    }

    // =========================
    // Camera Management (card)
    // =========================
    private void BuildCameraManagement()
    {
        _cardCameras = CreateCard();
        _cardCameras.Padding = new Padding(18, 16, 18, 16);
        _cardCameras.Margin = new Padding(0, 0, 0, 18);
        _stack.Controls.Add(_cardCameras);

        // header row
        var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Color.Transparent };
        _cardCameras.Controls.Add(header);

        header.Controls.Add(new Label
        {
            Text = "Camera Management",
            AutoSize = true,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = AppColors.TitleText,
            Location = new Point(0, 2)
        });

        header.Controls.Add(new Label
        {
            Text = "Configure RTSP streams and connection status.",
            AutoSize = true,
            Font = new Font("Segoe UI", 10),
            ForeColor = AppColors.MutedText,
            Location = new Point(0, 28)
        });

        var btnAdd = new Guna2Button
        {
            Text = "+  Add Camera",
            BorderRadius = 12,
            Size = new Size(132, 38),
            FillColor = AppColors.PrimaryBlue,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        header.Controls.Add(btnAdd);

        header.Resize += (_, _) => btnAdd.Location = new Point(header.Width - btnAdd.Width, 6);

        btnAdd.Click += (_, _) =>
        {
            using var dlg = new CameraEditDialog();
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var s = _app.Settings.Current;
            s.Cameras.Add(dlg.Camera);
            _app.Settings.Save(s);
            ReloadAll();
        };

        // ✅ listWrap: Dock TOP + AutoSize để card tính chiều cao đúng
        var listWrap = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 10, 0, 0)
        };
        _cardCameras.Controls.Add(listWrap);

        _cameraList = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.TopDown,
            BackColor = Color.Transparent
        };
        listWrap.Controls.Add(_cameraList);

        // ✅ khi card đổi size thì resize row theo inner width
        _cardCameras.Resize += (_, _) => ResizeCameraRows();
    }

    private void ResizeCameraRows()
    {
        if (_cardCameras == null || _cameraList == null) return;

        int inner = _cardCameras.Width - _cardCameras.Padding.Left - _cardCameras.Padding.Right;
        if (inner < 200) return;

        foreach (Control c in _cameraList.Controls)
        {
            c.Width = inner;
        }
    }


    private void ReloadCameras()
    {
        _cameraList.Controls.Clear();

        var cams = _app.Settings.Current.Cameras.ToList();

        // demo nếu chưa có
        if (cams.Count == 0)
        {
            cams.Add(new CameraConfig { Id = "CAM-01", Name = "CAM-01: Main Entrance", RtspUrl = "rtsp://192.168.1.10:554/stream1", Enabled = true });
            cams.Add(new CameraConfig { Id = "CAM-04", Name = "CAM-04: Back Alley", RtspUrl = "rtsp://192.168.1.14:554/stream1", Enabled = true });
        }

        foreach (var cam in cams)
        {
            var status = cam.Id.EndsWith("01", StringComparison.OrdinalIgnoreCase)
                ? CameraStatus.Connected
                : CameraStatus.Offline;

            var row = CreateCameraRow(cam, status);
            _cameraList.Controls.Add(row);
        }

        // ✅ sau khi add xong thì fit theo card
        ResizeCameraRows();

    }

    private Control CreateCameraRow(CameraConfig cam, CameraStatus status)
    {
        var row = new Guna2Panel
        {
            BorderRadius = 12,
            FillColor = Color.FromArgb(248, 250, 252),
            Height = 62,
            Width = 920,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(14, 10, 14, 10)
        };
        row.ShadowDecoration.Enabled = false;

        // icon box
        var iconBox = new Guna2Panel
        {
            BorderRadius = 10,
            FillColor = status == CameraStatus.Connected ? Color.FromArgb(232, 250, 241) : Color.FromArgb(255, 235, 235),
            Size = new Size(44, 44),
            Location = new Point(0, 4)
        };
        row.Controls.Add(iconBox);

        iconBox.Controls.Add(new Label
        {
            Text = status == CameraStatus.Connected ? "🎥" : "🚫",
            AutoSize = true,
            Font = new Font("Segoe UI", 14),
            Location = new Point(11, 9)
        });

        row.Controls.Add(new Label
        {
            Text = cam.Name,
            AutoSize = true,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = AppColors.TitleText,
            Location = new Point(58, 10)
        });

        row.Controls.Add(new Label
        {
            Text = cam.RtspUrl,
            AutoSize = true,
            Font = new Font("Consolas", 8),
            ForeColor = AppColors.MutedText,
            Location = new Point(58, 32)
        });

        // status badge
        var badge = new Guna2Button
        {
            Text = status == CameraStatus.Connected ? "Connected" : "Offline",
            BorderRadius = 10,
            Height = 26,
            Width = 96,
            FillColor = status == CameraStatus.Connected ? Color.FromArgb(232, 250, 241) : Color.FromArgb(255, 235, 235),
            ForeColor = status == CameraStatus.Connected ? Color.FromArgb(19, 120, 70) : Color.FromArgb(180, 40, 40),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Enabled = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        row.Controls.Add(badge);

        // action buttons
        var btnRefresh = IconBtn("⟳");
        var btnEdit = IconBtn("✎");
        var btnDel = IconBtn("🗑");

        row.Controls.Add(btnRefresh);
        row.Controls.Add(btnEdit);
        row.Controls.Add(btnDel);

        void LayoutRight()
        {
            btnDel.Location = new Point(row.Width - 14 - 28, 18);
            btnEdit.Location = new Point(row.Width - 14 - 28 - 30, 18);
            btnRefresh.Location = new Point(row.Width - 14 - 28 - 60, 18);
            badge.Location = new Point(row.Width - 14 - badge.Width - 92, 18);
        }
        row.Resize += (_, _) => LayoutRight();
        LayoutRight();

        btnRefresh.Click += (_, _) =>
        {
            MessageBox.Show(this, "TODO: Test connection snapshot.\nRealtime page will try to connect using RTSP URL.", "Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        btnEdit.Click += (_, _) =>
        {
            using var dlg = new CameraEditDialog(cam);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var s = _app.Settings.Current;
            var idx = s.Cameras.FindIndex(x => x.Id == cam.Id);
            if (idx >= 0) s.Cameras[idx] = dlg.Camera;
            _app.Settings.Save(s);
            ReloadAll();
        };

        btnDel.Click += (_, _) =>
        {
            if (MessageBox.Show(this, $"Delete {cam.Name}?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            var s = _app.Settings.Current;
            s.Cameras.RemoveAll(x => x.Id == cam.Id);
            _app.Settings.Save(s);
            ReloadAll();
        };
        btnDel.FillColor = Color.FromArgb(255, 235, 235);
        btnDel.ForeColor = Color.FromArgb(180, 40, 40);
        btnDel.HoverState.FillColor = Color.FromArgb(255, 220, 220);
        btnDel.HoverState.ForeColor = Color.FromArgb(160, 20, 20);


        return row;

        static Guna2Button IconBtn(string text)
        {
            var b = new Guna2Button
            {
                Text = text,
                BorderRadius = 8,
                Size = new Size(30, 30),

                // ✅ luôn có nền nhẹ để nhìn thấy vị trí nút
                FillColor = Color.FromArgb(242, 244, 248),
                ForeColor = Color.FromArgb(90, 100, 120),

                Font = new Font("Segoe UI Symbol", 11, FontStyle.Bold),
                TextOffset = new Point(0, -1),

                HoverState =
        {
            FillColor = Color.FromArgb(230, 235, 245),
            ForeColor = Color.FromArgb(50, 60, 80)
        },
                PressedColor = Color.FromArgb(220, 228, 242),

                Cursor = Cursors.Hand
            };

            // tắt border mặc định
            b.BorderThickness = 0;
            b.DisabledState.FillColor = b.FillColor;
            b.DisabledState.ForeColor = b.ForeColor;

            return b;
        }

    }

    // =========================
    // Detection Rules (card) + Add Rule tile
    // =========================
    private void BuildDetectionRules()
    {
        _cardRules = CreateCard();
        _cardRules.Padding = new Padding(18, 16, 18, 18);
        _cardRules.Margin = new Padding(0, 0, 0, 18);
        _stack.Controls.Add(_cardRules);

        _cardRules.Controls.Add(new Label
        {
            Text = "Detection Rules",
            AutoSize = true,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = AppColors.TitleText,
            Location = new Point(0, 0)
        });

        _cardRules.Controls.Add(new Label
        {
            Text = "Enable specific AI models and set sensitivity thresholds.",
            AutoSize = true,
            Font = new Font("Segoe UI", 10),
            ForeColor = AppColors.MutedText,
            Location = new Point(0, 26)
        });

        _rulesGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 54, 0, 0),
            BackColor = Color.Transparent
        };
        _rulesGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _rulesGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        _cardRules.Controls.Add(_rulesGrid);
    }

    private void ReloadRules()
    {
        // rebuild dynamic grid (rules + add tile)
        _rulesGrid.SuspendLayout();
        _rulesGrid.Controls.Clear();

        var rules = _app.Settings.Current.Rules.ToList();

        // +1 tile for Add Rule
        var tiles = rules.Count + 1;
        var rows = (int)Math.Ceiling(tiles / 2.0);
        _rulesGrid.RowStyles.Clear();
        _rulesGrid.RowCount = rows;

        for (int r = 0; r < rows; r++)
            _rulesGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));

        int i = 0;
        foreach (var rule in rules)
        {
            var tile = CreateRuleTile(rule);
            _rulesGrid.Controls.Add(tile, i % 2, i / 2);
            i++;
        }

        // Add Rule tile (always last)
        var addTile = CreateAddRuleTile();
        _rulesGrid.Controls.Add(addTile, i % 2, i / 2);

        _rulesGrid.ResumeLayout();
    }

    private Control CreateRuleTile(DetectionRule rule)
    {
        var p = new Guna2Panel
        {
            BorderRadius = 12,
            FillColor = Color.White,
            Height = 96,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 10, 12, 10),
            Padding = new Padding(14, 12, 14, 12)
        };
        p.ShadowDecoration.Enabled = false;

        var lbl = new Label
        {
            Text = rule.Type.ToString(),
            AutoSize = true,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = AppColors.TitleText,
            Location = new Point(0, 0)
        };
        p.Controls.Add(lbl);

        var sw = new Guna2ToggleSwitch
        {
            Checked = rule.Enabled,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        p.Controls.Add(sw);

        var btnDel = new Guna2Button
        {
            Text = "✕",
            Size = new Size(28, 28),
            BorderRadius = 8,
            FillColor = Color.Transparent,
            ForeColor = Color.FromArgb(120, 130, 150),
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            HoverState = { FillColor = Color.FromArgb(235, 238, 245) },
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        p.Controls.Add(btnDel);

        var lblSens = new Label
        {
            Text = $"Sensitivity  {(int)(rule.ConfidenceThreshold * 100)}%",
            AutoSize = true,
            Font = new Font("Segoe UI", 9),
            ForeColor = AppColors.MutedText,
            Location = new Point(0, 28)
        };
        p.Controls.Add(lblSens);

        var tb = new Guna2TrackBar
        {
            Minimum = 1,
            Maximum = 100,
            Value = Math.Max(1, Math.Min(100, (int)(rule.ConfidenceThreshold * 100))),
            Height = 20,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        p.Controls.Add(tb);

        void Layout()
        {
            sw.Location = new Point(p.Width - 46, 2);
            btnDel.Location = new Point(p.Width - 46 - 30, 0);
            tb.Location = new Point(0, 54);
            tb.Width = p.Width - 10;
        }
        p.Resize += (_, _) => Layout();
        Layout();

        // interactions
        sw.CheckedChanged += (_, _) =>
        {
            rule.Enabled = sw.Checked;
            SaveRule(rule);
            tb.Enabled = sw.Checked;
            lbl.ForeColor = sw.Checked ? AppColors.TitleText : AppColors.MutedText;
        };
        tb.Enabled = rule.Enabled;

        tb.ValueChanged += (_, _) =>
        {
            lblSens.Text = $"Sensitivity  {tb.Value}%";
            // debounce save while dragging
            Debounce(() =>
            {
                rule.ConfidenceThreshold = tb.Value / 100f;
                SaveRule(rule);
            });
        };

        btnDel.Click += (_, _) =>
        {
            if (MessageBox.Show(this, $"Delete rule '{rule.Type}'?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            var s = _app.Settings.Current;
            s.Rules.RemoveAll(r => Equals(r.Type, rule.Type));
            _app.Settings.Save(s);
            ReloadAll();
        };

        return p;
    }

    private Control CreateAddRuleTile()
    {
        var p = new Guna2Panel
        {
            BorderRadius = 12,
            FillColor = Color.FromArgb(248, 250, 252),
            Height = 96,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 10, 12, 10),
            Padding = new Padding(14, 12, 14, 12),
            Cursor = Cursors.Hand
        };
        p.ShadowDecoration.Enabled = false;

        var plus = new Label
        {
            Text = "+",
            AutoSize = true,
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = AppColors.PrimaryBlue,
            Location = new Point(2, 2)
        };
        p.Controls.Add(plus);

        var title = new Label
        {
            Text = "Add Rule",
            AutoSize = true,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = AppColors.TitleText,
            Location = new Point(44, 10)
        };
        p.Controls.Add(title);

        var sub = new Label
        {
            Text = "Create / enable a new detection rule.",
            AutoSize = true,
            Font = new Font("Segoe UI", 9),
            ForeColor = AppColors.MutedText,
            Location = new Point(44, 34)
        };
        p.Controls.Add(sub);

        void ClickAdd()
        {
            using var dlg = new AddRuleDialog(_app.Settings.Current.Rules);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var s = _app.Settings.Current;
            // nếu đã có thì update, chưa có thì add
            var idx = s.Rules.FindIndex(r => Equals(r.Type, dlg.Rule.Type));
            if (idx >= 0) s.Rules[idx] = dlg.Rule;
            else s.Rules.Add(dlg.Rule);

            _app.Settings.Save(s);
            ReloadAll();
        }

        p.Click += (_, _) => ClickAdd();
        plus.Click += (_, _) => ClickAdd();
        title.Click += (_, _) => ClickAdd();
        sub.Click += (_, _) => ClickAdd();

        return p;
    }

    private void SaveRule(DetectionRule rule)
    {
        var s = _app.Settings.Current;
        var idx = s.Rules.FindIndex(r => Equals(r.Type, rule.Type));
        if (idx >= 0) s.Rules[idx] = rule;
        _app.Settings.Save(s);
    }

    // =========================
    // System & Logic (card)
    // =========================
    private void BuildSystemLogic()
    {
        _cardLogic = CreateCard();
        _cardLogic.Padding = new Padding(18, 16, 18, 18);
        _cardLogic.Margin = new Padding(0, 0, 0, 0);
        _stack.Controls.Add(_cardLogic);

        _cardLogic.Controls.Add(new Label
        {
            Text = "System & Logic",
            AutoSize = true,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = AppColors.TitleText,
            Location = new Point(0, 0)
        });

        _cardLogic.Controls.Add(new Label
        {
            Text = "Anti-spam logic and storage retention.",
            AutoSize = true,
            Font = new Font("Segoe UI", 10),
            ForeColor = AppColors.MutedText,
            Location = new Point(0, 26)
        });

        // Grid 2 columns
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(0, 54, 0, 0),
            BackColor = Color.Transparent
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
        _cardLogic.Controls.Add(grid);

        // Anti-spam
        grid.Controls.Add(CreateLabeledNumeric("Anti-Spam Cooldown (Seconds)",
            "Prevents repeated alerts for the same violation.", out _numCooldown, 5, 1, 600), 0, 0);

        grid.Controls.Add(CreateLabeledNumeric("Min Consecutive Frames (N)",
            "Violation triggers only if it persists for N frames.", out _numMinFrames, 5, 1, 60), 1, 0);

        // Storage
        grid.Controls.Add(CreateStorageBlock(), 0, 1);
        grid.SetColumnSpan(grid.Controls[grid.Controls.Count - 1], 2);

        // Save on change (debounced)
        _numCooldown.ValueChanged += (_, _) => Debounce(SaveLogic);
        _numMinFrames.ValueChanged += (_, _) => Debounce(SaveLogic);
    }

    private Control CreateStorageBlock()
    {
        var p = new Guna2Panel
        {
            BorderRadius = 12,
            FillColor = Color.White,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 10, 0, 0),
            Padding = new Padding(14, 12, 14, 12)
        };
        p.ShadowDecoration.Enabled = false;

        var lbl1 = new Label
        {
            Text = "Evidence Path",
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = AppColors.TitleText,
            Location = new Point(0, 0)
        };
        p.Controls.Add(lbl1);

        _tbEvidenceRoot = new Guna2TextBox
        {
            BorderRadius = 10,
            Height = 38,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
        };
        p.Controls.Add(_tbEvidenceRoot);

        var btnBrowse = new Guna2Button
        {
            Text = "Browse",
            BorderRadius = 10,
            Height = 38,
            Width = 100,
            FillColor = Color.White,
            ForeColor = AppColors.TitleText,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        p.Controls.Add(btnBrowse);

        var lbl2 = new Label
        {
            Text = "Evidence Retention",
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = AppColors.TitleText
        };
        p.Controls.Add(lbl2);

        _cbRetention = new Guna2ComboBox
        {
            BorderRadius = 10,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Height = 38,
            Width = 160
        };
        _cbRetention.Items.AddRange(new object[] { "7 Days", "30 Days", "90 Days" });
        p.Controls.Add(_cbRetention);

        _swSnapshot = new Guna2ToggleSwitch { Checked = true };
        _swClip = new Guna2ToggleSwitch();

        var lblSnap = new Label
        {
            Text = "Save snapshot evidence",
            AutoSize = true,
            Font = new Font("Segoe UI", 9),
            ForeColor = AppColors.MutedText
        };
        var lblClip = new Label
        {
            Text = "Save short clip (optional)",
            AutoSize = true,
            Font = new Font("Segoe UI", 9),
            ForeColor = AppColors.MutedText
        };

        p.Controls.Add(_swSnapshot);
        p.Controls.Add(lblSnap);
        p.Controls.Add(_swClip);
        p.Controls.Add(lblClip);

        void Layout()
        {
            _tbEvidenceRoot.Location = new Point(0, 22);
            _tbEvidenceRoot.Width = p.Width - 120;

            btnBrowse.Location = new Point(p.Width - btnBrowse.Width, 22);

            lbl2.Location = new Point(0, 70);
            _cbRetention.Location = new Point(0, 92);

            _swSnapshot.Location = new Point(0, 136);
            lblSnap.Location = new Point(52, 136);

            _swClip.Location = new Point(0, 166);
            lblClip.Location = new Point(52, 166);
        }
        p.Resize += (_, _) => Layout();
        Layout();

        btnBrowse.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog(this) == DialogResult.OK)
            {
                _tbEvidenceRoot.Text = fbd.SelectedPath;
                Debounce(SaveLogic);
            }
        };

        _tbEvidenceRoot.TextChanged += (_, _) => Debounce(SaveLogic);
        _cbRetention.SelectedIndexChanged += (_, _) => Debounce(SaveLogic);
        _swSnapshot.CheckedChanged += (_, _) => Debounce(SaveLogic);
        _swClip.CheckedChanged += (_, _) => Debounce(SaveLogic);

        return p;
    }

    private Control CreateLabeledNumeric(string title, string hint, out Guna2NumericUpDown num, int def, int min, int max)
    {
        var p = new Guna2Panel
        {
            BorderRadius = 12,
            FillColor = Color.White,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 10, 12, 0),
            Padding = new Padding(14, 12, 14, 12)
        };
        p.ShadowDecoration.Enabled = false;

        p.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = AppColors.TitleText,
            Location = new Point(0, 0)
        });

        num = new Guna2NumericUpDown
        {
            BorderRadius = 10,
            Minimum = min,
            Maximum = max,
            Value = def,
            Size = new Size(220, 38),
            Location = new Point(0, 24)
        };
        p.Controls.Add(num);

        p.Controls.Add(new Label
        {
            Text = hint,
            AutoSize = true,
            Font = new Font("Segoe UI", 8),
            ForeColor = AppColors.MutedText,
            Location = new Point(0, 68)
        });

        return p;
    }

    private void SaveLogic()
    {
        var s = _app.Settings.Current;

        s.MinConsecutiveFrames = (int)_numMinFrames.Value;
        s.CooldownSeconds = (int)_numCooldown.Value;

        s.EvidenceRoot = _tbEvidenceRoot.Text.Trim();

        // retention parse
        var sel = _cbRetention.SelectedItem?.ToString() ?? "30 Days";
        s.RetentionDays = sel.StartsWith("7") ? 7 : sel.StartsWith("90") ? 90 : 30;

        s.SaveSnapshot = _swSnapshot.Checked;
        s.SaveShortClip = _swClip.Checked;

        _app.Settings.Save(s); // bạn đang save y hệt kiểu này :contentReference[oaicite:2]{index=2}
        _app.Violations.RemoveOlderThan(s.RetentionDays);
    }

    // =========================
    // Reload / defaults / layout helpers
    // =========================
    private void ReloadAll()
    {
        ReloadCameras();
        ReloadRules();
        LoadLogicFromSettings();
        FitCardsToWidth();
    }

    private void LoadLogicFromSettings()
    {
        var s = _app.Settings.Current;

        _numMinFrames.Value = Math.Max(_numMinFrames.Minimum, Math.Min(_numMinFrames.Maximum, s.MinConsecutiveFrames));
        _numCooldown.Value = Math.Max(_numCooldown.Minimum, Math.Min(_numCooldown.Maximum, s.CooldownSeconds));

        _tbEvidenceRoot.Text = s.EvidenceRoot;

        _cbRetention.SelectedItem = s.RetentionDays switch
        {
            7 => "7 Days",
            90 => "90 Days",
            _ => "30 Days"
        };

        _swSnapshot.Checked = s.SaveSnapshot;
        _swClip.Checked = s.SaveShortClip;
    }

    private void EnsureDefaultsIfEmpty()
    {
        var s = _app.Settings.Current;

        if (s.Rules == null) s.Rules = new List<DetectionRule>();

        if (s.Rules.Count == 0)
        {
            // tạo demo rules nếu trống
            s.Rules.Add(new DetectionRule { Type = ViolationType.NoHelmet, Enabled = true, ConfidenceThreshold = 0.75f });
            s.Rules.Add(new DetectionRule { Type = ViolationType.NoVest, Enabled = true, ConfidenceThreshold = 0.60f });
            s.Rules.Add(new DetectionRule { Type = ViolationType.Smoking, Enabled = false, ConfidenceThreshold = 0.50f });
            _app.Settings.Save(s);
        }
    }

    private void FitCardsToWidth()
    {
        if (_centerLayout == null) return;

        int max = 1100; // muốn rộng hơn thì tăng
        int usable = _scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8;

        int w = Math.Max(720, Math.Min(max, usable - 40));

        // ✅ set cột giữa
        _centerLayout.ColumnStyles[1].SizeType = SizeType.Absolute;
        _centerLayout.ColumnStyles[1].Width = w;

        // set width cho stack + cards
        _stack.Width = w;

        foreach (Control c in _stack.Controls)
        {
            if (c is Guna2Panel gp)
            {
                gp.Width = w;
                gp.MinimumSize = new Size(w, 0);
                gp.MaximumSize = new Size(w, 0);
            }
            else
            {
                c.Width = w;
            }
        }

        ResizeCameraRows();

        // refresh layout
        _centerLayout.PerformLayout();
    }




    private static Guna2Panel CreateCard()
    {
        var card = new Guna2Panel
        {
            BorderRadius = 16,
            FillColor = Color.White,
            Width = 980,

            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,

            // ✅ khóa width mặc định để không co lại
            MinimumSize = new Size(980, 0),
            MaximumSize = new Size(980, 0),
        };

        card.ShadowDecoration.Enabled = true;
        card.ShadowDecoration.Depth = 8;
        card.ShadowDecoration.BorderRadius = 16;
        card.ShadowDecoration.Shadow = new Padding(0, 4, 0, 8);

        return card;
    }


    private void Debounce(Action action)
    {
        _pendingSave = action;
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    // =========================
    // Add Rule dialog (simple)
    // =========================
    private sealed class AddRuleDialog : Form
    {
        private readonly Guna2ComboBox _cbType = new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BorderRadius = 10,
            Width = 260,
            Height = 36
        };

        private readonly Guna2TrackBar _tb = new()
        {
            Minimum = 1,
            Maximum = 100,
            Value = 60
        };

        private readonly Label _lbl = new()
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(110, 120, 140),
        };

        private readonly Guna2ToggleSwitch _sw = new() { Checked = true };

        public DetectionRule Rule { get; private set; } = new();

        public AddRuleDialog(List<DetectionRule> existing)
        {
            Text = "Add Rule";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(360, 220);

            var used = new HashSet<string>(existing.Select(r => r.Type.ToString()));

            // populate enum values not yet used
            var all = Enum.GetValues(typeof(ViolationType)).Cast<ViolationType>()
                .Select(v => v.ToString())
                .Where(n => !used.Contains(n))
                .ToList();

            if (all.Count == 0) all.AddRange(Enum.GetValues(typeof(ViolationType)).Cast<ViolationType>().Select(v => v.ToString()));

            _cbType.Items.AddRange(all.Cast<object>().ToArray());
            _cbType.SelectedIndex = 0;

            var lblType = new Label { Text = "Violation Type", AutoSize = true, Location = new Point(18, 16) };
            Controls.Add(lblType);
            _cbType.Location = new Point(18, 36);
            Controls.Add(_cbType);

            var lblEn = new Label { Text = "Enabled", AutoSize = true, Location = new Point(18, 78) };
            Controls.Add(lblEn);
            _sw.Location = new Point(80, 76);
            Controls.Add(_sw);

            var lblTh = new Label { Text = "Sensitivity", AutoSize = true, Location = new Point(18, 110) };
            Controls.Add(lblTh);

            _lbl.Location = new Point(100, 110);
            Controls.Add(_lbl);

            _tb.Location = new Point(18, 132);
            _tb.Width = 310;
            Controls.Add(_tb);

            var btnOk = new Guna2Button
            {
                Text = "Add",
                BorderRadius = 10,
                FillColor = AppColors.PrimaryBlue,
                ForeColor = Color.White,
                Size = new Size(88, 36),
                Location = new Point(240, 172)
            };
            Controls.Add(btnOk);

            var btnCancel = new Guna2Button
            {
                Text = "Cancel",
                BorderRadius = 10,
                FillColor = Color.White,
                ForeColor = AppColors.TitleText,
                Size = new Size(88, 36),
                Location = new Point(144, 172)
            };
            Controls.Add(btnCancel);

            void UpdateLbl() => _lbl.Text = $"{_tb.Value}%";
            _tb.ValueChanged += (_, _) => UpdateLbl();
            UpdateLbl();

            btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            btnOk.Click += (_, _) =>
            {
                var typeName = _cbType.SelectedItem?.ToString() ?? "NoHelmet";
                var type = (ViolationType)Enum.Parse(typeof(ViolationType), typeName);

                Rule = new DetectionRule
                {
                    Type = type,
                    Enabled = _sw.Checked,
                    ConfidenceThreshold = _tb.Value / 100f
                };

                DialogResult = DialogResult.OK;
                Close();
            };
        }
    }
}
