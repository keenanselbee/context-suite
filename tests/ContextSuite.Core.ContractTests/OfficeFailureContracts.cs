using System.Text.Json;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Office;
using ContextSuite.Core.Pdf;

internal static class OfficeFailureContracts
{
    internal static void Run(Action<bool, string> check)
    {
        var native = new IOException(@"Private engine failure at C:\private\customer.docx");
        foreach (var command in new[] { "office-export", "office-pdf-validate" })
        {
            var officeCategories = Enum.GetValues<ImageFailure>().Where(failure => !PdfDarkFailures.IsFeatureLimit(failure));
            foreach (var failure in Enum.GetValues<ImageFailure>().Where(PdfDarkFailures.IsFeatureLimit))
            {
                var refused = false;
                try { _ = new MediaWorkerException(failure, command: command); }
                catch (InvalidDataException) { refused = true; }
                check(refused, "Office refuses PDF dark-only failure category for " + command);
            }
            var failures = officeCategories.Select(failure => new MediaWorkerException(failure, native, command)).ToArray();
            check(failures.All(error => !error.Message.Contains("image", StringComparison.OrdinalIgnoreCase) &&
                !error.Message.Contains("damaged", StringComparison.OrdinalIgnoreCase) && !error.Message.Contains("private") &&
                ReferenceEquals(error.InnerException, native)), "Office failures: safe document wording without native paths for " + command);
            check(failures.Single(error => error.Failure == ImageFailure.TimedOut).Message.Contains("too long") &&
                failures.Single(error => error.Failure == ImageFailure.WorkerTerminated).Message.Contains("stopped unexpectedly"),
                "Office failures: timeout and stopped worker have distinct guidance for " + command);
            check(failures.Single(error => error.Failure == ImageFailure.OfficeDateFidelity).Message == ImageFailureException.Describe(ImageFailure.OfficeDateFidelity),
                "Office failures: known calculated-date explanation is preserved for " + command);
        }
        var export = new MediaWorkerException(ImageFailure.InvalidInput, command: "office-export");
        var validation = new MediaWorkerException(ImageFailure.InvalidInput, command: "office-pdf-validate");
        check(export.Message.Contains("read or convert") && validation.Message.Contains("could not be validated and was not saved"),
            "Office failures: failed export and refused PDF validation are distinguished");
        foreach (var command in new[] { "image-convert", "audio-convert", "pdf-render-page" })
            check(Enum.GetValues<ImageFailure>().All(failure => new MediaWorkerException(failure, command: command).Message ==
                new MediaWorkerException(failure).Message), "Office failures: existing exception wording unchanged for " + command);
    }

    internal static async Task RunWorkerAsync(string executable, string scratch)
    {
        var root = Path.GetFullPath(scratch);
        if (!root.Contains("\\.codex-temp\\office-failures\\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(root))
            throw new InvalidDataException("Use a fresh repository Office failure evidence directory.");
        Directory.CreateDirectory(root);
        var item = Guid.NewGuid();
        var work = new OfficeExportWork(item, Path.Combine(root, "office-" + item.ToString("N")),
            "ContextSuite.Office.Evaluation." + item.ToString("N"), "docx", "none", 1, new string('A', 64));
        Directory.CreateDirectory(work.DirectoryPath);
        var marker = Path.Combine(work.DirectoryPath, "unexpected-private-context.txt");
        const string contents = "Authored context refusal; do not modify.";
        File.WriteAllText(marker, contents);
        var checks = new List<string>();
        var errors = new List<object>();
        await using (var worker = new WorkerClient(executable, Path.Combine(root, "worker")))
        {
            await worker.GetCapabilitiesAsync(default); var process = worker.ProcessId;
            await Refused(() => worker.ExportOfficeAsync(work, default), ImageFailure.InvalidInput, "read or convert");
            var missing = Guid.NewGuid();
            var missingWork = work with { ItemId = missing, DirectoryPath = Path.Combine(root, "office-" + missing.ToString("N")),
                ProfileName = "ContextSuite.Office.Evaluation." + missing.ToString("N") };
            // Missing native directory handles currently use the stable generic
            // engine category. Do not change classification to satisfy this test.
            await Refused(() => worker.ExportOfficeAsync(missingWork, default), ImageFailure.EngineFailure, "could not finish");
            var candidate = new OfficeExportCandidate(item, new(Guid.NewGuid(), "docx", "none", 1, 1, new string('A', 64), new string('B', 64), []), work.Policy);
            await Refused(() => worker.ValidateOfficePdfAsync(new(work, candidate, Path.Combine(root, $".context-suite-{item:N}.tmp")), default),
                ImageFailure.FileAccess, "could not be accessed");
            Directory.CreateDirectory(Path.GetDirectoryName(work.SourcePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(work.CandidatePath)!);
            File.WriteAllBytes(work.SourcePath, [0x41]);
            File.WriteAllBytes(work.CandidatePath, [0x42]);
            var reservation = Path.Combine(root, $".context-suite-{item:N}.tmp");
            File.WriteAllBytes(reservation, []);
            await Refused(() => worker.ValidateOfficePdfAsync(new(work, candidate, reservation), default),
                ImageFailure.InvalidInput, "could not be validated and was not saved");
            Check(File.ReadAllBytes(work.SourcePath).SequenceEqual(new byte[] { 0x41 }) &&
                File.ReadAllBytes(work.CandidatePath).SequenceEqual(new byte[] { 0x42 }) && new FileInfo(reservation).Length == 0,
                "fabricated completion identity is refused before PDF parsing or output copying");
            await worker.GetCapabilitiesAsync(default);
            Check(worker.ProcessId == process, "worker remains usable after all four Office refusals");
            Check(File.ReadAllText(marker) == contents && Directory.GetFiles(work.DirectoryPath).Length == 1,
                "refused Office work leaves authored context bytes unchanged");
        }
        Check(!Directory.Exists(Path.Combine(root, "worker")) || !Directory.EnumerateFileSystemEntries(Path.Combine(root, "worker")).Any(),
            "worker exits and releases its scratch directory");
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks, Errors = errors,
            Scope = "Actual worker refusals before native Office launch; no Windows profile or export is created." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} Office failure worker checks.");

        async Task Refused(Func<Task> action, ImageFailure expected, string text)
        {
            try { await action(); throw new InvalidDataException("Office refusal was not reported."); }
            catch (MediaWorkerException error)
            {
                errors.Add(new { Failure = error.Failure.ToString(), error.Message });
                File.WriteAllText(Path.Combine(root, "observations.json"), JsonSerializer.Serialize(errors));
                Check(error.Failure == expected && error.Message.Contains(text) && !error.Message.Contains("image", StringComparison.OrdinalIgnoreCase) &&
                    !error.Message.Contains(root) && !error.Message.Contains("unexpected-private-context"), "typed safe Office refusal: " + expected);
            }
        }
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); checks.Add(message); Console.WriteLine("PASS: " + message); }
    }
}
