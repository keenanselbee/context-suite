using System.Buffers.Binary;
using ContextSuite.Core.Images;
using ImageMagick;

namespace ContextSuite.Private.Images;

// Own the container and DIB interpretation. ImageMagick sees an extracted PNG
// only, never an ICO file or a DIB with implicit palette/alpha conventions.
internal static class IcoInput
{
    internal sealed record Frame(uint Width, uint Height, uint Depth, byte[] Bgra, bool HadColorProfile = false);

    public static Frame Read(byte[] bytes, CancellationToken token)
    {
        if (bytes.Length < 22) throw new InvalidDataException("ICO directory is incomplete.");
        if (bytes.Length > ImageContainer.MaximumFileBytes)
            throw new ImageFailureException(ImageFailure.ResourceLimit);
        var data = bytes.AsSpan();
        static ushort U16(ReadOnlySpan<byte> value, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(offset, 2));
        static uint U32(ReadOnlySpan<byte> value, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(offset, 4));
        if (U16(data, 0) != 0 || U16(data, 2) != 1) throw new ImageFailureException(ImageFailure.UnsupportedInput);
        var count = U16(data, 4);
        if (count is 0 or > 256 || 6 + count * 16 > bytes.Length) throw new InvalidDataException("ICO directory is incomplete or too large.");
        var end = 6 + count * 16;
        var candidates = new List<(int Width, int Height, int Depth, int Offset, int Length, int Index)>();
        for (var index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            var at = 6 + index * 16;
            var width = data[at] == 0 ? 256 : data[at];
            var height = data[at + 1] == 0 ? 256 : data[at + 1];
            var length = U32(data, at + 8);
            var offset = U32(data, at + 12);
            if (data[at + 3] != 0 || U16(data, at + 4) > 1 || length < 8 || offset < end || offset > bytes.Length || length > bytes.Length - offset)
                throw new InvalidDataException("ICO directory contains an invalid image range.");
            var depth = U16(data, at + 6);
            if (depth == 0)
            {
                var embedded = data.Slice((int)offset, (int)length);
                if (embedded.Length >= 26 && embedded.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
                    embedded.Slice(12, 4).SequenceEqual("IHDR"u8))
                    depth = (ushort)(embedded[24] * (embedded[25] switch { 2 => 3, 4 => 2, 6 => 4, _ => 1 }));
                else if (embedded.Length >= 16 && BinaryPrimitives.ReadUInt32LittleEndian(embedded) == 40)
                    depth = U16(embedded, 14);
            }
            candidates.Add((width, height, depth, (int)offset, (int)length, index));
        }
        foreach (var entry in candidates.OrderBy(entry => entry.Offset).Zip(candidates.OrderBy(entry => entry.Offset).Skip(1)))
            if ((long)entry.First.Offset + entry.First.Length > entry.Second.Offset)
                throw new InvalidDataException("ICO image ranges overlap.");
        var selected = candidates.OrderByDescending(entry => entry.Width * entry.Height)
            .ThenByDescending(entry => entry.Depth).ThenBy(entry => entry.Index).First();
        var payload = data.Slice(selected.Offset, selected.Length);
        if (payload.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return ReadPng(payload.ToArray(), selected.Width, selected.Height, token);
        return ReadDib(payload, selected.Width, selected.Height, selected.Depth, token);
    }

    private static Frame ReadPng(byte[] bytes, int width, int height, CancellationToken token)
    {
        var header = ImageContainer.Read(bytes);
        if (header.Format != ImageFormat.Png || header.Width != width || header.Height != height)
            throw new InvalidDataException("ICO PNG dimensions disagree with its directory.");
        if (header.Depth > 8 || header.OtherMetadata ||
            header.ExpectedProfiles is { } profiles && profiles.Keys.Any(name => name != "icc"))
            throw new ImageFailureException(ImageFailure.UnsupportedInput);
        byte[]? icc = null;
        if (header.ExpectedProfiles is { } embeddedProfiles) embeddedProfiles.TryGetValue("icc", out icc);
        if (icc is not null) ImageAdapter.ValidateIcoColorProfile(icc);
        token.ThrowIfCancellationRequested();
        using var image = new MagickImage();
        var warned = false;
        if (icc is not null) image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
        image.Warning += (_, _) => warned = true;
        image.Progress += (_, progress) => { if (token.IsCancellationRequested) progress.Cancel = true; };
        image.Read(bytes, MagickFormat.Png);
        token.ThrowIfCancellationRequested();
        if (warned || image.Width != width || image.Height != height ||
            image.ColorSpace is not (ColorSpace.sRGB or ColorSpace.RGB or ColorSpace.Gray) ||
            icc is null && image.Gamma != 0 && Math.Abs(image.Gamma - 0.45455) > 0.001)
            throw new InvalidDataException("ICO PNG pixels or color model are unsupported.");
        if (icc is not null)
        {
            if (image.GetColorProfile()?.ToByteArray() is not { } decoded || !decoded.SequenceEqual(icc))
                throw new InvalidDataException("ICO PNG color profile was not retained by the decoder.");
            image.TransformColorSpace(new ColorProfile(icc), ColorProfiles.SRGB, ColorTransformMode.HighRes);
        }
        image.Depth = 8;
        var pixels = image.GetPixels().ToByteArray(PixelMapping.BGRA)
            ?? throw new InvalidDataException("ICO PNG has no pixels.");
        if (pixels.Length != width * height * 4) throw new InvalidDataException("ICO PNG pixel count is invalid.");
        return new((uint)width, (uint)height, header.Depth, pixels, icc is not null);
    }

    private static Frame ReadDib(ReadOnlySpan<byte> data, int width, int height, int declaredDepth, CancellationToken token)
    {
        if (data.Length < 40) throw new InvalidDataException("ICO bitmap header is incomplete.");
        static ushort U16(ReadOnlySpan<byte> value, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(offset, 2));
        static uint U32(ReadOnlySpan<byte> value, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(offset, 4));
        var depth = U16(data, 14);
        if (U32(data, 0) != 40 || U32(data, 4) != width || U32(data, 8) != height * 2 ||
            U16(data, 12) != 1 || depth is not (1 or 4 or 8 or 24 or 32) ||
            declaredDepth is not 0 && declaredDepth != depth || U32(data, 16) != 0 ||
            U32(data, 32) > (depth <= 8 ? 1u << depth : 0u) || U32(data, 36) != 0)
            throw new ImageFailureException(ImageFailure.UnsupportedInput);
        var colors = depth <= 8 ? (int)(U32(data, 32) == 0 ? 1u << depth : U32(data, 32)) : 0;
        var raster = 40 + colors * 4;
        var colorStride = ((width * depth + 31) / 32) * 4;
        var maskStride = ((width + 31) / 32) * 4;
        var colorBytes = colorStride * height;
        var maskBytes = maskStride * height;
        if (raster + colorBytes + maskBytes != data.Length ||
            U32(data, 20) is not 0 && U32(data, 20) != colorBytes && U32(data, 20) != colorBytes + maskBytes)
            throw new InvalidDataException("ICO bitmap pixels or transparency mask are incomplete.");
        var pixels = new byte[width * height * 4];
        var hasAlpha = false;
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            var row = data.Slice(raster + (height - 1 - y) * colorStride, colorStride);
            for (var x = 0; x < width; x++)
            {
                var destination = (y * width + x) * 4;
                if (depth <= 8)
                {
                    var index = depth switch
                    {
                        1 => (row[x / 8] >> (7 - x % 8)) & 1,
                        4 => (row[x / 2] >> (x % 2 == 0 ? 4 : 0)) & 15,
                        _ => row[x]
                    };
                    if (index >= colors) throw new InvalidDataException("ICO palette index is out of range.");
                    data.Slice(40 + index * 4, 3).CopyTo(pixels.AsSpan(destination, 3));
                }
                else row.Slice(x * (depth / 8), 3).CopyTo(pixels.AsSpan(destination, 3));
                if (depth == 32)
                {
                    pixels[destination + 3] = row[x * 4 + 3];
                    hasAlpha |= pixels[destination + 3] != 0;
                }
                else pixels[destination + 3] = 255;
            }
        }
        var maskStart = raster + colorBytes;
        // Normal Windows drawing uses the alpha plane for 32-bit icons and
        // ignores the legacy AND mask when any alpha sample is nonzero.
        if (depth == 32 && hasAlpha) return new((uint)width, (uint)height, 8, pixels);
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            var row = data.Slice(maskStart + (height - 1 - y) * maskStride, maskStride);
            for (var x = 0; x < width; x++)
            {
                var masked = (row[x / 8] & (128 >> (x % 8))) != 0;
                var destination = (y * width + x) * 4 + 3;
                if (masked)
                {
                    // AND=1 with nonblack XOR pixels depends on the desktop
                    // underneath the icon and cannot be represented by PNG.
                    if (pixels[destination - 3] != 0 || pixels[destination - 2] != 0 || pixels[destination - 1] != 0)
                        throw new ImageFailureException(ImageFailure.UnsupportedInput);
                    pixels[destination] = 0;
                }
                else if (depth == 32) pixels[destination] = 255;
            }
        }
        return new((uint)width, (uint)height, Math.Min((uint)depth, 8), pixels);
    }
}
