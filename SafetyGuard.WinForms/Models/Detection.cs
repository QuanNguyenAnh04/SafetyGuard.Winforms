namespace SafetyGuard.WinForms.Models;

public sealed class Detection
{
    public ViolationType Type { get; set; }
    public BoundingBox Box { get; set; }
    public float Confidence { get; set; }
}
