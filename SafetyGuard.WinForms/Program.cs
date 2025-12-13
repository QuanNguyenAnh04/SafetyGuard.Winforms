using SafetyGuard.WinForms;
using System;
using System.Windows.Forms;

namespace SafetyGuard.WinForms;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var app = AppBootstrap.Build();
        Application.Run(new MainForm(app));
    }
}
