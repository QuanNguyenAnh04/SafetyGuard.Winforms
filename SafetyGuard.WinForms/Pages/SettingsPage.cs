using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using SafetyGuard.WinForms.Dialogs;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.UI;

namespace SafetyGuard.WinForms.Pages;

public sealed class SettingsPage : UserControl
{
    private readonly AppBootstrap _app;

    private readonly ListBox _lstCams = new() { Dock = DockStyle.Left, Width = 320 };
    private readonly ListBox _lstRules = new() { Dock = DockStyle.Left, Width = 320 };

    private readonly Label _lblBuild = ControlFactory.Muted("", 9);

    public SettingsPage(AppBootstrap app)
    {
        _app = app;
        Dock = DockStyle.Fill;
        BackColor = AppColors.ContentBg;

        BuildUI();
        Reload();
    }

    private void BuildUI()
    {
        var card = ControlFactory.Card();
        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(16);
        Controls.Add(card);

        var tabs = new Guna2TabControl
        {
            Dock = DockStyle.Fill,
            ItemSize = new Size(160, 40)
        };
        card.Controls.Add(tabs);

        // Camera Settings
        var tpCam = new TabPage("Camera Settings") { BackColor = AppColors.ContentBg };
        tabs.TabPages.Add(tpCam);

        // Detection Settings
        var tpDet = new TabPage("Detection Settings") { BackColor = AppColors.ContentBg };
        tabs.TabPages.Add(tpDet);

        // Notifications
        var tpNoti = new TabPage("Notification") { BackColor = AppColors.ContentBg };
        tabs.TabPages.Add(tpNoti);

        // Storage
        var tpStore = new TabPage("Storage") { BackColor = AppColors.ContentBg };
        tabs.TabPages.Add(tpStore);

        // About / Logs
        var tpAbout = new TabPage("System") { BackColor = AppColors.ContentBg };
        tabs.TabPages.Add(tpAbout);

        BuildCameraTab(tpCam);
        BuildDetectionTab(tpDet);
        BuildNotificationTab(tpNoti);
        BuildStorageTab(tpStore);
        BuildSystemTab(tpAbout);
    }

    private void BuildCameraTab(TabPage tp)
    {
        var wrap = ControlFactory.Card();
        wrap.Dock = DockStyle.Fill;
        tp.Controls.Add(wrap);

        _lstCams.Dock = DockStyle.Left;
        wrap.Controls.Add(_lstCams);

        var right = new Panel { Dock = DockStyle.Fill };
        wrap.Controls.Add(right);

        var btnAdd = new Guna2Button { Text = "Add", BorderRadius = 10, FillColor = AppColors.PrimaryBlue, ForeColor = Color.White };
        btnAdd.Location = new Point(16, 16); btnAdd.Size = new Size(88, 36);
        right.Controls.Add(btnAdd);

        var btnEdit = new Guna2Button { Text = "Edit", BorderRadius = 10, FillColor = Color.White, ForeColor = AppColors.TitleText };
        btnEdit.Location = new Point(112, 16); btnEdit.Size = new Size(88, 36);
        right.Controls.Add(btnEdit);

        var btnDel = new Guna2Button { Text = "Delete", BorderRadius = 10, FillColor = AppColors.BadRed, ForeColor = Color.White };
        btnDel.Location = new Point(208, 16); btnDel.Size = new Size(88, 36);
        right.Controls.Add(btnDel);

        var btnTest = new Guna2Button { Text = "Test Snapshot", BorderRadius = 10, FillColor = AppColors.GoodGreen, ForeColor = Color.White };
        btnTest.Location = new Point(304, 16); btnTest.Size = new Size(130, 36);
        right.Controls.Add(btnTest);

        btnAdd.Click += (_, _) =>
        {
            using var dlg = new CameraEditDialog();
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var s = _app.Settings.Current;
            s.Cameras.Add(dlg.Camera);
            _app.Settings.Save(s);
            Reload();
        };

        btnEdit.Click += (_, _) =>
        {
            var cam = SelectedCam();
            if (cam == null) return;

            using var dlg = new CameraEditDialog(cam);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var s = _app.Settings.Current;
            var idx = s.Cameras.FindIndex(x => x.Id == cam.Id);
            if (idx >= 0) s.Cameras[idx] = dlg.Camera;
            _app.Settings.Save(s);
            Reload();
        };

        btnDel.Click += (_, _) =>
        {
            var cam = SelectedCam();
            if (cam == null) return;

            var s = _app.Settings.Current;
            s.Cameras.RemoveAll(x => x.Id == cam.Id);
            _app.Settings.Save(s);
            Reload();
        };

        btnTest.Click += (_, _) =>
        {
            // For demo: just show message. You can implement a real one-shot capture using RtspFrameSource.
            var cam = SelectedCam();
            if (cam == null) return;
            MessageBox.Show(this, "Test snapshot is a demo hook.\nRealtime page will try to connect using RTSP URL.", "Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
    }

    private void BuildDetectionTab(TabPage tp)
    {
        var wrap = ControlFactory.Card();
        wrap.Dock = DockStyle.Fill;
        tp.Controls.Add(wrap);

        _lstRules.Dock = DockStyle.Left;
        wrap.Controls.Add(_lstRules);

        var right = new Panel { Dock = DockStyle.Fill };
        wrap.Controls.Add(right);

        var swEnabled = new Guna2ToggleSwitch { Location = new Point(16, 18) };
        right.Controls.Add(swEnabled);

        var lblEnabled = ControlFactory.Muted("Enable rule", 9, true);
        lblEnabled.Location = new Point(68, 18);
        right.Controls.Add(lblEnabled);

        var lblTh = ControlFactory.Muted("Confidence Threshold", 9, true);
        lblTh.Location = new Point(16, 60);
        right.Controls.Add(lblTh);

        var tbTh = new Guna2TrackBar { Location = new Point(16, 84), Width = 320, Minimum = 0, Maximum = 100, Value = 45 };
        right.Controls.Add(tbTh);

        var lblThVal = ControlFactory.Muted("0.45", 9, true);
        lblThVal.Location = new Point(346, 84);
        right.Controls.Add(lblThVal);

        var lblMinF = ControlFactory.Muted("Min consecutive frames (N)", 9, true);
        lblMinF.Location = new Point(16, 140);
        right.Controls.Add(lblMinF);

        var numMinF = new NumericUpDown { Location = new Point(16, 164), Width = 120, Minimum = 1, Maximum = 60 };
        right.Controls.Add(numMinF);

        var lblCd = ControlFactory.Muted("Cooldown seconds (X)", 9, true);
        lblCd.Location = new Point(160, 140);
        right.Controls.Add(lblCd);

        var numCd = new NumericUpDown { Location = new Point(160, 164), Width = 120, Minimum = 1, Maximum = 600 };
        right.Controls.Add(numCd);

        var btnSave = new Guna2Button { Text = "Save", BorderRadius = 10, FillColor = AppColors.PrimaryBlue, ForeColor = Color.White };
        btnSave.Location = new Point(16, 220);
        btnSave.Size = new Size(92, 36);
        right.Controls.Add(btnSave);

        void LoadSelected()
        {
            var rule = SelectedRule();
            if (rule == null) return;

            swEnabled.Checked = rule.Enabled;
            tbTh.Value = (int)(rule.ConfidenceThreshold * 100);
            lblThVal.Text = rule.ConfidenceThreshold.ToString("0.00");

            numMinF.Value = _app.Settings.Current.MinConsecutiveFrames;
            numCd.Value = _app.Settings.Current.CooldownSeconds;
        }

        _lstRules.SelectedIndexChanged += (_, _) => LoadSelected();
        tbTh.ValueChanged += (_, _) =>
        {
            lblThVal.Text = (tbTh.Value / 100.0).ToString("0.00");
        };

        btnSave.Click += (_, _) =>
        {
            var s = _app.Settings.Current;
            var rule = SelectedRule();
            if (rule == null) return;

            var idx = s.Rules.FindIndex(r => r.Type == rule.Type);
            if (idx < 0) return;

            s.Rules[idx].Enabled = swEnabled.Checked;
            s.Rules[idx].ConfidenceThreshold = tbTh.Value / 100f;

            s.MinConsecutiveFrames = (int)numMinF.Value;
            s.CooldownSeconds = (int)numCd.Value;

            _app.Settings.Save(s);
            Reload();
        };
    }

    private void BuildNotificationTab(TabPage tp)
    {
        var wrap = ControlFactory.Card();
        wrap.Dock = DockStyle.Fill;
        tp.Controls.Add(wrap);

        var sw = new Guna2ToggleSwitch { Location = new Point(18, 24) };
        wrap.Controls.Add(sw);

        var lbl = ControlFactory.Muted("Enable notifications (demo)", 10, true);
        lbl.Location = new Point(70, 24);
        wrap.Controls.Add(lbl);

        var btnSave = new Guna2Button { Text = "Save", BorderRadius = 10, FillColor = AppColors.PrimaryBlue, ForeColor = Color.White };
        btnSave.Location = new Point(18, 70);
        btnSave.Size = new Size(92, 36);
        wrap.Controls.Add(btnSave);

        sw.Checked = _app.Settings.Current.EnableNotifications;

        btnSave.Click += (_, _) =>
        {
            var s = _app.Settings.Current;
            s.EnableNotifications = sw.Checked;
            _app.Settings.Save(s);
        };
    }

    private void BuildStorageTab(TabPage tp)
    {
        var wrap = ControlFactory.Card();
        wrap.Dock = DockStyle.Fill;
        tp.Controls.Add(wrap);

        var lblPath = ControlFactory.Muted("Evidence path", 9, true);
        lblPath.Location = new Point(18, 18);
        wrap.Controls.Add(lblPath);

        var tb = new Guna2TextBox { BorderRadius = 10, Location = new Point(18, 38), Size = new Size(560, 38) };
        wrap.Controls.Add(tb);

        var btnBrowse = new Guna2Button { Text = "Browse", BorderRadius = 10, FillColor = Color.White, ForeColor = AppColors.TitleText };
        btnBrowse.Location = new Point(586, 38); btnBrowse.Size = new Size(100, 38);
        wrap.Controls.Add(btnBrowse);

        var lblRet = ControlFactory.Muted("Retention days (7 / 30)", 9, true);
        lblRet.Location = new Point(18, 92);
        wrap.Controls.Add(lblRet);

        var cb = new Guna2ComboBox { BorderRadius = 10, Location = new Point(18, 112), Size = new Size(120, 38), DrawMode = DrawMode.OwnerDrawFixed, DropDownStyle = ComboBoxStyle.DropDownList };
        cb.Items.AddRange(new object[] { "7", "30" });
        wrap.Controls.Add(cb);

        var swSnap = new Guna2ToggleSwitch { Location = new Point(18, 170), Checked = true };
        wrap.Controls.Add(swSnap);
        var lblSnap = ControlFactory.Muted("Save snapshot evidence", 10, true);
        lblSnap.Location = new Point(70, 170);
        wrap.Controls.Add(lblSnap);

        var swClip = new Guna2ToggleSwitch { Location = new Point(18, 210) };
        wrap.Controls.Add(swClip);
        var lblClip = ControlFactory.Muted("Save short clip (optional)", 10, true);
        lblClip.Location = new Point(70, 210);
        wrap.Controls.Add(lblClip);

        var btnSave = new Guna2Button { Text = "Save", BorderRadius = 10, FillColor = AppColors.PrimaryBlue, ForeColor = Color.White };
        btnSave.Location = new Point(18, 260); btnSave.Size = new Size(92, 36);
        wrap.Controls.Add(btnSave);

        // load
        tb.Text = _app.Settings.Current.EvidenceRoot;
        cb.SelectedItem = _app.Settings.Current.RetentionDays.ToString();
        swSnap.Checked = _app.Settings.Current.SaveSnapshot;
        swClip.Checked = _app.Settings.Current.SaveShortClip;

        btnBrowse.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog(this) == DialogResult.OK)
                tb.Text = fbd.SelectedPath;
        };

        btnSave.Click += (_, _) =>
        {
            var s = _app.Settings.Current;
            s.EvidenceRoot = tb.Text.Trim();
            s.RetentionDays = int.TryParse(cb.SelectedItem?.ToString(), out var d) ? d : 30;
            s.SaveSnapshot = swSnap.Checked;
            s.SaveShortClip = swClip.Checked;
            _app.Settings.Save(s);

            // cleanup old
            _app.Violations.RemoveOlderThan(s.RetentionDays);
        };
    }

    private void BuildSystemTab(TabPage tp)
    {
        var wrap = ControlFactory.Card();
        wrap.Dock = DockStyle.Fill;
        tp.Controls.Add(wrap);

        var title = ControlFactory.Title("System & Polish", 14, true);
        title.Location = new Point(18, 18);
        wrap.Controls.Add(title);

        _lblBuild.Text = UI.UiHelpers.BuildInfo();
        _lblBuild.Location = new Point(18, 50);
        wrap.Controls.Add(_lblBuild);

        var lblDet = ControlFactory.Muted("Detector: " + _app.Detector.Name, 9, true);
        lblDet.Location = new Point(18, 76);
        wrap.Controls.Add(lblDet);

        var lblLog = ControlFactory.Muted("System log (last 200 lines)", 9, true);
        lblLog.Location = new Point(18, 110);
        wrap.Controls.Add(lblLog);

        var tb = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(18, 130),
            Size = new Size(900, 360),
            ReadOnly = true
        };
        wrap.Controls.Add(tb);

        void RefreshLog()
        {
            var lines = _app.Logs.SnapshotRing().TakeLast(200);
            tb.Text = string.Join(Environment.NewLine, lines);
            tb.SelectionStart = tb.TextLength;
            tb.ScrollToCaret();
        }

        RefreshLog();
        _app.Logs.OnLog += _ => this.SafeInvoke(RefreshLog);
    }

    private void Reload()
    {
        _lstCams.Items.Clear();
        foreach (var c in _app.Settings.Current.Cameras)
            _lstCams.Items.Add($"{(c.Enabled ? "●" : "○")} {c.Name}");

        _lstRules.Items.Clear();
        foreach (var r in _app.Settings.Current.Rules)
            _lstRules.Items.Add($"{(r.Enabled ? "●" : "○")} {r.Type}  (th={r.ConfidenceThreshold:0.00})");

        if (_lstCams.Items.Count > 0) _lstCams.SelectedIndex = 0;
        if (_lstRules.Items.Count > 0) _lstRules.SelectedIndex = 0;
    }

    private CameraConfig? SelectedCam()
    {
        var idx = _lstCams.SelectedIndex;
        if (idx < 0 || idx >= _app.Settings.Current.Cameras.Count) return null;
        return _app.Settings.Current.Cameras[idx];
    }

    private DetectionRule? SelectedRule()
    {
        var idx = _lstRules.SelectedIndex;
        if (idx < 0 || idx >= _app.Settings.Current.Rules.Count) return null;
        return _app.Settings.Current.Rules[idx];
    }
}
