using System.ComponentModel;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Operations;

internal static class AnalysisMappingContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "analysis-mapping-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var observations = new List<object>();
        var bytes = "%PDF-1.7\nAuthored header only, not a complete document.\n"u8.ToArray();
        foreach (var mode in new[] { "read", "copy-on-write", "write-mapping", "write-view-only" })
        {
            var writable = mode.StartsWith("write-", StringComparison.Ordinal);
            var access = writable ? MemoryMappedFileAccess.ReadWrite : mode == "read" ? MemoryMappedFileAccess.Read : MemoryMappedFileAccess.CopyOnWrite;
            var path = Path.Combine(root, mode + ".pdf");
            await File.WriteAllBytesAsync(path, bytes);
            using var source = new FileStream(path, FileMode.Open, writable ? FileAccess.ReadWrite : FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var mapping = MemoryMappedFile.CreateFromFile(source, null, bytes.Length, access, HandleInheritability.None, leaveOpen: true);
            using var view = mapping.CreateViewAccessor(0, bytes.Length, access);
            var sourceHandle = source.SafeFileHandle;
            source.Dispose();
            check(sourceHandle.IsClosed, "analysis mapping: original file handle closed " + mode);
            if (mode == "write-view-only") mapping.Dispose();
            if (mode != "read")
            {
                view.Write(bytes.Length - 2, (byte)'X'); view.Flush();
                check(view.ReadByte(bytes.Length - 2) == (byte)'X', "analysis mapping: live view accepts its authored mutation " + mode);
            }
            // A sharing-compatible reader proves readability. A write-mapping
            // refusal below must come from Analyze's stronger stability lease.
            var baseline = await ReadSharedAsync(path);
            var modified = File.GetLastWriteTimeUtc(path);
            check(baseline.AsSpan().SequenceEqual(writable ? bytes[..^2].Concat(new byte[] { (byte)'X', bytes[^1] }).ToArray() : bytes),
                "analysis mapping: shared read sees file-backed changes only " + mode);
            var probes = 0;
            var refused = false;
            try
            {
                var result = await FileAnalysisReader.ReadAsync(path, default, pdfProbe: (snapshot, _) =>
                {
                    probes++;
                    check(snapshot.Span.SequenceEqual(bytes), "analysis mapping: read-only/COW snapshot retains original file " + mode);
                    var writerRefused = false;
                    try { using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete); }
                    catch (IOException error) when ((error.HResult & 0xffff) == 32) { writerRefused = true; }
                    check(writerRefused, "analysis mapping: later writer cannot enter during deeper inspection " + mode);
                    return Task.FromResult(new PdfProbeFacts(1, false, false, 0, 0, 0, 0, false));
                });
                check(!writable && result.Identity.FormatId == "pdf" && result.FileBytes == bytes.Length,
                    "analysis mapping: read-only/COW mapping permits complete analysis " + mode);
            }
            catch (Win32Exception error) when (error.NativeErrorCode == 32) { refused = true; }
            check(refused == writable && probes == (writable ? 0 : 1), "analysis mapping: incompatible view stops before deeper inspection " + mode);
            if (writable)
            {
                var good = Path.Combine(root, mode + "-later.bin"); await File.WriteAllBytesAsync(good, [0, 255, 1]);
                await using var worker = new WorkerClient(Path.Combine(root, "must-not-start.exe"));
                await using var model = new MainViewModel(worker);
                check(model.Admit(new(Guid.NewGuid(), "analyze", "open-details", [path, good])).Accepted,
                    "analysis mapping: mapped and readable files enter one batch " + mode);
                await model.WaitForIdleAsync();
                check(model.Rows[0].Result.State == OperationState.Failed && model.Rows[0].Status.Contains("Another program is using this file.", StringComparison.Ordinal) &&
                    model.Rows[1].Result.State == OperationState.Succeeded && !model.CanRetry && model.Rows.All(row => !row.HasOutput),
                    "analysis mapping: readable sharing guidance and later result without publication " + mode);
            }
            check((await ReadSharedAsync(path)).AsSpan().SequenceEqual(baseline) && File.GetLastWriteTimeUtc(path) == modified,
                "analysis mapping: analysis preserves source bytes and time " + mode);
            view.Dispose(); mapping.Dispose();
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                check(true, "analysis mapping: releasing authored mapping leaves no analysis lease " + mode);
            check((await FileAnalysisReader.ReadAsync(path, default)).FileBytes == bytes.Length,
                "analysis mapping: subsequent analysis succeeds after release " + mode);
            observations.Add(new { mode, refused, probes, sourceHandleClosed = true, sha256 = Convert.ToHexString(SHA256.HashData(baseline)) });
        }
        await File.WriteAllTextAsync(Path.Combine(root, "observations.json"), JsonSerializer.Serialize(observations));
        Console.WriteLine("Analysis mapping evidence: " + root);
    }

    private static async Task<byte[]> ReadSharedAsync(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[file.Length]; await file.ReadExactlyAsync(bytes); return bytes;
    }
}
