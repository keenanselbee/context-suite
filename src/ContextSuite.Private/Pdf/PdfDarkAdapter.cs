using System.Security.Cryptography;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;
using ContextSuite.Private.Images;
using ContextSuite.Private.IO;
using UglyToad.PdfPig;

namespace ContextSuite.Private.Pdf;

public sealed class PdfDarkAdapter : IDisposable
{
    // Updated only after native source, admission policy and fixtures are reviewed.
    private const string HostHash = "11E24AA7BE995A33903B24621514C1DB71B954C1A8341E0F6091C1A99A813E9C";
    private readonly string _executable, _workingDirectory;
    private readonly List<FileStream> _leases = [];
    private bool _disposed;

    public PdfDarkAdapter(string binaryDirectory, string scratchDirectory)
    {
        if (!Path.IsPathFullyQualified(binaryDirectory) || !Path.IsPathFullyQualified(scratchDirectory))
            throw new ArgumentException("Absolute PDF dark-mode engine and scratch paths are required.");
        _executable = Path.Combine(Path.GetFullPath(binaryDirectory), "ContextSuite.PdfDark.exe");
        _workingDirectory = Path.GetFullPath(scratchDirectory);
        try
        {
            CheckAncestors(Path.GetDirectoryName(_executable)!);
            var files = new[] { (_executable, HostHash) }.Concat(PdfProbeAdapter.PinnedFiles
                .Where(entry => entry.Key != "qpdf.exe").Select(entry => (Path.Combine(binaryDirectory, entry.Key), entry.Value)));
            foreach (var (file, hash) in files)
            {
                var lease = MediaFiles.OpenRead(file);
                _leases.Add(lease);
                if (Convert.ToHexString(SHA256.HashData(lease)) != hash)
                    throw new InvalidDataException("PDF dark-mode engine identity mismatch.");
            }
            CheckAncestors(_workingDirectory);
            Directory.CreateDirectory(_workingDirectory);
            CheckAncestors(_workingDirectory);
            while (_workingDirectory.Length > 240 && Path.GetDirectoryName(_workingDirectory) is { } parent)
                _workingDirectory = parent;
        }
        catch { Dispose(); throw; }
    }

    public async Task<PdfDarkSource> ProbeAsync(PdfFileProbe request, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        request.Validate();
        using var input = MediaFiles.OpenRead(request.Path);
        var hash = await Hash(input, token);
        var frame = await Run(input, false, token);
        if (frame.SourceBytes != input.Length || await Hash(input, token) != hash)
            throw new ImageFailureException(ImageFailure.SourceChanged);
        var source = new PdfDarkSource(request.ItemId, request.Path, hash, input.Length, frame.Facts);
        source.Validate();
        return source;
    }

    public async Task<PdfDarkResult> ConvertAsync(PdfDarkWork work, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        work.Validate();
        using var input = MediaFiles.OpenRead(work.Source.Path);
        if (input.Length != work.Source.FileBytes || await Hash(input, token) != work.Source.Sha256)
            throw new ImageFailureException(ImageFailure.SourceChanged);
        using var output = new ReservedMediaOutput(work.TemporaryPath, PdfDarkWork.MaximumOutputBytes);
        var frame = await Run(input, true, token);
        if (frame.SourceBytes != work.Source.FileBytes || frame.Facts != work.Source.Facts || frame.Pdf.IsEmpty)
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        // A second parser verifies searchable text and glyph placement independently
        // of the native paint-token and object-preservation checks.
        using (var memory = new ProcessMemoryScope(2UL * 1024 * 1024 * 1024))
            ValidateText(input, frame.Pdf, frame.Facts.PageCount, token);
        token.ThrowIfCancellationRequested();
        var hash = Convert.ToHexString(SHA256.HashData(frame.Pdf.Span));
        output.Write(frame.Pdf.Span);
        output.Flush(true);
        output.Position = 0;
        if (output.Length != frame.Pdf.Length || Convert.ToHexString(await SHA256.HashDataAsync(output, token)) != hash ||
            await Hash(input, token) != work.Source.Sha256)
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        return new(new(work.Source.ItemId, hash, true), work.Source.Sha256, input.Length, output.Length,
            frame.Facts, "qpdf 12.4.1; " + PdfDarkWork.CurrentPolicy);
    }

    private async Task<PdfDarkFrame> Run(FileStream input, bool conversion, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            input.Position = 0;
            var bytes = await SeekableMediaProcess.RunAsync(_executable, _workingDirectory, input,
                [conversion ? "--convert" : "--inspect"], conversion ? PdfDarkProtocol.MaximumConversionBytes : PdfDarkProtocol.MaximumInspectionBytes,
                deadline.Token, diagnosticFailure: PdfDarkDiagnostic.Failure);
            return PdfDarkProtocol.Read(bytes, conversion);
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new TimeoutException("PDF dark-mode conversion exceeded its deadline.", error); }
    }

    private static void ValidateText(FileStream input, ReadOnlyMemory<byte> candidate, int pages, CancellationToken token)
    {
        input.Position = 0;
        var options = new ParsingOptions { UseLenientParsing = false, SkipMissingFonts = false };
        using var original = PdfDocument.Open(input, options);
        using var output = PdfDocument.Open(candidate.ToArray(), options);
        if (original.IsEncrypted || output.IsEncrypted || original.NumberOfPages != pages || output.NumberOfPages != pages)
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        long letters = 0;
        for (var index = 1; index <= pages; index++)
        {
            token.ThrowIfCancellationRequested();
            var before = original.GetPage(index);
            var after = output.GetPage(index);
            letters += before.Letters.Count;
            if (letters > 2_000_000 || before.Letters.Count > 30_000)
                throw new ImageFailureException(ImageFailure.ResourceLimit);
            if (before.Width != after.Width || before.Height != after.Height || before.Rotation != after.Rotation ||
                before.Letters.Count != after.Letters.Count || before.NumberOfImages != after.NumberOfImages)
                throw new ImageFailureException(ImageFailure.ValidationFailed);
            for (var letterIndex = 0; letterIndex < before.Letters.Count; letterIndex++)
            {
                var a = before.Letters[letterIndex]; var b = after.Letters[letterIndex];
                if (a.Value != b.Value || a.FontName != b.FontName || a.FontSize != b.FontSize ||
                    a.BoundingBox != b.BoundingBox || a.StartBaseLine != b.StartBaseLine || a.EndBaseLine != b.EndBaseLine)
                    throw new ImageFailureException(ImageFailure.ValidationFailed);
            }
        }
    }

    private static async Task<string> Hash(FileStream input, CancellationToken token)
    {
        if (input.Length is <= 0 or > PdfDarkSource.MaximumSourceBytes)
            throw new ImageFailureException(ImageFailure.ResourceLimit);
        input.Position = 0;
        return Convert.ToHexString(await SHA256.HashDataAsync(input, token));
    }

    private static void CheckAncestors(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked PDF dark-mode engine directories are not allowed.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var lease in _leases) lease.Dispose();
    }
}
