using System.Security.Cryptography;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ContextSuite.Private.IO;
using ImageMagick;

namespace ContextSuite.Private.Pdf;

public sealed record PdfRenderedPage(PdfRasterPage Page, byte[] Bytes, string SourceSha256, string OutputSha256, string PixelSha256,
    string Policy = PdfRasterProtocol.Policy);

// Optional pinned candidate. A PNG result is not a publication receipt; the app
// still owns all-page planning, access, reservations and final output names.
public sealed class PdfRasterAdapter : IDisposable
{
    private const string HostHash = "4D58C3D9DFFCC8973391C93A0612C7DF97D516519BB726F6FEB2A29836485913";
    private const string PdfiumHash = "04100C03E41CAC1F979E36E5E26FB860BCB5A7461F53830D3C098716624A27A9";
    private readonly string _executable, _scratch, _workingDirectory;
    private readonly List<FileStream> _leases = [];
    private bool _disposed;

    public PdfRasterAdapter(string binaryDirectory, string scratchDirectory)
    {
        if (!Path.IsPathFullyQualified(binaryDirectory) || !Path.IsPathFullyQualified(scratchDirectory))
            throw new ArgumentException("Absolute renderer and scratch paths are required.");
        _executable = Path.Combine(Path.GetFullPath(binaryDirectory), "ContextSuite.PdfRenderer.exe");
        _scratch = Path.GetFullPath(scratchDirectory);
        _workingDirectory = _scratch;
        try
        {
            CheckAncestors(Path.GetDirectoryName(_executable)!);
            var files = new[] { (_executable, HostHash), (Path.Combine(binaryDirectory, "pdfium.dll"), PdfiumHash) }
                .Concat(PdfProbeAdapter.PinnedFiles.Where(entry => entry.Key != "qpdf.exe")
                    .Select(entry => (Path.Combine(binaryDirectory, entry.Key), entry.Value)));
            foreach (var (file, expected) in files)
            {
                var lease = MediaFiles.OpenRead(file); _leases.Add(lease);
                if (Convert.ToHexString(SHA256.HashData(lease)) != expected) throw new InvalidDataException("PDF renderer identity mismatch.");
            }
            CheckAncestors(_scratch);
            Directory.CreateDirectory(_scratch);
            CheckAncestors(_scratch);
            while (_workingDirectory.Length > 240 && Path.GetDirectoryName(_workingDirectory) is { } parent) _workingDirectory = parent;
        }
        catch { Dispose(); throw; }
    }

    public async Task<PdfRasterSource> ProbeAsync(PdfFileProbe request, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        request.Validate();
        using var input = MediaFiles.OpenRead(request.Path);
        var hash = await SourceHash(input, token);
        var reply = await Run(input, ["--inspect"], PdfRasterProtocol.MaximumInspectionBytes, token);
        var document = PdfRasterProtocol.ReadInspection(reply);
        if (await SourceHash(input, token) != hash) throw new IOException("PDF changed during page inspection.");
        return new(request.ItemId, request.Path, hash, input.Length, document);
    }

    public async Task<PdfRenderedPage> RenderAsync(PdfRasterSource source, int pageIndex, CancellationToken token,
        bool transparentBackground = false, PdfPageFormat format = PdfPageFormat.Png)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        source.Validate();
        if (!Enum.IsDefined(format) || format == PdfPageFormat.Jpeg && transparentBackground)
            throw new InvalidDataException("Invalid PDF page image format or background.");
        if (pageIndex < 0 || pageIndex >= source.Document.Pages.Length) throw new InvalidDataException("PDF page is outside the inspected document.");
        var page = source.Document.Pages[pageIndex];
        using var input = MediaFiles.OpenRead(source.Path);
        if (input.Length != source.FileBytes || await SourceHash(input, token) != source.Sha256) throw new InvalidDataException("PDF changed after planning.");
        var reply = await Run(input, ["--render-page", pageIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)], PdfRasterProtocol.MaximumRenderedBytes, token);
        var pixels = PdfRasterProtocol.ReadPage(reply, page);
        if (!transparentBackground)
        {
            // Composite straight BGRA over ordinary white paper before encoding.
            // Keep colored page fills and antialiased foreground appearances.
            for (var offset = 0; offset < pixels.Length; offset += 4)
            {
                var alpha = pixels[offset + 3];
                for (var channel = 0; channel < 3; channel++)
                    pixels[offset + channel] = (byte)((pixels[offset + channel] * alpha + 255 * (255 - alpha) + 127) / 255);
                pixels[offset + 3] = 255;
            }
        }
        token.ThrowIfCancellationRequested();
        ImageEngineRuntime.Initialize(_scratch);
        using var image = new MagickImage();
        image.ReadPixels(pixels, new PixelReadSettings((uint)page.Width, (uint)page.Height, StorageType.Char, PixelMapping.BGRA));
        image.SetProfile(ColorProfiles.SRGB); image.ColorSpace = ColorSpace.sRGB; image.Depth = 8;
        image.Density = new Density(PdfRasterProtocol.Dpi, PdfRasterProtocol.Dpi, DensityUnit.PixelsPerInch);
        byte[] output;
        if (format == PdfPageFormat.Png)
        {
            image.Settings.SetDefine(MagickFormat.Png, "bit-depth", "8");
            image.Settings.SetDefine(MagickFormat.Png, "color-type", "6");
            image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
            image.Settings.SetDefine(MagickFormat.Png, "exclude-chunks", "date,time");
            output = image.ToByteArray(MagickFormat.Png);
        }
        else
        {
            image.Quality = 90;
            image.ColorType = ColorType.TrueColor;
            image.Settings.ColorType = ColorType.TrueColor;
            image.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "1x1,1x1,1x1");
            output = image.ToByteArray(MagickFormat.Jpeg);
        }
        if (output.Length > PdfPageWork.MaximumOutputBytes) throw new InvalidDataException("Rendered PDF page exceeds the output bound.");
        token.ThrowIfCancellationRequested();
        using var verified = new MagickImage(output);
        var decoded = verified.GetPixels().ToByteArray(PixelMapping.BGRA);
        var profile = verified.GetProfile("icc");
        if (verified.Width != page.Width || verified.Height != page.Height || verified.Depth != 8 || decoded is null ||
            verified.Format != (format == PdfPageFormat.Jpeg ? MagickFormat.Jpeg : MagickFormat.Png))
            throw new InvalidDataException("Rendered PDF page has invalid image geometry or format.");
        if (format == PdfPageFormat.Png && !decoded.AsSpan().SequenceEqual(pixels))
            throw new InvalidDataException("Rendered PNG did not preserve page pixels.");
        if (format == PdfPageFormat.Jpeg)
        {
            if (verified.Quality != 90 || !verified.IsOpaque || decoded.Length != pixels.Length)
                throw new InvalidDataException("Rendered JPEG has invalid quality or transparency.");
            long squaredError = 0;
            for (var offset = 0; offset < pixels.Length; offset += 4)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    var difference = decoded[offset + channel] - pixels[offset + channel];
                    squaredError += (long)difference * difference;
                }
            }
            if (squaredError > (long)page.Width * page.Height * 3 * 16 * 16)
                throw new InvalidDataException("Rendered JPEG differs too much from the page pixels.");
        }
        if (profile is null || !profile.ToByteArray().AsSpan().SequenceEqual(ColorProfiles.SRGB.ToByteArray()))
            throw new InvalidDataException("Rendered PDF page did not preserve its color profile.");
        if (await SourceHash(input, token) != source.Sha256) throw new IOException("PDF changed while rendering.");
        return new(page, output, source.Sha256, Convert.ToHexString(SHA256.HashData(output)),
            Convert.ToHexString(SHA256.HashData(pixels)), format == PdfPageFormat.Jpeg ? PdfRasterProtocol.JpegPolicy : PdfRasterProtocol.Policy);
    }

    public async Task<PdfPageResult> RenderFileAsync(PdfPageWork work, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        work.Validate();
        using var source = MediaFiles.OpenRead(work.Source.Path);
        if (source.Length != work.Source.FileBytes || await SourceHash(source, token) != work.Source.Sha256)
            throw new InvalidDataException("PDF changed after planning.");
        using var output = new ReservedMediaOutput(work.TemporaryPath, PdfPageWork.MaximumOutputBytes);
        var rendered = await RenderAsync(work.Source, work.PageIndex, token, work.TransparentBackground, work.Format);
        token.ThrowIfCancellationRequested();
        output.Write(rendered.Bytes);
        output.Flush(true);
        output.Position = 0;
        if (output.Length != rendered.Bytes.Length || Convert.ToHexString(await SHA256.HashDataAsync(output, token)) != rendered.OutputSha256 ||
            await SourceHash(source, token) != work.Source.Sha256)
            throw new IOException("PDF page output or source changed during validation.");
        return new(new(work.OutputId, rendered.OutputSha256, true), rendered.Page, rendered.SourceSha256, rendered.Bytes.Length,
            rendered.PixelSha256, rendered.Policy, "PDFium 8044; " + rendered.Policy);
    }

    private async Task<byte[]> Run(FileStream input, string[] arguments, int maximum, CancellationToken token)
    {
        CheckAncestors(_scratch);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try { return await SeekableMediaProcess.RunAsync(_executable, _workingDirectory, input, arguments, maximum, deadline.Token); }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new TimeoutException("PDF page rendering exceeded its deadline.", error); }
    }

    private static async Task<string> SourceHash(FileStream input, CancellationToken token)
    {
        if (input.Length <= 0) throw new InvalidDataException("PDF input is empty.");
        if (input.Length > PdfRasterProtocol.MaximumSourceBytes) throw new ImageFailureException(ImageFailure.ResourceLimit);
        input.Position = 0;
        return Convert.ToHexString(await SHA256.HashDataAsync(input, token));
    }

    private static void CheckAncestors(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked renderer directories are not allowed.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var lease in _leases) lease.Dispose();
    }
}
