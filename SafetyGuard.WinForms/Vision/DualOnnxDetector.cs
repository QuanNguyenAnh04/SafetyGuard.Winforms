
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.Services;

using System.Diagnostics;
using System.IO;

namespace SafetyGuard.WinForms.Vision;

public sealed class DualOnnxDetector : IDetector, IDisposable
{
    private readonly IAppSettingsService _settings;
    private readonly LogService _logs;

    private readonly InferenceSession _ppe;
    private readonly InferenceSession _smoke;

    // Input size YOLO (thường 640). Bạn có thể đọc từ model metadata nếu muốn.
    private const int InW = 640;
    private const int InH = 640;

    public string Name => "DualOnnxDetector (PPE + Smoke)";

    // Mapping classId -> ViolationType (bạn chỉnh theo label thật của model)
    // Nếu model PPE detect "no_helmet", "no_vest"... thì map về các ViolationType tương ứng.
    private readonly Dictionary<int, ViolationType> _ppeMap = new()
    {
        { 4, ViolationType.NoBoots },
        { 5, ViolationType.NoGlasses },
        { 6, ViolationType.NoGloves },
        { 7, ViolationType.NoHelmet },
        { 8, ViolationType.NoVest },
    };


    private readonly Dictionary<int, ViolationType> _smokeMap = new()
    {
        // ví dụ: 0 = smoking
        { 0, ViolationType.Smoking }
    };

    public DualOnnxDetector(IAppSettingsService settings, LogService logs)
    {
        _settings = settings;
        _logs = logs;

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var ppePath = Path.Combine(baseDir, "Assets", "Models", "YOLOv11_ppe.onnx");
        var smokePath = Path.Combine(baseDir, "Assets", "Models", "YOLOv11_smoke.onnx");

        if (!File.Exists(ppePath)) throw new FileNotFoundException("Missing model", ppePath);
        if (!File.Exists(smokePath)) throw new FileNotFoundException("Missing model", smokePath);

        var so = BuildSessionOptions();
        try
        {
            _ppe = new InferenceSession(ppePath, so);
            HardLog("PPE session created OK");
            _smoke = new InferenceSession(smokePath, so);
            HardLog("SMOKE session created OK");
        }
        catch (Exception ex)
        {
            HardLog("Create session FAILED: " + ex);
            throw;
        }
        _ppe = new InferenceSession(ppePath, so);
        _smoke = new InferenceSession(smokePath, so);

        _logs.Info($"Loaded PPE model: {ppePath}");
        _logs.Info($"Loaded Smoke model: {smokePath}");

        DumpSessionIO("PPE", _ppe);
        DumpSessionIO("SMOKE", _smoke);
    }


    private SessionOptions BuildSessionOptions()
    {
        var so = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };

        try
        {
            var providers = OrtEnv.Instance().GetAvailableProviders();
            HardLog($"ORT available EPs: {string.Join(", ", providers)}");

            if (providers.Any(p => p.Equals("CUDAExecutionProvider", StringComparison.OrdinalIgnoreCase)))
            {
                so.AppendExecutionProvider_CUDA(0);
                HardLog("ORT selected EP: CUDA (deviceId=0)");
            }
            else
            {
                HardLog("No GPU EP found -> CPU fallback");
            }

        }
        catch (Exception ex)
        {
            HardLog("CUDA init failed: " + ex);
            throw; // để biết chắc chắn GPU không chạy
        }


        return so;
    }




    public Detection[] Detect(Bitmap frame)
    {
        // thresholds lấy từ SettingsPage của bạn
        var rules = _settings.Current.Rules;
        var confMin = rules.Count > 0 ? rules.Min(r => r.ConfidenceThreshold) : 0.4f;

        var ppe = RunOne(_ppe, frame, confMin, _ppeMap);
        var smoke = RunOne(_smoke, frame, confMin, _smokeMap);

        // gộp và trả
        return ppe.Concat(smoke).ToArray();
    }

    private Detection[] RunOne(InferenceSession session, Bitmap frame, float confMin, Dictionary<int, ViolationType> map)
    {
        // Letterbox -> tensor
        using var resized = Letterbox(frame, InW, InH, out var pad, out var scale);

        var inputName = session.InputMetadata.Keys.First();
        var tensor = ImageToCHWFloat(resized); // [1,3,640,640]
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, tensor)
        };

        using var outputs = session.Run(inputs);
        var out0 = outputs.First().AsTensor<float>();
        var dims = out0.Dimensions.ToArray();
        _logs.Info($"ONNX output dims: [{string.Join(",", dims)}]");


        // parse output -> raw boxes
        var raw = ParseYolo(outputs, confMin);

        // NMS
        float iou = 0.45f;
        var keep = Nms(raw, iou);

        // map về Detection[] + scale back về frame gốc
        var dets = new List<Detection>(keep.Count);
        foreach (var b in keep)
        {
            if (!map.TryGetValue(b.ClassId, out var vt))
                continue; // class không quan tâm

            // b.X/Y/W/H đang theo input 640, đã letterbox. Scale về ảnh gốc
            var x = (b.X - pad.X) / scale;
            var y = (b.Y - pad.Y) / scale;
            var w = b.W / scale;
            var h = b.H / scale;

            // clamp
            x = Math.Max(0, Math.Min(frame.Width - 1, x));
            y = Math.Max(0, Math.Min(frame.Height - 1, y));
            w = Math.Max(1, Math.Min(frame.Width - x, w));
            h = Math.Max(1, Math.Min(frame.Height - y, h));

            dets.Add(new Detection
            {
                Type = vt,
                Confidence = b.Score,
                Box = new BoundingBox((int)x, (int)y, (int)w, (int)h)
            });
        }

        return dets.ToArray();
    }

    private static void DumpSessionIO(string tag, InferenceSession s)
    {
        // In log để bạn biết output shape thật (giúp sửa parser nếu cần)
        // Không bắt buộc, nhưng rất hữu ích lúc debug
        // (Bạn có LogService nên có thể log ra file)
        // Ở đây để đơn giản, dùng Console (nếu bạn muốn dùng _logs thì sửa lại).
        Console.WriteLine($"[{tag}] Inputs:");
        foreach (var kv in s.InputMetadata)
            Console.WriteLine($"  {kv.Key}  {string.Join(",", kv.Value.Dimensions)}  {kv.Value.ElementType}");

        Console.WriteLine($"[{tag}] Outputs:");
        foreach (var kv in s.OutputMetadata)
            Console.WriteLine($"  {kv.Key}  {string.Join(",", kv.Value.Dimensions)}  {kv.Value.ElementType}");
    }

    // ===== Preprocess =====

    private static Bitmap Letterbox(Bitmap src, int dstW, int dstH, out PointF pad, out float scale)
    {
        scale = Math.Min((float)dstW / src.Width, (float)dstH / src.Height);
        var newW = (int)Math.Round(src.Width * scale);
        var newH = (int)Math.Round(src.Height * scale);

        var padX = (dstW - newW) / 2f;
        var padY = (dstH - newH) / 2f;
        pad = new PointF(padX, padY);

        var bmp = new Bitmap(dstW, dstH);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(114, 114, 114));
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, padX, padY, newW, newH);
        return bmp;
    }

    private static DenseTensor<float> ImageToCHWFloat(Bitmap bmp)
    {
        var t = new DenseTensor<float>(new[] { 1, 3, bmp.Height, bmp.Width });

        // LockBits nhanh hơn GetPixel, nhưng để đơn giản demo dùng GetPixel.
        // Nếu realtime, bạn nên tối ưu LockBits.
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                t[0, 0, y, x] = c.R / 255f;
                t[0, 1, y, x] = c.G / 255f;
                t[0, 2, y, x] = c.B / 255f;
            }

        return t;
    }

    // ===== Postprocess =====

    private sealed record RawBox(float X, float Y, float W, float H, float Score, int ClassId);

    private static List<RawBox> ParseYolo(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs, float confMin)
    {
        // chọn output đầu tiên
        var o = outputs.First().AsTensor<float>();
        var dims = o.Dimensions.ToArray();

        // Convert tensor -> list box. Cố gắng hỗ trợ 2 kiểu phổ biến.
        // case A: [1, N, K]
        // case B: [1, K, N]
        if (dims.Length == 3)
        {
            int d1 = dims[1];
            int d2 = dims[2];

            // heuristic: K thường nhỏ hơn N (vd K= (4+1+nc), N=8400)
            bool isNK = d1 > d2; // [1, N, K]
            if (isNK)
                return Parse_1_N_K(o, confMin);
            else
                return Parse_1_K_N(o, confMin);
        }

        // case C: [N, K]
        if (dims.Length == 2)
            return Parse_N_K(o, confMin);

        // fallback: rỗng
        return new List<RawBox>();
    }

    private static List<RawBox> Parse_1_N_K(Tensor<float> t, float confMin)
    {
        int N = t.Dimensions[1];
        int K = t.Dimensions[2];

        // Nếu là output đã NMS: [x1,y1,x2,y2,score,class] và N nhỏ
        if (K == 6 && N <= 1000)
        {
            var res6 = new List<RawBox>();
            for (int i = 0; i < N; i++)
            {
                float x1 = t[0, i, 0];
                float y1 = t[0, i, 1];
                float x2 = t[0, i, 2];
                float y2 = t[0, i, 3];
                float score = t[0, i, 4];
                int cls = (int)t[0, i, 5];
                if (score < confMin) continue;
                res6.Add(new RawBox(x1, y1, x2 - x1, y2 - y1, score, cls));
            }
            return res6;
        }

        // ---- Try kiểu "NO objectness" (YOLOv11/v8 thường gặp): [cx,cy,w,h,cls...]
        List<RawBox> noObj = new();
        if (K >= 5)
        {
            int numClass = K - 4;
            for (int i = 0; i < N; i++)
            {
                float cx = t[0, i, 0];
                float cy = t[0, i, 1];
                float w = t[0, i, 2];
                float h = t[0, i, 3];

                int bestId = -1;
                float bestCls = 0;
                for (int c = 0; c < numClass; c++)
                {
                    float p = t[0, i, 4 + c];
                    if (p > bestCls) { bestCls = p; bestId = c; }
                }

                float score = bestCls;
                if (score < confMin) continue;

                noObj.Add(new RawBox(cx - w / 2, cy - h / 2, w, h, score, bestId));
            }
        }

        // ---- Try kiểu "HAS objectness": [cx,cy,w,h,obj,cls...]
        List<RawBox> withObj = new();
        if (K >= 6)
        {
            int numClass = K - 5;
            for (int i = 0; i < N; i++)
            {
                float cx = t[0, i, 0];
                float cy = t[0, i, 1];
                float w = t[0, i, 2];
                float h = t[0, i, 3];
                float obj = t[0, i, 4];

                int bestId = -1;
                float bestCls = 0;
                for (int c = 0; c < numClass; c++)
                {
                    float p = t[0, i, 5 + c];
                    if (p > bestCls) { bestCls = p; bestId = c; }
                }

                float score = obj * bestCls;
                if (score < confMin) continue;

                withObj.Add(new RawBox(cx - w / 2, cy - h / 2, w, h, score, bestId));
            }
        }

        // Chọn parse nào ra nhiều box hơn (thực tế sẽ đúng cho model của bạn)
        return noObj.Count >= withObj.Count ? noObj : withObj;
    }


    private static List<RawBox> Parse_1_K_N(Tensor<float> t, float confMin)
    {
        // [1, K, N]
        int K = t.Dimensions[1];
        int N = t.Dimensions[2];

        // Heuristic: nếu là output đã NMS [x1,y1,x2,y2,score,cls] và N nhỏ
        if (K == 6 && N <= 1000)
        {
            var res6 = new List<RawBox>();
            for (int i = 0; i < N; i++)
            {
                float x1 = t[0, 0, i];
                float y1 = t[0, 1, i];
                float x2 = t[0, 2, i];
                float y2 = t[0, 3, i];
                float score = t[0, 4, i];
                int cls = (int)t[0, 5, i];
                if (score < confMin) continue;

                res6.Add(new RawBox(x1, y1, x2 - x1, y2 - y1, score, cls));
            }
            return res6;
        }

        // ✅ YOLOv11/v8 thường gặp: KHÔNG objectness => [cx,cy,w,h,cls...]
        var noObj = new List<RawBox>();
        if (K >= 5)
        {
            int numClass = K - 4; // <- quan trọng: K=5 => numClass=1 (smoke)
            for (int i = 0; i < N; i++)
            {
                float cx = t[0, 0, i];
                float cy = t[0, 1, i];
                float w = t[0, 2, i];
                float h = t[0, 3, i];

                int bestId = -1;
                float bestCls = 0f;
                for (int c = 0; c < numClass; c++)
                {
                    float p = t[0, 4 + c, i];
                    if (p > bestCls) { bestCls = p; bestId = c; }
                }

                float score = bestCls;
                if (score < confMin) continue;
                noObj.Add(new RawBox(cx - w / 2, cy - h / 2, w, h, score, bestId));
            }
        }

        // (tuỳ chọn) nếu model thật sự có obj: [cx,cy,w,h,obj,cls...]
        var withObj = new List<RawBox>();
        if (K >= 6)
        {
            int numClass = K - 5;
            for (int i = 0; i < N; i++)
            {
                float cx = t[0, 0, i];
                float cy = t[0, 1, i];
                float w = t[0, 2, i];
                float h = t[0, 3, i];
                float obj = t[0, 4, i];

                int bestId = -1;
                float bestCls = 0f;
                for (int c = 0; c < numClass; c++)
                {
                    float p = t[0, 5 + c, i];
                    if (p > bestCls) { bestCls = p; bestId = c; }
                }

                float score = obj * bestCls;
                if (score < confMin) continue;
                withObj.Add(new RawBox(cx - w / 2, cy - h / 2, w, h, score, bestId));
            }
        }

        // Chọn nhánh ra nhiều box hơn
        return noObj.Count >= withObj.Count ? noObj : withObj;
    }



    private static List<RawBox> Parse_N_K(Tensor<float> t, float confMin)
    {
        int N = t.Dimensions[0];
        int K = t.Dimensions[1];

        if (K == 6 && N <= 1000)
        {
            var res6 = new List<RawBox>();
            for (int i = 0; i < N; i++)
            {
                float x1 = t[i, 0];
                float y1 = t[i, 1];
                float x2 = t[i, 2];
                float y2 = t[i, 3];
                float score = t[i, 4];
                int cls = (int)t[i, 5];
                if (score < confMin) continue;

                res6.Add(new RawBox(x1, y1, x2 - x1, y2 - y1, score, cls));
            }
            return res6;
        }

        // ✅ no-objectness: [cx,cy,w,h,cls...]
        var noObj = new List<RawBox>();
        if (K >= 5)
        {
            int numClass = K - 4; // K=5 => 1 class
            for (int i = 0; i < N; i++)
            {
                float cx = t[i, 0];
                float cy = t[i, 1];
                float w = t[i, 2];
                float h = t[i, 3];

                int bestId = -1;
                float bestCls = 0f;
                for (int c = 0; c < numClass; c++)
                {
                    float p = t[i, 4 + c];
                    if (p > bestCls) { bestCls = p; bestId = c; }
                }

                float score = bestCls;
                if (score < confMin) continue;
                noObj.Add(new RawBox(cx - w / 2, cy - h / 2, w, h, score, bestId));
            }
        }

        // (tuỳ chọn) has-objectness
        var withObj = new List<RawBox>();
        if (K >= 6)
        {
            int numClass = K - 5;
            for (int i = 0; i < N; i++)
            {
                float cx = t[i, 0];
                float cy = t[i, 1];
                float w = t[i, 2];
                float h = t[i, 3];
                float obj = t[i, 4];

                int bestId = -1;
                float bestCls = 0f;
                for (int c = 0; c < numClass; c++)
                {
                    float p = t[i, 5 + c];
                    if (p > bestCls) { bestCls = p; bestId = c; }
                }

                float score = obj * bestCls;
                if (score < confMin) continue;
                withObj.Add(new RawBox(cx - w / 2, cy - h / 2, w, h, score, bestId));
            }
        }

        return noObj.Count >= withObj.Count ? noObj : withObj;
    }



    private static List<RawBox> Nms(List<RawBox> boxes, float iouThres)
    {
        var sorted = boxes.OrderByDescending(b => b.Score).ToList();
        var keep = new List<RawBox>(sorted.Count);

        while (sorted.Count > 0)
        {
            var best = sorted[0];
            keep.Add(best);
            sorted.RemoveAt(0);

            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                if (sorted[i].ClassId != best.ClassId) continue;
                if (IoU(best, sorted[i]) >= iouThres)
                    sorted.RemoveAt(i);
            }
        }
        return keep;
    }

    private static float IoU(RawBox a, RawBox b)
    {
        float ax2 = a.X + a.W;
        float ay2 = a.Y + a.H;
        float bx2 = b.X + b.W;
        float by2 = b.Y + b.H;

        float x1 = Math.Max(a.X, b.X);
        float y1 = Math.Max(a.Y, b.Y);
        float x2 = Math.Min(ax2, bx2);
        float y2 = Math.Min(ay2, by2);

        float inter = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        float union = a.W * a.H + b.W * b.H - inter;

        return union <= 0 ? 0 : inter / union;
    }

    public void Dispose()
    {
        _ppe.Dispose();
        _smoke.Dispose();
    }

    private static void HardLog(string s)
    {
        Debug.WriteLine(s);
        File.AppendAllText(
            Path.Combine(Path.GetTempPath(), "ort_startup.log"),
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {s}{Environment.NewLine}"
        );
    }
}
