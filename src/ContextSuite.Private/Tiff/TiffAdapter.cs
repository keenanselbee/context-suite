using System.Collections.Immutable;
using System.Security.Cryptography;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Tiff;
using ContextSuite.Private.Images;
using ContextSuite.Private.IO;
using ContextSuite.Private.Pdf;
using ImageMagick;

namespace ContextSuite.Private.Tiff;

public sealed class TiffAdapter
{
    private readonly ImagePdfValidator? _pdfValidator;
    public TiffAdapter(string scratchDirectory, ImagePdfValidator? pdfValidator = null)
    {
        ImageEngineRuntime.Initialize(scratchDirectory);
        _pdfValidator = pdfValidator;
    }

    public TiffSourceFacts Probe(TiffProbe request, CancellationToken token)
    {
        request.Validate();
        using var budget = new ProcessMemoryScope(1536UL * 1024 * 1024);
        using var source = MediaFiles.OpenRead(request.Path);
        var bytes = Read(source, token);
        var directories = TiffDirectory.Read(bytes, token);
        var pages = ImmutableArray.CreateBuilder<TiffPageFacts>();
        foreach (var directory in directories)
        {
            token.ThrowIfCancellationRequested();
            var facts = directory.Facts;
            if (facts.UnsupportedReason is null)
            {
                try
                {
                    var pixels = WicTiffDecoder.Read(bytes, directories.Length, facts, token);
                    using var image = ImageAdapter.PrepareTiffPixels(directory, pixels, token);
                    facts = facts with { HasTransparency = !image.IsOpaque };
                }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException or System.Runtime.InteropServices.COMException or MagickException or
                    ImageFailureException { Failure: ImageFailure.InvalidInput or ImageFailure.UnsupportedInput })
                { facts = facts with { UnsupportedReason = "This TIFF page is damaged or uses an unverified pixel or color variant. Other supported pages can still be copied." }; }
            }
            pages.Add(facts);
        }
        MediaFiles.CheckHandle(source, request.Path);
        var result = new TiffSourceFacts(request.ItemId, Path.GetFullPath(request.Path), Hash(bytes), bytes.LongLength, pages.ToImmutable());
        result.Validate();
        return result;
    }

    public async Task<TiffWorkResult> ConvertAsync(TiffWork request, CancellationToken token)
    {
        request.Validate();
        using var budget = new ProcessMemoryScope(1536UL * 1024 * 1024);
        using var source = MediaFiles.OpenRead(request.Source.Path);
        var bytes = Read(source, token);
        var directories = ValidateSource(request.Source, bytes, token);
        byte[] encoded;
        string engine;
        uint width = 0, height = 0;
        if (request.Options.Target == TiffTarget.Pdf)
        {
            if (_pdfValidator is null) throw new NotSupportedException("TIFF to PDF needs the packaged PDF validator.");
            var pages = request.Source.Pages.Select(page => page.CreatePdfPage()).ToArray();
            var candidate = ImagePdfWriter.Create(pages, index =>
            {
                using var image = DecodePage(bytes, directories, request.Source, index, token);
                using var pixels = image.GetPixels();
                token.ThrowIfCancellationRequested();
                return new RasterPdfPixels(pages[index], pixels.ToByteArray(PixelMapping.RGB)!,
                    pages[index].HasTransparency ? pixels.ToByteArray("A") : null, ColorProfiles.SRGB.ToByteArray());
            }, token);
            _ = await _pdfValidator.ValidateAsync(candidate, pages, token);
            encoded = candidate.Bytes;
            engine = "Windows TIFF; Context Suite raster PDF 1; qpdf 12.4.1 validation";
        }
        else
        {
            var page = request.Source.Pages[request.PageIndex];
            using var image = DecodePage(bytes, directories, request.Source, request.PageIndex, token);
            encoded = ImageAdapter.EncodeTiffPixels(image, page, request.Options, token);
            width = page.OutputWidth; height = page.OutputHeight;
            engine = "Windows TIFF; curated image encoder";
        }
        token.ThrowIfCancellationRequested();
        using (var output = new ReservedMediaOutput(request.TemporaryPath, TiffWork.MaximumOutputBytes))
        {
            if (output.Length != 0) throw new InvalidDataException("TIFF reservation is not empty.");
            for (var at = 0; at < encoded.Length; at += 65536)
            { token.ThrowIfCancellationRequested(); output.Write(encoded.AsSpan(at, Math.Min(65536, encoded.Length - at))); }
            output.Flush(true); output.Position = 0;
            if (output.Length != encoded.Length || Convert.ToHexString(await SHA256.HashDataAsync(output, token)) != Hash(encoded))
                throw new ImageFailureException(ImageFailure.ValidationFailed);
        }
        MediaFiles.CheckHandle(source, request.Source.Path);
        source.Position = 0;
        if (Convert.ToHexString(await SHA256.HashDataAsync(source, token)) != request.Source.Sha256)
            throw new ImageFailureException(ImageFailure.SourceChanged);
        return new(new(request.OutputId, Hash(encoded), true), request.Source.ItemId, request.PageIndex,
            request.Options.Target == TiffTarget.Pdf ? request.Source.Pages.Length : 1, width, height, encoded.Length, request.Source.Sha256, engine);
    }

    public ImagePreview Preview(TiffPreviewRequest request, CancellationToken token)
    {
        request.Validate();
        using var budget = new ProcessMemoryScope(1536UL * 1024 * 1024);
        using var source = MediaFiles.OpenRead(request.Source.Path);
        var bytes = Read(source, token);
        var directories = ValidateSource(request.Source, bytes, token);
        using var image = DecodePage(bytes, directories, request.Source, request.PageIndex, token);
        return ImageAdapter.PreviewTiffPixels(image, request.MatteRgb, token);
    }

    private static ImmutableArray<TiffDirectory> ValidateSource(TiffSourceFacts source, byte[] bytes, CancellationToken token)
    {
        if (bytes.LongLength != source.FileBytes || Hash(bytes) != source.Sha256) throw new ImageFailureException(ImageFailure.SourceChanged);
        var directories = TiffDirectory.Read(bytes, token);
        if (directories.Length != source.Pages.Length) throw new InvalidDataException("TIFF page count differs from its plan.");
        for (var i = 0; i < directories.Length; i++)
        {
            var declared = directories[i].Facts; var planned = source.Pages[i];
            if (declared with { HasTransparency = planned.HasTransparency, UnsupportedReason = planned.UnsupportedReason } != planned ||
                !declared.HasTransparency && planned.HasTransparency || declared.UnsupportedReason is not null && planned.UnsupportedReason is null)
                throw new InvalidDataException("TIFF directory facts differ from the confirmed plan.");
        }
        return directories;
    }

    private static MagickImage DecodePage(byte[] bytes, ImmutableArray<TiffDirectory> directories, TiffSourceFacts source, int index, CancellationToken token)
    {
        var pixels = WicTiffDecoder.Read(bytes, directories.Length, directories[index].Facts, token);
        var image = ImageAdapter.PrepareTiffPixels(directories[index], pixels, token);
        if (!image.IsOpaque == source.Pages[index].HasTransparency) return image;
        image.Dispose(); throw new InvalidDataException("TIFF alpha facts differ from the plan.");
    }

    private static byte[] Read(FileStream source, CancellationToken token)
    {
        if (source.Length is < 8 or > TiffSourceFacts.MaximumFileBytes) throw new ImageFailureException(ImageFailure.ResourceLimit);
        var bytes = new byte[(int)source.Length];
        for (var offset = 0; offset < bytes.Length;)
        {
            token.ThrowIfCancellationRequested();
            var read = source.Read(bytes, offset, Math.Min(65536, bytes.Length - offset));
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
        return bytes;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
