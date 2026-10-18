using System.Security.Cryptography;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.ContractTests;

internal static class AvifPublicContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "avif-public-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "source.png");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);
        var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(sourcePath));
        var workerPath = Path.Combine(root, "ContextSuite.Worker.exe");
        await using var worker = new WorkerClient(workerPath);
        var trial = new LocalTrialStore(Path.Combine(root, "trial.json"));
        var facts = new ImageSourceFacts(Guid.NewGuid(), sourcePath, Convert.ToHexString(sourceHash), 4,
            ImageFormat.Png, 2, 2, 8, 1, true, false, "sRGB", []);

        check(!worker.HasAvifEncoder, "AVIF app availability requires the encoder and independent decoder payloads");
        await using (var view = new MainViewModel(worker, new() { PlayCompletionSound = false },
            new OutputPublisher(Path.Combine(root, "records"), null!), trial))
        {
            view.Admit(new(Guid.NewGuid(), "convert", "avif", [sourcePath]));
            await view.WaitForIdleAsync();
            check(view.Rows.Single().Result.State == OperationState.Unsupported && worker.ProcessId is null &&
                SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)).SequenceEqual(sourceHash),
                "AVIF direct command refuses a missing engine before worker launch or publication");
        }
        await using (var unavailable = new ConversionViewModel(worker, trial, Guid.NewGuid(),
            [new(facts.ItemId, sourcePath, facts, null)], new("convert", new()), false))
            check(!unavailable.Formats.Any(format => format.Value == ImageFormat.Avif),
                "AVIF legacy target choice is hidden without the required engine files");

        Directory.CreateDirectory(Path.Combine(root, "avif"));
        Directory.CreateDirectory(Path.Combine(root, "heif"));
        foreach (var path in new[] { Path.Combine(root, "avif", "ContextSuite.Avif.Native.dll"),
            Path.Combine(root, "heif", "ContextSuite.Heif.Native.dll"),
            Path.Combine(root, "heif", "heif.dll"), Path.Combine(root, "heif", "libde265.dll") })
            await File.WriteAllBytesAsync(path, [0]);
        check(worker.HasAvifEncoder, "AVIF app availability requires all four adjacent native files");
        await using (var available = new ConversionViewModel(worker, trial, Guid.NewGuid(),
            [new(facts.ItemId, sourcePath, facts, null)], new("convert", new(ReplaceOriginals: true)), true))
        {
            check(available.Formats.Last().Value == ImageFormat.Avif,
                "AVIF legacy target choice follows ICO in stable order");
            available.Target = ImageFormat.Avif;
            check(!available.HasQuality && !available.ReplaceOriginal && !available.CanReplaceOriginal &&
                available.OutputNotice.Contains("Originals kept", StringComparison.Ordinal),
                "AVIF focused choice uses fixed quality and mandatory copies");
            await available.InitializeAsync();
            ConfirmedImageBatch? confirmed = null;
            available.Confirmed += value => confirmed = value;
            available.ConfirmCommand.Execute(null);
            check(confirmed?.Plan.Options.Quality == ImageConversionOptions.AvifQuality,
                "AVIF focused choice captures fixed quality 95 in its confirmed plan");
        }
    }
}
