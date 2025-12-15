using System;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;

namespace SafetyGuard.WinForms.Services;

public sealed class SqliteDb
{
    private readonly string _dbPath;
    private readonly string _connString;

    public string DbPath => _dbPath;

    public SqliteDb(AppPaths paths, LogService logs)
    {
        // ưu tiên dùng AppPaths nếu có property DbPath / DbFilePath / DbFile
        _dbPath =
            GetPath(paths, "DbPath") ??
            GetPath(paths, "DbFilePath") ??
            GetPath(paths, "DbFile") ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SafetyGuard",
                "safetyguard.db"
            );

        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        _connString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        logs.Info($"SQLite DB: {_dbPath}");
    }

    public IDbConnection Open()
    {
        var con = new SqliteConnection(_connString);
        con.Open();
        return con;
    }

    private static string? GetPath(object obj, string propName)
    {
        var p = obj.GetType().GetProperty(propName);
        if (p == null) return null;
        return p.GetValue(obj) as string;
    }
}
