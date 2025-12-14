using System.Drawing;

namespace SafetyGuard.WinForms.Models;

//public readonly record struct BoundingBox(float X, float Y, float W, float H);


public class BoundingBox
{
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public BoundingBox() { }

    public BoundingBox(float x, float y, float w, float h)
    {
        X = x;
        Y = y;
        W = w;
        H = h;
    }

    public Rectangle ToRectClamped(int imgW, int imgH)
    {
        int x = (int)Math.Round(X);
        int y = (int)Math.Round(Y);
        int w = (int)Math.Round(W);
        int h = (int)Math.Round(H);

        // Clamp để không vẽ ra ngoài
        if (w < 1) w = 1;
        if (h < 1) h = 1;
        if (x < 0) x = 0;
        if (y < 0) y = 0;
        if (x + w > imgW) w = imgW - x;
        if (y + h > imgH) h = imgH - y;

        return new Rectangle(x, y, w, h);
    }

    public static BoundingBox FromNormalized(float nx, float ny, float nw, float nh, int imgW, int imgH)
        => new BoundingBox
        {
            X = nx * imgW,
            Y = ny * imgH,
            W = nw * imgW,
            H = nh * imgH
        };
}