using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.Services;

namespace SafetyGuard.WinForms.Vision;

public sealed class DualOnnxDetector : IDetector, IDisposable
{
    public string Name => "DualONNX (PPE+SMOKE)";

    private readonly IAppSettingsService _settings;
    private readonly LogService _logs;

    private InferenceSession? _ppe;
    private InferenceSession? _smoke;

    // Mapping classId -> ObjectClass (hãy chỉnh theo label thật của model bạn export)
    private readonly Dictionary<int, ObjectClass> _ppeMap = new()
    {
        { 0, ObjectClass.Boots },
        { 1, ObjectClass.Glasses },
        { 2, ObjectClass.Gloves },
        { 3, ObjectClass.Helmet },
        { 4, ObjectClass.NoBoots },
        { 5, ObjectClass.NoGlasses },
        { 6, ObjectClass.NoGloves },
        { 7, ObjectClass.NoHelmet },
        { 8, ObjectClass.NoVest },
        { 9, ObjectClass.Person },
        { 10, ObjectClass.Vest },
    };

    private readonly Dictionary<int, ObjectClass> _smokeMap = new()
    {
        { 0, ObjectClass.Smoking }
    };

    private readonly float _nmsIou = 0.45f;

    public DualOnnxDetector(IAppSettingsService settings, LogService logs)
    {
        _settings = settings;
        _logs = logs;

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var ppePath = Path.Combine(baseDir, "Assets", "Models", "YOLOv11_ppe.onnx");
        var smokePath = Path.Combine(baseDir, "Assets", "Models", "YOLOv11_smoke.onnx");

        var so = CreateSessionOptions();

        _ppe = new InferenceSession(ppePath, so);
        _smoke = new InferenceSession(smokePath, so);

        _logs.Info($"Loaded ONNX: PPE={ppePath} | SMOKE={smokePath}");
    }

    private SessionOptions CreateSessionOptions()
    {
        var so = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            EnableCpuMemArena = true,
        };

        // ✅ Try CUDA first (requires Microsoft.ML.OnnxRuntime.Gpu + đúng CUDA runtime)
        try
        {
            // device 0
            so.AppendExecutionProvider_CUDA(0);
            _logs.Info("ONNX Runtime: CUDA Execution Provider ENABLED (device 0).");
        }
        catch (Exception ex)
        {
            _logs.Warn("ONNX Runtime: CUDA EP not available -> fallback CPU. Reason: " + ex.Message);
        }

        return so;
    }

    public DetectionResult[] Detect(Bitmap frame)
    {
        if (_ppe == null || _smoke == null) return Array.Empty<DetectionResult>();

        // ✅ chỉ lấy rule đang bật (nếu project bạn không có Enabled thì đổi lại như cũ)
        var rules = _settings.Current.Rules;
        var enabled = rules.Where(r => r.Enabled).ToList();
        var confMin = enabled.Count > 0 ? enabled.Min(r => r.ConfidenceThreshold) : 0.35f;

        var ppe = RunOne(_ppe, frame, confMin, _ppeMap);
        var smoke = RunOne(_smoke, frame, confMin, _smokeMap);

        return ppe.Concat(smoke).ToArray();
    }

    private DetectionResult[] RunOne(InferenceSession session, Bitmap frame, float confMin, Dictionary<int, ObjectClass> map)
    {
        // ===== infer input size =====
        var inputMeta = session.InputMetadata;
        var inputName = inputMeta.Keys.First();

        var shape = inputMeta[inputName].Dimensions.ToArray();
        int inW = (shape.Length >= 4 && shape[3] > 0) ? shape[3] : 640;
        int inH = (shape.Length >= 4 && shape[2] > 0) ? shape[2] : 640;

        using var resized = new Bitmap(frame, new Size(inW, inH));
        var input = BitmapToCHWTensor(resized);

        var inputNv = NamedOnnxValue.CreateFromTensor(inputName, input);
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(new[] { inputNv });

        var outTensor = results.First().AsTensor<float>();
        var dims = outTensor.Dimensions.ToArray();
        var arr = outTensor.ToArray();

        // Case A: NMS output [1, N, 6] or [N, 6] or [1, 6, N]
        var detsNms = TryParseNmsLike(arr, dims, confMin, map, frame.Width, frame.Height, inW, inH);
        if (detsNms != null) return detsNms;

        // Case B: Raw head [1, N, S] or [1, S, N]
        return ParseRawHead(arr, dims, confMin, map, frame.Width, frame.Height, inW, inH);
    }

    private DetectionResult[]? TryParseNmsLike(
        float[] arr,
        int[] dims,
        float confMin,
        Dictionary<int, ObjectClass> map,
        int srcW, int srcH,
        int inW, int inH)
    {
        if (dims.Length == 3 && dims[0] == 1 && dims[2] == 6)
        {
            int n = dims[1];
            return ParseNmsArray(arr, n, stride: 6, offset: 0, transposed: false, confMin, map, srcW, srcH, inW, inH);
        }

        if (dims.Length == 3 && dims[0] == 1 && dims[1] == 6)
        {
            int n = dims[2];
            return ParseNmsArray(arr, n, stride: 6, offset: 0, transposed: true, confMin, map, srcW, srcH, inW, inH);
        }

        if (dims.Length == 2 && dims[1] == 6)
        {
            int n = dims[0];
            return ParseNmsArray(arr, n, stride: 6, offset: 0, transposed: false, confMin, map, srcW, srcH, inW, inH);
        }

        return null;
    }

    private DetectionResult[] ParseNmsArray(
        float[] arr,
        int n,
        int stride,
        int offset,
        bool transposed,
        float confMin,
        Dictionary<int, ObjectClass> map,
        int srcW, int srcH,
        int inW, int inH)
    {
        float sx = srcW / (float)inW;
        float sy = srcH / (float)inH;

        var dets = new List<DetectionResult>(Math.Min(n, 200));

        for (int i = 0; i < n; i++)
        {
            float x1, y1, x2, y2, score, clsF;

            if (!transposed)
            {
                int baseIdx = offset + i * stride;
                x1 = arr[baseIdx + 0];
                y1 = arr[baseIdx + 1];
                x2 = arr[baseIdx + 2];
                y2 = arr[baseIdx + 3];
                score = arr[baseIdx + 4];
                clsF = arr[baseIdx + 5];
            }
            else
            {
                x1 = arr[0 * n + i];
                y1 = arr[1 * n + i];
                x2 = arr[2 * n + i];
                y2 = arr[3 * n + i];
                score = arr[4 * n + i];
                clsF = arr[5 * n + i];
            }

            if (score < confMin) continue;

            int classId = (int)clsF;
            if (!map.TryGetValue(classId, out var oc)) continue;

            var bx = x1 * sx;
            var by = y1 * sy;
            var bw = (x2 - x1) * sx;
            var bh = (y2 - y1) * sy;

            dets.Add(new DetectionResult
            {
                Class = oc,
                Confidence = score,
                Box = new BoundingBox((int)bx, (int)by, (int)bw, (int)bh)
            });
        }

        return dets.ToArray();
    }

    private DetectionResult[] ParseRawHead(
        float[] outArr,
        int[] outDims,
        float confMin,
        Dictionary<int, ObjectClass> map,
        int srcW, int srcH,
        int inW, int inH)
    {
        // ✅ Fix quan trọng:
        // - tự nhận layout [1,N,S] hoặc [1,S,N]
        // - hỗ trợ stride = 4+nc (không obj) và 5+nc (có obj)
        if (!(outDims.Length == 3 && outDims[0] == 1))
            return Array.Empty<DetectionResult>();

        int nA = outDims[1], strideA = outDims[2]; // [1,N,S]
        int nB = outDims[2], strideB = outDims[1]; // [1,S,N]

        int expectedNoObj = 4 + map.Count;
        int expectedObj = 5 + map.Count;

        bool layoutAOk = (strideA == expectedNoObj || strideA == expectedObj);
        bool layoutBOk = (strideB == expectedNoObj || strideB == expectedObj);

        bool transposed;
        int n, stride;

        if (layoutBOk && !layoutAOk)
        {
            transposed = true;   // [1,S,N]
            stride = strideB;
            n = nB;
        }
        else if (layoutAOk && !layoutBOk)
        {
            transposed = false;  // [1,N,S]
            stride = strideA;
            n = nA;
        }
        else
        {
            transposed = nB > nA;
            stride = transposed ? strideB : strideA;
            n = transposed ? nB : nA;
        }

        if (n <= 0 || stride <= 0) return Array.Empty<DetectionResult>();

        bool hasObj;
        int clsStart;

        if (stride == expectedObj) { hasObj = true; clsStart = 5; }
        else { hasObj = false; clsStart = 4; }

        int clsCount = stride - clsStart;
        if (clsCount <= 0) return Array.Empty<DetectionResult>();

        float Get(int comp, int i) =>
            !transposed ? outArr[i * stride + comp] : outArr[comp * n + i];

        var boxes = new List<RawBox>(Math.Min(n, 2000));

        for (int i = 0; i < n; i++)
        {
            float cx = Get(0, i);
            float cy = Get(1, i);
            float w = Get(2, i);
            float h = Get(3, i);

            float obj = hasObj ? Get(4, i) : 1f;
            if (hasObj && obj <= 0) continue;

            int bestCls = -1;
            float bestProb = 0f;

            for (int c = 0; c < clsCount; c++)
            {
                float p = Get(clsStart + c, i);
                if (p > bestProb)
                {
                    bestProb = p;
                    bestCls = c;
                }
            }

            if (bestCls < 0) continue;

            float score = hasObj ? (obj * bestProb) : bestProb;
            if (score < confMin) continue;

            boxes.Add(new RawBox { Cx = cx, Cy = cy, W = w, H = h, Score = score, ClassId = bestCls });
        }

        if (boxes.Count == 0) return Array.Empty<DetectionResult>();

        var keep = Nms(boxes, _nmsIou);

        float sx = srcW / (float)inW;
        float sy = srcH / (float)inH;

        var dets = new List<DetectionResult>(keep.Count);
        foreach (var b in keep)
        {
            if (!map.TryGetValue(b.ClassId, out var oc))
                continue;

            float x = (b.Cx - b.W / 2f) * sx;
            float y = (b.Cy - b.H / 2f) * sy;
            float ww = b.W * sx;
            float hh = b.H * sy;

            dets.Add(new DetectionResult
            {
                Class = oc,
                Confidence = b.Score,
                Box = new BoundingBox((int)x, (int)y, (int)ww, (int)hh)
            });
        }

        return dets.ToArray();
    }

    private sealed class RawBox
    {
        public float Cx, Cy, W, H, Score;
        public int ClassId;
    }

    private List<RawBox> Nms(List<RawBox> boxes, float iouThres)
    {
        var sorted = boxes.OrderByDescending(b => b.Score).ToList();
        var keep = new List<RawBox>();

        while (sorted.Count > 0)
        {
            var cur = sorted[0];
            keep.Add(cur);
            sorted.RemoveAt(0);

            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                if (IoU(cur, sorted[i]) >= iouThres)
                    sorted.RemoveAt(i);
            }
        }
        return keep;
    }

    private float IoU(RawBox a, RawBox b)
    {
        float ax1 = a.Cx - a.W / 2f;
        float ay1 = a.Cy - a.H / 2f;
        float ax2 = a.Cx + a.W / 2f;
        float ay2 = a.Cy + a.H / 2f;

        float bx1 = b.Cx - b.W / 2f;
        float by1 = b.Cy - b.H / 2f;
        float bx2 = b.Cx + b.W / 2f;
        float by2 = b.Cy + b.H / 2f;

        float x1 = Math.Max(ax1, bx1);
        float y1 = Math.Max(ay1, by1);
        float x2 = Math.Min(ax2, bx2);
        float y2 = Math.Min(ay2, by2);

        float inter = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        float areaA = a.W * a.H;
        float areaB = b.W * b.H;
        float union = areaA + areaB - inter;

        return union <= 0 ? 0 : inter / union;
    }

    private static DenseTensor<float> BitmapToCHWTensor(Bitmap bmp)
    {
        int w = bmp.Width;
        int h = bmp.Height;

        using var src24 = bmp.PixelFormat == PixelFormat.Format24bppRgb ? null : ConvertTo24bpp(bmp);
        var useBmp = src24 ?? bmp;

        var tensor = new DenseTensor<float>(new[] { 1, 3, h, w });

        var rect = new Rectangle(0, 0, w, h);
        var data = useBmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        try
        {
            int stride = data.Stride;
            int bytes = Math.Abs(stride) * h;
            var buffer = new byte[bytes];
            Marshal.Copy(data.Scan0, buffer, 0, bytes);

            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int idx = row + x * 3;
                    byte b = buffer[idx + 0];
                    byte g = buffer[idx + 1];
                    byte r = buffer[idx + 2];

                    tensor[0, 0, y, x] = r / 255f;
                    tensor[0, 1, y, x] = g / 255f;
                    tensor[0, 2, y, x] = b / 255f;
                }
            }
        }
        finally
        {
            useBmp.UnlockBits(data);
        }

        return tensor;

        static Bitmap ConvertTo24bpp(Bitmap src)
        {
            var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(dst);
            g.DrawImage(src, 0, 0, src.Width, src.Height);
            return dst;
        }
    }

    public void Dispose()
    {
        try { _ppe?.Dispose(); } catch { }
        try { _smoke?.Dispose(); } catch { }
        _ppe = null;
        _smoke = null;
    }
}
