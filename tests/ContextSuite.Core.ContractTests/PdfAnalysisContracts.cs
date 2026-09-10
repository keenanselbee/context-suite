using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Transport;

internal static class PdfAnalysisContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var known = new PdfProbeFacts(2, false, true, 1, 0, 1, 1, false);
        var bytes = "%PDF-1.7\nAuthored header fixture, not a complete PDF.\n"u8.ToArray();
        var header = HeaderAnalyzer.Analyze("authored.pdf", bytes, bytes.Length);
        var enriched = PdfAnalysis.AddProbe(header, known, bytes.Length);
        check(enriched.Facts.Single(fact => fact.Id == "document.pages").Integer == 2 &&
            enriched.Facts.Single(fact => fact.Id == "document.encryption").Boolean == false &&
            enriched.Identity.Evidence.All(item => !item.Contains("were not parsed")),
            "PDF Analyze: structured facts replace unavailable placeholders and obsolete evidence");
        var locked = PdfAnalysis.AddProbe(header, new(null, true, null, null, null, null, null, null), bytes.Length);
        check(locked.Facts.Single(fact => fact.Id == "document.pages").Availability == FactAvailability.Unavailable &&
            locked.Facts.Single(fact => fact.Id == "document.encryption").Boolean == true && locked.Warnings.Any(warning => warning.Contains("password")),
            "PDF Analyze: locked input reports encryption without inventing zero pages");
        try { PdfAnalysis.AddProbe(header, known, bytes.Length - 1); check(false, "PDF Analyze: partial snapshot rejected"); }
        catch (InvalidDataException) { check(true, "PDF Analyze: partial snapshot rejected"); }
        foreach (var command in new[] { new WorkerCommand(1, Guid.NewGuid(), "pdf-probe"),
            new WorkerCommand(1, Guid.NewGuid(), "audio-probe", AudioBytes: [1], PdfBytes: [1]),
            new WorkerCommand(1, Guid.NewGuid(), "shutdown", PdfBytes: [1]),
            new WorkerCommand(1, Guid.NewGuid(), "pdf-probe", AudioBytes: [1], PdfBytes: [1]),
            new WorkerCommand(1, Guid.NewGuid(), "pdf-probe", PdfBytes: new byte[WorkerCommand.MaximumPdfProbeBytes + 1]) })
        {
            try { command.Validate(); check(false, "PDF protocol: missing, contradictory or oversized payload rejected"); }
            catch (InvalidDataException) { check(true, "PDF protocol: missing, contradictory or oversized payload rejected"); }
        }
        // Exercise the actual serializer at the largest admitted PDF size.
        using (var frame = new MemoryStream())
        {
            var maximum = new byte[WorkerCommand.MaximumPdfProbeBytes]; maximum[^1] = 42;
            var command = new WorkerCommand(1, Guid.NewGuid(), "pdf-probe", PdfBytes: maximum);
            command.Validate();
            await JsonFrames.WriteAsync(frame, command, CancellationToken.None);
            frame.Position = 0;
            var restored = await JsonFrames.ReadAsync<WorkerCommand>(frame, CancellationToken.None);
            restored.Validate();
            check(restored.PdfBytes!.Length == maximum.Length && restored.PdfBytes[^1] == 42,
                "PDF protocol: largest complete snapshot fits the real framed JSON transport");
        }
        var root = Path.Combine(scratch, "pdf-reader-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "fixture.pdf"); await File.WriteAllBytesAsync(path, bytes);
        var read = await FileAnalysisReader.ReadAsync(path, CancellationToken.None, pdfProbe: (snapshot, _) =>
        {
            check(snapshot.Span.SequenceEqual(bytes), "PDF reader: probe receives complete bytes instead of the customer path");
            try { using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); check(false, "PDF reader: original lease held during probe"); }
            catch (IOException) { check(true, "PDF reader: original lease held during probe"); }
            return Task.FromResult(known);
        });
        check(read.Facts.Single(fact => fact.Id == "document.pages").Integer == 2, "PDF reader: optional probe enriches read-only report");
        var failed = await FileAnalysisReader.ReadAsync(path, CancellationToken.None, pdfProbe: (_, _) => Task.FromException<PdfProbeFacts>(new InvalidDataException("Authored failure")));
        check(failed.Identity.FormatId == "pdf" && failed.Warnings.Any(warning => warning.Contains("Deeper PDF")), "PDF reader: probe failure retains header report");
        await using (var file = new FileStream(path, FileMode.Open, FileAccess.Write)) file.SetLength(WorkerCommand.MaximumPdfProbeBytes + 1);
        var large = await FileAnalysisReader.ReadAsync(path, CancellationToken.None, pdfProbe: (_, _) => throw new Exception("Oversized snapshot reached probe."));
        check(large.InspectedBytes == HeaderAnalyzer.MaximumBytes && large.Warnings.Any(warning => warning.Contains("16 MiB")),
            "PDF reader: oversized PDF retains header report without whole-file read or worker startup");
    }
}
