using System;
using System.Drawing;
using System.Windows.Forms;

namespace SafetyGuard.WinForms.UI;

public static class UiHelpers
{
    public static void SafeInvoke(this Control c, Action a)
    {
        if (c.IsDisposed) return;
        if (c.InvokeRequired) c.BeginInvoke(a);
        else a();
    }

    public static string BuildInfo()
    {
        var asm = typeof(UiHelpers).Assembly;
        var v = asm.GetName().Version?.ToString() ?? "1.0.0";
        var build = System.IO.File.GetLastWriteTime(asm.Location);
        return $"v{v} | build {build:yyyy-MM-dd HH:mm}";
    }

    public static Color SeverityColor(Models.ViolationLevel level) =>
        level switch
        {
            Models.ViolationLevel.Critical => AppColors.BadRed,
            Models.ViolationLevel.Warning => AppColors.WarnAmber,
            _ => AppColors.MutedText
        };

    public static Color StatusColor(Models.ViolationStatus status) =>
        status switch
        {
            Models.ViolationStatus.New => AppColors.PrimaryBlue,
            Models.ViolationStatus.Acknowledged => AppColors.WarnAmber,
            Models.ViolationStatus.Resolved => AppColors.GoodGreen,
            Models.ViolationStatus.FalseAlarm => AppColors.BadRed,
            _ => AppColors.MutedText
        };
}
