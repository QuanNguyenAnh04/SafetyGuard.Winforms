using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SafetyGuard.WinForms.Models;

public sealed class DetectionResult
{
    public ObjectClass Class { get; init; }
    public BoundingBox Box { get; init; } = new BoundingBox();
    public float Confidence { get; init; }
}
