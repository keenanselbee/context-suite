using System.Security.Cryptography;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.ContractTests;

internal static class OptimizationPresetWorkflowContracts
{
    internal static async Task RunAsync(string root, string executable, string fixtures, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".xlsx", ".pptx", ".odt", ".ods", ".odp", ".epub", ".tga" };
        var inputs = Directory.GetFiles(fixtures).Where(path => extensions.Contains(Path.GetExtension(path))).Order(StringComparer.Ordinal).ToArray();
        check(inputs.Length >= 4, "preset integration: independent document and TGA inputs available");
        var sources = new List<string>();
        foreach (var input in inputs)
        {
            var path = Path.Combine(root, Path.GetFileName(input));
            File.Copy(input, path, overwrite: false);
            sources.Add(path);
        }
        var originals = sources.ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
        var times = sources.ToDictionary(path => path, File.GetLastWriteTimeUtc);
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle(), replacementVerified: true);
        IOperationAccess access = new OperationAdmissionService();
        await using var view = new MainViewModel(new WorkerClient(executable, Path.Combine(root, "worker")),
            new SuiteSettings { PlayCompletionSound = false, Optimize = new(ReplaceOriginals: true) }, publisher, access);
        var receipts = new List<object>();
        foreach (var preset in Enum.GetValues<ImageOptimizationPreset>())
        {
            var start = view.Rows.Count;
            view.Admit(new(Guid.NewGuid(), "optimize", preset.ToString().ToLowerInvariant(), [..sources]));
            await view.WaitForIdleAsync();
            var rows = view.Rows.Skip(start).ToArray();
            check(rows.Length == sources.Count, $"preset integration {preset}: one result per selected input");
            foreach (var row in rows)
            {
                var supportsLoss = Path.GetExtension(row.Path) is ".pdf" or ".docx" or ".xlsx" or ".pptx";
                var unsupported = !supportsLoss && preset is ImageOptimizationPreset.Balanced or ImageOptimizationPreset.Smallest;
                check(unsupported ? row.Result.State == OperationState.Unsupported :
                    row.Result.State is OperationState.Succeeded or OperationState.Unchanged,
                    $"preset integration {preset}/{Path.GetFileName(row.Path)}: supported action completes or explains unavailable quality tier ({row.Result.State}: {row.Result.Message})");
                check(row.Result.Publication?.IsCommitted != true ||
                    row.Result.Publication is { Outcome: PublicationOutcome.CopyCreated, OutputPath: { } output } &&
                    new FileInfo(output).Length < new FileInfo(row.Path).Length,
                    $"preset integration {preset}/{Path.GetFileName(row.Path)}: only smaller copies published despite overwrite preference");
                receipts.Add(new { preset, source = Path.GetFileName(row.Path), row.Result.State, row.Result.Message,
                    row.Result.EngineIdentity, output = row.Result.Publication?.OutputPath });
            }
            check(originals.All(pair => File.GetLastWriteTimeUtc(pair.Key) == times[pair.Key] &&
                SHA256.HashData(File.ReadAllBytes(pair.Key)).SequenceEqual(pair.Value)),
                $"preset integration {preset}: source bytes and timestamps unchanged");
        }
        check(!Directory.GetFiles(root, ".context-suite-*.tmp").Any() &&
            !Directory.GetFiles(Path.Combine(root, "records")).Any(),
            "preset integration: completed and unsupported batches leave no reservation or publication journals");
        await File.WriteAllTextAsync(Path.Combine(root, "results.json"), System.Text.Json.JsonSerializer.Serialize(receipts,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Expanded optimization must keep originals.");
    }
}
