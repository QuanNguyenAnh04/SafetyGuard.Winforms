using System.Reflection;
using System.Windows.Forms;

namespace SafetyGuard.WinForms.UI;

public static class ControlPerf
{
    public static void EnableDoubleBuffer(Control c)
    {
        if (c == null) return;

        typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(c, true, null);
    }
}
