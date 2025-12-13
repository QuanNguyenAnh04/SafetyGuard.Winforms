using System;

namespace SafetyGuard.WinForms.Models;

public sealed class CameraConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Camera";
    public string RtspUrl { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
