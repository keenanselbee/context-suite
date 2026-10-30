using System.Buffers.Binary;

namespace ContextSuite.Private.Images;

// GIF frame data is scanned once, then decoded against a single evolving canvas.
// Returned arrays are owned snapshots, so a caller may publish and discard each frame.
internal sealed class GifInput
{
    private const int MaximumBytes = 128 * 1024 * 1024;
    private const int MaximumSide = 16384;
    private const int MaximumPixels = 40_000_000;
    private const int MaximumFrames = 4096;
    private const long MaximumDisplayedPixels = 512_000_000;

    private readonly byte[] _data;
    private readonly FrameInfo[] _frames;
    private readonly int _backgroundIndex;
    private readonly int[]? _globalPalette;

    private GifInput(byte[] data, int width, int height, int backgroundIndex,
        int[]? globalPalette, FrameInfo[] frames, int? loopCount)
    {
        _data = data;
        Width = width;
        Height = height;
        _backgroundIndex = backgroundIndex;
        _globalPalette = globalPalette;
        _frames = frames;
        LoopCount = loopCount;
    }

    public int Width { get; }
    public int Height { get; }
    public int FrameCount => _frames.Length;
    // Null means no application loop instruction; zero means infinite looping.
    public int? LoopCount { get; }
    public IReadOnlyList<int> DelayCentiseconds => Array.ConvertAll(_frames, frame => frame.DelayCentiseconds);

    public static GifInput Scan(ReadOnlyMemory<byte> input, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (input.Length > MaximumBytes) throw new InvalidDataException("GIF exceeds the input limit.");
        var data = input.ToArray();
        if (data.Length < 14 || !(data.AsSpan(0, 6).SequenceEqual("GIF87a"u8) ||
                                  data.AsSpan(0, 6).SequenceEqual("GIF89a"u8)))
            throw new InvalidDataException("GIF header is invalid.");
        var is89a = data[4] == (byte)'9';
        var width = ReadU16(data, 6);
        var height = ReadU16(data, 8);
        if (width == 0 || height == 0 || width > MaximumSide || height > MaximumSide ||
            (long)width * height > MaximumPixels)
            throw new InvalidDataException("GIF canvas exceeds the image limit.");
        if (data[12] is not (0 or 49))
            throw new InvalidDataException("GIF non-square pixel aspect ratio is unsupported.");
        var packed = data[10];
        var backgroundIndex = data[11];
        var cursor = 13;
        int[]? globalPalette = null;
        if ((packed & 0x80) != 0)
            globalPalette = ReadPalette(data, ref cursor, 1 << ((packed & 7) + 1));
        if (globalPalette is not null && backgroundIndex >= globalPalette.Length)
            throw new InvalidDataException("GIF background index is outside its palette.");
        var frames = new List<FrameInfo>();
        var gce = new GraphicControl(0, -1, 0);
        var hasGce = false;
        int? loopCount = null;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var marker = ReadByte(data, ref cursor);
            if (marker == 0x3b)
            {
                if (cursor != data.Length || frames.Count == 0 || hasGce)
                    throw new InvalidDataException("GIF trailer or final control block is invalid.");
                return new GifInput(data, width, height, backgroundIndex, globalPalette,
                    frames.ToArray(), loopCount);
            }
            if (marker == 0x21)
            {
                if (!is89a) throw new InvalidDataException("GIF87a contains an extension.");
                var label = ReadByte(data, ref cursor);
                if (label == 0xf9)
                {
                    if (hasGce || ReadByte(data, ref cursor) != 4)
                        throw new InvalidDataException("GIF graphic control block is invalid.");
                    Require(data, cursor, 5);
                    var control = data[cursor++];
                    var delay = ReadU16(data, cursor);
                    cursor += 2;
                    var transparent = data[cursor++];
                    if (data[cursor++] != 0 || (control & 0xe2) != 0)
                        throw new InvalidDataException("GIF graphic control flags are unsupported.");
                    var disposal = (control >> 2) & 7;
                    if (disposal > 3) throw new InvalidDataException("GIF disposal method is unsupported.");
                    gce = new GraphicControl(disposal, (control & 1) != 0 ? transparent : -1, delay);
                    hasGce = true;
                }
                else if (label == 0xff)
                {
                    var length = ReadByte(data, ref cursor);
                    if (length != 11) throw new InvalidDataException("GIF application identifier is invalid.");
                    Require(data, cursor, 11);
                    var identifier = data.AsSpan(cursor, 11);
                    if (!identifier.SequenceEqual("NETSCAPE2.0"u8) && !identifier.SequenceEqual("ANIMEXTS1.0"u8))
                        throw new InvalidDataException("GIF application extension is unsupported.");
                    cursor += 11;
                    if (loopCount is not null || ReadByte(data, ref cursor) != 3)
                        throw new InvalidDataException("GIF loop extension is invalid.");
                    Require(data, cursor, 4);
                    if (data[cursor++] != 1) throw new InvalidDataException("GIF loop extension is invalid.");
                    loopCount = ReadU16(data, cursor);
                    cursor += 2;
                    if (data[cursor++] != 0) throw new InvalidDataException("GIF loop terminator is invalid.");
                }
                else if (label == 0xfe)
                {
                    // Comments do not alter the displayed image or its timing.
                    SkipSubBlocks(data, ref cursor, token);
                }
                else throw new InvalidDataException("GIF extension needs an explicit rendering handler.");
                continue;
            }
            if (marker != 0x2c) throw new InvalidDataException("GIF block marker is invalid.");
            if (frames.Count == MaximumFrames || (long)(frames.Count + 1) * width * height > MaximumDisplayedPixels)
                throw new InvalidDataException("GIF displayed frame limit is exceeded.");
            Require(data, cursor, 9);
            var left = ReadU16(data, cursor);
            var top = ReadU16(data, cursor + 2);
            var frameWidth = ReadU16(data, cursor + 4);
            var frameHeight = ReadU16(data, cursor + 6);
            var flags = data[cursor + 8];
            cursor += 9;
            if (frameWidth == 0 || frameHeight == 0 || left + frameWidth > width || top + frameHeight > height ||
                (flags & 0x18) != 0)
                throw new InvalidDataException("GIF frame bounds or flags are invalid.");
            int[]? localPalette = null;
            if ((flags & 0x80) != 0)
                localPalette = ReadPalette(data, ref cursor, 1 << ((flags & 7) + 1));
            var palette = localPalette ?? globalPalette;
            if (palette is null || gce.TransparentIndex >= palette.Length)
                throw new InvalidDataException("GIF frame palette is absent or transparent index is invalid.");
            var minCodeSize = ReadByte(data, ref cursor);
            if (minCodeSize is < 2 or > 8)
                throw new InvalidDataException("GIF LZW minimum code size is unsupported.");
            var imageDataOffset = cursor;
            SkipSubBlocks(data, ref cursor, token);
            frames.Add(new FrameInfo(left, top, frameWidth, frameHeight, (flags & 0x40) != 0,
                palette, minCodeSize, imageDataOffset, cursor, gce.Disposal,
                gce.TransparentIndex, gce.DelayCentiseconds));
            gce = new GraphicControl(0, -1, 0);
            hasGce = false;
        }
    }

    public IEnumerable<GifDisplayedFrame> ReadFrames(CancellationToken token = default)
    {
        var canvas = new byte[checked(Width * Height * 4)];
        var background = _globalPalette is null ? 0 : _globalPalette[_backgroundIndex];
        // Common display decoders treat a transparent frame's logical background
        // as transparent, regardless of the RGB value at the background index.
        var firstTransparent = _frames[0].TransparentIndex >= 0;
        if (_globalPalette is not null && !firstTransparent)
            Fill(canvas, Width, 0, 0, Width, Height, background, token);
        for (var index = 0; index < _frames.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var frame = _frames[index];
            byte[]? restore = frame.Disposal == 3 ? (byte[])canvas.Clone() : null;
            var pixels = DecodeIndices(frame, token);
            var at = 0;
            var passStarts = frame.Interlaced ? new[] { 0, 4, 2, 1 } : new[] { 0 };
            var passSteps = frame.Interlaced ? new[] { 8, 8, 4, 2 } : new[] { 1 };
            for (var pass = 0; pass < passStarts.Length; pass++)
                for (var y = passStarts[pass]; y < frame.Height; y += passSteps[pass])
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = 0; x < frame.Width; x++)
                    {
                        var paletteIndex = pixels[at++];
                        if (paletteIndex == frame.TransparentIndex) continue;
                        if (paletteIndex >= frame.Palette.Length)
                            throw new InvalidDataException("GIF pixel index is outside its palette.");
                        var rgba = frame.Palette[paletteIndex];
                        var target = ((frame.Top + y) * Width + frame.Left + x) * 4;
                        canvas[target] = (byte)(rgba >> 16);
                        canvas[target + 1] = (byte)(rgba >> 8);
                        canvas[target + 2] = (byte)rgba;
                        canvas[target + 3] = 255;
                    }
                }
            token.ThrowIfCancellationRequested();
            yield return new GifDisplayedFrame(index, Width, Height, frame.DelayCentiseconds, (byte[])canvas.Clone());
            token.ThrowIfCancellationRequested();
            if (frame.Disposal == 2)
            {
                var clear = _globalPalette is null || frame.TransparentIndex >= 0 ? -1 : background;
                Fill(canvas, Width, frame.Left, frame.Top, frame.Width, frame.Height, clear, token);
            }
            else if (restore is not null) canvas = restore;
        }
    }

    private byte[] DecodeIndices(FrameInfo frame, CancellationToken token)
    {
        var output = new byte[checked(frame.Width * frame.Height)];
        var prefix = new ushort[4096];
        var suffix = new byte[4096];
        var stack = new byte[4096];
        var clear = 1 << frame.MinCodeSize;
        var end = clear + 1;
        var next = end + 1;
        var codeSize = frame.MinCodeSize + 1;
        var previous = -1;
        var written = 0;
        var cursor = frame.ImageDataOffset;
        var blockRemaining = 0;
        uint bits = 0;
        var bitCount = 0;
        var sawClear = false;
        var sawEnd = false;
        while (!sawEnd)
        {
            token.ThrowIfCancellationRequested();
            while (bitCount < codeSize)
            {
                if (blockRemaining == 0)
                {
                    if (cursor >= frame.ImageDataEnd) throw new InvalidDataException("GIF LZW data is truncated.");
                    blockRemaining = _data[cursor++];
                    if (blockRemaining == 0) throw new InvalidDataException("GIF LZW end code is missing.");
                }
                bits |= (uint)_data[cursor++] << bitCount;
                bitCount += 8;
                blockRemaining--;
            }
            var code = (int)(bits & ((1u << codeSize) - 1));
            bits >>= codeSize;
            bitCount -= codeSize;
            if (code == clear)
            {
                next = end + 1;
                codeSize = frame.MinCodeSize + 1;
                previous = -1;
                sawClear = true;
                continue;
            }
            if (code == end) { sawEnd = true; break; }
            if (!sawClear || code > next || code >= 4096 || previous < 0 && code >= clear)
                throw new InvalidDataException("GIF LZW code is invalid.");
            var current = code;
            var top = 0;
            if (code == next)
            {
                if (previous < 0) throw new InvalidDataException("GIF LZW code is invalid.");
                current = previous;
                stack[top++] = 0; // Replaced by the decoded first symbol below.
            }
            var chain = 0;
            while (current >= clear)
            {
                if (current >= next || ++chain >= 4096 || top >= stack.Length)
                    throw new InvalidDataException("GIF LZW dictionary chain is invalid.");
                stack[top++] = suffix[current];
                current = prefix[current];
            }
            var first = (byte)current;
            if (code == next) stack[0] = first;
            stack[top++] = first;
            if (written + top > output.Length) throw new InvalidDataException("GIF LZW produces excess pixels.");
            while (top > 0) output[written++] = stack[--top];
            if (previous >= 0 && next < 4096)
            {
                prefix[next] = (ushort)previous;
                suffix[next] = first;
                next++;
                if (next == 1 << codeSize && codeSize < 12) codeSize++;
            }
            previous = code;
        }
        if (written != output.Length) throw new InvalidDataException("GIF LZW pixel count is incomplete.");
        return output;
    }

    private static void Fill(byte[] canvas, int canvasWidth, int left, int top, int width, int height,
        int color, CancellationToken token)
    {
        for (var y = top; y < top + height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = left; x < left + width; x++)
            {
                var at = (y * canvasWidth + x) * 4;
                canvas[at] = color < 0 ? (byte)0 : (byte)(color >> 16);
                canvas[at + 1] = color < 0 ? (byte)0 : (byte)(color >> 8);
                canvas[at + 2] = color < 0 ? (byte)0 : (byte)color;
                canvas[at + 3] = color < 0 ? (byte)0 : (byte)255;
            }
        }
    }

    private static int[] ReadPalette(byte[] data, ref int cursor, int count)
    {
        Require(data, cursor, count * 3);
        var result = new int[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = data[cursor] << 16 | data[cursor + 1] << 8 | data[cursor + 2];
            cursor += 3;
        }
        return result;
    }

    private static void SkipSubBlocks(byte[] data, ref int cursor, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var length = ReadByte(data, ref cursor);
            if (length == 0) return;
            Require(data, cursor, length);
            cursor += length;
        }
    }

    private static int ReadByte(byte[] data, ref int cursor)
    {
        Require(data, cursor, 1);
        return data[cursor++];
    }

    private static int ReadU16(byte[] data, int cursor)
    {
        Require(data, cursor, 2);
        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(cursor, 2));
    }

    private static void Require(byte[] data, int cursor, int length)
    {
        if (cursor < 0 || length < 0 || cursor > data.Length - length)
            throw new InvalidDataException("GIF data is truncated.");
    }

    private sealed record GraphicControl(int Disposal, int TransparentIndex, int DelayCentiseconds);
    private sealed record FrameInfo(int Left, int Top, int Width, int Height, bool Interlaced, int[] Palette,
        int MinCodeSize, int ImageDataOffset, int ImageDataEnd, int Disposal, int TransparentIndex,
        int DelayCentiseconds);
}

internal sealed record GifDisplayedFrame(int Index, int Width, int Height, int DelayCentiseconds, byte[] RgbaPixels);
