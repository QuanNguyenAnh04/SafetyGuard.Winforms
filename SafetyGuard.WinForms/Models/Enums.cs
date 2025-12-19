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

// ✅ NEW: object classes (đầu ra detector)
public enum ObjectClass
{
    Person,
    Helmet,
    Vest,
    Gloves,
    Glasses,
    Boots,

    // optional (nếu model có class "no_xxx")
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
