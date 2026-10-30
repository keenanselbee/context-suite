using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Images;
using ContextSuite.Private.Images;
using ImageMagick;

internal static class PngOptimizationContracts
{
    public static async Task RunAsync(string parent, Action<bool, string> check)
    {
        var root = Path.Combine(parent, "png-optimization");
        Directory.CreateDirectory(root);
        var adapter = new ImageAdapter(Path.Combine(root, "scratch"));
        foreach (var (depth, color) in new (byte, byte)[] { (8, 0), (16, 0), (8, 2), (16, 2), (8, 4), (16, 4), (8, 6), (16, 6), (1, 0), (2, 0), (4, 0), (1, 3), (2, 3), (4, 3), (8, 3) })
        {
            var bytes = Fixture(depth, color);
            var name = $"png-{color}-{depth}";
            var source = Path.Combine(root, name + " 空白.png");
            File.WriteAllBytes(source, bytes);
            var before = SHA256.HashData(bytes);
            var facts = adapter.ProbeOptimization(new(Guid.NewGuid(), source), default);
            check(facts.UnsupportedReason is null, name + ": admitted PNG sample representation");
            check((facts.PngLossyBlockReason is null) == (depth == 8 && color is 2 or 6), name + ": lossy eligibility is representation-specific");
            var temporary = Path.Combine(root, $".context-suite-{facts.ItemId:N}.tmp");
            File.WriteAllBytes(temporary, []);
            var result = await adapter.OptimizeAsync(new(facts, temporary), default);
            var output = File.ReadAllBytes(temporary);
            check(result.Validation.MatchesPlan && result.Validation.Sha256 == Convert.ToHexString(SHA256.HashData(output)) && result.BitDepth == depth,
                name + ": output digest and exact bit depth validated");
            check(output.Length < bytes.Length && SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(before), name + ": reduces size and preserves source");
            check(NonData(bytes).SequenceEqual(NonData(output)), name + ": original non-IDAT chunks preserved byte for byte");
            using var first = new MagickImage(bytes);
            using var second = new MagickImage(output);
            check(first.GetPixels().ToArray()!.SequenceEqual(second.GetPixels().ToArray()!), name + ": second decoder verifies samples including hidden RGB");
            var validator = new PngLosslessContainer(bytes);
            var stable = validator.RestoreAndValidate(output, default);
            check(stable.SequenceEqual(output), name + ": restoration is stable");
        }

        var ordinary = Fixture(8, 6);
        using (var compressedProfile = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedProfile, CompressionLevel.Fastest, true))
                zlib.Write(ColorProfiles.SRGB.ToByteArray());
            var profiled = AddChunk(RemoveChunk(ordinary, "sRGB"), "iCCP", "Profile\0\0"u8.ToArray().Concat(compressedProfile.ToArray()).ToArray());
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            exif.SetValue(ExifTag.ImageDescription, "Keep orientation and description");
            var exifBytes = exif.ToByteArray()!;
            profiled = AddChunk(profiled, "eXIf", exifBytes.AsSpan().StartsWith("Exif\0\0"u8) ? exifBytes[6..] : exifBytes);
            profiled = AddChunk(profiled, "iTXt", "XML:com.adobe.xmp\0\0\0\0\0"u8.ToArray().Concat(Encoding.UTF8.GetBytes(
                "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description/></rdf:RDF></x:xmpmeta>")).ToArray());
            var path = Path.Combine(root, "profiled-oriented.png");
            File.WriteAllBytes(path, profiled);
            var facts = adapter.ProbeOptimization(new(Guid.NewGuid(), path), default);
            check(facts.PngLossyBlockReason is not null, "PNG ICC: retained by Lossless, blocked by precision presets");
            var temporary = Path.Combine(root, $".context-suite-{facts.ItemId:N}.tmp");
            File.WriteAllBytes(temporary, []);
            await adapter.OptimizeAsync(new(facts, temporary), default);
            var output = File.ReadAllBytes(temporary);
            check(NonData(profiled).SequenceEqual(NonData(output)), "PNG profiles: ICC, EXIF, XMP and original orientation chunks retained exactly");
            var after = adapter.ProbeOptimization(new(Guid.NewGuid(), temporary), default);
            check(after.Width == facts.Width && after.Height == facts.Height && after.Orientation == 6 && after.ProfileNames.SequenceEqual(facts.ProfileNames),
                "PNG profiles: optimization does not auto-orient or strip profiles");
        }
        byte[][] filtered = [[0,10,20,30,0,40,50,60], [1,10,10,10,1,40,10,10], [2,10,20,30,2,30,30,30],
            [3,10,15,20,3,35,20,20], [4,10,10,10,4,30,10,10]];
        for (var filter = 0; filter < filtered.Length; filter++)
            check(new PngLosslessContainer(FilterFixture(filtered[filter])).SampleDigest(default).SequenceEqual(SHA256.HashData(new byte[] {10,20,30,40,50,60})),
                "PNG independent validator: exact known samples for filter " + filter);
        Reject(() => new PngLosslessContainer(FilterFixture([5,10,20,30,0,40,50,60])).SampleDigest(default), "invalid row filter");
        Reject(() => new PngLosslessContainer(FilterFixture([0,10,20,30])).SampleDigest(default), "truncated inflated samples");
        Reject(() => new PngLosslessContainer(FilterFixture([0,10,20,30,0,40,50,60,0])).SampleDigest(default), "excess inflated samples");
        var originalPath = Path.Combine(root, "fault-source.png");
        File.WriteAllBytes(originalPath, ordinary);
        var admitted = adapter.ProbeOptimization(new(Guid.NewGuid(), originalPath), default);
        var reservation = Path.Combine(root, $".context-suite-{admitted.ItemId:N}.tmp");
        File.WriteAllBytes(reservation, []);
        await RejectAsync(() => adapter.OptimizeAsync(new(admitted, reservation, "unknown"), default), "unknown policy");
        await RejectAsync(() => adapter.OptimizeAsync(new(admitted with { Sha256 = new string('0', 64) }, reservation), default), "stale digest");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await RejectAsync(() => adapter.OptimizeAsync(new(admitted, reservation), cancelled.Token), "pre-cancelled execution");
        }
        check(new FileInfo(reservation).Length == 0 && File.ReadAllBytes(originalPath).SequenceEqual(ordinary), "PNG rejected work does not touch reservation or source");
        File.WriteAllBytes(reservation, [1, 2, 3]);
        await RejectAsync(() => adapter.OptimizeAsync(new(admitted, reservation), default), "nonempty reservation");
        check(File.ReadAllBytes(reservation).SequenceEqual(new byte[] { 1, 2, 3 }), "PNG nonempty reservation retained");
        await RejectAsync(() => adapter.OptimizeAsync(new(admitted, originalPath), default), "source-as-output");

        var badPixel = Fixture(8, 6, altered: true);
        Reject(() => new PngLosslessContainer(ordinary).RestoreAndValidate(badPixel, default), "changed hidden RGB sample");
        Reject(() => new PngLosslessContainer(ordinary).RestoreAndValidate(Fixture(16, 6), default), "changed precision");
        var corrupt = ordinary.ToArray(); corrupt[29] ^= 1;
        Reject(() => new PngLosslessContainer(corrupt), "corrupt CRC");
        Reject(() => new PngLosslessContainer(ordinary[..^2]), "truncated PNG");
        Reject(() => new PngLosslessContainer(AddChunk(ordinary, "acTL", new byte[8])), "animation");
        Reject(() => new PngLosslessContainer(AddChunk(ordinary, "iDOT", [])), "offset-dependent metadata");
        Reject(() => new PngLosslessContainer(AddChunk(ordinary, "caBX", [])), "signed provenance metadata");
        Reject(() => new PngLosslessContainer(AddChunk(ordinary, "vpAU", [])), "unknown unsafe-to-copy chunk");
        Reject(() => new PngLosslessContainer(AddChunk(ordinary, "fdEC", [1, 2, 3, 4, 5])), "Afterburner fdEC is not silently stripped");
        var tagged = AddChunk(AddChunk(ordinary, "tEXt", "Comment\0keep me"u8.ToArray()), "fdEC", [1, 2, 3, 4, 5]);
        var cleaned = PngLosslessContainer.RemoveFdEC(tagged, default, out var removed);
        check(removed && cleaned.SequenceEqual(AddChunk(ordinary, "tEXt", "Comment\0keep me"u8.ToArray())),
            "fdEC normalization removes only fdEC, retaining every other byte");
        var badTagged = tagged.ToArray(); badTagged[41] ^= 1;
        Reject(() => PngLosslessContainer.RemoveFdEC(badTagged, default, out _), "fdEC cannot hide invalid CRC");
        var taggedPath = Path.Combine(root, "fdEC-copy.png");
        File.WriteAllBytes(taggedPath, tagged);
        foreach (var policy in new[] { PngOptimizationPlan.Policy, PngOptimizationPlan.AutoPolicy, PngOptimizationPlan.BalancedPolicy, PngOptimizationPlan.SmallestPolicy })
        {
            var facts = adapter.ProbeOptimization(new(Guid.NewGuid(), taggedPath), default);
            check(facts.PngFdECRemovalRequired && facts.UnsupportedReason is null, "fdEC is admitted with explicit removal facts");
            var temporary = Path.Combine(root, $".context-suite-{facts.ItemId:N}.tmp");
            File.WriteAllBytes(temporary, []);
            var work = await adapter.OptimizeAsync(new(facts, temporary, policy), default);
            var output = File.ReadAllBytes(temporary);
            check(work.PngFdECRemoved && NonData(output).SequenceEqual(NonData(cleaned)) &&
                File.ReadAllBytes(taggedPath).SequenceEqual(tagged), "fdEC presets report removal and preserve other chunks and original");
            if (policy == PngOptimizationPlan.Policy)
                check(new PngLosslessContainer(cleaned).SampleDigest(default).SequenceEqual(new PngLosslessContainer(output).SampleDigest(default)),
                    "fdEC Lossless preserves all image and alpha samples");
            await RejectAsync(() => adapter.OptimizeAsync(new(facts with { PngFdECRemovalRequired = false }, temporary, policy), default),
                "mismatched metadata removal facts");
        }
        var withMetadata = AddChunk(ordinary, "vpAg", Encoding.ASCII.GetBytes("opaque private metadata"));
        check(new PngLosslessContainer(withMetadata).RestoreAndValidate(ordinary, default).SequenceEqual(withMetadata), "PNG unknown safe-to-copy metadata retained");
        check(new PngLosslessContainer(withMetadata).LossyBlockReason is not null, "PNG unknown metadata requires Lossless after sample changes");
        foreach (var (depth, color) in new (byte, byte)[] { (1,0), (2,0), (4,0), (8,0), (16,0),
            (1,3), (2,3), (4,3), (8,3), (8,2), (16,2), (8,4), (16,4), (8,6), (16,6) })
        foreach (var (width, height) in new[] { (1,1), (2,3), (4,1), (5,7), (17,19) })
        {
            var bytes = InterlacedFixture(depth, color, width, height);
            var path = Path.Combine(root, $"adam7-{depth}-{color}-{width}-{height}.png");
            File.WriteAllBytes(path, bytes);
            var facts = adapter.ProbeOptimization(new(Guid.NewGuid(), path), default);
            check(facts.UnsupportedReason is null && facts.PngLossyBlockReason is not null,
                "Adam7: admitted with lossless fallback: " + Path.GetFileName(path));
            var temporary = Path.Combine(root, $".context-suite-{facts.ItemId:N}.tmp");
            File.WriteAllBytes(temporary, []);
            var result = await adapter.OptimizeAsync(new(facts, temporary, PngOptimizationPlan.AutoPolicy), default);
            var output = File.ReadAllBytes(temporary);
            using var before = new MagickImage(bytes);
            using var after = new MagickImage(output);
            check(result.OptimizationMethod == "Lossless" && output.Length <= bytes.Length &&
                before.GetPixels().ToArray()!.SequenceEqual(after.GetPixels().ToArray()!) &&
                NonData(bytes).SequenceEqual(NonData(output)) && File.ReadAllBytes(path).SequenceEqual(bytes),
                "Adam7: independent decoder, metadata, interlace, size and source safety: " + Path.GetFileName(path));
        }
        var interlaced = InterlacedFixture(8, 6, 17, 19);
        Reject(() => new PngLosslessContainer(interlaced).RestoreAndValidate(InterlacedFixture(8, 6, 17, 19, true), default),
            "changed interlaced sample");
        check(File.ReadAllBytes(originalPath).SequenceEqual(ordinary), "PNG all failure tests preserve original");

        async Task RejectAsync(Func<Task> action, string name)
        {
            try { await action(); }
            catch (Exception error) when (error is InvalidDataException or IOException or OperationCanceledException)
            { check(true, "PNG rejects " + name); return; }
            throw new InvalidOperationException("PNG unexpectedly accepted " + name);
        }
        void Reject(Action action, string name)
        {
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or IOException)
            { check(true, "PNG rejects " + name); return; }
            throw new InvalidOperationException("PNG unexpectedly accepted " + name);
        }
    }

    private static byte[] InterlacedFixture(byte depth, byte color, int width, int height, bool altered = false)
    {
        // Independent fixture oracle: select pixels from the specification's repeating
        // pass-number grid, rather than reproducing the validator's pass geometry.
        int[,] grid = { {1,6,4,6,2,6,4,6}, {7,7,7,7,7,7,7,7}, {5,6,5,6,5,6,5,6}, {7,7,7,7,7,7,7,7},
            {3,6,4,6,3,6,4,6}, {7,7,7,7,7,7,7,7}, {5,6,5,6,5,6,5,6}, {7,7,7,7,7,7,7,7} };
        var channels = color switch { 0 or 3 => 1, 2 => 3, 4 => 2, _ => 4 };
        using var raw = new MemoryStream();
        for (var pass = 1; pass <= 7; pass++)
        for (var y = 0; y < height; y++)
        {
            var xs = Enumerable.Range(0, width).Where(x => grid[y % 8, x % 8] == pass).ToArray();
            if (xs.Length == 0) continue;
            raw.WriteByte(0);
            var row = new byte[(xs.Length * channels * depth + 7) / 8];
            var offset = 0;
            foreach (var x in xs)
            for (var c = 0; c < channels; c++)
            {
                var value = color == 3 ? (x + y) % 2 : (x * 37 + y * 19 + c * 11) & ((1 << depth) - 1);
                if (altered && x == 0 && y == 0 && c == 0) value ^= 1;
                for (var bit = depth - 1; bit >= 0; bit--, offset++)
                    row[offset / 8] |= (byte)(((value >> bit) & 1) << (7 - offset % 8));
            }
            raw.Write(row);
        }
        using var file = new MemoryStream();
        file.Write(new byte[] {137,80,78,71,13,10,26,10});
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = depth; header[9] = color; header[12] = 1;
        file.Write(PngFixture.Chunk("IHDR", header));
        if (color == 3) file.Write(PngFixture.Chunk("PLTE", [23,45,67,89,10,20]));
        if (color == 3) file.Write(PngFixture.Chunk("tRNS", [0,200]));
        file.Write(PngFixture.Chunk("tEXt", "Comment\0Adam7 preservation"u8.ToArray()));
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.NoCompression, true)) zlib.Write(raw.ToArray());
        file.Write(PngFixture.Chunk("IDAT", compressed.ToArray()));
        file.Write(PngFixture.Chunk("IEND", []));
        return file.ToArray();
    }

    private static byte[] Fixture(byte depth, byte color, bool altered = false)
    {
        const int width = 64, height = 32;
        var channels = color switch { 0 or 3 => 1, 2 => 3, 4 => 2, _ => 4 };
        using var file = new MemoryStream();
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), height);
        header[8] = depth; header[9] = color;
        file.Write(PngFixture.Chunk("IHDR", header));
        file.Write(PngFixture.Chunk("sRGB", [0]));
        if (color == 3) file.Write(PngFixture.Chunk("PLTE", [23, 45, 67, 89, 10, 20]));
        if (color == 3) file.Write(PngFixture.Chunk("tRNS", [0, 200]));
        file.Write(PngFixture.Chunk("tEXt", Encoding.ASCII.GetBytes("Comment\0preserve me exactly")));
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.NoCompression, true))
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                var row = new byte[width * channels * depth / 8];
                if (color != 3 && depth >= 8)
                    for (var x = 0; x < row.Length; x++) row[x] = (byte)(color is 4 or 6 && (x / (depth / 8)) % channels == channels - 1 ? 0 : 37 + x % 3);
                if (altered && y == 0) row[0] ^= 1;
                zlib.Write(row);
            }
        file.Write(PngFixture.Chunk("IDAT", compressed.ToArray()));
        file.Write(PngFixture.Chunk("IEND", []));
        return file.ToArray();
    }

    private static byte[] AddChunk(byte[] bytes, string name, byte[] payload)
    {
        return bytes[..33].Concat(PngFixture.Chunk(name, payload)).Concat(bytes[33..]).ToArray();
    }

    private static byte[] RemoveChunk(byte[] bytes, string name)
    {
        using var output = new MemoryStream();
        output.Write(bytes, 0, 8);
        for (var offset = 8; offset < bytes.Length;)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)) + 12;
            if (Encoding.ASCII.GetString(bytes, offset + 4, 4) != name) output.Write(bytes, offset, length);
            offset += length;
        }
        return output.ToArray();
    }

    private static byte[] FilterFixture(byte[] rows)
    {
        using var file = new MemoryStream();
        file.Write(new byte[] { 137,80,78,71,13,10,26,10 });
        byte[] header = [0,0,0,3,0,0,0,2,8,0,0,0,0];
        file.Write(PngFixture.Chunk("IHDR", header));
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true)) zlib.Write(rows);
        file.Write(PngFixture.Chunk("IDAT", compressed.ToArray()));
        file.Write(PngFixture.Chunk("IEND", []));
        return file.ToArray();
    }

    private static byte[] NonData(byte[] bytes)
    {
        using var result = new MemoryStream();
        for (var offset = 8; offset < bytes.Length;)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)) + 12;
            if (!bytes.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8)) result.Write(bytes, offset, length);
            offset += length;
        }
        return result.ToArray();
    }
}
