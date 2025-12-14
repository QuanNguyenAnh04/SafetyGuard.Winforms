namespace SafetyGuard.WinForms.Models;

public class Detection
{
    public ViolationType Type { get; init; }
    public BoundingBox Box { get; init; } = new BoundingBox();
    public float Confidence { get; init; }
}

