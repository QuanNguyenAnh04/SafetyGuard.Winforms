using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using SafetyGuard.WinForms.Models;

namespace SafetyGuard.WinForms.Services;

public sealed class SqliteViolationRepository : IViolationRepository
{
    private readonly SqliteDb _db;

    public event Action? OnChanged;

    public SqliteViolationRepository(SqliteDb db) => _db = db;

    public void Add(ViolationRecord v)
    {
        using var con = _db.Open();
        con.Execute("""
            INSERT INTO violations(
              id, time_utc_ms, camera_id, camera_name, type, level, status, confidence,
              snapshot_path, clip_path, notes
            ) VALUES (
              @id, @t, @camId, @camName, @type, @level, @status, @conf,
              @snap, @clip, @notes
            )
        """, new
        {
            id = v.Id,
            t = Epoch.ToMs(v.TimeUtc),
            camId = v.CameraId,
            camName = v.CameraName,
            type = (int)v.Type,
            level = (int)v.Level,
            status = (int)v.Status,
            conf = v.Confidence,
            snap = v.SnapshotPath,
            clip = v.ClipPath,
            notes = v.Notes
        });

        OnChanged?.Invoke();
    }

    public void UpdateStatus(string id, ViolationStatus status, string? notes = null)
    {
        using var con = _db.Open();
        con.Execute("""
            UPDATE violations
            SET status=@st,
                notes=COALESCE(@notes, notes)
            WHERE id=@id
        """, new { id, st = (int)status, notes });

        OnChanged?.Invoke();
    }

    public IReadOnlyList<ViolationRecord> Query(
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        ViolationType? type = null,
        ViolationStatus? status = null,
        string? search = null,
        int limit = 2000)
    {
        using var con = _db.Open();

        var where = new List<string>();
        var p = new DynamicParameters();

        if (fromUtc is not null)
        {
            where.Add("time_utc_ms >= @fromMs");
            p.Add("fromMs", Epoch.ToMs(fromUtc.Value));
        }
        if (toUtc is not null)
        {
            where.Add("time_utc_ms <= @toMs");
            p.Add("toMs", Epoch.ToMs(toUtc.Value));
        }
        if (type is not null)
        {
            where.Add("type = @type");
            p.Add("type", (int)type.Value);
        }
        if (status is not null)
        {
            where.Add("status = @status");
            p.Add("status", (int)status.Value);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(camera_name LIKE @q OR notes LIKE @q)");
            p.Add("q", "%" + search + "%");
        }

        var sql = """
            SELECT
              id,
              time_utc_ms,
              camera_id,
              camera_name,
              type,
              level,
              status,
              confidence,
              snapshot_path,
              clip_path,
              notes
            FROM violations
        """;

        if (where.Count > 0)
            sql += " WHERE " + string.Join(" AND ", where);

        sql += " ORDER BY time_utc_ms DESC LIMIT @limit;";
        p.Add("limit", limit);

        var rows = con.Query(sql, p).ToList();

        return rows.Select(r => new ViolationRecord
        {
            Id = (string)r.id,
            TimeUtc = Epoch.FromMs((long)r.time_utc_ms),
            CameraId = (string)r.camera_id,
            CameraName = (string)r.camera_name,
            Type = (ViolationType)(long)r.type,
            Level = (ViolationLevel)(long)r.level,
            Status = (ViolationStatus)(long)r.status,
            Confidence = (float)(double)r.confidence,
            SnapshotPath = (string?)r.snapshot_path,
            ClipPath = (string?)r.clip_path,
            Notes = (string?)r.notes
        }).ToList();
    }

    public void DeleteOlderThanUtc(DateTime cutoffUtc)
    {
        using var con = _db.Open();
        con.Execute("DELETE FROM violations WHERE time_utc_ms < @cutoff;",
            new { cutoff = Epoch.ToMs(cutoffUtc) });

        OnChanged?.Invoke();
    }

    public long CountByDateRange(DateTime fromUtc, DateTime toUtc)
    {
        using var con = _db.Open();
        return con.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM violations WHERE time_utc_ms BETWEEN @a AND @b",
            new { a = Epoch.ToMs(fromUtc), b = Epoch.ToMs(toUtc) });
    }

    public long CountByDateRangeAndLevel(DateTime fromUtc, DateTime toUtc, ViolationLevel level)
    {
        using var con = _db.Open();
        return con.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM violations WHERE time_utc_ms BETWEEN @a AND @b AND level=@lv",
            new { a = Epoch.ToMs(fromUtc), b = Epoch.ToMs(toUtc), lv = (int)level });
    }
}
