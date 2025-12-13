using System.Drawing;
using System.Windows.Forms;
using Guna.UI2.WinForms;

namespace SafetyGuard.WinForms.Controls;

public sealed class BadgeLabel : Guna2Panel
{
    private readonly Label _lbl;

    public BadgeLabel(string text, Color bg, Color fg)
    {
        BorderRadius = 10;
        FillColor = bg;
        Padding = new Padding(10, 4, 10, 4);
        Height = 26;

        _lbl = new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = fg,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(_lbl);
    }

    public void Set(string text, Color bg, Color fg)
    {
        _lbl.Text = text;
        FillColor = bg;
        _lbl.ForeColor = fg;
    }
}
