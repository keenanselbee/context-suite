using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;

internal static class ImageHeaderContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var jpeg = new byte[] { 0xff, 0xd8, 0xff, 0xc0, 0, 17, 8, 0, 3, 0, 4, 3, 1, 0x11, 0, 2, 0x11, 1, 3, 0x11, 1 };
        var gif = "GIF89a\x04\0\x03\0\0\0\0"u8.ToArray();
        var bmp = new byte[90]; "BM"u8.CopyTo(bmp);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2), 90);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10), 54);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 4);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), -3);
        bmp[26] = 1; bmp[28] = 24;
        var extended = WebP("VP8X"u8, [0x12, 0, 0, 0, 3, 0, 0, 2, 0, 0]);
        var losslessData = new byte[5]; losslessData[0] = 0x2f;
        BinaryPrimitives.WriteUInt32LittleEndian(losslessData.AsSpan(1), 3 | (2 << 14) | (1 << 28));
        var lossless = WebP("VP8L"u8, losslessData);
        var lossy = WebP("VP8 "u8, [0x10, 0, 0, 0x9d, 1, 0x2a, 4, 0, 3, 0]);
        var fixtures = new (string Name, byte[] Bytes, string Id)[]
        { ("jpeg", jpeg, "jpeg"), ("gif", gif, "gif"), ("bitmap", bmp, "bmp"),
          ("extended", extended, "webp"), ("lossless", lossless, "webp"), ("lossy", lossy, "webp") };
        foreach (var (name, bytes, id) in fixtures)
        {
            var result = Inspect(bytes);
            check(result.Identity is { Basis: IdentificationBasis.Content, Confidence: IdentificationConfidence.Likely } &&
                result.Identity.FormatId == id && Value(result, "image.width") == 4 && Value(result, "image.height") == 3,
                "image headers: " + name + " declares dimensions without relying on a filename");
            check(new[] { "image.transparency", "image.animation", "image.orientation", "image.color-profile" }
                .All(key => result.Facts.Single(fact => fact.Id == key).Availability == FactAvailability.Unavailable),
                "image headers: " + name + " does not infer decoded media properties");
        }
        check(Inspect(bmp).Facts.Single(fact => fact.Id == "bmp.row-order").Text == "Top down",
            "image headers: signed BMP height describes storage order, not EXIF display orientation");
        check(Inspect(extended).Facts.Single(fact => fact.Id == "webp.animation-flag").Boolean == true &&
            Inspect(lossless).Facts.Single(fact => fact.Id == "webp.alpha-flag").Boolean == true,
            "image headers: WebP declared feature bits are separate from actual transparency and animation");
        foreach (var marker in new byte[] { 0xc1, 0xc2, 0xc3 })
        {
            var variant = (byte[])jpeg.Clone(); variant[3] = marker; variant[6] = 12;
            check(Value(Inspect(variant), "jpeg.precision") == 12, "image headers: extended, progressive and lossless JPEG declarations");
        }
        var deferred = (byte[])jpeg.Clone(); deferred[8] = 0;
        check(Value(Inspect(deferred), "image.width") == 4 && Inspect(deferred).Facts.Single(fact => fact.Id == "image.height")
            .Availability == FactAvailability.Unavailable, "image headers: JPEG deferred height stays unavailable without scanning entropy data");
        var withApplication = new byte[] { 0xff, 0xd8, 0xff, 0xe1, 0, 6, 0xff, 0xc0, 0, 0 }.Concat(jpeg.Skip(2)).ToArray();
        check(Value(Inspect(withApplication), "image.width") == 4, "image headers: embedded marker-like metadata is skipped by its declared segment length");
        var core = new byte[30]; "BM"u8.CopyTo(core); core[10] = 26; core[14] = 12; core[18] = 4; core[20] = 3; core[22] = 1; core[24] = 24;
        check(Value(Inspect(core), "image.height") == 3, "image headers: bitmap core header uses unsigned dimensions");
        var oldGif = (byte[])gif.Clone(); oldGif[4] = (byte)'7';
        check(Inspect(oldGif).Facts.Single(fact => fact.Id == "gif.version").Text == "87a", "image headers: GIF87a signature supported");

        var malformed = new List<byte[]>();
        var invalidJpeg = (byte[])jpeg.Clone(); invalidJpeg[5] = 2; malformed.Add(invalidJpeg);
        invalidJpeg = (byte[])jpeg.Clone(); invalidJpeg[6] = 12; malformed.Add(invalidJpeg);
        malformed.Add([0xff, 0xd8, 0xff, 0xda, 0, 2, .. jpeg[2..]]);
        malformed.Add(new byte[] { 0xff, 0xd8 }.Concat(Enumerable.Repeat(new byte[] { 0xff, 0xfe, 0, 2 }, 256)
            .SelectMany(value => value)).Concat(jpeg.Skip(2)).ToArray());
        var invalidGif = (byte[])gif.Clone(); invalidGif[6] = 0; malformed.Add(invalidGif);
        invalidGif = (byte[])gif.Clone(); invalidGif[10] = 0x87; malformed.Add(invalidGif);
        var invalidBmp = (byte[])bmp.Clone(); BinaryPrimitives.WriteInt32LittleEndian(invalidBmp.AsSpan(22), int.MinValue); malformed.Add(invalidBmp);
        invalidBmp = (byte[])bmp.Clone(); invalidBmp[14] = 64; malformed.Add(invalidBmp);
        invalidBmp = (byte[])bmp.Clone(); invalidBmp[10] = 1; malformed.Add(invalidBmp);
        invalidBmp = (byte[])bmp.Clone(); invalidBmp[26] = 2; malformed.Add(invalidBmp);
        invalidBmp = (byte[])bmp.Clone(); invalidBmp[30] = 1; malformed.Add(invalidBmp);
        var invalidWebP = (byte[])extended.Clone(); invalidWebP[4] = 255; malformed.Add(invalidWebP);
        invalidWebP = (byte[])extended.Clone(); invalidWebP[16] = 255; malformed.Add(invalidWebP);
        invalidWebP = (byte[])extended.Clone(); Array.Fill(invalidWebP, (byte)255, 24, 6); malformed.Add(invalidWebP);
        invalidWebP = (byte[])lossless.Clone(); invalidWebP[24] |= 0xe0; malformed.Add(invalidWebP);
        invalidWebP = (byte[])lossy.Clone(); invalidWebP[20] |= 1; malformed.Add(invalidWebP);
        foreach (var bytes in malformed)
        {
            var result = Inspect(bytes);
            check(result.Identity.Basis == IdentificationBasis.Content && !result.Facts.Any(fact => fact.Id == "image.width") && result.Warnings.Length != 0,
                "image headers: malformed, unsupported or over-budget declarations retain identity without false dimensions");
        }
        var random = new Random(8819);
        foreach (var (_, bytes, _) in fixtures)
        {
            for (var length = 0; length < bytes.Length; length++) _ = HeaderAnalyzer.Analyze("prefix.bin", bytes.AsSpan(0, length), bytes.Length);
            for (var i = 0; i < 200; i++)
            {
                var mutated = (byte[])bytes.Clone(); mutated[random.Next(mutated.Length)] = (byte)random.Next(256);
                var result = Inspect(mutated);
                if (result.InspectedBytes != mutated.Length || result.Facts.Select(fact => fact.Id).Distinct().Count() != result.Facts.Length)
                    throw new InvalidDataException("Image mutation produced inconsistent facts.");
            }
        }
        check(true, "image headers: every fixture prefix and 1,200 deterministic mutations remain bounded without unexpected exceptions");
        var root = Path.Combine(scratch, "image-headers-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        foreach (var (name, bytes, id) in fixtures)
        {
            var path = Path.Combine(root, name + ".pdf"); await File.WriteAllBytesAsync(path, bytes);
            var time = File.GetLastWriteTimeUtc(path); var hash = SHA256.HashData(bytes);
            var result = await FileAnalysisReader.ReadAsync(path, default);
            var after = await File.ReadAllBytesAsync(path);
            check(result.Identity.FormatId == id && result.Warnings.Any(warning => warning.Contains("different type")) &&
                hash.SequenceEqual(SHA256.HashData(after)) && time == File.GetLastWriteTimeUtc(path),
                "image headers: real reader uses content despite PDF extension and preserves original " + name);
        }
        var pixels = Enumerable.Range(0, 7 * 5 * 3).Select(value => (byte)(value * 17)).ToArray();
        var image = BitmapSource.Create(7, 5, 96, 96, PixelFormats.Rgb24, null, pixels, 21); image.Freeze();
        foreach (var id in new[] { "jpeg", "gif", "bmp" })
        {
            // An async continuation may use a different thread after writing the
            // previous fixture. Create each thread-affine encoder where it is used.
            BitmapEncoder encoder = id switch { "jpeg" => new JpegBitmapEncoder(), "gif" => new GifBitmapEncoder(), _ => new BmpBitmapEncoder() };
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var encoded = new MemoryStream(); encoder.Save(encoded);
            var bytes = encoded.ToArray(); var result = Inspect(bytes);
            await File.WriteAllBytesAsync(Path.Combine(root, "windows-encoded." + id), bytes);
            check(result.Identity.FormatId == id && Value(result, "image.width") == 7 && Value(result, "image.height") == 5,
                "image headers: independently Windows-encoded " + id + " agrees with known source dimensions");
        }
    }

    private static FileAnalysis Inspect(byte[] bytes) => HeaderAnalyzer.Analyze("fixture.bin", bytes, bytes.Length);
    private static long? Value(FileAnalysis result, string id) => result.Facts.Single(fact => fact.Id == id).Integer;

    private static byte[] WebP(ReadOnlySpan<byte> tag, byte[] data)
    {
        var bytes = new byte[20 + data.Length + (data.Length & 1)];
        "RIFF"u8.CopyTo(bytes); "WEBP"u8.CopyTo(bytes.AsSpan(8)); tag.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)data.Length); data.CopyTo(bytes, 20);
        return bytes;
    }
}
