using System.Buffers.Binary;
using System.Text;
using ContextSuite.Private.Images;

internal static class GifInputContracts
{
    public static void Run(Action<bool, string> check)
    {
        var palette = new byte[] { 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255 };
        var source = Gif(3, 2, palette,
            new Part(0, 0, 3, 2, [1, 1, 1, 1, 1, 1], Disposal: 1, Delay: 4),
            new Part(1, 0, 1, 1, [2], Disposal: 2, Delay: 8),
            new Part(0, 1, 1, 1, [3], Disposal: 3, Delay: 12),
            new Part(2, 0, 1, 1, [0], Disposal: 1, Transparent: 0),
            new Part(2, 0, 1, 1, [3], Disposal: 1));
        var scan = GifInput.Scan(source);
        var frames = scan.ReadFrames().ToArray();
        check(scan.Width == 3 && scan.Height == 2 && scan.FrameCount == 5 &&
              scan.DelayCentiseconds.SequenceEqual(new[] { 4, 8, 12, 0, 0 }),
            "GIF scan retains ordered frames and delays");
        check(frames.Select(frame => frame.Index).SequenceEqual(new[] { 0, 1, 2, 3, 4 }) &&
              frames.All(frame => frame.RgbaPixels.Length == 24),
            "GIF enumerates full displayed canvases with original indices");
        check(Pixel(frames[0], 1, 0).SequenceEqual(new byte[] { 255, 0, 0, 255 }) &&
              Pixel(frames[1], 1, 0).SequenceEqual(new byte[] { 0, 255, 0, 255 }),
            "GIF draws source frame and offset overlay");
        check(Pixel(frames[2], 1, 0).SequenceEqual(new byte[] { 0, 0, 0, 255 }) &&
              Pixel(frames[2], 0, 1).SequenceEqual(new byte[] { 0, 0, 255, 255 }),
            "GIF disposal two clears prior rectangle to background");
        check(Pixel(frames[3], 0, 1).SequenceEqual(new byte[] { 255, 0, 0, 255 }) &&
              frames[3].RgbaPixels.SequenceEqual(frames[2].RgbaPixels.AsSpan().ToArray()) == false,
            "GIF disposal three restores canvas and transparent pixel leaves destination");
        check(Pixel(frames[4], 2, 0).SequenceEqual(new byte[] { 0, 0, 255, 255 }),
            "GIF subsequent frame follows restored state");

        var duplicates = Gif(1, 1, palette,
            new Part(0, 0, 1, 1, [1]), new Part(0, 0, 1, 1, [1]));
        var duplicateFrames = GifInput.Scan(duplicates).ReadFrames().ToArray();
        check(duplicateFrames.Length == 2 && duplicateFrames[0].RgbaPixels.SequenceEqual(duplicateFrames[1].RgbaPixels) &&
              !ReferenceEquals(duplicateFrames[0].RgbaPixels, duplicateFrames[1].RgbaPixels),
            "GIF retains duplicate displayed frames as independent buffers");

        var interlaced = Gif(1, 8, palette,
            new Part(0, 0, 1, 8, [1, 2, 3, 1, 2, 3, 1, 2], Interlaced: true));
        var interlaceFrame = GifInput.Scan(interlaced).ReadFrames().Single();
        check(Pixel(interlaceFrame, 0, 0)[0] == 255 && Pixel(interlaceFrame, 0, 4)[1] == 255 &&
              Pixel(interlaceFrame, 0, 2)[2] == 255 && Pixel(interlaceFrame, 0, 1)[1] == 255,
            "GIF four interlace passes map to displayed rows");

        var local = Gif(1, 1, palette, new Part(0, 0, 1, 1, [1], LocalPalette: new byte[]
            { 0, 0, 0, 0, 255, 255, 0, 0, 0, 0, 0, 0 }));
        check(Pixel(GifInput.Scan(local).ReadFrames().Single(), 0, 0).SequenceEqual(new byte[] { 0, 255, 255, 255 }),
            "GIF local palette overrides global palette");

        var transparent = Gif(1, 1, palette, new Part(0, 0, 1, 1, [0], Transparent: 0));
        check(Pixel(GifInput.Scan(transparent).ReadFrames().Single(), 0, 0).SequenceEqual(new byte[] { 0, 0, 0, 0 }),
            "GIF transparent background retains alpha");
        var transparentOtherIndex = Gif(1, 1, palette,
            new Part(0, 0, 1, 1, [1], Transparent: 1));
        check(Pixel(GifInput.Scan(transparentOtherIndex).ReadFrames().Single(), 0, 0)[3] == 0,
            "GIF transparent frame uses a transparent canvas even when background index differs");
        var transparentClear = Gif(1, 1, palette,
            new Part(0, 0, 1, 1, [1]),
            new Part(0, 0, 1, 1, [2], Disposal: 2, Transparent: 3),
            new Part(0, 0, 1, 1, [3], Transparent: 3));
        check(Pixel(GifInput.Scan(transparentClear).ReadFrames().Last(), 0, 0)[3] == 0,
            "GIF transparent disposal two clears its region to transparent");

        var looped = Gif(1, 1, palette, new Part(0, 0, 1, 1, [1]), Loop: 0);
        check(GifInput.Scan(looped).LoopCount == 0, "GIF scan retains infinite loop fact");
        var gif87a = Gif(1, 1, palette, new Part(0, 0, 1, 1, [1]), Version87a: true);
        check(GifInput.Scan(gif87a).ReadFrames().Count() == 1, "GIF87a still image is decoded");

        var dictionaryData = PackVariableCodes((4, 3), (0, 3), (1, 3), (6, 3), (8, 4), (5, 4));
        var dictionaryGif = Gif(1, 7, palette, new Part(0, 0, 1, 7, [], Compressed: dictionaryData));
        var expanded = GifInput.Scan(dictionaryGif).ReadFrames().Single();
        check(Enumerable.Range(0, 7).Select(y => Pixel(expanded, 0, y)[0])
            .SequenceEqual(new byte[] { 0, 255, 0, 255, 0, 255, 0 }),
            "GIF LZW dictionary code growth and code-equals-next expand exactly");

        Refuses(Gif(1, 1, palette, new Part(0, 0, 1, 1, [], Compressed: PackCodes([4, 7, 5]))),
            "invalid initial dictionary code");
        Refuses(Gif(1, 2, palette, new Part(0, 0, 1, 2, [], Compressed: PackCodes([4, 1, 5]))),
            "short LZW output");
        Refuses(Gif(1, 1, palette, new Part(0, 0, 1, 1, [], Compressed: PackCodes([4, 1, 4, 1, 5]))),
            "excess LZW output");
        Refuses(Gif(1, 1, palette, new Part(0, 0, 1, 1, [], Compressed: PackCodes([4, 1]))),
            "missing LZW end code");

        var onePixel = Gif(1024, 1024, palette, new Part(0, 0, 1, 1, [1]));
        var frameStart = Array.IndexOf(onePixel, (byte)0x2c, 25);
        using (var many = new MemoryStream())
        {
            many.Write(onePixel.AsSpan(0, frameStart));
            for (var i = 0; i < 489; i++) many.Write(onePixel.AsSpan(frameStart, onePixel.Length - frameStart - 1));
            many.WriteByte(0x3b);
            Refuses(many.ToArray(), "total displayed pixel limit");
        }
        var tiny = Gif(1, 1, palette, new Part(0, 0, 1, 1, [1]));
        var tinyFrameStart = Array.IndexOf(tiny, (byte)0x2c, 25);
        using (var many = new MemoryStream())
        {
            many.Write(tiny.AsSpan(0, tinyFrameStart));
            for (var i = 0; i < 4097; i++) many.Write(tiny.AsSpan(tinyFrameStart, tiny.Length - tinyFrameStart - 1));
            many.WriteByte(0x3b);
            Refuses(many.ToArray(), "frame count limit");
        }
        var nonsquare = (byte[])source.Clone();
        nonsquare[12] = 1;
        Refuses(nonsquare, "non-square pixel aspect ratio");
        var square = (byte[])source.Clone();
        square[12] = 49;
        check(GifInput.Scan(square).FrameCount == 5, "GIF explicit square aspect ratio is admitted");

        Refuses(source[..^1], "missing trailer");
        Refuses(source[..^2], "truncated final data");
        var outside = (byte[])source.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(outside.AsSpan(6, 2), 0);
        Refuses(outside, "zero canvas width");
        var huge = (byte[])source.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(huge.AsSpan(6, 2), 16385);
        Refuses(huge, "excessive canvas side");
        var unknown = InsertBeforeImage(duplicates, [0x21, 0xff, 0x0b, 0x4e, 0x4f, 0x4e, 0x45, 0x20,
            0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0]);
        Refuses(unknown, "unsupported application extension");
        var plainText = InsertBeforeImage(duplicates, [0x21, 0x01, 0]);
        Refuses(plainText, "plain text rendering extension");
        var canceled = new CancellationToken(true);
        try { _ = scan.ReadFrames(canceled).ToArray(); check(false, "GIF canceled enumeration"); }
        catch (OperationCanceledException) { check(true, "GIF canceled enumeration"); }
        try { _ = GifInput.Scan(source, canceled); check(false, "GIF canceled scan"); }
        catch (OperationCanceledException) { check(true, "GIF canceled scan"); }

        void Refuses(byte[] bytes, string label)
        {
            try { _ = GifInput.Scan(bytes).ReadFrames().ToArray(); check(false, "GIF refuses " + label); }
            catch (InvalidDataException) { check(true, "GIF refuses " + label); }
        }
    }

    private static byte[] Pixel(GifDisplayedFrame frame, int x, int y) =>
        frame.RgbaPixels.AsSpan((y * frame.Width + x) * 4, 4).ToArray();

    private sealed record Part(int Left, int Top, int Width, int Height, byte[] Indices,
        int Disposal = 0, int Transparent = -1, int Delay = 0, bool Interlaced = false,
        byte[]? LocalPalette = null, byte[]? Compressed = null);

    // Literal codes with a Clear before each pixel keep fixtures independent of
    // dictionary construction, while still exercising the decoder's bit reader.
    private static byte[] Gif(int width, int height, byte[] palette, Part first, Part? second = null,
        Part? third = null, Part? fourth = null, Part? fifth = null, int? Loop = null,
        bool Version87a = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes(Version87a ? "GIF87a" : "GIF89a"));
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write((byte)(0x80 | ((int)Math.Log2(palette.Length / 3) - 1)));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(palette);
        if (Loop is { } loop)
        {
            writer.Write(new byte[] { 0x21, 0xff, 11 });
            writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            writer.Write(new byte[] { 3, 1 });
            writer.Write((ushort)loop);
            writer.Write((byte)0);
        }
        foreach (var part in new[] { first, second, third, fourth, fifth }.OfType<Part>())
        {
            if (!Version87a && (part.Disposal != 0 || part.Transparent >= 0 || part.Delay != 0))
            {
                writer.Write(new byte[] { 0x21, 0xf9, 4, (byte)(part.Disposal << 2 | (part.Transparent >= 0 ? 1 : 0)) });
                writer.Write((ushort)part.Delay);
                writer.Write((byte)Math.Max(0, part.Transparent));
                writer.Write((byte)0);
            }
            writer.Write((byte)0x2c);
            writer.Write((ushort)part.Left);
            writer.Write((ushort)part.Top);
            writer.Write((ushort)part.Width);
            writer.Write((ushort)part.Height);
            var flags = (byte)((part.Interlaced ? 0x40 : 0) |
                (part.LocalPalette is null ? 0 : 0x80 | ((int)Math.Log2(part.LocalPalette.Length / 3) - 1)));
            writer.Write(flags);
            if (part.LocalPalette is not null) writer.Write(part.LocalPalette);
            writer.Write((byte)2);
            var codes = new List<int>();
            foreach (var index in part.Indices) { codes.Add(4); codes.Add(index); }
            codes.Add(5);
            var packed = part.Compressed ?? PackCodes(codes);
            writer.Write((byte)packed.Length);
            writer.Write(packed);
            writer.Write((byte)0);
        }
        writer.Write((byte)0x3b);
        return stream.ToArray();
    }

    private static byte[] PackCodes(IReadOnlyList<int> codes)
    {
        var result = new List<byte>();
        uint bits = 0;
        var count = 0;
        foreach (var code in codes)
        {
            bits |= (uint)code << count;
            count += 3;
            while (count >= 8)
            {
                result.Add((byte)bits);
                bits >>= 8;
                count -= 8;
            }
        }
        if (count > 0) result.Add((byte)bits);
        return result.ToArray();
    }

    private static byte[] PackVariableCodes(params (int Code, int Width)[] codes)
    {
        var result = new List<byte>();
        uint bits = 0;
        var count = 0;
        foreach (var (code, width) in codes)
        {
            bits |= (uint)code << count;
            count += width;
            while (count >= 8)
            {
                result.Add((byte)bits);
                bits >>= 8;
                count -= 8;
            }
        }
        if (count > 0) result.Add((byte)bits);
        return result.ToArray();
    }

    private static byte[] InsertBeforeImage(byte[] source, byte[] extension)
    {
        var index = Array.IndexOf(source, (byte)0x2c, 19);
        return [.. source.AsSpan(0, index), .. extension, .. source.AsSpan(index)];
    }
}
