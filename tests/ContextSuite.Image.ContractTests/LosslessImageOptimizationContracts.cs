using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class LosslessImageOptimizationContracts
{
    public static void Run(string parent, Action<bool, string> check)
    {
        var root = Path.Combine(parent, "lossless-image-optimization");
        Directory.CreateDirectory(root);
        var adapter = new ImageAdapter(Path.Combine(root, "scratch"));
        foreach (var progressive in new[] { false, true })
        foreach (var orientation in new ushort[] { 1, 6, 8 })
        {
            using var image = new MagickImage(PngFixture.Create(97, 65, 8, 2,
                (x, y) => [(ushort)((x / 8 % 2) * 65535), (ushort)((y / 8 % 2) * 65535), (ushort)32768]));
            image.Quality = 90;
            image.Settings.Interlace = progressive ? Interlace.Plane : Interlace.NoInterlace;
            image.Settings.SetDefine(MagickFormat.Jpeg, "optimize-coding", false);
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, orientation);
            exif.SetValue(ExifTag.ImageDescription, "Original description");
            image.SetProfile(exif);
            image.SetProfile(new XmpProfile(Encoding.UTF8.GetBytes(
                "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description/></rdf:RDF></x:xmpmeta>")));
            image.SetProfile(ColorProfiles.SRGB);
            using var encoded = new MemoryStream();
            image.Write(encoded, MagickFormat.Jpeg);
            var bytes = AddComment(SplitIcc(encoded.ToArray()));
            Exercise($"jpeg-{progressive}-{orientation}", ".jpg", bytes, ImageFormat.Jpeg);
            var unknown = bytes.ToArray();
            unknown[3] = 227;
            Reject(() => new JpegLosslessEnvelope(unknown), "unknown JPEG application marker refused");
        }
        foreach (var alpha in new[] { false, true })
        {
            using var image = new MagickImage(PngFixture.Create(71, 53, 8, (byte)(alpha ? 6 : 2),
                (x, y) => alpha ? [(ushort)(x * 257), (ushort)(y * 257), (ushort)50000, (ushort)(x % 3 == 0 ? 0 : 65535)] :
                    [(ushort)(x * 257), (ushort)(y * 257), (ushort)50000]));
            image.Quality = 100;
            image.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
            image.Settings.SetDefine(MagickFormat.WebP, "exact", true);
            image.Settings.SetDefine(MagickFormat.WebP, "method", "0");
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            exif.SetValue(ExifTag.ImageDescription, "Keep exact EXIF bytes");
            image.SetProfile(exif);
            image.SetProfile(ColorProfiles.SRGB);
            using var encoded = new MemoryStream();
            image.Write(encoded, MagickFormat.WebP);
            var bytes = encoded.ToArray();
            Exercise("webp-" + alpha, ".webp", bytes, ImageFormat.WebP);
            var unknown = bytes.Concat(new byte[] { 84, 69, 83, 84, 0, 0, 0, 0 }).ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(unknown.AsSpan(4), (uint)unknown.Length - 8);
            Reject(() => new WebPLosslessEnvelope(unknown), "unknown WebP chunk refused");
            var malformed = bytes.ToArray();
            var payload = FindWebPChunk(malformed, "VP8L");
            malformed[payload + 12] |= 0x80;
            Reject(() => new WebPLosslessEnvelope(malformed), "unsupported VP8L version refused");
        }
        using (var lossy = new MagickImage(PngFixture.Create(32, 32, 8, 2, (_, _) => [65535, 0, 0])))
        {
            lossy.Quality = 80;
            using var encoded = new MemoryStream();
            lossy.Write(encoded, MagickFormat.WebP);
            Exercise("webp-lossy-exact-decoded", ".webp", encoded.ToArray(), ImageFormat.WebP);
        }
        using (var lossyAlpha = new MagickImage(PngFixture.Create(47, 39, 8, 6, (x, y) =>
            [(ushort)(x * 1000), (ushort)(y * 1000), (ushort)40000, (ushort)(x % 3 == 0 ? 0 : x % 3 == 1 ? 32768 : 65535)])))
        {
            lossyAlpha.Quality = 80;
            lossyAlpha.Settings.SetDefine(MagickFormat.WebP, "lossless", false);
            using var encoded = new MemoryStream();
            lossyAlpha.Write(encoded, MagickFormat.WebP);
            Exercise("webp-lossy-alpha-exact-decoded", ".webp", encoded.ToArray(), ImageFormat.WebP);
        }
        var widePath = Path.Combine(root, "decoder-valid-too-wide.webp");
        File.WriteAllBytes(widePath, SolidLosslessWebP(16384));
        // VP8L can declare 16,384 pixels, while the selected encoder stops at
        // 16,383. Author a tiny valid single-color stream without that encoder.
        var wide = adapter.Probe(new(Guid.NewGuid(), widePath), default);
        check(wide.Width == 16384 && wide.Height == 1 && !wide.IsLossy, "WebP width guard: independent VP8L fixture decodes at 16,384 pixels");
        var wideTemporary = Path.Combine(root, $".context-suite-{wide.ItemId:N}.tmp");
        File.WriteAllBytes(wideTemporary, []);
        RejectWide(() => adapter.ProbeLosslessOptimization(new(wide.ItemId, widePath), default), "probe");
        RejectWide(() => adapter.OptimizeLosslessImage(new(wide, wideTemporary, LosslessImageOptimizationPlan.WebPPolicy), default), "execution");
        check(new FileInfo(wideTemporary).Length == 0, "WebP width guard: unsupported encoding width leaves reservation empty");

        void RejectWide(Action action, string stage)
        {
            var rejected = false;
            try { action(); }
            catch (ImageFailureException error) when (error.Failure == ImageFailure.UnsupportedInput) { rejected = true; }
            check(rejected, "WebP width guard: " + stage + " rejects unsupported encoding width before encoding");
        }

        void Exercise(string name, string extension, byte[] original, ImageFormat format)
        {
            var path = Path.Combine(root, name + " Unicode \u03bb" + extension);
            File.WriteAllBytes(path, original);
            var facts = adapter.ProbeLosslessOptimization(new(Guid.NewGuid(), path), default);
            check(facts.Format == format && facts.UnsupportedReason is null, name + ": admitted exact representation");
            var temporary = Path.Combine(root, $".context-suite-{facts.ItemId:N}.tmp");
            File.WriteAllBytes(temporary, []);
            var work = new LosslessImageOptimizationWork(facts, temporary, LosslessImageOptimizationPlan.PolicyFor(format));
            var result = adapter.OptimizeLosslessImage(work, default);
            var output = File.ReadAllBytes(temporary);
            check(result.Validation.MatchesPlan && result.Validation.Sha256 == Convert.ToHexString(SHA256.HashData(output)) &&
                result.OptimizationMethod == "Lossless" && result.OptimizationAttempts == 1, name + ": validated hash and one lossless recipe");
            check(output.Length <= original.Length && File.ReadAllBytes(path).SequenceEqual(original), name + ": no larger output and unchanged source");
            using var before = new MagickImage(original);
            using var after = new MagickImage(output);
            check(before.Width == after.Width && before.Height == after.Height &&
                before.GetPixels().ToArray()!.SequenceEqual(after.GetPixels().ToArray()!), name + ": exact samples including transparent RGB");
            check(before.ProfileNames.All(profile => before.GetProfile(profile)!.ToByteArray().SequenceEqual(after.GetProfile(profile)!.ToByteArray())),
                name + ": independent decoder retains complete ICC, EXIF, and XMP");
            check(format == ImageFormat.Jpeg
                ? new JpegLosslessEnvelope(original).MetadataBytes().SequenceEqual(new JpegLosslessEnvelope(output).MetadataBytes())
                : new WebPLosslessEnvelope(original).MetadataBytes().SequenceEqual(new WebPLosslessEnvelope(output).MetadataBytes()),
                name + ": raw original metadata and framing retained");
            if (format == ImageFormat.WebP && original.AsSpan(12, 4).SequenceEqual("VP8X"u8))
                check(original.AsSpan(12, 18).SequenceEqual(output.AsSpan(12, 18)), name + ": original extended canvas and alpha header retained");
            var afterFacts = adapter.ProbeLosslessOptimization(new(Guid.NewGuid(), temporary), default);
            check(afterFacts.Orientation == facts.Orientation && afterFacts.Resolution == facts.Resolution,
                name + ": original orientation and resolution retained");
            Reject(() => adapter.OptimizeLosslessImage(work with { Policy = "unknown" }, default), name + ": unknown policy refused");
            Reject(() => adapter.OptimizeLosslessImage(work with { TemporaryPath = path }, default), name + ": direct source write refused");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var cancellationObserved = false;
            try { adapter.OptimizeLosslessImage(work, cancelled.Token); }
            catch (OperationCanceledException) { cancellationObserved = true; }
            check(cancellationObserved && File.ReadAllBytes(path).SequenceEqual(original), name + ": cancellation preserves source");
            File.WriteAllBytes(path, original.Concat(new byte[] { 0 }).ToArray());
            Reject(() => adapter.OptimizeLosslessImage(work, default), name + ": source change refused");
            File.WriteAllBytes(path, original);
        }

        void Reject(Action action, string name)
        {
            var rejected = false;
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or ImageFailureException or IOException) { rejected = true; }
            check(rejected, "Lossless images: " + name);
        }
    }

    private static byte[] AddComment(byte[] bytes)
    {
        var comment = Encoding.ASCII.GetBytes("Keep original comment");
        using var output = new MemoryStream();
        output.Write(bytes.AsSpan(0, 2));
        output.Write(new byte[] { 255, 254, 0, (byte)(comment.Length + 2) });
        output.Write(comment);
        output.Write(bytes.AsSpan(2));
        return output.ToArray();
    }

    private static int FindWebPChunk(byte[] bytes, string type)
    {
        for (var position = 12; position < bytes.Length;)
        {
            if (Encoding.ASCII.GetString(bytes, position, 4) == type) return position;
            var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 4)));
            position += length + 8 + (length & 1);
        }
        throw new InvalidDataException("Fixture payload missing.");
    }

    private static byte[] SplitIcc(byte[] bytes)
    {
        using var output = new MemoryStream();
        output.Write(bytes.AsSpan(0, 2));
        var position = 2;
        while (position < bytes.Length)
        {
            if (bytes[position + 1] == 218) { output.Write(bytes.AsSpan(position)); break; }
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position + 2));
            var data = bytes.AsSpan(position + 4, length - 2);
            if (bytes[position + 1] == 226 && data.StartsWith("ICC_PROFILE\0"u8))
            {
                var profile = data[14..];
                var midpoint = profile.Length / 2;
                for (var index = 0; index < 2; index++)
                {
                    var part = index == 0 ? profile[..midpoint] : profile[midpoint..];
                    var segment = new byte[part.Length + 18];
                    segment[0] = 255; segment[1] = 226;
                    BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), checked((ushort)(part.Length + 16)));
                    "ICC_PROFILE\0"u8.CopyTo(segment.AsSpan(4));
                    segment[16] = (byte)(index + 1); segment[17] = 2;
                    part.CopyTo(segment.AsSpan(18));
                    output.Write(segment);
                }
            }
            else output.Write(bytes.AsSpan(position, length + 2));
            position += length + 2;
        }
        return output.ToArray();
    }

    private static byte[] SolidLosslessWebP(uint width)
    {
        // No transforms, cache, or meta-Huffman image. Every channel's simple
        // Huffman tree has one symbol, so all pixels require zero entropy bits.
        var entropy = new List<byte>(); var position = 0;
        WriteBits(0, 3);
        foreach (var symbol in new uint[] { 0, 0, 0, 255, 0 })
        {
            WriteBits(1, 1); WriteBits(0, 1);
            WriteBits(symbol > 1 ? 1u : 0u, 1);
            WriteBits(symbol, symbol > 1 ? 8 : 1);
        }
        var payload = new byte[5 + entropy.Count];
        payload[0] = 0x2f;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1), width - 1);
        entropy.CopyTo(payload, 5);
        var bytes = new byte[20 + payload.Length + (payload.Length & 1)];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length - 8);
        "WEBPVP8L"u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)payload.Length);
        payload.CopyTo(bytes, 20);
        return bytes;

        void WriteBits(uint value, int count)
        {
            for (var bit = 0; bit < count; bit++, position++)
            {
                if (position / 8 == entropy.Count) entropy.Add(0);
                entropy[position / 8] |= (byte)(((value >> bit) & 1) << (position % 8));
            }
        }
    }
}
