using System.Buffers.Binary;
using System.Collections.Immutable;

namespace ContextSuite.Core.Dds;

public static class DdsParser
{
    public const int MaximumHeaderBytes = 148;

    public static async Task<DdsInfo> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = new byte[MaximumHeaderBytes];
        var count = await stream.ReadAtLeastAsync(bytes, 128, false, cancellationToken);
        if (count >= 128 && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(84)) == FourCc("DX10") && count < 148)
            count += await stream.ReadAtLeastAsync(bytes.AsMemory(count), 148 - count, false, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Parse(bytes.AsSpan(0, count), stream.Length);
    }

    public static DdsInfo Parse(ReadOnlySpan<byte> header, long fileBytes)
    {
        if (header.Length < 128 || fileBytes < 128 || !header[..4].SequenceEqual("DDS "u8))
            throw new InvalidDataException("Not a complete DDS header.");
        if (Read(header, 4) != 124 || Read(header, 76) != 32)
            throw new InvalidDataException("DDS header sizes are invalid.");
        var warnings = ImmutableArray.CreateBuilder<string>();
        var flags = Read(header, 8);
        var height = Read(header, 12);
        var width = Read(header, 16);
        var rawDepth = Read(header, 24);
        var rawMips = Read(header, 28);
        var pixelFlags = Read(header, 80);
        var fourCc = Read(header, 84);
        var caps = Read(header, 108);
        var caps2 = Read(header, 112);
        var dx10 = (pixelFlags & 4) != 0 && fourCc == FourCc("DX10");
        if (dx10 && (header.Length < 148 || fileBytes < 148)) throw new InvalidDataException("Truncated DDS DX10 header.");
        var headerBytes = dx10 ? 148 : 128;
        uint dxgi = 0, arraySize = 1, alpha = 0;
        var kind = (caps2 & 0x200000) != 0 ? DdsTextureKind.Texture3D :
            (caps2 & 0x200) != 0 ? DdsTextureKind.Cube : DdsTextureKind.Texture2D;
        var depth = kind == DdsTextureKind.Texture3D ? rawDepth : 1;
        var mips = rawMips == 0 ? 1 : rawMips;
        DdsFormat format;
        if (dx10)
        {
            dxgi = Read(header, 128);
            format = Enum.IsDefined((DdsFormat)dxgi) ? (DdsFormat)dxgi : DdsFormat.Unknown;
            var dimension = Read(header, 132);
            var misc = Read(header, 136);
            arraySize = Read(header, 140);
            alpha = Read(header, 144);
            kind = dimension switch
            {
                2 => DdsTextureKind.Texture1D,
                3 => (misc & 4) != 0 ? DdsTextureKind.Cube : DdsTextureKind.Texture2D,
                4 => DdsTextureKind.Texture3D,
                _ => DdsTextureKind.Unknown
            };
            depth = kind == DdsTextureKind.Texture3D ? rawDepth : 1;
            if (arraySize == 0) warnings.Add("Array size is zero.");
            if ((Read(header, 144) & ~7u) != 0 || alpha > 4) warnings.Add("Unknown alpha-mode flags.");
            if ((misc & ~4u) != 0 || ((misc & 4) != 0 && dimension != 3)) warnings.Add("Unsupported or contradictory DX10 texture flags.");
            if (kind == DdsTextureKind.Unknown) warnings.Add("Unknown resource dimension.");
            if (kind == DdsTextureKind.Texture1D && height != 1) warnings.Add("A 1D texture must have height one.");
            if (kind == DdsTextureKind.Texture3D && arraySize != 1) warnings.Add("A volume texture cannot be an array.");
            if (((caps2 & 0x200000) != 0) != (kind == DdsTextureKind.Texture3D) ||
                ((caps2 & 0x200) != 0) != (kind == DdsTextureKind.Cube)) warnings.Add("Legacy caps contradict the DX10 texture structure.");
        }
        else
        {
            format = LegacyFormat(header, pixelFlags, fourCc);
            if (fourCc == FourCc("DXT2") || fourCc == FourCc("DXT4")) alpha = 2;
        }
        if ((flags & 0x1007) != 0x1007 || (caps & 0x1000) == 0) warnings.Add("Required DDS texture flags are missing.");
        if (width == 0 || height == 0 || depth == 0) warnings.Add("Texture dimensions contain zero.");
        if (kind == DdsTextureKind.Cube && width != height) warnings.Add("Cube faces are not square.");
        if (kind != DdsTextureKind.Texture3D && rawDepth > 1) warnings.Add("Non-volume texture declares depth greater than one.");
        if (kind == DdsTextureKind.Texture3D && (flags & 0x800000) == 0) warnings.Add("Volume depth flag is missing.");
        if (mips > 1 && ((flags & 0x20000) == 0 || (caps & 0x400008) != 0x400008)) warnings.Add("Mip-chain flags are incomplete.");
        if (mips > MaximumMipLevels(width, height, depth)) warnings.Add("Mip count exceeds the dimensions.");
        if (kind == DdsTextureKind.Cube && (caps2 & 0xfc00) != 0xfc00) warnings.Add("Cube faces are incomplete.");
        if (kind != DdsTextureKind.Cube && (caps2 & 0xfc00) != 0) warnings.Add("Cube-face flags are set on a non-cube texture.");
        ulong? expected = null;
        if (width > 0 && height > 0 && depth > 0 && mips <= MaximumMipLevels(width, height, depth) && arraySize > 0 && format != DdsFormat.Unknown)
        {
            try
            {
                expected = PayloadSize(format, width, height, depth, mips, arraySize, kind == DdsTextureKind.Cube);
                if (expected != (ulong)(fileBytes - headerBytes)) warnings.Add("Payload length does not match the declared texture layout.");
            }
            catch (OverflowException) { warnings.Add("Texture payload size overflows the supported accounting range."); }
        }
        return new(dx10, fourCc, dxgi, format, width, height, depth, mips, arraySize, kind, alpha,
            headerBytes, fileBytes, expected, warnings.ToImmutable());
    }

    public static uint MaximumMipLevels(uint width, uint height, uint depth = 1)
    {
        var largest = Math.Max(Math.Max(width, height), depth);
        uint levels = 0;
        while (largest != 0) { levels++; largest >>= 1; }
        return levels;
    }

    public static ulong PayloadSize(DdsFormat format, uint width, uint height, uint depth, uint mips, uint arraySize, bool cube)
    {
        if (width == 0 || height == 0 || depth == 0 || arraySize == 0 || mips == 0 || mips > MaximumMipLevels(width, height, depth))
            throw new ArgumentException("Invalid DDS dimensions or mip count.");
        var blockBytes = (uint)format is >= 70 and <= 72 or >= 79 and <= 81 ? 8u :
            (uint)format is >= 73 and <= 78 or >= 82 and <= 84 or >= 94 and <= 99 ? 16u : 0u;
        var pixelBytes = format == DdsFormat.R8 ? 1u : format == DdsFormat.Rg8 ? 2u :
            format is DdsFormat.Rgba8 or DdsFormat.Rgba8Srgb or DdsFormat.Rgba8Typeless or DdsFormat.Bgra8 or
                DdsFormat.Bgra8Srgb or DdsFormat.Bgra8Typeless or DdsFormat.Bgrx8 or DdsFormat.Bgrx8Srgb or DdsFormat.Bgrx8Typeless ? 4u : 0u;
        if (blockBytes == 0 && pixelBytes == 0) throw new ArgumentException("Unknown DDS storage layout.");
        checked
        {
            ulong size = 0;
            for (uint mip = 0; mip < mips; mip++)
            {
                size += blockBytes == 0 ? (ulong)width * height * depth * pixelBytes :
                    (((ulong)width + 3) / 4) * (((ulong)height + 3) / 4) * depth * blockBytes;
                width = Math.Max(1, width >> 1); height = Math.Max(1, height >> 1); depth = Math.Max(1, depth >> 1);
            }
            return size * arraySize * (cube ? 6ul : 1ul);
        }
    }

    private static DdsFormat LegacyFormat(ReadOnlySpan<byte> bytes, uint flags, uint fourCc)
    {
        if ((flags & 4) != 0)
        {
            if (fourCc == FourCc("DXT1")) return DdsFormat.Bc1;
            if (fourCc == FourCc("DXT2") || fourCc == FourCc("DXT3")) return DdsFormat.Bc2;
            if (fourCc == FourCc("DXT4") || fourCc == FourCc("DXT5")) return DdsFormat.Bc3;
            if (fourCc == FourCc("ATI1") || fourCc == FourCc("BC4U")) return DdsFormat.Bc4;
            if (fourCc == FourCc("BC4S")) return DdsFormat.Bc4Snorm;
            if (fourCc == FourCc("ATI2") || fourCc == FourCc("BC5U")) return DdsFormat.Bc5;
            if (fourCc == FourCc("BC5S")) return DdsFormat.Bc5Snorm;
            return DdsFormat.Unknown;
        }
        var bits = Read(bytes, 88);
        var r = Read(bytes, 92); var g = Read(bytes, 96); var b = Read(bytes, 100); var a = Read(bytes, 104);
        if (flags == 0x20000 && bits == 8 && r == 255 && g == 0 && b == 0 && a == 0) return DdsFormat.R8;
        if (bits == 32 && g == 0xff00 && flags is 0x40 or 0x41)
        {
            if (flags == 0x41 && a == 0xff000000)
            {
                if (r == 255 && b == 0xff0000) return DdsFormat.Rgba8;
                if (b == 255 && r == 0xff0000) return DdsFormat.Bgra8;
            }
            if (flags == 0x40 && a == 0 && b == 255 && r == 0xff0000) return DdsFormat.Bgrx8;
        }
        return DdsFormat.Unknown;
    }

    private static uint Read(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    private static uint FourCc(string value) => (uint)value[0] | (uint)value[1] << 8 | (uint)value[2] << 16 | (uint)value[3] << 24;
}
