namespace ContextSuite.Application.Infrastructure;

// Normal composition input. Production paths are fixed for the current user.
// UI tests compile the real application sources into their test-only host and supply isolated paths here.
internal sealed record ApplicationPaths(string Settings, string Publications, string Worker, string WorkerScratch)
{
    public string ActivationCleanupDirectory { get; init; } = ActivationStore.DirectoryPath;
    public string OfficeContexts => Path.Combine(WorkerScratch, "OfficeContexts");
    public static ApplicationPaths Production { get; } = new(SettingsStore.DefaultPath,
        OutputPublisher.DefaultRecordDirectory, Path.Combine(AppContext.BaseDirectory, "ContextSuite.Worker.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextSuite", "WorkerScratch"));
}
