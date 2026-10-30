using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContextSuite.Core.Images;
using ContextSuite.Core.Svg;
using ContextSuite.Private.Images;
using ContextSuite.Private.IO;
using ImageMagick;

namespace ContextSuite.Private.Svg;

public sealed class SvgAdapter
{
    private const string ExecutableName = "ContextSuite.Svg.exe";
    private const string ExecutableSha256 = "1348469816E870C68857423BBBEAB045FAB45BA848E085079A866A358004B090";
    private const int MaximumHeaderBytes = 32768;
    private static readonly JsonSerializerOptions HeaderOptions = new()
    { MaxDepth = 8, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public SvgAdapter(string scratchDirectory) { ImageEngineRuntime.Initialize(scratchDirectory); }

    public async Task<SvgSourceFacts> ProbeAsync(SvgProbe request, CancellationToken token)
    {
        request.Validate();
        using var source = MediaFiles.OpenRead(request.Path);
        CheckLength(source);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, token)); source.Position = 0;
        var bytes = await RunAsync(source, ["inspect"], async (stream, cancellation) =>
        {
            using var result = new MemoryStream();
            var buffer = new byte[4096]; int count;
            while ((count = await stream.ReadAsync(buffer, cancellation)) != 0)
            {
                if (result.Length + count > MaximumHeaderBytes) throw new ImageFailureException(ImageFailure.ValidationFailed);
                result.Write(buffer, 0, count);
            }
            return result.ToArray();
        }, token);
        var header = ReadHeader(bytes);
        var facts = Facts(request.ItemId, request.Path, hash, source.Length, header);
        if (header.Width != Math.Ceiling(facts.IntrinsicWidth) || header.Height != Math.Ceiling(facts.IntrinsicHeight))
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        await VerifySourceAsync(source, facts, token);
        return facts;
    }

    public async Task<SvgWorkResult> ConvertAsync(SvgWork request, CancellationToken token)
    {
        request.Validate();
        using var budget = new ProcessMemoryScope(1536UL * 1024 * 1024);
        using var source = MediaFiles.OpenRead(request.Source.Path);
        CheckLength(source);
        await VerifySourceAsync(source, request.Source, token); source.Position = 0;
        var size = request.Options.GetSize(request.Source);
        var rendered = await RunAsync(source, ["render", size.Width.ToString(CultureInfo.InvariantCulture), size.Height.ToString(CultureInfo.InvariantCulture)],
            async (stream, cancellation) =>
            {
                var lengthBytes = new byte[4]; await stream.ReadExactlyAsync(lengthBytes, cancellation);
                var length = BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes);
                if (length is 0 or > MaximumHeaderBytes) throw new ImageFailureException(ImageFailure.ValidationFailed);
                var headerBytes = new byte[length]; await stream.ReadExactlyAsync(headerBytes, cancellation);
                var header = ReadHeader(headerBytes);
                var observed = Facts(request.Source.ItemId, request.Source.Path, request.Source.Sha256, source.Length, header);
                if (header.Width != size.Width || header.Height != size.Height ||
                    observed.IntrinsicWidth != request.Source.IntrinsicWidth || observed.IntrinsicHeight != request.Source.IntrinsicHeight ||
                    observed.MaximumSide != request.Source.MaximumSide || observed.MaximumPixels != request.Source.MaximumPixels ||
                    observed.FontFingerprint != request.Source.FontFingerprint || !observed.FontNotices.SequenceEqual(request.Source.FontNotices))
                    throw new ImageFailureException(ImageFailure.SourceChanged);
                var pixels = new byte[checked(size.Width * size.Height * 4)];
                await stream.ReadExactlyAsync(pixels, cancellation);
                if (await stream.ReadAsync(new byte[1], cancellation) != 0) throw new ImageFailureException(ImageFailure.ValidationFailed);
                return pixels;
            }, token);
        var encoded = Encode(rendered, size, request.Options, token);
        using (var output = new ReservedMediaOutput(request.TemporaryPath, SvgWork.MaximumOutputBytes))
        {
            if (output.Length != 0) throw new InvalidDataException("SVG output reservation is not empty.");
            for (var at = 0; at < encoded.Length; at += 65536)
            { token.ThrowIfCancellationRequested(); output.Write(encoded.AsSpan(at, Math.Min(65536, encoded.Length - at))); }
            output.Flush(true); output.Position = 0;
            if (output.Length != encoded.Length || Convert.ToHexString(await SHA256.HashDataAsync(output, token)) != Hash(encoded))
                throw new ImageFailureException(ImageFailure.ValidationFailed);
        }
        await VerifySourceAsync(source, request.Source, token);
        return new(new(request.OutputId, Hash(encoded), true), request.Source.ItemId, size.Width, size.Height, encoded.Length,
            request.Source.Sha256, request.Source.FontFingerprint,
            "resvg 0.48.1; Context Suite SVG 1; curated " + (request.Options.Target == ImageFormat.Jpeg ? "JPEG quality 90" : "PNG") + " encoder",
            request.Options.Target);
    }

    private static byte[] Encode(byte[] pixels, SvgPixelSize size, SvgConversionOptions options, CancellationToken token)
    {
        using var image = new MagickImage();
        image.Progress += (_, progress) => { if (token.IsCancellationRequested) progress.Cancel = true; };
        image.ReadPixels(pixels, new PixelReadSettings(size.Width, size.Height, StorageType.Char, PixelMapping.RGBA));
        image.ColorSpace = ColorSpace.sRGB; image.Depth = 8; image.SetProfile(ColorProfiles.SRGB);
        image.Settings.SetDefine(MagickFormat.Png, "color-type", "6");
        image.Settings.SetDefine(MagickFormat.Png, "bit-depth", "8");
        image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
        image.Settings.SetDefine(MagickFormat.Png, "exclude-chunks", "date,time");
        var format = options.Target == ImageFormat.Jpeg ? MagickFormat.Jpeg : MagickFormat.Png;
        if (options.Target == ImageFormat.Jpeg)
        {
            image.BackgroundColor = new MagickColor($"#{options.MatteRgb!.Value:X6}");
            image.Alpha(AlphaOption.Remove);
            image.Quality = 90;
            image.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "1x1,1x1,1x1");
        }
        using var output = new MemoryStream();
        token.ThrowIfCancellationRequested(); image.Write(output, format);
        if (output.Length is <= 0 or > SvgWork.MaximumOutputBytes) throw new ImageFailureException(ImageFailure.ResourceLimit);
        var encoded = output.ToArray();
        var container = ImageContainer.Read(encoded);
        using var decoded = new MagickImage(encoded, format);
        if (container.Format != options.Target || decoded.Width != size.Width || decoded.Height != size.Height || decoded.Depth != 8 ||
            decoded.GetColorProfile()?.ToByteArray() is not { } profile || !profile.SequenceEqual(ColorProfiles.SRGB.ToByteArray()) ||
            decoded.GetPixels().ToByteArray(PixelMapping.RGBA) is not { } actual ||
            options.Target == ImageFormat.Png && !actual.SequenceEqual(pixels) ||
            options.Target == ImageFormat.Jpeg && (!decoded.IsOpaque || decoded.Quality != 90))
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        if (options.Target == ImageFormat.Jpeg)
        {
            var expected = image.GetPixels().ToByteArray(PixelMapping.RGBA)!;
            long squaredError = 0;
            for (var at = 0; at < expected.Length; at += 4)
            {
                if (at % 65536 == 0) token.ThrowIfCancellationRequested();
                for (var channel = 0; channel < 3; channel++)
                {
                    var difference = actual[at + channel] - expected[at + channel];
                    squaredError += (long)difference * difference;
                }
            }
            if (squaredError > (long)size.Width * size.Height * 3 * 16 * 16)
                throw new ImageFailureException(ImageFailure.ValidationFailed);
        }
        token.ThrowIfCancellationRequested();
        return encoded;
    }

    private static SvgSourceFacts Facts(Guid id, string path, string hash, long length, Header header)
    {
        if (header.Abi != 1) throw new ImageFailureException(ImageFailure.ValidationFailed);
        var facts = new SvgSourceFacts(id, path, hash, length, header.IntrinsicWidth, header.IntrinsicHeight,
            header.MaximumSide, header.MaximumPixels, header.FontFingerprint, header.FontNotices);
        facts.Validate(); return facts;
    }

    private static Header ReadHeader(byte[] bytes)
    {
        try { return JsonSerializer.Deserialize<Header>(bytes, HeaderOptions) ?? throw new ImageFailureException(ImageFailure.ValidationFailed); }
        catch (JsonException error) { throw new ImageFailureException(ImageFailure.ValidationFailed, error); }
    }

    private static async Task<T> RunAsync<T>(FileStream source, string[] arguments,
        Func<Stream, CancellationToken, Task<T>> consume, CancellationToken token)
    {
        using var directories = new RuntimeDirectoryLeases();
        var directory = Path.Combine(AppContext.BaseDirectory, "svg");
        var executable = Path.Combine(directory, ExecutableName);
        using var engine = OpenEngine();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            return await SeekableMediaProcess.RunAsync(executable, directory, source, arguments, consume, deadline.Token,
                code => new ImageFailureException(code switch
                { 2 => ImageFailure.InvalidInput, 3 => ImageFailure.UnsupportedInput, 4 => ImageFailure.ResourceLimit, _ => ImageFailure.EngineFailure }));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new ImageFailureException(ImageFailure.TimedOut); }

        FileStream OpenEngine()
        {
            FileStream? lease = null;
            try
            {
                directories.Open(directory);
                if (Directory.EnumerateFiles(directory, "*.dll").Any() ||
                    !Directory.EnumerateFiles(directory, "*.exe").Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals([ExecutableName]))
                    throw new InvalidDataException("SVG renderer runtime membership mismatch.");
                lease = directories.OpenFileRead(executable);
                if (Convert.ToHexString(SHA256.HashData(lease)) != ExecutableSha256)
                    throw new InvalidDataException("SVG renderer identity mismatch.");
                return lease;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { lease?.Dispose(); throw new ImageFailureException(ImageFailure.EngineUnavailable, error); }
        }
    }

    private static void CheckLength(FileStream source)
    {
        if (source.Length is <= 0 or > SvgSourceFacts.MaximumFileBytes) throw new ImageFailureException(ImageFailure.ResourceLimit);
    }

    private static async Task VerifySourceAsync(FileStream source, SvgSourceFacts expected, CancellationToken token)
    {
        MediaFiles.CheckHandle(source, expected.Path); source.Position = 0;
        if (source.Length != expected.FileBytes || Convert.ToHexString(await SHA256.HashDataAsync(source, token)) != expected.Sha256)
            throw new ImageFailureException(ImageFailure.SourceChanged);
    }

    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    private sealed record Header(uint Abi, double IntrinsicWidth, double IntrinsicHeight, uint Width, uint Height,
        uint MaximumSide, ulong MaximumPixels, string FontFingerprint, ImmutableArray<string> FontNotices);
}
