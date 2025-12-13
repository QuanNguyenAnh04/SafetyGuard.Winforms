using System;
using System.Linq;
using SafetyGuard.WinForms.Models;

namespace SafetyGuard.WinForms.Services;

public static class DemoDataSeeder
{
    public static void SeedIfEmpty(ViolationRepository repo, LogService logs)
    {
        var all = repo.All();
        if (all.Count > 0) return;

        var rnd = new Random(7);
        var cams = new[] { "Gate Cam", "Workshop Cam", "Warehouse Cam", "Packing Cam" };
        var types = Enum.GetValues(typeof(ViolationType)).Cast<ViolationType>().ToArray();

        for (int i = 0; i < 40; i++)
        {
            var t = DateTime.UtcNow.AddHours(-rnd.Next(1, 72));
            var cam = cams[rnd.Next(cams.Length)];
            var type = types[rnd.Next(types.Length)];

            repo.Add(new ViolationRecord
            {
                TimeUtc = t,
                CameraId = cam.Replace(" ", "").ToLowerInvariant(),
                CameraName = cam,
                Type = type,
                Level = type == ViolationType.Smoking ? ViolationLevel.Critical : ViolationLevel.Warning,
                Status = (ViolationStatus)rnd.Next(0, 4),
                Confidence = (float)(0.45 + rnd.NextDouble() * 0.5),
                Notes = "Demo data"
            });
        }

        logs.Info("Seeded demo violations.");
    }
}
