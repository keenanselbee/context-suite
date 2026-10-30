using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Transport;
using ContextSuite.Private.Images;

namespace ContextSuite.Private.Pdf;

// Optional integration candidate. Native calls receive only bounded owned
// snapshots; the file adapter keeps customer paths out of engine arguments.
public sealed partial class PdfProbeAdapter : IDisposable
{
    private readonly string _executable;
    private readonly string _scratch;
    private readonly string _workingDirectory;
    private readonly List<FileStream> _leases = [];
    private bool _disposed;
    internal static readonly IReadOnlyDictionary<string, string> PinnedFiles = new Dictionary<string, string>()
    {
        ["qpdf.exe"] = "57C003E868FB66CD343FD5AFB91BE8C2277F56434EEA8635762499731BF9F60D",
        ["qpdf30.dll"] = "36FE5B2023F244E5785D96B8372DBA26B75EA51B63ADF4D4F1E66AD0AA1C8A61",
        ["concrt140.dll"] = "54716F0738AF891F283D213B5C8D11B25896BB8EE3097D301EAE718560CF974E",
        ["msvcp140.dll"] = "7C26614E1D733892C2DEAC7E245CE115504B1D80592DD0A01B08E3E5A55F89CA",
        ["msvcp140_1.dll"] = "206C931BF90FDAD8816DE3B5E2EF80B2BCAA9406C89ECC05FE6FDDFFE251E982",
        ["msvcp140_2.dll"] = "D50D7883F20D1DC6191768D3746F52DD9CAC89C346FFAED5BE1F110C2F34A838",
        ["msvcp140_atomic_wait.dll"] = "3D0CBFAA1BF3EECF5A3F4491D2960EE803CB994F30292C6ADC4A07C498F60E2B",
        ["msvcp140_codecvt_ids.dll"] = "8A65C7596EF2E6938731F5A1058E7E40145B6D97967CC649231A076B9A608D78",
        ["vcruntime140.dll"] = "D1F4225DF2CD877DBF130D5668A021DCE3F94118455FF5EC952061C30AFC9CE7",
        ["vcruntime140_1.dll"] = "A7146C08F89FE5B04541AB507CDB59FF7B44534D4BA3C668A426C6450A03434E"
    };

    public PdfProbeAdapter(string binaryDirectory, string scratchDirectory)
    {
        if (!Path.IsPathFullyQualified(binaryDirectory) || !Path.IsPathFullyQualified(scratchDirectory) ||
            binaryDirectory.IndexOfAny(['\r', '\n']) >= 0 || scratchDirectory.IndexOfAny(['\r', '\n']) >= 0)
            throw new ArgumentException("Absolute engine and scratch paths without line breaks are required.");
        _executable = Path.Combine(Path.GetFullPath(binaryDirectory), "qpdf.exe");
        _scratch = Path.GetFullPath(scratchDirectory);
        _workingDirectory = _scratch;
        try
        {
            RequireUnlinkedDirectory(Path.GetDirectoryName(_executable)!);
            foreach (var file in PinnedFiles)
            {
                var path = Path.Combine(binaryDirectory, file.Key);
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked engine files are not allowed.");
                var lease = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                _leases.Add(lease);
                if (Convert.ToHexString(SHA256.HashData(lease)) != file.Value) throw new InvalidDataException("PDF engine identity mismatch.");
            }
            // Check existing ancestors before creating job storage, then the final directory.
            for (var parent = _scratch; parent is not null; parent = Path.GetDirectoryName(parent))
                if (Directory.Exists(parent)) { RequireUnlinkedDirectory(parent); break; }
            Directory.CreateDirectory(_scratch);
            RequireUnlinkedDirectory(_scratch);
            // CreateProcess rejects an overlong current directory even with
            // extended syntax. Use a checked ancestor; all native file operands
            // remain absolute owned snapshots, and all output goes to stdout.
            while (_workingDirectory.Length > 240 && Path.GetDirectoryName(_workingDirectory) is { } ancestor)
                _workingDirectory = ancestor;
        }
        catch { Dispose(); throw; }
    }

    public async Task<PdfProbeFacts> ProbeAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.IsEmpty || source.Length > WorkerCommand.MaximumPdfProbeBytes || !source.Span.StartsWith("%PDF-"u8))
            throw new InvalidDataException("PDF probe input is unsupported or exceeds its bounds.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        RequireUnlinkedDirectory(_scratch);
        var input = Path.Combine(_scratch, "pdf-probe-" + Guid.NewGuid().ToString("N") + ".pdf");
        var engineInput = NativeSnapshotPath(input);
        var created = false;
        try
        {
            await using (var output = new FileStream(input, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
            {
                created = true;
                await output.WriteAsync(source, deadline.Token);
            }
            if ((File.GetAttributes(input) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked probe snapshots are not allowed.");
            await using var lease = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
            var hash = await SHA256.HashDataAsync(lease, deadline.Token);
            if (!hash.AsSpan().SequenceEqual(SHA256.HashData(source.Span))) throw new IOException("PDF snapshot changed before probing.");
            var result = await RunAsync(["--suppress-recovery", "--json=2", "--json-key=pages", "--json-key=encrypt", "--json-key=acroform",
                "--json-key=attachments", "--json-key=outlines", engineInput], PdfProbeParser.MaximumBytes, deadline.Token);
            if (result.ExitCode == 0) return PdfProbeParser.Parse(result.Output);
            // qpdf's JSON needs a readable document. Its dedicated status command
            // can identify encryption without supplying or bypassing a password.
            if (result.ExitCode == 2 && (await RunAsync(["--suppress-recovery", "--is-encrypted", engineInput], PdfProbeParser.MaximumBytes, deadline.Token)).ExitCode == 0)
                return new(null, true, null, null, null, null, null, null);
            throw new InvalidDataException("PDF details are unavailable for this input.");
        }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException("PDF probing cancelled.", error, cancellationToken);
            if (deadline.IsCancellationRequested) throw new TimeoutException("PDF probing exceeded its deadline.", error);
            throw;
        }
        finally
        {
            if (created && File.Exists(input))
            {
                RequireUnlinkedDirectory(_scratch);
                if ((File.GetAttributes(input) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked PDF scratch entry retained.");
                File.Delete(input);
            }
        }
    }

    private static string NativeSnapshotPath(string path)
    {
        // qpdf's Windows file API needs extended syntax beyond MAX_PATH. Only
        // generated, normalized snapshot paths use this; @- preserves it literally.
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }

    private async Task<(int ExitCode, byte[] Output)> RunAsync(string[] arguments, int maximumOutput, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(_executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _workingDirectory,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false) };
        // @- blocks on arguments: job ownership is established before the native
        // parser receives the snapshot path. This is not an OS security sandbox.
        start.ArgumentList.Add("@-");
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("QPDF_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        using var process = new Process { StartInfo = start };
        using var job = new EncoderJob();
        process.Start();
        void Stop()
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception) { job.Dispose(); }
        }
        try
        {
            job.Assign(process);
            using var registration = cancellationToken.Register(Stop);
            var output = ReadBoundedAsync(process.StandardOutput.BaseStream, maximumOutput, cancellationToken);
            var errors = ReadBoundedAsync(process.StandardError.BaseStream, 65536, cancellationToken);
            var send = SendAsync();
            foreach (var task in new Task[] { output, errors, send })
                _ = task.ContinueWith(_ => Stop(), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            await Task.WhenAll(output, errors, send, process.WaitForExitAsync(cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            return (process.ExitCode, await output);

            async Task SendAsync()
            {
                try { await process.StandardInput.WriteAsync((string.Join('\n', arguments) + "\n").AsMemory(), cancellationToken); }
                finally { process.StandardInput.Close(); }
            }
        }
        finally { Stop(); await process.WaitForExitAsync(CancellationToken.None); }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + count > maximum) throw new InvalidDataException("PDF probe output exceeds its budget.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static void RequireUnlinkedDirectory(string directory)
    {
        for (var current = directory; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked PDF engine or scratch paths are not allowed.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var lease in _leases) lease.Dispose();
        _leases.Clear();
    }
}
