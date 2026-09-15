namespace ContextSuite.Application.Infrastructure;

// Normal composition input. Production has no command-line/environment override for trial or access.
// UI tests compile the real application sources into their test-only host and supply isolated paths here.
internal sealed record ApplicationPaths(string Settings, string Trial, string Publications, string Worker, string WorkerScratch)
{
    public string ActivationCleanupDirectory { get; init; } = ActivationStore.DirectoryPath;
    public string OfficeContexts => Path.Combine(WorkerScratch, "OfficeContexts");
    public static ApplicationPaths Production { get; } = new(SettingsStore.DefaultPath, LocalTrialStore.DefaultPath,
        OutputPublisher.DefaultRecordDirectory, Path.Combine(AppContext.BaseDirectory, "ContextSuite.Worker.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextSuite", "WorkerScratch"));
}
