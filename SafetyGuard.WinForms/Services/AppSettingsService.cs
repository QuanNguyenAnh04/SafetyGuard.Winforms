using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SafetyGuard.WinForms.Models;

namespace SafetyGuard.WinForms.Services;

public sealed class AppSettingsService
{
    private readonly AppPaths _paths;
    private readonly LogService _logs;

    public AppSettings Current { get; private set; }

    public event Action<AppSettings>? OnChanged;

    public AppSettingsService(AppPaths paths, LogService logs)
    {
        _paths = paths;
        _logs = logs;
        Current = LoadOrCreate();
    }

    private static void NormalizeRules(AppSettings s)
    {
        // 1) bỏ trùng theo Type (lấy rule cuối cùng)
        s.Rules = s.Rules
            .GroupBy(r => r.Type)
            .Select(g => g.Last())
            .ToList();

        // 2) đảm bảo luôn có rule cho mọi ViolationType (đặc biệt khi bạn thêm enum mới)
        foreach (var t in Enum.GetValues(typeof(ViolationType)).Cast<ViolationType>())
        {
            if (s.Rules.Any(r => r.Type == t)) continue;

            s.Rules.Add(new DetectionRule
            {
                Type = t,
                Enabled = true,
                ConfidenceThreshold = t == ViolationType.Smoking ? 0.55f : 0.45f,
                Level = t == ViolationType.Smoking ? ViolationLevel.Critical : ViolationLevel.Warning
            });
        }
    }



    public void Save(AppSettings settings)
    {
        Current = settings;
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.SettingsPath)!);

        EnsureDefaults(settings);
        NormalizeRules(settings);

        var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_paths.SettingsPath, json);

        _logs.Info("Settings saved.");
        OnChanged?.Invoke(Current);
    }

    private AppSettings LoadOrCreate()
    {
        try
        {
            if (File.Exists(_paths.SettingsPath))
            {
                var json = File.ReadAllText(_paths.SettingsPath);
                var s = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                EnsureDefaults(s);
                if (string.IsNullOrWhiteSpace(s.EvidenceRoot))
                    s.EvidenceRoot = _paths.EvidenceDir;

                _logs.Info("Settings loaded.");
                return s;
            }
        }
        catch (Exception ex)
        {
            _logs.Error("Settings load failed: " + ex.Message);
        }

        var created = new AppSettings();
        EnsureDefaults(created);
        Save(created);
        return created;
    }

    private static void EnsureDefaults(AppSettings s)
    {

        if (s.Cameras.Count == 0)
        {
            // demo RTSP placeholders (user sửa trong Settings)
            s.Cameras.Add(new CameraConfig { Name = "Gate Cam", RtspUrl = "rtsp://your_rtsp_here", Enabled = true });
            s.Cameras.Add(new CameraConfig { Name = "Workshop Cam", RtspUrl = "rtsp://your_rtsp_here", Enabled = true });
            s.Cameras.Add(new CameraConfig { Name = "Warehouse Cam", RtspUrl = "rtsp://your_rtsp_here", Enabled = true });
            s.Cameras.Add(new CameraConfig { Name = "Packing Cam", RtspUrl = "rtsp://your_rtsp_here", Enabled = true });
        }

        if (s.Rules.Count == 0)
        {
            s.Rules = Enum.GetValues(typeof(ViolationType))
                .Cast<ViolationType>()
                .Select(t => new DetectionRule
                {
                    Type = t,
                    Enabled = true,
                    ConfidenceThreshold = t == ViolationType.Smoking ? 0.55f : 0.45f,
                    Level = t == ViolationType.Smoking ? ViolationLevel.Critical : ViolationLevel.Warning
                })
                .ToList();
        }
        NormalizeRules(s);

    }
}
