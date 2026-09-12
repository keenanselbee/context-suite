using System.ComponentModel;
using System.Runtime.InteropServices;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Transport;
using Microsoft.Win32.SafeHandles;

namespace ContextSuite.Application.Infrastructure;

internal static class FileAnalysisReader
{
    private const uint ExcludedAttributes = 0x10 | 0x40 | 0x400 | 0x1000 | 0x40000 | 0x400000;

    public static async Task<FileAnalysis> ReadAsync(string path, CancellationToken cancellationToken,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task<AudioProbeFacts>>? audioProbe = null,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task<PdfProbeFacts>>? pdfProbe = null, bool headerOnly = false)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            return await Task.Run(() => ReadCoreAsync(path, cancellationToken, deadline, audioProbe, pdfProbe, headerOnly), cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new FileAnalysisTimeoutException("Opening or reading the file exceeded the time limit."); }
    }

    private static async Task<FileAnalysis> ReadCoreAsync(string path, CancellationToken cancellationToken,
        CancellationTokenSource deadline, Func<ReadOnlyMemory<byte>, CancellationToken, Task<AudioProbeFacts>>? audioProbe,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task<PdfProbeFacts>>? pdfProbe, bool headerOnly)
    {
        await using var stream = OpenRead(path, deadline.Token);
        var (length, modified) = ReadState(stream, deadline.Token);
        deadline.Token.ThrowIfCancellationRequested();
        var bytes = new byte[(int)Math.Min(length, HeaderAnalyzer.MaximumBytes)];
        int read;
        try { read = await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, deadline.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new FileAnalysisTimeoutException("Reading the file header exceeded the time limit."); }
        cancellationToken.ThrowIfCancellationRequested();
        if (read != bytes.Length || ReadState(stream, deadline.Token) != (length, modified))
            throw new IOException("The file changed during analysis. Try again after it finishes saving.");
        deadline.Token.ThrowIfCancellationRequested();
        var analysis = HeaderAnalyzer.Analyze(path, bytes, length);
        if (headerOnly) return analysis;
        if (pdfProbe is not null && analysis.Identity.FormatId == "pdf" && analysis.Identity.Basis == IdentificationBasis.Content)
        {
            if (length > WorkerCommand.MaximumPdfProbeBytes)
                analysis = analysis with { Warnings = analysis.Warnings.Add("Detailed PDF inspection currently supports files up to 16 MiB. Header information is shown.") };
            else
            {
                var pdfBytes = new byte[(int)length];
                bytes.CopyTo(pdfBytes, 0);
                try
                {
                    await stream.ReadExactlyAsync(pdfBytes.AsMemory(bytes.Length), deadline.Token);
                    var probed = await pdfProbe(pdfBytes, cancellationToken);
                    analysis = PdfAnalysis.AddProbe(analysis, probed, pdfBytes.Length);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
                { analysis = analysis with { Warnings = analysis.Warnings.Add("Deeper PDF information was unavailable within the read limit. Header information is shown.") }; }
                catch (Exception error) when (error is IOException or InvalidDataException or TimeoutException or Win32Exception or MediaWorkerException)
                { analysis = analysis with { Warnings = analysis.Warnings.Add("Deeper PDF information is unavailable for this file. Header information is shown.") }; }
                cancellationToken.ThrowIfCancellationRequested();
                VerifyUnchanged(stream, length, modified, cancellationToken);
            }
        }
        if (analysis.Identity.FormatId == "zip")
        {
            try { analysis = await DocumentAnalysis.AddPackageAsync(analysis, stream, deadline.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { analysis = analysis with { Warnings = analysis.Warnings.Add("Document package details were unavailable within the read limit. Basic file information is shown.") }; }
            cancellationToken.ThrowIfCancellationRequested();
            VerifyUnchanged(stream, length, modified, cancellationToken);
        }
        if (analysis.Identity.FormatId == "ole")
        {
            try { analysis = await LegacyDocumentAnalysis.AddCompoundAsync(analysis, stream, deadline.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { analysis = analysis with { Warnings = analysis.Warnings.Add("Legacy document details were unavailable within the read limit. Basic file information is shown.") }; }
            cancellationToken.ThrowIfCancellationRequested();
            VerifyUnchanged(stream, length, modified, cancellationToken);
        }
        if (audioProbe is not null && AudioAnalysis.CanProbe(analysis, bytes))
        {
            var audioBytes = new byte[(int)Math.Min(length, WorkerCommand.MaximumAudioProbeBytes)];
            bytes.CopyTo(audioBytes, 0);
            try
            {
                await stream.ReadExactlyAsync(audioBytes.AsMemory(bytes.Length), deadline.Token);
                var probed = await audioProbe(audioBytes, cancellationToken);
                analysis = AudioAnalysis.AddProbe(analysis, probed, audioBytes.Length);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
            { analysis = analysis with { Warnings = analysis.Warnings.Add("Deeper audio information was unavailable within the read limit. Header information is shown.") }; }
            catch (Exception error) when (error is IOException or InvalidDataException or TimeoutException or Win32Exception or MediaWorkerException)
            { analysis = analysis with { Warnings = analysis.Warnings.Add("Deeper audio information is unavailable for this file. Header information is shown.") }; }
            cancellationToken.ThrowIfCancellationRequested();
            VerifyUnchanged(stream, length, modified, cancellationToken);
        }
        return analysis;
    }

    private static FileStream OpenRead(string path, CancellationToken token) => SynchronousFileIo.Run(() =>
    {
        // No worker or paid admission. Validate the opened handle before reading
        // content; a filename check alone cannot exclude a replacement link/device.
        PublicationFiles.Normalize(path);
        token.ThrowIfCancellationRequested();
        if (((uint)File.GetAttributes(path) & ExcludedAttributes) != 0)
            throw new InvalidDataException("Analyze requires an available regular file. Folders, devices, linked files and offline placeholders are not read.");
        token.ThrowIfCancellationRequested();
        var handle = CreateFileW(path, 0x80000000, 1, IntPtr.Zero, 3,
            0x40000000 | 0x08000000 | 0x00200000 | 0x00100000, IntPtr.Zero);
        try
        {
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            token.ThrowIfCancellationRequested();
            if (GetFileType(handle) != 1 || ((uint)File.GetAttributes(handle) & ExcludedAttributes) != 0)
                throw new InvalidDataException("This item is not an available regular file. Its contents were not read.");
            token.ThrowIfCancellationRequested();
            return new FileStream(handle, FileAccess.Read, 1, isAsync: true);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }, token);

    private static (long Length, DateTime Modified) ReadState(FileStream stream, CancellationToken token) =>
        SynchronousFileIo.Run(() =>
        {
            var length = stream.Length;
            token.ThrowIfCancellationRequested();
            var modified = File.GetLastWriteTimeUtc(stream.SafeFileHandle);
            token.ThrowIfCancellationRequested();
            return (length, modified);
        }, token);

    private static void VerifyUnchanged(FileStream stream, long length, DateTime modified, CancellationToken cancellationToken)
    {
        // Optional workers have their own budgets. Give the final metadata query
        // its own deadline instead of expiring a valid probe against the read timer.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            if (ReadState(stream, deadline.Token) != (length, modified))
                throw new IOException("The file changed during analysis. Try again after it finishes saving.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new FileAnalysisTimeoutException("Checking the file after analysis exceeded the time limit."); }
    }

    // OPEN_REPARSE_POINT and OPEN_NO_RECALL complement preflight attribute checks;
    // no file content is read until the opened handle has been checked as a disk file.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(SafeFileHandle handle);
}

internal sealed class FileAnalysisTimeoutException(string message) : IOException(message);
