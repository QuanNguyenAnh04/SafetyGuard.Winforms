using Guna.UI2.WinForms;
using SafetyGuard.WinForms.Models;
using System.Drawing;
using System.Windows.Forms;
using GButtonMode = Guna.UI2.WinForms.Enums.ButtonMode;


namespace SafetyGuard.WinForms.UI;

public static class ControlFactory
{
    public static Guna2Button NavButton(string text)
    {
        var btn = new Guna2Button
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 44,
            BorderRadius = 12,
            FillColor = Color.Transparent,
            ForeColor = Color.Gainsboro,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            TextAlign = HorizontalAlignment.Left,
            Padding = new Padding(14, 0, 0, 0),
            ButtonMode = GButtonMode.RadioButton,
            Cursor = Cursors.Hand,
            Margin = new Padding(12, 0, 12, 0),
            Animated = true
        };
        btn.CheckedState.FillColor = AppColors.PrimaryBlue;
        btn.CheckedState.ForeColor = Color.White;
        btn.HoverState.FillColor = AppColors.HoverDark;
        btn.HoverState.ForeColor = Color.White;
        return btn;
    }

    public static Guna2ShadowPanel Card(int radius = 14, Padding? padding = null)
    {
        return new Guna2ShadowPanel
        {
            FillColor = AppColors.CardBg,
            Radius = radius,
            ShadowDepth = 12,
            ShadowShift = 2,
            ShadowColor = Color.Black,
            Padding = padding ?? new Padding(16),
            Margin = new Padding(10)
        };
    }

    public static Label Title(string text, float size = 12, bool bold = true)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = AppColors.TitleText,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular)
        };
    }

    public static Label Muted(string text, float size = 9, bool bold = false)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = AppColors.MutedText,
            Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular)
        };
    }

    public static Guna2Panel IconBox(Color fill) =>
        new()
        {
            Size = new Size(44, 44),
            BorderRadius = 12,
            FillColor = fill
        };
}
