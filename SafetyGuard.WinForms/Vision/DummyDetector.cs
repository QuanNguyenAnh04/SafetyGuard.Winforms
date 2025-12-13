using System;
using System.Drawing;
using SafetyGuard.WinForms.Models;
using SafetyGuard.WinForms.Services;

namespace SafetyGuard.WinForms.Vision;

public sealed class DummyDetector : IDetector
{
    private readonly AppSettingsService _settings;
    private readonly LogService _logs;
    private readonly Random _rnd = new(3);

    public string Name => "DummyDetector (demo)";

    public DummyDetector(AppSettingsService settings, LogService logs)
    {
        _settings = settings;
        _logs = logs;
        _logs.Info("Detector loaded: DummyDetector");
    }

    public Detection[] Detect(Bitmap frame)
    {
        // Fake detections: 0..3 objects
        var n = _rnd.Next(0, 4);
        var rules = _settings.Current.Rules;

        var res = new Detection[n];
        for (int i = 0; i < n; i++)
        {
            var rule = rules[_rnd.Next(rules.Count)];
            var conf = (float)(0.35 + _rnd.NextDouble() * 0.6);

            var w = Math.Max(60, frame.Width / 6);
            var h = Math.Max(60, frame.Height / 6);
            var x = _rnd.Next(0, Math.Max(1, frame.Width - w));
            var y = _rnd.Next(0, Math.Max(1, frame.Height - h));

            res[i] = new Detection
            {
                Type = rule.Type,
                Confidence = conf,
                Box = new BoundingBox(x, y, w, h)
            };
        }
        return res;
    }
}
