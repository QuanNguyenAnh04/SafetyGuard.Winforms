using System;
using System.Drawing;
using System.Globalization;
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

    // =========================
    // Delete / Cleanup helpers
    // =========================

    public bool TryDeleteEvidence(ViolationRecord v)
    {
        var ok1 = TryDeleteFile(v.SnapshotPath);
        var ok2 = TryDeleteFile(v.ClipPath);
        return ok1 && ok2;
    }

    public void CleanupEvidenceOlderThan(DateTime cutoffUtc)
    {
        try
        {
            var root = GetEvidenceRoot();
            if (!Directory.Exists(root)) return;

            var cutoffDate = cutoffUtc.Date;

            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(name)) continue;

                // chỉ xóa folder theo format yyyyMMdd
                if (!DateTime.TryParseExact(
                        name,
                        "yyyyMMdd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var folderDate))
                    continue;

                if (folderDate.Date < cutoffDate)
                    TryDeleteDirectory(dir);
            }
        }
        catch (Exception ex)
        {
            _logs.Error("CleanupEvidenceOlderThan failed: " + ex.Message);
        }
    }

    public void DeleteAllEvidence()
    {
        try
        {
            var root = GetEvidenceRoot();
            if (!Directory.Exists(root)) return;

            foreach (var dir in Directory.GetDirectories(root))
                TryDeleteDirectory(dir);
        }
        catch (Exception ex)
        {
            _logs.Error("DeleteAllEvidence failed: " + ex.Message);
        }
    }

    private bool TryDeleteFile(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            if (!File.Exists(path)) return true;

            File.Delete(path);
            _logs.Info($"Evidence deleted: {path}");
            return true;
        }
        catch (Exception ex)
        {
            _logs.Error("TryDeleteFile failed: " + ex.Message);
            return false;
        }
    }

    private bool TryDeleteDirectory(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return true;
            Directory.Delete(dir, recursive: true);
            _logs.Info($"Evidence folder deleted: {dir}");
            return true;
        }
        catch (Exception ex)
        {
            _logs.Error("TryDeleteDirectory failed: " + ex.Message);
            return false;
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    public string? SavePersonSnapshot(Bitmap fullFrame, ViolationRecord v, BoundingBox personBox)
    {
        try
        {
            if (!_settings.Current.SaveSnapshot) return null;

            var rect = personBox.ToRectClamped(fullFrame.Width, fullFrame.Height);
            if (rect.Width < 2 || rect.Height < 2)
                return SaveSnapshot(fullFrame, v);

            using var crop = new Bitmap(rect.Width, rect.Height);
            using (var g = Graphics.FromImage(crop))
            {
                g.DrawImage(fullFrame,
                    destRect: new Rectangle(0, 0, rect.Width, rect.Height),
                    srcRect: rect,
                    srcUnit: GraphicsUnit.Pixel);
            }

            return SaveSnapshot(crop, v);
        }
        catch (Exception ex)
        {
            _logs.Error("SavePersonSnapshot failed: " + ex.Message);
            return null;
        }
    }

}
