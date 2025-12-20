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

    // ===== Realtime pipeline (NEW) =====
    public int DetectEveryNFrames { get; set; } = 5;          // YOLO mỗi N frame
    public int TrackMaxMissedFrames { get; set; } = 30;       // mất track sau N frame
    public float TrackIouThreshold { get; set; } = 0.30f;     // match person<->track
    public float PpeIouThreshold { get; set; } = 0.10f;       // assign ppe->person

    public double NoHelmetSeconds { get; set; } = 2.0;        // NoHelmet > 2s
    public double NoVestSeconds { get; set; } = 3.0;          // NoVest > 3s
    public double NoGlovesSeconds { get; set; } = 3.0;        // gợi ý
    public double NoGlassesSeconds { get; set; } = 3.0;      // gợi ý
    public double NoBootsSeconds { get; set; } = 3.0;        // gợi ý
    public double SmokingSeconds { get; set; } = 1.0;         // gợi ý

    // Evidence
    public bool SaveSnapshot { get; set; } = true;
    public bool SaveShortClip { get; set; } = false; // optional demo
    public int ClipSeconds { get; set; } = 7;

    // Storage
    public string EvidenceRoot { get; set; } = ""; // if empty, default AppData
    public int RetentionDays { get; set; } = 30;

    // Notification (demo)
    public bool EnableNotifications { get; set; } = false;

    // Demo
    public bool SeedDemoData { get; set; } = false;
}
