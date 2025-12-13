using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using SafetyGuard.WinForms.Models;

namespace SafetyGuard.WinForms.Services;

public sealed class ExportService
{
    private readonly LogService _logs;
    public ExportService(LogService logs) => _logs = logs;

    public void ExportCsv(string path, IEnumerable<ViolationRecord> items)
    {
        var rows = items.ToList();
        using var sw = new StreamWriter(path);

        sw.WriteLine("TimeUTC,Camera,Type,Level,Status,Confidence,SnapshotPath,ClipPath,Notes");
        foreach (var v in rows)
        {
            sw.WriteLine($"{v.TimeUtc:O},{Esc(v.CameraName)},{v.Type},{v.Level},{v.Status},{v.Confidence:0.000},{Esc(v.SnapshotPath)},{Esc(v.ClipPath)},{Esc(v.Notes)}");
        }

        _logs.Info($"Export CSV: {path} ({rows.Count} rows)");
    }

    public void ExportExcel(string path, IEnumerable<ViolationRecord> items)
    {
        var rows = items.ToList();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Violations");

        ws.Cell(1, 1).Value = "TimeUTC";
        ws.Cell(1, 2).Value = "Camera";
        ws.Cell(1, 3).Value = "Type";
        ws.Cell(1, 4).Value = "Level";
        ws.Cell(1, 5).Value = "Status";
        ws.Cell(1, 6).Value = "Confidence";
        ws.Cell(1, 7).Value = "SnapshotPath";
        ws.Cell(1, 8).Value = "ClipPath";
        ws.Cell(1, 9).Value = "Notes";

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            ws.Cell(i + 2, 1).Value = r.TimeUtc.ToString("O");
            ws.Cell(i + 2, 2).Value = r.CameraName;
            ws.Cell(i + 2, 3).Value = r.Type.ToString();
            ws.Cell(i + 2, 4).Value = r.Level.ToString();
            ws.Cell(i + 2, 5).Value = r.Status.ToString();
            ws.Cell(i + 2, 6).Value = r.Confidence;
            ws.Cell(i + 2, 7).Value = r.SnapshotPath ?? "";
            ws.Cell(i + 2, 8).Value = r.ClipPath ?? "";
            ws.Cell(i + 2, 9).Value = r.Notes ?? "";
        }

        ws.Range(1, 1, 1, 9).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();

        wb.SaveAs(path);
        _logs.Info($"Export Excel: {path} ({rows.Count} rows)");
    }

    private static string Esc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
