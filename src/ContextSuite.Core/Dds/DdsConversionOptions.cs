namespace ContextSuite.Core.Dds;

public enum DdsPurpose { Color, Data, Normal }
public enum DdsInterpretation { Unspecified, Linear, Srgb, Data }
public enum DdsMipPolicy { Preserve, Generate, Remove }
public enum DdsColorOperation { ConvertValues, Reinterpret }
public enum DdsAlphaPolicy { Preserve, Flatten, Cutout, Discard, Data }
public enum DdsChannel { Red, Green, Blue, Alpha }
public enum DdsEdgeMode { Clamp, Wrap }
public enum DdsHeaderMode { Dx10, Legacy }

public sealed record DdsConversionOptions(DdsFormat Format = DdsFormat.Bc7Srgb,
    DdsPurpose Purpose = DdsPurpose.Color, DdsInterpretation SourceInterpretation = DdsInterpretation.Unspecified,
    DdsColorOperation ColorOperation = DdsColorOperation.ConvertValues, DdsMipPolicy Mips = DdsMipPolicy.Preserve,
    DdsAlphaPolicy Alpha = DdsAlphaPolicy.Preserve, uint? MatteRgb = null, float CutoutThreshold = 0.5f,
    bool PreserveCutoutCoverage = true, DdsChannel RedChannel = DdsChannel.Red, DdsChannel GreenChannel = DdsChannel.Green,
    bool ConfirmChannelLoss = false, DdsEdgeMode Edge = DdsEdgeMode.Clamp, DdsHeaderMode Header = DdsHeaderMode.Dx10,
    uint Quality = 1)
{
    public bool IsSrgb => Format is DdsFormat.Bc1Srgb or DdsFormat.Bc2Srgb or DdsFormat.Bc3Srgb or DdsFormat.Bc7Srgb or
        DdsFormat.Rgba8Srgb or DdsFormat.Bgra8Srgb;
    public bool IsSigned => Format is DdsFormat.Bc4Snorm or DdsFormat.Bc5Snorm;
    public int Channels => Format is DdsFormat.R8 or DdsFormat.Bc4 or DdsFormat.Bc4Snorm ? 1 :
        Format is DdsFormat.Rg8 or DdsFormat.Bc5 or DdsFormat.Bc5Snorm ? 2 : 4;
    public bool IsCompressed => Format is >= DdsFormat.Bc1 and <= DdsFormat.Bc5Snorm or DdsFormat.Bc7 or DdsFormat.Bc7Srgb;

    public void Validate()
    {
        if (Format is not (DdsFormat.Bc1 or DdsFormat.Bc1Srgb or DdsFormat.Bc2 or DdsFormat.Bc2Srgb or
            DdsFormat.Bc3 or DdsFormat.Bc3Srgb or DdsFormat.Bc4 or DdsFormat.Bc4Snorm or DdsFormat.Bc5 or DdsFormat.Bc5Snorm or
            DdsFormat.Bc7 or DdsFormat.Bc7Srgb or DdsFormat.R8 or DdsFormat.Rg8 or DdsFormat.Rgba8 or DdsFormat.Rgba8Srgb or
            DdsFormat.Bgra8 or DdsFormat.Bgra8Srgb) || !Enum.IsDefined(Purpose) || !Enum.IsDefined(SourceInterpretation) ||
            !Enum.IsDefined(ColorOperation) || !Enum.IsDefined(Mips) || !Enum.IsDefined(Alpha) || !Enum.IsDefined(RedChannel) ||
            !Enum.IsDefined(GreenChannel) || !Enum.IsDefined(Edge) || !Enum.IsDefined(Header) || MatteRgb > 0xffffff ||
            !float.IsFinite(CutoutThreshold) || CutoutThreshold is <= 0 or >= 1 || Quality > 2)
            throw new InvalidDataException("DDS conversion options are invalid.");
        if (Purpose != DdsPurpose.Color && (IsSrgb || SourceInterpretation != DdsInterpretation.Data))
            throw new InvalidDataException("Normals and data require data interpretation without sRGB transfer.");
        if (Purpose == DdsPurpose.Color && (SourceInterpretation == DdsInterpretation.Data || Channels != 4))
            throw new InvalidDataException("Color textures require color interpretation and RGB storage.");
        if (Purpose == DdsPurpose.Normal && (Format is not (DdsFormat.Bc5 or DdsFormat.Bc5Snorm) ||
            RedChannel != DdsChannel.Red || GreenChannel != DdsChannel.Green || Alpha != DdsAlphaPolicy.Discard))
            throw new InvalidDataException("Normal-map output requires explicit RG-to-BC5 mapping and acknowledged discarded channels.");
        if (Channels < 4 && !ConfirmChannelLoss) throw new InvalidDataException("Acknowledge the selected channels and discarded data.");
        if ((Purpose != DdsPurpose.Data || Channels == 4) && (RedChannel != DdsChannel.Red || GreenChannel != DdsChannel.Green))
            throw new InvalidDataException("Custom channel mapping applies only to one/two-channel data output.");
        if ((Alpha == DdsAlphaPolicy.Flatten) != (MatteRgb is not null)) throw new InvalidDataException("Flattening requires an explicit matte and only flattening uses a matte.");
        if ((Purpose == DdsPurpose.Data && Alpha != DdsAlphaPolicy.Data) || (Purpose == DdsPurpose.Color && Alpha == DdsAlphaPolicy.Data))
            throw new InvalidDataException("Data channels must be explicitly treated as data, not transparency.");
        if (Alpha == DdsAlphaPolicy.Cutout && Format is not (DdsFormat.Bc1 or DdsFormat.Bc1Srgb))
            throw new InvalidDataException("Cutout alpha is offered only for BC1 in this slice.");
        if (Header == DdsHeaderMode.Legacy && (IsSrgb || Format is DdsFormat.Bc7 or DdsFormat.Rg8))
            throw new InvalidDataException("The legacy header cannot represent this selected output declaration.");
        if (ColorOperation == DdsColorOperation.Reinterpret && (Purpose != DdsPurpose.Color || Mips != DdsMipPolicy.Preserve ||
            Alpha != DdsAlphaPolicy.Preserve || Header != DdsHeaderMode.Dx10))
            throw new InvalidDataException("Interpretation-only changes preserve pixels, mips and alpha and require a DX10 header.");
    }

    public void ValidateSource(DdsInfo source, bool hasTransparency)
    {
        Validate();
        if (!source.IsSupported2D) throw new InvalidDataException("This DDS structure or format is not supported for conversion.");
        if (Purpose == DdsPurpose.Normal && source.Format is (DdsFormat.R8 or DdsFormat.Bc4 or DdsFormat.Bc4Snorm))
            throw new InvalidDataException("Normal-map conversion needs RGB normals or an explicit RG pair; one-channel textures do not encode a normal direction.");
        if (source.RawAlphaMode == 4 && Purpose == DdsPurpose.Color) throw new InvalidDataException("The fourth channel is declared custom data, not transparency.");
        if (Purpose == DdsPurpose.Color && SourceInterpretation == DdsInterpretation.Unspecified && !source.IsSrgb)
            throw new InvalidDataException("Choose source interpretation; an UNORM or legacy header does not prove linear color.");
        if (hasTransparency && Format is (DdsFormat.Bc1 or DdsFormat.Bc1Srgb) && Alpha == DdsAlphaPolicy.Preserve && !PreservesPayload(source))
            throw new InvalidDataException("Choose BC1 cutout transparency or an explicit matte; smooth alpha cannot be preserved.");
        if (IsCompressed && (source.Width % 4 != 0 || source.Height % 4 != 0))
            throw new InvalidDataException("GPU-compatible BC output requires top-level dimensions divisible by four. No automatic resize is applied.");
        if (ColorOperation == DdsColorOperation.Reinterpret && LinearFormat(source.Format) != LinearFormat(Format))
            throw new InvalidDataException("Interpretation-only changes cannot change compression or channel layout.");
    }

    public bool PreservesPayload(DdsInfo source)
    {
        if (ColorOperation == DdsColorOperation.Reinterpret) return LinearFormat(source.Format) == LinearFormat(Format);
        var sourceSrgb = SourceInterpretation == DdsInterpretation.Srgb ||
            (SourceInterpretation == DdsInterpretation.Unspecified && source.IsSrgb);
        return Purpose == DdsPurpose.Color && Mips == DdsMipPolicy.Preserve && Alpha == DdsAlphaPolicy.Preserve &&
            sourceSrgb == IsSrgb && source.Format == Format;
    }

    public static DdsFormat LinearFormat(DdsFormat format) => format switch
    {
        DdsFormat.Bc1Srgb => DdsFormat.Bc1, DdsFormat.Bc2Srgb => DdsFormat.Bc2,
        DdsFormat.Bc3Srgb => DdsFormat.Bc3, DdsFormat.Bc7Srgb => DdsFormat.Bc7,
        DdsFormat.Rgba8Srgb => DdsFormat.Rgba8, DdsFormat.Bgra8Srgb => DdsFormat.Bgra8,
        _ => format
    };
}
