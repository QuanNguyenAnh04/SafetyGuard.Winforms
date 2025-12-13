using System.Drawing;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.UI;

namespace SafetyGuard.WinForms.Dialogs;

public sealed class CameraEditDialog : Form
{
    public CameraConfig Camera { get; private set; }

    private readonly Guna2TextBox _tbName = new() { BorderRadius = 10, PlaceholderText = "Camera Name" };
    private readonly Guna2TextBox _tbUrl = new() { BorderRadius = 10, PlaceholderText = "RTSP URL" };
    private readonly Guna2ToggleSwitch _swEnabled = new() { Checked = true };

    public CameraEditDialog(CameraConfig? existing = null)
    {
        Camera = existing != null
            ? new CameraConfig { Id = existing.Id, Name = existing.Name, RtspUrl = existing.RtspUrl, Enabled = existing.Enabled }
            : new CameraConfig();

        Text = existing == null ? "Add Camera" : "Edit Camera";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(520, 260);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        BackColor = AppColors.ContentBg;

        var card = ControlFactory.Card();
        card.Dock = DockStyle.Fill;
        Controls.Add(card);

        var lbl1 = ControlFactory.Muted("Name", 9, true);
        lbl1.Location = new Point(18, 18);
        card.Controls.Add(lbl1);
        _tbName.Location = new Point(18, 38);
        _tbName.Size = new Size(460, 38);
        card.Controls.Add(_tbName);

        var lbl2 = ControlFactory.Muted("RTSP URL", 9, true);
        lbl2.Location = new Point(18, 84);
        card.Controls.Add(lbl2);
        _tbUrl.Location = new Point(18, 104);
        _tbUrl.Size = new Size(460, 38);
        card.Controls.Add(_tbUrl);

        var lbl3 = ControlFactory.Muted("Enabled", 9, true);
        lbl3.Location = new Point(18, 150);
        card.Controls.Add(lbl3);
        _swEnabled.Location = new Point(90, 150);
        card.Controls.Add(_swEnabled);

        var btnOk = new Guna2Button { Text = "Save", BorderRadius = 10, FillColor = AppColors.PrimaryBlue, ForeColor = Color.White };
        btnOk.Location = new Point(286, 184);
        btnOk.Size = new Size(92, 36);
        btnOk.Click += (_, _) => { SaveAndClose(); };
        card.Controls.Add(btnOk);

        var btnCancel = new Guna2Button { Text = "Cancel", BorderRadius = 10, FillColor = Color.White, ForeColor = AppColors.TitleText };
        btnCancel.Location = new Point(386, 184);
        btnCancel.Size = new Size(92, 36);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        card.Controls.Add(btnCancel);

        _tbName.Text = Camera.Name;
        _tbUrl.Text = Camera.RtspUrl;
        _swEnabled.Checked = Camera.Enabled;
    }

    private void SaveAndClose()
    {
        Camera.Name = _tbName.Text.Trim();
        Camera.RtspUrl = _tbUrl.Text.Trim();
        Camera.Enabled = _swEnabled.Checked;

        DialogResult = DialogResult.OK;
        Close();
    }
}
