using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SafetyGuard.WinForms.Models;

namespace SafetyGuard.WinForms.Services;

public sealed class ViolationRepository
{
    private readonly AppPaths _paths;
    private readonly LogService _logs;
    private readonly object _lock = new();

    private List<ViolationRecord> _items = new();

    public event Action? OnChanged;

    public ViolationRepository(AppPaths paths, LogService logs)
    {
        _paths = paths;
        _logs = logs;
        _items = Load();
    }

    public IReadOnlyList<ViolationRecord> All()
    {
        lock (_lock) return _items.ToList();
    }

    public void Add(ViolationRecord v)
    {
        lock (_lock)
        {
            _items.Add(v);
            SaveLocked();
        }
        OnChanged?.Invoke();
    }

    public void UpdateStatus(string id, ViolationStatus status)
    {
        lock (_lock)
        {
            var it = _items.FirstOrDefault(x => x.Id == id);
            if (it == null) return;
            it.Status = status;
            SaveLocked();
        }
        OnChanged?.Invoke();
    }

    public void RemoveOlderThan(int retentionDays, Func<ViolationRecord, bool>? predicate = null)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        lock (_lock)
        {
            var before = _items.Count;
            _items = _items.Where(x =>
            {
                if (x.TimeUtc < cutoff) return false;
                if (predicate != null && !predicate(x)) return false;
                return true;
            }).ToList();
            if (_items.Count != before) SaveLocked();
        }
        OnChanged?.Invoke();
    }

    private List<ViolationRecord> Load()
    {
        try
        {
            if (!File.Exists(_paths.ViolationsPath)) return new();
            var json = File.ReadAllText(_paths.ViolationsPath);
            var list = JsonSerializer.Deserialize<List<ViolationRecord>>(json) ?? new();
            _logs.Info($"Loaded violations: {list.Count}");
            return list;
        }
        catch (Exception ex)
        {
            _logs.Error("Violation load failed: " + ex.Message);
            return new();
        }
    }

    private void SaveLocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.ViolationsPath)!);
        var json = JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_paths.ViolationsPath, json);
    }
}
