using System;
using System.Drawing;

namespace SafetyGuard.WinForms.Models;

public sealed class FramePacket
{
    public int FrameIndex { get; }
    public DateTime TimestampUtc { get; }
    public Bitmap Frame { get; }

    public FramePacket(int frameIndex, Bitmap frame, DateTime timestampUtc)
    {
        FrameIndex = frameIndex;
        Frame = frame;
        TimestampUtc = timestampUtc;
    }
}
