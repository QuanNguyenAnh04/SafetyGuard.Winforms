using System;
using System.Drawing;

namespace SafetyGuard.WinForms.Models;

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

    private static int Clamp(int v, int lo, int hi)
        => v < lo ? lo : (v > hi ? hi : v);

    public Rectangle ToRectClamped(int imgW, int imgH)
    {
        // ảnh không hợp lệ
        if (imgW <= 0 || imgH <= 0) return Rectangle.Empty;

        // bbox lỗi (NaN/Inf)
        if (float.IsNaN(X) || float.IsNaN(Y) || float.IsNaN(W) || float.IsNaN(H) ||
            float.IsInfinity(X) || float.IsInfinity(Y) || float.IsInfinity(W) || float.IsInfinity(H))
            return Rectangle.Empty;

        // chuyển sang 2 điểm (x1,y1)-(x2,y2)
        int x1 = (int)Math.Round(X);
        int y1 = (int)Math.Round(Y);
        int x2 = (int)Math.Round(X + W);
        int y2 = (int)Math.Round(Y + H);

        // nếu W/H âm thì swap lại
        if (x2 < x1) (x1, x2) = (x2, x1);
        if (y2 < y1) (y1, y2) = (y2, y1);

        // clamp điểm đầu vào trong [0 .. imgW-1], [0 .. imgH-1]
        x1 = Clamp(x1, 0, imgW - 1);
        y1 = Clamp(y1, 0, imgH - 1);

        // clamp điểm cuối trong [x1+1 .. imgW], [y1+1 .. imgH] để đảm bảo w/h >= 1
        x2 = Clamp(x2, x1 + 1, imgW);
        y2 = Clamp(y2, y1 + 1, imgH);

        int w = x2 - x1;
        int h = y2 - y1;

        // double safety (hiếm khi cần)
        if (w < 1) w = 1;
        if (h < 1) h = 1;

        return new Rectangle(x1, y1, w, h);
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
