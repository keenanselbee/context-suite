using System.Text.Json;
using ContextSuite.Core.Office;
using ContextSuite.Core.Transport;

internal static class OfficeWorkContracts
{
    internal static void Run(Action<bool, string> check)
    {
        var item = Guid.NewGuid(); var request = Guid.NewGuid(); var hash = new string('A', 64);
        var work = new OfficeExportWork(item, @"C:\owned\office-" + item.ToString("N"),
            "ContextSuite.Office.Evaluation." + item.ToString("N"), "docx", "none", 100, hash);
        var command = new WorkerCommand(1, request, "office-export", OfficeWork: work);
        foreach (var (format, calculation) in new[] { ("docx", "none"), ("xlsx", "cached"), ("xlsx", "recalculate"), ("pptx", "none") })
        {
            var current = command with { OfficeWork = work with { Format = format, Calculation = calculation } };
            var restored = JsonSerializer.Deserialize<WorkerCommand>(JsonSerializer.Serialize(current))!;
            restored.Validate();
            check(restored == current, "Office work round trip: " + format + "/" + calculation);
        }
        foreach (var invalid in new[] { work with { ItemId = Guid.Empty }, work with { Format = "docm" },
            work with { Calculation = "cached" }, work with { Format = "xlsx", Calculation = "none" },
            work with { Format = "xlsx", Calculation = "" }, work with { Policy = "unrestricted" },
            work with { SourceBytes = 0 }, work with { SourceBytes = OfficeHostProtocol.MaximumSourceBytes + 1 },
            work with { SourceSha256 = new string('G', 64) }, work with { ProfileName = "existing-profile" },
            work with { ProfileName = "ContextSuite.Office.Evaluation." + Guid.NewGuid().ToString("N") },
            work with { DirectoryPath = work.DirectoryPath + "\\.." }, work with { DirectoryPath = @"\\server\share\office-" + item.ToString("N") },
            work with { DirectoryPath = work.DirectoryPath + ":stream" }, work with { DirectoryPath = work.DirectoryPath + "." },
            work with { DirectoryPath = work.DirectoryPath + " " }, work with { DirectoryPath = @"C:\owned\different" } })
            Refused(() => (command with { OfficeWork = invalid }).Validate(), "invalid Office identity, context or policy");
        Refused(() => (command with { OfficeWork = null }).Validate(), "missing work");
        Refused(() => (command with { Command = "capabilities" }).Validate(), "Office payload on another command");
        Refused(() => (command with { PdfBytes = [1] }).Validate(), "PDF bytes on Office command");
        Refused(() => (command with { AudioBytes = [1] }).Validate(), "audio bytes on Office command");
        Refused(() => (command with { AudioTarget = ContextSuite.Core.Audio.AudioFormat.Flac }).Validate(), "audio target on Office command");
        var completion = new OfficeHostCompletion(request, "docx", "none", 100, 200, hash, new string('B', 64), []);
        var candidate = new OfficeExportCandidate(item, completion, OfficeHostProtocol.Policy);
        candidate.Validate(work, request); check(true, "Office candidate matches admitted request");
        foreach (var invalid in new[] { candidate with { ItemId = Guid.NewGuid() }, candidate with { Policy = "other" },
            candidate with { Completion = completion with { RequestId = Guid.NewGuid() } },
            candidate with { Completion = completion with { SourceBytes = 101 } },
            candidate with { Completion = completion with { SourceSha256 = new string('B', 64) } },
            candidate with { Completion = completion with { Format = "xlsx" } },
            candidate with { Completion = completion with { Calculation = "cached" } },
            candidate with { Completion = completion with { MissingFontFamilies = default } },
            candidate with { Completion = completion with { MissingFontFamilies = ["A", "a"] } },
            candidate with { Completion = completion with { OutputBytes = 0 } },
            candidate with { Completion = completion with { OutputBytes = OfficeHostProtocol.MaximumOutputBytes + 1 } },
            candidate with { Completion = completion with { OutputSha256 = "invalid" } } })
            Refused(() => invalid.Validate(work, request), "mismatched Office candidate");

        void Refused(Action action, string name)
        {
            try { action(); check(false, "Office accepted " + name); }
            catch (InvalidDataException) { check(true, "Office refuses " + name); }
        }
    }
}
