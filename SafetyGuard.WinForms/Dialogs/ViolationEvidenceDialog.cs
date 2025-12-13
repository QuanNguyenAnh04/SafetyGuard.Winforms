using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.UI;

namespace SafetyGuard.WinForms.Dialogs;

public sealed class ViolationEvidenceDialog : Form
{
    public ViolationStatus? NewStatus { get; private set; }

    public ViolationEvidenceDialog(ViolationRecord v)
    {
        Text = "Violation Evidence";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 600);
        BackColor = AppColors.ContentBg;

        var card = ControlFactory.Card();
        card.Dock = DockStyle.Fill;
        Controls.Add(card);

        var title = ControlFactory.Title($"{v.CameraName} • {v.Type}", 14, true);
        title.Location = new Point(16, 16);
        card.Controls.Add(title);

        var meta = ControlFactory.Muted($"Time(UTC): {v.TimeUtc:O}  |  Confidence: {v.Confidence:0.00}  |  Level: {v.Level}  |  Status: {v.Status}", 9);
        meta.Location = new Point(18, 46);
        card.Controls.Add(meta);

        var pic = new PictureBox { Location = new Point(16, 76), Size = new Size(840, 420), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
        card.Controls.Add(pic);

        if (!string.IsNullOrWhiteSpace(v.SnapshotPath) && File.Exists(v.SnapshotPath))
        {
            using var img = Image.FromFile(v.SnapshotPath);
            pic.Image = (Image)img.Clone();
        }
        else
        {
            var lbl = new Label { Text = "No snapshot evidence.", ForeColor = Color.White, BackColor = Color.FromArgb(120, 0, 0, 0), AutoSize = true, Padding = new Padding(6) };
            lbl.Location = new Point(20, 20);
            pic.Controls.Add(lbl);
        }

        var cb = new Guna2ComboBox
        {
            Location = new Point(16, 510),
            Size = new Size(240, 36),
            BorderRadius = 10,
            DrawMode = DrawMode.OwnerDrawFixed,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cb.Items.AddRange(Enum.GetNames(typeof(ViolationStatus)));
        cb.SelectedItem = v.Status.ToString();
        card.Controls.Add(cb);

        var btnApply = new Guna2Button { Text = "Apply Status", BorderRadius = 10, FillColor = AppColors.PrimaryBlue, ForeColor = Color.White };
        btnApply.Location = new Point(270, 510);
        btnApply.Size = new Size(120, 36);
        btnApply.Click += (_, _) =>
        {
            if (Enum.TryParse<ViolationStatus>(cb.SelectedItem?.ToString(), out var st))
                NewStatus = st;
            DialogResult = DialogResult.OK;
            Close();
        };
        card.Controls.Add(btnApply);

        var btnClose = new Guna2Button { Text = "Close", BorderRadius = 10, FillColor = Color.White, ForeColor = AppColors.TitleText };
        btnClose.Location = new Point(398, 510);
        btnClose.Size = new Size(92, 36);
        btnClose.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        card.Controls.Add(btnClose);
    }
}
