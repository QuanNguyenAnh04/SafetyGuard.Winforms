using SafetyGuard.WinForms.Services;
using SafetyGuard.WinForms.Vision;


namespace SafetyGuard.WinForms;

public sealed class AppBootstrap
{
    public AppSettingsService Settings { get; }
    public LogService Logs { get; }
    public ViolationRepository Violations { get; }
    public EvidenceService Evidence { get; }
    public ExportService Export { get; }
    public ViolationEngine Engine { get; }
    public OfflineAnalyzer Offline { get; }

    // Detection
    public IDetector Detector { get; }

    private AppBootstrap(
        AppSettingsService settings,
        LogService logs,
        ViolationRepository violations,
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
        var settings = new AppSettingsService(paths, logs);
        var repo = new ViolationRepository(paths, logs);
        var evidence = new EvidenceService(paths, settings, logs);
        var export = new ExportService(logs);

        // Default: dummy detector (demo UI/logic). Switch to OnnxDetector later.
        IDetector detector = new DummyDetector(settings, logs);

        var engine = new ViolationEngine(settings, repo, evidence, logs);
        var offline = new OfflineAnalyzer(detector, engine, logs);

        // Seed demo data if empty
        DemoDataSeeder.SeedIfEmpty(repo, logs);

        return new AppBootstrap(settings, logs, repo, evidence, export, engine, offline, detector);
    }
}

