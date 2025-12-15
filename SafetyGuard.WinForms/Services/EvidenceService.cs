using System;
using System.Drawing;
using System.IO;
using SafetyGuard.WinForms.Models;

namespace SafetyGuard.WinForms.Services;

public sealed class EvidenceService
{
    private readonly AppPaths _paths;
    private readonly IAppSettingsService _settings;
    private readonly LogService _logs;

    public EvidenceService(AppPaths paths, IAppSettingsService settings, LogService logs)
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

            var safeCam = SanitizeFileName(v.CameraName);
            var file = $"{v.TimeUtc:HHmmss_fff}_{safeCam}_{v.Type}_{v.Confidence:0.00}.jpg"
                .Replace(" ", "_");

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

    public string? SaveClipPlaceholder(ViolationRecord v)
    {
        if (!_settings.Current.SaveShortClip) return null;
        return null;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
