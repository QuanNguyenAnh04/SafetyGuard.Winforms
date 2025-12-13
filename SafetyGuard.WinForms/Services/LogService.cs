using System;
using System.Collections.Concurrent;
using System.IO;

namespace SafetyGuard.WinForms.Services;

public sealed class LogService
{
    private readonly AppPaths _paths;
    private readonly ConcurrentQueue<string> _ring = new();

    public event Action<string>? OnLog;

    public LogService(AppPaths paths) => _paths = paths;

    public void Info(string msg) => Write("INFO", msg);
    public void Warn(string msg) => Write("WARN", msg);
    public void Error(string msg) => Write("ERROR", msg);

    private void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}";
        _ring.Enqueue(line);
        while (_ring.Count > 500) _ring.TryDequeue(out _);

        OnLog?.Invoke(line);

        try
        {
            var path = Path.Combine(_paths.LogsDir, $"app-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch { /* ignore */ }
    }

    public string[] SnapshotRing()
    {
        return _ring.ToArray();
    }
}
