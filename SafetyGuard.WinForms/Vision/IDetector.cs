using System.Drawing;
using SafetyGuard.WinForms.Models;

namespace SafetyGuard.WinForms.Vision;

public interface IDetector
{
    Detection[] Detect(Bitmap frame);
    string Name { get; }
}
