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

    public static Task<FileAnalysis> ReadAsync(string path, CancellationToken cancellationToken,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task<AudioProbeFacts>>? audioProbe = null,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task<PdfProbeFacts>>? pdfProbe = null, bool headerOnly = false) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Reuse the existing ordinary-path checks, without publication's one-link restriction.
        // The header path needs no worker or paid admission. Optional deeper probing
        // uses this same read lease and never receives path or publication authority.
        PublicationFiles.Normalize(path);
        if (((uint)File.GetAttributes(path) & ExcludedAttributes) != 0)
            throw new InvalidDataException("Analyze requires an available regular file. Folders, devices, linked files and offline placeholders are not read.");
        using var handle = CreateFileW(path, 0x80000000, 1, IntPtr.Zero, 3,
            0x40000000 | 0x08000000 | 0x00200000 | 0x00100000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (GetFileType(handle) != 1 || ((uint)File.GetAttributes(handle) & ExcludedAttributes) != 0)
            throw new InvalidDataException("This item is not an available regular file. Its contents were not read.");
        await using var stream = new FileStream(handle, FileAccess.Read, 1, isAsync: true);
        var length = stream.Length;
        var modified = File.GetLastWriteTimeUtc(handle);
        var bytes = new byte[(int)Math.Min(length, HeaderAnalyzer.MaximumBytes)];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        int read;
        try { read = await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, deadline.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new IOException("Reading the file header exceeded the time limit."); }
        cancellationToken.ThrowIfCancellationRequested();
        if (read != bytes.Length || stream.Length != length || File.GetLastWriteTimeUtc(handle) != modified)
            throw new IOException("The file changed during analysis. Try again after it finishes saving.");
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
                if (stream.Length != length || File.GetLastWriteTimeUtc(handle) != modified)
                    throw new IOException("The file changed during analysis. Try again after it finishes saving.");
            }
        }
        if (analysis.Identity.FormatId == "zip")
        {
            try { analysis = await DocumentAnalysis.AddPackageAsync(analysis, stream, deadline.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { analysis = analysis with { Warnings = analysis.Warnings.Add("Document package details were unavailable within the read limit. Basic file information is shown.") }; }
            cancellationToken.ThrowIfCancellationRequested();
            if (stream.Length != length || File.GetLastWriteTimeUtc(handle) != modified)
                throw new IOException("The file changed during analysis. Try again after it finishes saving.");
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
            if (stream.Length != length || File.GetLastWriteTimeUtc(handle) != modified)
                throw new IOException("The file changed during analysis. Try again after it finishes saving.");
        }
        return analysis;
    }, cancellationToken);

    // OPEN_REPARSE_POINT and OPEN_NO_RECALL complement preflight attribute checks;
    // no file content is read until the opened handle has been checked as a disk file.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(SafeFileHandle handle);
}
