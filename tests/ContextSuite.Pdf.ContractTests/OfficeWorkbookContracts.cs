using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Office;
using ContextSuite.Private.Office;

internal static class OfficeWorkbookContracts
{
    internal static async Task RunAsync(string retained, string evidence)
    {
        var root = Path.GetFullPath(evidence);
        retained = Path.GetFullPath(retained);
        if (!root.Contains("\\.codex-temp\\office-workbook-", StringComparison.OrdinalIgnoreCase) || Directory.Exists(root) ||
            !retained.Contains("\\.codex-temp\\office-engine\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use retained authored native snapshots and fresh repository scratch.");
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(retained, "office-evaluation.json")));
        if (report.RootElement.GetProperty("Mode").GetString() != "ExcelNativeDateSnapshots") throw new InvalidDataException("Expected native snapshot evidence.");
        var cases = new HashSet<(string, string)>(from name in new[] { "1900-default", "1900-explicit", "1904" }
            from calculation in new[] { "calc-always", "calc-never" } select ("Excel dates " + name + ".xlsx", calculation));
        foreach (var item in report.RootElement.GetProperty("Results").EnumerateArray())
        {
            var name = item.GetProperty("Source").GetString()!; var style = item.GetProperty("ProfileStyle").GetString()!;
            if (!cases.Remove((name, style))) throw new InvalidDataException("Unexpected workbook case.");
            var source = Path.Combine(retained, Path.GetFileNameWithoutExtension(name) + "-" + style, name);
            var bytes = File.ReadAllBytes(source); var written = File.GetLastWriteTimeUtc(source);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (hash != item.GetProperty("SnapshotSha256").GetString()) throw new InvalidDataException("Changed workbook evidence.");
            var id = Guid.NewGuid(); var directory = Path.Combine(root, "office-" + id.ToString("N"));
            var work = new OfficeExportWork(id, directory, "ContextSuite.Office." + id.ToString("N"), "xlsx",
                style == "calc-always" ? "recalculate" : "cached", 1, new string('A', 64));
            Directory.CreateDirectory(Path.GetDirectoryName(work.WorkbookPath)!);
            File.WriteAllBytes(work.WorkbookPath, bytes);
            var completion = new OfficeHostCompletion(Guid.NewGuid(), "xlsx", work.Calculation, 1, 1,
                work.SourceSha256, new string('B', 64), [], bytes.Length, hash);
            var refusesDates = style == "calc-always" && !name.Contains("1904", StringComparison.Ordinal);
            if (refusesDates)
                await Refuse(() => OfficeWorkbookValidation.CheckAsync(work, completion, default), name + "/" + style + ": calculated early dates refused",
                    error => error.Message.Contains("calculated workbook contains dates", StringComparison.Ordinal));
            else
            {
                await OfficeWorkbookValidation.CheckAsync(work, completion, default);
                Check(true, name + "/" + style + ": saved/1904 date control accepted");
            }
            Check(File.ReadAllBytes(work.WorkbookPath).SequenceEqual(bytes), "inspection preserves workbook bytes");
            using (new FileStream(work.WorkbookPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            Check(true, "inspection releases workbook lease");
            await OfficeWorkbookInterruptionContracts.CheckAsync(work, completion, bytes, Check);
            if (refusesDates)
                await Refuse(() => OfficeWorkbookValidation.CheckAsync(work, completion, default), "following validation retains early-date refusal",
                    error => error.Message.Contains("calculated workbook contains dates", StringComparison.Ordinal));
            else
            {
                await OfficeWorkbookValidation.CheckAsync(work, completion, default);
                Check(true, "following validation accepts the same saved/1904 control");
            }
            await Refuse(() => OfficeWorkbookValidation.CheckAsync(work, completion with { WorkbookSha256 = new string('0', 64) }, default), "changed workbook hash refused");
            await Refuse(() => OfficeWorkbookValidation.CheckAsync(work, completion with { WorkbookBytes = bytes.Length + 1 }, default), "changed workbook length refused");
            await Refuse(() => OfficeWorkbookValidation.CheckAsync(work, completion with { WorkbookBytes = 0, WorkbookSha256 = null }, default), "missing Excel evidence refused");
            File.Move(work.WorkbookPath, work.WorkbookPath + ".held");
            await Refuse(() => OfficeWorkbookValidation.CheckAsync(work, completion, default), "missing workbook file refused");
            File.Move(work.WorkbookPath + ".held", work.WorkbookPath);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await OfficeWorkbookValidation.CheckAsync(work, completion, cancelled.Token); throw new InvalidOperationException("Cancellation ignored."); }
            catch (OperationCanceledException) { Check(true, "cancelled workbook validation stops"); }
            byte[] invalid = [80, 75, 3, 4, 0];
            File.WriteAllBytes(work.WorkbookPath, invalid);
            var malformed = completion with { WorkbookBytes = invalid.Length, WorkbookSha256 = Convert.ToHexString(SHA256.HashData(invalid)) };
            await Refuse(() => OfficeWorkbookValidation.CheckAsync(work, malformed, default), "malformed workbook with matching hash refused");
            Check(File.ReadAllBytes(source).SequenceEqual(bytes) && File.GetLastWriteTimeUtc(source) == written, "retained evidence bytes and timestamp unchanged");
        }
        Check(cases.Count == 0, "all six calculation/date cases checked");
        var other = new OfficeExportWork(Guid.NewGuid(), root, "unused", "docx", "none", 1, new string('A', 64));
        var otherCompletion = new OfficeHostCompletion(Guid.NewGuid(), "docx", "none", 1, 1, other.SourceSha256, new string('B', 64), []);
        await OfficeWorkbookValidation.CheckAsync(other, otherCompletion, default);
        Check(true, "Word does not require a calculated workbook");
        await Refuse(() => OfficeWorkbookValidation.CheckAsync(other, otherCompletion with { WorkbookBytes = 1, WorkbookSha256 = new string('A', 64) }, default),
            "Word cannot carry Excel evidence");
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks,
            Scope = "Private calculated-workbook guard with retained native evidence; no new native profiles or engine execution." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} calculated workbook checks: {root}");

        void Check(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); checks.Add(message); }
        async Task Refuse(Func<Task> action, string message, Func<Exception, bool>? filter = null)
        {
            try { await action(); throw new InvalidOperationException("Accepted: " + message); }
            catch (Exception error) when ((error is IOException or InvalidDataException) && (filter is null || filter(error))) { Check(true, message); }
        }
    }
}
