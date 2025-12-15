using System;
using System.Globalization;
using System.Linq;
using Dapper;
using SafetyGuard.WinForms.Models;

namespace SafetyGuard.WinForms.Services;

public sealed class SqliteAppSettingsService : IAppSettingsService
{
    private readonly SqliteDb _db;

    public AppSettings Current { get; private set; } = new();

    public event Action<AppSettings>? OnChanged;

    public SqliteAppSettingsService(SqliteDb db)
    {
        _db = db;
        Reload();
    }

    public void Reload()
    {
        using var con = _db.Open();

        var cams = con.Query("SELECT id, name, rtsp_url AS RtspUrl, enabled AS Enabled FROM cameras")
            .Select(r => new CameraConfig
            {
                Id = (string)r.id,
                Name = (string)r.name,
                RtspUrl = (string)r.RtspUrl,
                Enabled = ((long)r.Enabled) == 1
            })
            .ToList();

        var rules = con.Query("SELECT type, enabled, threshold, level FROM rules")
            .Select(r => new DetectionRule
            {
                Type = (ViolationType)(long)r.type,
                Enabled = ((long)r.enabled) == 1,
                ConfidenceThreshold = (float)(double)r.threshold,
                Level = (ViolationLevel)(long)r.level
            })
            .ToList();

        string Get(string key, string def)
            => con.QuerySingleOrDefault<string>("SELECT value FROM settings WHERE key=@k", new { k = key }) ?? def;

        int GetInt(string key, int def)
            => int.TryParse(Get(key, def.ToString()), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

        bool GetBool(string key, bool def)
            => Get(key, def ? "1" : "0") == "1";

        Current = new AppSettings
        {
            Cameras = cams,
            Rules = rules,
            MinConsecutiveFrames = GetInt("MinConsecutiveFrames", 6),
            CooldownSeconds = GetInt("CooldownSeconds", 10),
            RetentionDays = GetInt("RetentionDays", 30),
            SaveSnapshot = GetBool("SaveSnapshot", true),
            SaveShortClip = GetBool("SaveShortClip", false),
            ClipSeconds = GetInt("ClipSeconds", 7),
            EvidenceRoot = Get("EvidenceRoot", ""),
            EnableNotifications = GetBool("EnableNotifications", false),
            SeedDemoData = GetBool("SeedDemoData", false)
        };

        NormalizeRules(Current);
        OnChanged?.Invoke(Current);
    }

    public void Save(AppSettings s)
    {
        using var con = _db.Open();

        // settings
        Upsert(con, "MinConsecutiveFrames", s.MinConsecutiveFrames.ToString(CultureInfo.InvariantCulture));
        Upsert(con, "CooldownSeconds", s.CooldownSeconds.ToString(CultureInfo.InvariantCulture));
        Upsert(con, "RetentionDays", s.RetentionDays.ToString(CultureInfo.InvariantCulture));
        Upsert(con, "SaveSnapshot", s.SaveSnapshot ? "1" : "0");
        Upsert(con, "SaveShortClip", s.SaveShortClip ? "1" : "0");
        Upsert(con, "ClipSeconds", s.ClipSeconds.ToString(CultureInfo.InvariantCulture));
        Upsert(con, "EvidenceRoot", s.EvidenceRoot ?? "");
        Upsert(con, "EnableNotifications", s.EnableNotifications ? "1" : "0");
        Upsert(con, "SeedDemoData", s.SeedDemoData ? "1" : "0");

        // cameras (đồ án: replace-all là OK)
        con.Execute("DELETE FROM cameras");
        foreach (var c in s.Cameras)
        {
            var id = string.IsNullOrWhiteSpace(c.Id) ? Guid.NewGuid().ToString("N") : c.Id;
            con.Execute("""
                INSERT INTO cameras(id,name,rtsp_url,enabled)
                VALUES(@id,@name,@url,@en)
            """, new { id, name = c.Name, url = c.RtspUrl, en = c.Enabled ? 1 : 0 });

            c.Id = id; // nếu CameraConfig cho set
        }

        // rules (upsert per type; ensure unique)
        NormalizeRules(s);
        foreach (var r in s.Rules)
        {
            con.Execute("""
                INSERT INTO rules(type, enabled, threshold, level)
                VALUES(@type,@en,@th,@lv)
                ON CONFLICT(type) DO UPDATE SET enabled=@en, threshold=@th, level=@lv
            """, new
            {
                type = (int)r.Type,
                en = r.Enabled ? 1 : 0,
                th = r.ConfidenceThreshold,
                lv = (int)r.Level
            });
        }

        Current = s;
        OnChanged?.Invoke(Current);
    }

    private static void Upsert(System.Data.IDbConnection con, string key, string value)
    {
        con.Execute("""
            INSERT INTO settings(key,value) VALUES(@key,@value)
            ON CONFLICT(key) DO UPDATE SET value=@value
        """, new { key, value });
    }

    private static void NormalizeRules(AppSettings s)
    {
        s.Rules = s.Rules
            .GroupBy(r => r.Type)
            .Select(g => g.Last())
            .ToList();

        // ensure every enum exists
        foreach (var t in Enum.GetValues(typeof(ViolationType)).Cast<ViolationType>())
        {
            if (s.Rules.Any(r => r.Type == t)) continue;

            s.Rules.Add(new DetectionRule
            {
                Type = t,
                Enabled = true,
                ConfidenceThreshold = (t == ViolationType.Smoking) ? 0.55f : 0.45f,
                Level = (t == ViolationType.NoHelmet) ? ViolationLevel.Critical : ViolationLevel.Warning
            });
        }
    }
}
