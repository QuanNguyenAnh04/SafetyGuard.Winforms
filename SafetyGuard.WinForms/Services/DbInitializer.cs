using System;
using System.Linq;
using Dapper;
using SafetyGuard.WinForms.Models;

namespace SafetyGuard.WinForms.Services;

public static class DbInitializer
{
    public static void EnsureCreated(SqliteDb db)
    {
        using var con = db.Open();

        con.Execute("""
        PRAGMA journal_mode=WAL;
        PRAGMA foreign_keys=ON;
        """);

        con.Execute("""
        CREATE TABLE IF NOT EXISTS cameras(
          id TEXT PRIMARY KEY,
          name TEXT NOT NULL,
          rtsp_url TEXT NOT NULL,
          enabled INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS rules(
          type INTEGER PRIMARY KEY,              -- enum ViolationType (int)
          enabled INTEGER NOT NULL,
          threshold REAL NOT NULL,
          level INTEGER NOT NULL                 -- enum ViolationLevel (int)
        );

        CREATE TABLE IF NOT EXISTS settings(
          key TEXT PRIMARY KEY,
          value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS violations(
          id TEXT PRIMARY KEY,
          time_utc_ms INTEGER NOT NULL,          -- epoch ms UTC
          camera_id TEXT NOT NULL,
          camera_name TEXT NOT NULL,
          type INTEGER NOT NULL,
          level INTEGER NOT NULL,
          status INTEGER NOT NULL,
          confidence REAL NOT NULL,
          snapshot_path TEXT,
          clip_path TEXT,
          notes TEXT
        );

        CREATE INDEX IF NOT EXISTS idx_viol_time ON violations(time_utc_ms);
        CREATE INDEX IF NOT EXISTS idx_viol_type ON violations(type);
        CREATE INDEX IF NOT EXISTS idx_viol_status ON violations(status);
        """);

        SeedDefaults(con);
    }

    private static void SeedDefaults(System.Data.IDbConnection con)
    {
        // settings defaults
        UpsertSetting(con, "MinConsecutiveFrames", "6");
        UpsertSetting(con, "CooldownSeconds", "10");
        UpsertSetting(con, "RetentionDays", "30");
        UpsertSetting(con, "SaveSnapshot", "1");
        UpsertSetting(con, "SaveShortClip", "0");
        UpsertSetting(con, "ClipSeconds", "7");
        UpsertSetting(con, "EvidenceRoot", "");
        UpsertSetting(con, "EnableNotifications", "0");
        UpsertSetting(con, "SeedDemoData", "0");

        // rules defaults: ensure every ViolationType has a row (NoHelmet..Smoking)
        var existing = con.Query<int>("SELECT type FROM rules").ToHashSet();

        foreach (var t in Enum.GetValues(typeof(ViolationType)).Cast<ViolationType>())
        {
            var key = (int)t;
            if (existing.Contains(key)) continue;

            // sensible defaults
            var enabled = 1;
            var threshold = (t == ViolationType.Smoking) ? 0.55 : 0.45;
            var level = (t == ViolationType.NoHelmet) ? ViolationLevel.Critical : ViolationLevel.Warning;

            con.Execute("""
            INSERT INTO rules(type, enabled, threshold, level)
            VALUES(@type, @enabled, @threshold, @level)
            """, new { type = key, enabled, threshold, level = (int)level });
        }

        // cameras table can start empty
    }

    private static void UpsertSetting(System.Data.IDbConnection con, string key, string value)
    {
        con.Execute("""
        INSERT INTO settings(key,value) VALUES(@key,@value)
        ON CONFLICT(key) DO NOTHING
        """, new { key, value });
    }
}
