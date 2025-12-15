using SafetyGuard.WinForms.Services;
using SafetyGuard.WinForms.Vision;

namespace SafetyGuard.WinForms;

public sealed class AppBootstrap
{
    public IAppSettingsService Settings { get; }
    public LogService Logs { get; }
    public IViolationRepository Violations { get; }
    public EvidenceService Evidence { get; }
    public ExportService Export { get; }
    public ViolationEngine Engine { get; }
    public OfflineAnalyzer Offline { get; }

    public IDetector Detector { get; }

    private AppBootstrap(
        IAppSettingsService settings,
        LogService logs,
        IViolationRepository violations,
        EvidenceService evidence,
        ExportService export,
        ViolationEngine engine,
        OfflineAnalyzer offline,
        IDetector detector)
    {
        Settings = settings;
        Logs = logs;
        Violations = violations;
        Evidence = evidence;
        Export = export;
        Engine = engine;
        Offline = offline;
        Detector = detector;
    }

    public static AppBootstrap Build()
    {
        var paths = new AppPaths();
        var logs = new LogService(paths);

        // ===== SQLite =====
        var db = new SqliteDb(paths, logs);
        DbInitializer.EnsureCreated(db);

        IAppSettingsService settings = new SqliteAppSettingsService(db);
        IViolationRepository repo = new SqliteViolationRepository(db);

        var evidence = new EvidenceService(paths, settings, logs);
        var export = new ExportService(logs);

        // demo detector (sau này thay OnnxDetector)
        IDetector detector = new DualOnnxDetector(settings, logs);

        var engine = new ViolationEngine(settings, repo, evidence, logs);
        var offline = new OfflineAnalyzer(detector, engine, logs);

        return new AppBootstrap(settings, logs, repo, evidence, export, engine, offline, detector);
    }
}
