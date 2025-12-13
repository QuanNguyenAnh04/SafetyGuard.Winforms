using System;

namespace SafetyGuard.WinForms.Models;

public sealed class ViolationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime TimeUtc { get; set; } = DateTime.UtcNow;

    public string CameraId { get; set; } = "";
    public string CameraName { get; set; } = "";

    public ViolationType Type { get; set; }
    public ViolationLevel Level { get; set; } = ViolationLevel.Warning;
    public ViolationStatus Status { get; set; } = ViolationStatus.New;

    public float Confidence { get; set; }

    // Evidence paths
    public string? SnapshotPath { get; set; }
    public string? ClipPath { get; set; }

    // Useful for filtering
    public string? Notes { get; set; }
}
