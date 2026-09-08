using System.Collections.Immutable;

namespace ContextSuite.Core.Dds;

// Numeric values deliberately match DXGI, so unknown values remain reportable.
public enum DdsFormat : uint
{
    Unknown = 0, Rgba8Typeless = 27, Rgba8 = 28, Rgba8Srgb = 29, Rg8 = 49,
    R8 = 61, Bc1Typeless = 70, Bc1 = 71, Bc1Srgb = 72, Bc2Typeless = 73,
    Bc2 = 74, Bc2Srgb = 75, Bc3Typeless = 76, Bc3 = 77, Bc3Srgb = 78,
    Bc4Typeless = 79, Bc4 = 80, Bc4Snorm = 81, Bc5Typeless = 82, Bc5 = 83,
    Bc5Snorm = 84, Bgra8 = 87, Bgrx8 = 88, Bgra8Typeless = 90, Bgra8Srgb = 91,
    Bgrx8Typeless = 92, Bgrx8Srgb = 93, Bc6HTypeless = 94, Bc6HUfloat = 95,
    Bc6HSfloat = 96, Bc7Typeless = 97, Bc7 = 98, Bc7Srgb = 99
}

public enum DdsTextureKind { Unknown, Texture1D, Texture2D, Cube, Texture3D }
public enum DdsAlphaMode { Unknown, Straight, Premultiplied, Opaque, Custom }

public sealed record DdsInfo(bool HasDx10Header, uint RawFourCc, uint RawDxgiFormat,
    DdsFormat Format, uint Width, uint Height, uint Depth, uint MipLevels,
    uint ArraySize, DdsTextureKind Kind, uint RawAlphaMode, int HeaderBytes,
    long FileBytes, ulong? ExpectedPayloadBytes, ImmutableArray<string> Warnings)
{
    public bool IsSrgb => Format is DdsFormat.Rgba8Srgb or DdsFormat.Bgra8Srgb or DdsFormat.Bgrx8Srgb or
        DdsFormat.Bc1Srgb or DdsFormat.Bc2Srgb or DdsFormat.Bc3Srgb or DdsFormat.Bc7Srgb;
    public bool IsTypeless => Format is DdsFormat.Rgba8Typeless or DdsFormat.Bgra8Typeless or DdsFormat.Bgrx8Typeless or
        DdsFormat.Bc1Typeless or DdsFormat.Bc2Typeless or DdsFormat.Bc3Typeless or DdsFormat.Bc4Typeless or
        DdsFormat.Bc5Typeless or DdsFormat.Bc6HTypeless or DdsFormat.Bc7Typeless;
    public bool IsHdr => Format is DdsFormat.Bc6HTypeless or DdsFormat.Bc6HUfloat or DdsFormat.Bc6HSfloat;
    public bool IsBlockCompressed => (uint)Format is >= 70 and <= 84 or >= 94 and <= 99;
    public bool IsSupported2D => Kind == DdsTextureKind.Texture2D && ArraySize == 1 && Depth == 1 &&
        Format != DdsFormat.Unknown && !IsTypeless && !IsHdr && Warnings.IsEmpty &&
        RawAlphaMode is 0 or 1 or 3 or 4 && Width <= 16384 && Height <= 16384 &&
        (ulong)Width * Height <= 40_000_000 && FileBytes <= 128 * 1024 * 1024;
}
