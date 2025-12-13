namespace SafetyGuard.WinForms.Models;

public enum ViolationType
{
    NoHelmet,
    NoVest,
    NoGloves,
    NoGlasses,
    NoBoots,
    Smoking
}

public enum CameraStatus
{
    Offline,
    Reconnecting,
    Connected
}

public enum ViolationStatus
{
    New,
    Acknowledged,
    Resolved,
    FalseAlarm
}

public enum ViolationLevel
{
    Info,
    Warning,
    Critical
}
