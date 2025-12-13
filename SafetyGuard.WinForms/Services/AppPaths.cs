using System;
using System.IO;

namespace SafetyGuard.WinForms.Services;

public sealed class AppPaths
{
    public string Root { get; }
    public string DataDir { get; }
    public string LogsDir { get; }
    public string EvidenceDir { get; }
    public string SettingsPath { get; }
    public string ViolationsPath { get; }

    public AppPaths()
    {
        Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafetyGuard");

        DataDir = Path.Combine(Root, "data");
        LogsDir = Path.Combine(Root, "logs");
        EvidenceDir = Path.Combine(Root, "evidence");

        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(EvidenceDir);

        SettingsPath = Path.Combine(DataDir, "settings.json");
        ViolationsPath = Path.Combine(DataDir, "violations.json");
    }
}
