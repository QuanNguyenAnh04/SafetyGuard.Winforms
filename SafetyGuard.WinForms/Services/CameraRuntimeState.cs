using System;
using SafetyGuard.WinForms.Models;

namespace SafetyGuard.WinForms.Services;

public sealed class CameraRuntimeState
{
    public string CameraId { get; init; } = "";
    public string CameraName { get; init; } = "";

    public CameraStatus Status { get; set; } = CameraStatus.Offline;

    public DateTime LastFrameUtc { get; set; } = DateTime.MinValue;

    public double Fps { get; set; } = 0;
}
