namespace SafetyGuard.WinForms.Models;

public sealed class DetectionRule
{
    public ViolationType Type { get; set; }
    public bool Enabled { get; set; } = true;

    // threshold riêng từng loại vi phạm
    public float ConfidenceThreshold { get; set; } = 0.45f;

    // level cho workflow
    public ViolationLevel Level { get; set; } = ViolationLevel.Warning;
}
