using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
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
        ws.Cell(1, 7).Value = "Snapshot"; // embed image
        ws.Cell(1, 8).Value = "Clip";     // hyperlink
        ws.Cell(1, 9).Value = "Notes";

        ws.Range(1, 1, 1, 9).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);

        const int imgW = 120;
        const int imgH = 80;

        // ✅ set kích thước cột ảnh trước khi add picture
        ws.Column(7).Width = 18;  // ~120px
        ws.Column(8).Width = 28;
        ws.Column(9).Width = 40;

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            int row = i + 2;

            ws.Cell(row, 1).Value = r.TimeUtc.ToString("O");
            ws.Cell(row, 2).Value = r.CameraName;
            ws.Cell(row, 3).Value = r.Type.ToString();
            ws.Cell(row, 4).Value = r.Level.ToString();
            ws.Cell(row, 5).Value = r.Status.ToString();
            ws.Cell(row, 6).Value = r.Confidence;

            // ===== Embed snapshot =====
            if (!string.IsNullOrWhiteSpace(r.SnapshotPath) && File.Exists(r.SnapshotPath))
            {
                ws.Row(row).Height = 60; // đủ cho ảnh ~80px

                var cell = ws.Cell(row, 7);
                cell.Value = Path.GetFileName(r.SnapshotPath);

                var pic = ws.AddPicture(r.SnapshotPath);
                pic.Name = $"snap_{row}";
                pic.Placement = XLPicturePlacement.Move;     // ✅ neo theo cell (không dồn)
                pic.MoveTo(cell, 2, 2);
                pic.WithSize(imgW, imgH);
            }

            // ===== Clip hyperlink =====
            if (!string.IsNullOrWhiteSpace(r.ClipPath))
            {
                var clipCell = ws.Cell(row, 8);
                clipCell.Value = Path.GetFileName(r.ClipPath);
                if (File.Exists(r.ClipPath))
                    clipCell.SetHyperlink(new XLHyperlink(r.ClipPath));
            }

            ws.Cell(row, 9).Value = r.Notes ?? "";
        }

        // ✅ Chỉ adjust cột text, KHÔNG adjust toàn sheet sau khi add ảnh
        ws.Columns(1, 6).AdjustToContents();
        ws.Column(8).AdjustToContents();
        ws.Column(9).AdjustToContents();

        wb.SaveAs(path);
    }




    private static string Esc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
