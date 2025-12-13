using System.Collections.Generic;

namespace SafetyGuard.WinForms.Models;

public sealed class AppSettings
{
    // Cameras
    public List<CameraConfig> Cameras { get; set; } = new();

    // Detection rules
    public List<DetectionRule> Rules { get; set; } = new();

    // Anti-spam logic
    public int MinConsecutiveFrames { get; set; } = 6; // N frames
    public int CooldownSeconds { get; set; } = 10;     // X seconds

    // Evidence
    public bool SaveSnapshot { get; set; } = true;
    public bool SaveShortClip { get; set; } = false; // optional demo
    public int ClipSeconds { get; set; } = 7;

    // Storage
    public string EvidenceRoot { get; set; } = ""; // if empty, default AppData
    public int RetentionDays { get; set; } = 30;

    // Notification (demo)
    public bool EnableNotifications { get; set; } = true;

    // Demo
    public bool SeedDemoData { get; set; } = true;
}
