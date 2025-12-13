using System;
using System.Drawing;
using System.IO;
using SafetyGuard.WinForms.Models;

namespace SafetyGuard.WinForms.Services;

public sealed class EvidenceService
{
    private readonly AppPaths _paths;
    private readonly AppSettingsService _settings;
    private readonly LogService _logs;

    public EvidenceService(AppPaths paths, AppSettingsService settings, LogService logs)
    {
        _paths = paths;
        _settings = settings;
        _logs = logs;
    }

    public string GetEvidenceRoot()
    {
        var s = _settings.Current;
        if (!string.IsNullOrWhiteSpace(s.EvidenceRoot))
        {
            Directory.CreateDirectory(s.EvidenceRoot);
            return s.EvidenceRoot;
        }
        Directory.CreateDirectory(_paths.EvidenceDir);
        return _paths.EvidenceDir;
    }

    public string? SaveSnapshot(Bitmap bmp, ViolationRecord v)
    {
        try
        {
            if (!_settings.Current.SaveSnapshot) return null;

            var root = GetEvidenceRoot();
            var dayDir = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd"));
            Directory.CreateDirectory(dayDir);

            var file = $"{v.TimeUtc:HHmmss}_{v.CameraName}_{v.Type}_{v.Confidence:0.00}.jpg"
                .Replace(" ", "_").Replace(":", "");
            var path = Path.Combine(dayDir, file);

            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Jpeg);
            _logs.Info($"Snapshot saved: {path}");
            return path;
        }
        catch (Exception ex)
        {
            _logs.Error("SaveSnapshot failed: " + ex.Message);
            return null;
        }
    }

    // Optional clip: để đơn giản demo, bạn có thể mở rộng sau
    public string? SaveClipPlaceholder(ViolationRecord v)
    {
        if (!_settings.Current.SaveShortClip) return null;
        // TODO: implement ring-buffer + VideoWriter if cần.
        return null;
    }
}
