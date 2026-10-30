using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Core.Images;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Office;
using ContextSuite.Private.Office;
using ContextSuite.Private.Pdf;

internal static class OfficePdfValidationContracts
{
    public static async Task RunAsync(string engine, string renderer, string exports, string evidence, string? worker = null)
    {
        if (Directory.Exists(evidence)) throw new IOException("Evidence directory must be new.");
        Directory.CreateDirectory(evidence);
        var scratch = Path.Combine(evidence, "scratch");
        Directory.CreateDirectory(scratch);
        using var structure = worker is null ? new PdfProbeAdapter(engine, scratch) : null;
        using var pages = worker is null ? new PdfRasterAdapter(renderer, scratch) : null;
        var adapter = worker is null ? new OfficePdfFileAdapter(structure!, pages!) : null;
        await using var client = worker is null ? null : new WorkerClient(worker, scratch);
        using var report = JsonDocument.Parse(File.ReadAllBytes(exports));
        if (!report.RootElement.GetProperty("Passed").GetBoolean()) throw new InvalidDataException("Exports did not pass.");
        var checks = new List<string>();
        var results = new List<object>();
        var rows = report.RootElement.GetProperty("Reports").EnumerateArray().ToArray();
        Check(rows.Length == 3 && rows.Select(row => row.GetProperty("Work").GetProperty("Format").GetString()).Order()
            .SequenceEqual(new[] { "docx", "pptx", "xlsx" }), "retained exports cover all three Office families");
        foreach (var row in rows)
        {
            var work = row.GetProperty("Work").Deserialize<OfficeExportWork>()!;
            var candidate = row.GetProperty("Candidate").Deserialize<OfficeExportCandidate>()!;
            var request = Reserve(work, candidate, work.Format);
            var sourceHash = Hash(work.SourcePath); var candidateHash = Hash(work.CandidatePath);
            var result = await Validate(request, CancellationToken.None);
            Check(result.Validation.ItemId == work.ItemId && result.Validation.MatchesPlan &&
                result.Validation.Sha256 == candidateHash && result.Policy == OfficePdfPolicy.Policy,
                work.Format + ": validation is bound to the completed candidate");
            Check(result.SourceBytes == work.SourceBytes && result.SourceSha256 == sourceHash &&
                result.OutputBytes == candidate.Completion.OutputBytes && result.PageCount > 0,
                work.Format + ": independent readers agree on pages and source/output identity");
            Check(Hash(request.TemporaryPath) == candidateHash && Hash(work.SourcePath) == sourceHash && Hash(work.CandidatePath) == candidateHash,
                work.Format + ": reservation is an exact copy and retained inputs are unchanged");
            Clean(work.Format);
            results.Add(new { Format = work.Format, Result = result, Reservation = request.TemporaryPath });
        }

        var template = rows[0].GetProperty("Work").Deserialize<OfficeExportWork>()!;
        var source = File.ReadAllBytes(template.SourcePath);
        var valid = Fixture();
        var ordinary = Create("authored-ordinary", valid);
        var ordinaryResult = await Validate(ordinary, CancellationToken.None);
        Check(ordinaryResult.PageCount == 1 && Hash(ordinary.TemporaryPath) == Hash(ordinary.Export.CandidatePath),
            "authored valid PDF passes both actual readers");
        Clean("authored-ordinary");

        await Refuse("javascript", Fixture("/OpenAction << /S /JavaScript /JS (doNothing) >>"));
        await Refuse("form", Fixture("/AcroForm << /Fields [] >>"));
        await Refuse("attachment", Fixture("/Names << /EmbeddedFiles << /Names [] >> >>"));
        await Refuse("signature", Fixture("/ByteRange [0 1 2 3]"));
        await Refuse("truncated", valid[..(valid.Length / 2)]);
        await Refuse("broken-cross-reference", Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(valid).Replace("startxref\n", "startxref\n999999%")));
        await Refuse("invalid-compressed-stream", Fixture(stream: "broken", filter: "/Filter /FlateDecode"));
        await Refuse("independent-page-bound", Fixture(box: "0 0 14401 792"), pageLimit: true);
        await Refuse("candidate-hash-mismatch", valid, change: work => work with
        { Candidate = work.Candidate with { Completion = work.Candidate.Completion with { OutputSha256 = new string('0', 64) } } });
        await Refuse("source-hash-mismatch", valid, change: work => work with
        {
            Export = work.Export with { SourceSha256 = new string('0', 64) },
            Candidate = work.Candidate with { Completion = work.Candidate.Completion with { SourceSha256 = new string('0', 64) } }
        });
        await Refuse("candidate-size-mismatch", valid, change: work => work with
        { Candidate = work.Candidate with { Completion = work.Candidate.Completion with { OutputBytes = valid.Length + 1 } } });
        await Refuse("candidate-size-bound", valid, change: work => work with
        { Candidate = work.Candidate with { Completion = work.Candidate.Completion with { OutputBytes = OfficeHostProtocol.MaximumOutputBytes + 1 } } });
        await Refuse("nonempty-reservation", valid, occupied: true);
        await Refuse("pre-cancelled", valid, cancel: true);
        if (client is not null)
        {
            Check(client.HasOfficePdfValidator, "worker exposes both validation readers");
            var reused = client.ProcessId;
            var following = Create("following-refusals", valid);
            var followingResult = await Validate(following, CancellationToken.None);
            Check(followingResult.PageCount == 1 && client.ProcessId == reused, "ordinary validation failures leave the sequential worker reusable");
            await InterruptActiveAsync(false);
            var recovery = Create("following-cancellation", valid);
            var recoveryResult = await Validate(recovery, CancellationToken.None);
            Check(recoveryResult.PageCount == 1 && Hash(recovery.TemporaryPath) == Hash(recovery.Export.CandidatePath),
                "fresh worker validates following candidate after active cancellation");
            await InterruptActiveAsync(true);
            var afterLoss = Create("following-worker-loss", valid);
            var afterLossResult = await Validate(afterLoss, CancellationToken.None);
            Check(afterLossResult.PageCount == 1 && Hash(afterLoss.TemporaryPath) == Hash(afterLoss.Export.CandidatePath),
                "fresh worker validates following candidate after worker termination");
            await client.DisposeAsync();
            Check(client.ProcessId is null, "explicit client disposal releases the worker");
            Clean("worker-disposal");
        }
        File.WriteAllText(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks, Reports = results },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} Office PDF validation checks.");

        void Check(bool condition, string label)
        { if (!condition) throw new InvalidDataException(label); checks.Add(label); Console.WriteLine("PASS: " + label); }
        Task<OfficePdfResult> Validate(OfficePdfWork request, CancellationToken token) =>
            client is null ? adapter!.ValidateAsync(request, token) : client.ValidateOfficePdfAsync(request, token);
        void Clean(string label) => Check(!Directory.EnumerateFiles(scratch, "*", SearchOption.AllDirectories).Any(), label + ": validator scratch has no retained files");
        OfficePdfWork Reserve(OfficeExportWork work, OfficeExportCandidate candidate, string label)
        {
            var output = Path.Combine(evidence, label); Directory.CreateDirectory(output);
            var path = Path.Combine(output, $".context-suite-{work.ItemId:N}.tmp");
            using (File.Open(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)) { }
            return new(work, candidate, path);
        }
        OfficePdfWork Create(string label, byte[] pdf)
        {
            var id = Guid.NewGuid();
            var work = template with { ItemId = id, DirectoryPath = Path.Combine(evidence, "office-" + id.ToString("N")),
                ProfileName = "ContextSuite.Office." + id.ToString("N") };
            Directory.CreateDirectory(Path.GetDirectoryName(work.SourcePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(work.CandidatePath)!);
            File.WriteAllBytes(work.SourcePath, source); File.WriteAllBytes(work.CandidatePath, pdf);
            var candidate = new OfficeExportCandidate(id, new(Guid.NewGuid(), work.Format, work.Calculation, source.Length,
                pdf.Length, Hash(work.SourcePath), Hash(work.CandidatePath), []), work.Policy);
            return Reserve(work, candidate, label);
        }
        async Task Refuse(string label, byte[] pdf, Func<OfficePdfWork, OfficePdfWork>? change = null, bool occupied = false, bool cancel = false, bool pageLimit = false)
        {
            var request = Create(label, pdf);
            if (change is not null) request = change(request);
            if (occupied) File.WriteAllText(request.TemporaryPath, "existing reservation contents");
            var before = Hash(request.TemporaryPath);
            using var cancellation = new CancellationTokenSource(); if (cancel) cancellation.Cancel();
            Exception? failure = null;
            try { await Validate(request, cancellation.Token); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or OperationCanceledException or ImageFailureException or MediaWorkerException) { failure = error; }
            var expectedFailure = pageLimit ? ImageFailure.ResourceLimit : label == "signature" ? ImageFailure.UnsupportedInput : ImageFailure.InvalidInput;
            Check(cancel ? failure is OperationCanceledException : client is not null && failure is MediaWorkerException remote ? remote.Failure == expectedFailure :
                pageLimit ? failure is ImageFailureException { Failure: ImageFailure.ResourceLimit } : failure is InvalidDataException or NotSupportedException,
                label + ": no validation result is issued");
            Check(Hash(request.TemporaryPath) == before && Hash(request.Export.SourcePath) == Convert.ToHexString(SHA256.HashData(source)) &&
                Hash(request.Export.CandidatePath) == Convert.ToHexString(SHA256.HashData(pdf)), label + ": refusal leaves reservation, source and candidate unchanged");
            Clean(label);
            results.Add(new { Case = label, Failure = failure!.GetType().Name, failure.Message });
        }
        async Task InterruptActiveAsync(bool terminate)
        {
            // A large legal whitespace suffix leaves enough time to observe the
            // actual worker's owned snapshot before cancelling its request.
            var large = new byte[32 * 1024 * 1024]; Array.Fill(large, (byte)' '); valid.CopyTo(large, 0);
            var label = terminate ? "active-worker-loss" : "active-cancel";
            var request = Create(label, large);
            using var cancellation = new CancellationTokenSource();
            var task = Validate(request, cancellation.Token);
            var observed = false;
            int? stoppedPid = null;
            using var observation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                while (!task.IsCompleted && !observation.IsCancellationRequested)
                {
                    if (Directory.EnumerateFiles(scratch, "office-pdf-validation-*.pdf", SearchOption.AllDirectories).Any())
                    { observed = true; break; }
                    await Task.Delay(1);
                }
                stoppedPid = client!.ProcessId;
                if (terminate && observed && stoppedPid is not null)
                { using var process = Process.GetProcessById(stoppedPid.Value); process.Kill(); }
                else cancellation.Cancel();
            }
            catch { cancellation.Cancel(); try { await task; } catch (Exception) { } throw; }
            var interrupted = false;
            try { await task; }
            catch (OperationCanceledException) when (!terminate) { interrupted = true; }
            catch (MediaWorkerException error) when (terminate && error.Failure == ImageFailure.WorkerTerminated) { interrupted = true; }
            Check(observed && interrupted && stoppedPid is not null && client!.ProcessId is null, label + ": active snapshot observation leads to expected interruption and worker stop");
            var exited = false;
            try { using var process = Process.GetProcessById(stoppedPid!.Value); exited = process.HasExited; }
            catch (ArgumentException) { exited = true; }
            Check(exited, label + ": interrupted worker has exited before the client returns");
            Check(new FileInfo(request.TemporaryPath).Length == 0 && Hash(request.Export.CandidatePath) == Convert.ToHexString(SHA256.HashData(large)) &&
                Hash(request.Export.SourcePath) == Convert.ToHexString(SHA256.HashData(source)), label + ": preserves source, candidate and unfilled reservation");
            Clean(label);
        }
    }

    private static string Hash(string path)
    { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }

    // Independently authored tiny PDF with exact xref offsets; no Office engine or profile is used.
    private static byte[] Fixture(string catalog = "", string box = "0 0 612 792", string stream = "q Q", string filter = "")
    {
        var objects = new[] { $"<< /Type /Catalog /Pages 2 0 R {catalog} >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [{box}] /Resources << >> /Contents 4 0 R >>",
            $"<< /Length {stream.Length} {filter} >>\nstream\n{stream}\nendstream" };
        var text = new StringBuilder("%PDF-1.7\n"); var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        { offsets.Add(text.Length); text.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n"); }
        var xref = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) text.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Root 1 0 R /Size {objects.Length + 1} >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }
}
