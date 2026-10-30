using System.Buffers.Binary;
using ContextSuite.Core.Dds;
using ContextSuite.Private.Dds;

internal static class TexturePolicyContracts
{
    public static void Run(Action<bool, string> check)
    {
        var rgba = new byte[64];
        for (var p = 0; p < rgba.Length; p += 4) { rgba[p] = 128; rgba[p + 3] = 128; }
        var source = Rgba(rgba);
        var linear = new DdsConversionOptions(Format: DdsFormat.Rgba8, SourceInterpretation: DdsInterpretation.Srgb);
        var converted = DdsTextureProcessor.Convert(source, linear, CancellationToken.None);
        check(converted[148] is >= 54 and <= 56 && converted[151] == 128, "DDS policy: sRGB-to-linear changes RGB, never alpha");
        var srgb = linear with { Format = DdsFormat.Rgba8Srgb, SourceInterpretation = DdsInterpretation.Linear };
        var restored = DdsTextureProcessor.Convert(converted, srgb, CancellationToken.None);
        check(Math.Abs(restored[148] - 128) <= 1 && restored[151] == 128, "DDS policy: acknowledged color roundtrip within quantization tolerance");
        var reinterpreted = DdsTextureProcessor.Convert(source, srgb with { ColorOperation = DdsColorOperation.Reinterpret }, CancellationToken.None);
        check(reinterpreted.AsSpan(148).SequenceEqual(source.AsSpan(148)) && DdsParser.Parse(reinterpreted, reinterpreted.Length).IsSrgb,
            "DDS policy: reinterpret changes declaration without changing pixels");
        var unknown = linear with { SourceInterpretation = DdsInterpretation.Unspecified };
        Reject(() => DdsTextureProcessor.Convert(source, unknown, CancellationToken.None), "DDS policy: UNORM requires explicit source interpretation", check);
        Reject(() => (srgb with { Header = DdsHeaderMode.Legacy }).Validate(), "DDS policy: legacy output cannot silently drop sRGB declaration", check);
        Reject(() => (linear with { ColorOperation = DdsColorOperation.Reinterpret, Mips = DdsMipPolicy.Generate }).Validate(), "DDS policy: reinterpret forbids regeneration", check);
        Reject(() => (linear with { Purpose = DdsPurpose.Data }).Validate(), "DDS policy: data cannot inherit a color-transfer setting", check);
        Reject(() => new DdsConversionOptions(Format: DdsFormat.Bc4, Purpose: DdsPurpose.Data, SourceInterpretation: DdsInterpretation.Data, Alpha: DdsAlphaPolicy.Data).Validate(),
            "DDS policy: dropping channels requires acknowledgment", check);
        Reject(() => DdsTextureProcessor.Convert(source, linear with { Format = DdsFormat.Bc1 }, CancellationToken.None),
            "DDS policy: BC1 cannot silently quantize smooth alpha", check);
        var matte = DdsTextureProcessor.Convert(source, linear with { Alpha = DdsAlphaPolicy.Flatten, MatteRgb = 0xffffff }, CancellationToken.None);
        check(matte[151] == 255 && matte[148] is >= 153 and <= 156 && matte[149] is >= 126 and <= 128,
            "DDS policy: selected sRGB matte composites in linear light");
        var opaqueDeclaration = (byte[])source.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(opaqueDeclaration.AsSpan(144), 3);
        var opaqueResult = DdsTextureProcessor.Convert(opaqueDeclaration, linear, default);
        check(opaqueResult[151] == 255, "DDS policy: declared opaque alpha is not accidentally interpreted as transparency");
        Reject(() => (linear with { RedChannel = DdsChannel.Alpha }).Validate(),
            "DDS policy: inapplicable channel mappings are rejected, never silently ignored", check);
        var checker = new byte[64];
        for (var p = 0; p < 16; p++)
        {
            var white = (p % 4 + p / 4) % 2 == 0;
            checker[p * 4] = checker[p * 4 + 1] = checker[p * 4 + 2] = white ? (byte)255 : (byte)0;
            checker[p * 4 + 3] = 255;
        }
        var mipOptions = linear with { Format = DdsFormat.Rgba8Srgb, Mips = DdsMipPolicy.Generate };
        var mipChain = DdsTextureProcessor.Convert(Rgba(checker), mipOptions, CancellationToken.None);
        var mip = DdsCodec.DecodeMip(mipChain, 1);
        check(DdsParser.Parse(mipChain, mipChain.Length).MipLevels == 3 && mip.Rgba[0] is > 0.73f and < 0.74f,
            "DDS policy: generated color mips average light, not sRGB bytes");
        var wrap = DdsTextureProcessor.Convert(Rgba(checker), mipOptions with { Edge = DdsEdgeMode.Wrap }, CancellationToken.None);
        check(mipChain.SequenceEqual(wrap), "DDS policy: in-raster box footprints make clamp and wrap equivalent");
        var withoutMips = DdsTextureProcessor.Convert(mipChain, mipOptions with { Mips = DdsMipPolicy.Remove }, CancellationToken.None);
        check(DdsParser.Parse(withoutMips, withoutMips.Length).MipLevels == 1, "DDS policy: remove mips explicitly retains only the base level");
        var alphaEdge = new byte[64];
        for (var p = 0; p < 16; p++)
        {
            var visible = (p % 4 + p / 4) % 2 == 0;
            alphaEdge[p * 4] = visible ? (byte)255 : (byte)0;
            alphaEdge[p * 4 + 2] = visible ? (byte)0 : (byte)255;
            alphaEdge[p * 4 + 3] = visible ? (byte)255 : (byte)0;
        }
        var alphaMip = DdsCodec.DecodeMip(DdsTextureProcessor.Convert(Rgba(alphaEdge), mipOptions, CancellationToken.None), 1).Rgba;
        check(alphaMip[0] > 0.99f && alphaMip[2] < 0.01f && alphaMip[3] is > 0.49f and < 0.51f,
            "DDS policy: alpha-aware mips do not bleed hidden blue into visible red");
        var cutout = DdsTextureProcessor.Convert(Rgba(alphaEdge), mipOptions with { Format = DdsFormat.Bc1Srgb, Alpha = DdsAlphaPolicy.Cutout }, CancellationToken.None);
        check(DdsCodec.DecodeMip(cutout, 0).Rgba.Where((_, i) => i % 4 == 3).All(v => v is 0 or 1),
            "DDS policy: BC1 cutout retains binary alpha");
        var retainedCutout = DdsTextureProcessor.Convert(cutout, new(Format: DdsFormat.Bc1Srgb), default);
        check(retainedCutout.SequenceEqual(cutout), "DDS policy: no-op BC1 alpha preserves every authored block and mip");
        var reinterpretedCutout = DdsTextureProcessor.Convert(cutout, new(Format: DdsFormat.Bc1, ColorOperation: DdsColorOperation.Reinterpret), default);
        check(reinterpretedCutout.AsSpan(148).SequenceEqual(cutout.AsSpan(148)), "DDS policy: BC1 alpha reinterpretation leaves compressed blocks untouched");
        var dataOptions = new DdsConversionOptions(Format: DdsFormat.Rg8, Purpose: DdsPurpose.Data, SourceInterpretation: DdsInterpretation.Data,
            Alpha: DdsAlphaPolicy.Data, RedChannel: DdsChannel.Alpha, GreenChannel: DdsChannel.Red, ConfirmChannelLoss: true);
        var data = DdsTextureProcessor.Convert(source, dataOptions, CancellationToken.None);
        check(data[148] == 128 && data[149] == 128, "DDS policy: explicit data channel mapping applies without gamma");
        var normal = new byte[64];
        for (var p = 0; p < 64; p += 4) { normal[p] = 128; normal[p + 1] = 128; normal[p + 2] = 255; normal[p + 3] = 255; }
        var normalOptions = new DdsConversionOptions(Format: DdsFormat.Bc5Snorm, Purpose: DdsPurpose.Normal, SourceInterpretation: DdsInterpretation.Data,
            Mips: DdsMipPolicy.Generate, Alpha: DdsAlphaPolicy.Discard, ConfirmChannelLoss: true);
        var normalMips = DdsTextureProcessor.Convert(Rgba(normal), normalOptions, CancellationToken.None);
        var lastNormal = DdsCodec.DecodeMip(normalMips, 2).Rgba;
        check(Math.Abs(lastNormal[0]) < 0.02f && Math.Abs(lastNormal[1]) < 0.02f, "DDS policy: normal mip filtering retains a flat tangent-space normal");
        for (var p = 0; p < 16; p++)
        { normal[p * 4] = p % 2 == 0 ? (byte)255 : (byte)128; normal[p * 4 + 2] = p % 2 == 0 ? (byte)128 : (byte)255; }
        var angled = DdsCodec.DecodeMip(DdsTextureProcessor.Convert(Rgba(normal), normalOptions, default), 2).Rgba;
        check(angled[0] is > 0.68f and < 0.74f && Math.Abs(angled[1]) < 0.02f,
            "DDS policy: angled normals renormalize to the mean direction rather than average encoded channels");
        var backwardNormal = (byte[])normal.Clone(); backwardNormal[2] = 0;
        Reject(() => DdsTextureProcessor.Convert(Rgba(backwardNormal), normalOptions, default),
            "DDS policy: negative-Z normals cannot silently flip hemisphere in BC5", check);
        Reject(() => DdsTextureProcessor.Convert(DdsCodec.Encode(Rgba(normal), DdsFormat.Bc4), normalOptions, default),
            "DDS policy: one-channel input cannot invent a missing normal direction", check);
        Reject(() => DdsTextureProcessor.Convert(source, new(Format: DdsFormat.Bc1, Purpose: DdsPurpose.Data,
            SourceInterpretation: DdsInterpretation.Data, Alpha: DdsAlphaPolicy.Data), default),
            "DDS policy: continuous fourth-channel data cannot silently become binary BC1", check);
        Reject(() => DdsTextureProcessor.Convert(Rgba(alphaEdge), new(Format: DdsFormat.Bc1, Purpose: DdsPurpose.Data,
            SourceInterpretation: DdsInterpretation.Data, Alpha: DdsAlphaPolicy.Data, Mips: DdsMipPolicy.Generate), default),
            "DDS policy: binary fourth-channel data cannot erase other channels through BC1 transparency", check);
        var coveragePixels = new byte[64];
        int[] visiblePerBlock = [1, 1, 2, 3];
        for (var y = 0; y < 4; y++) for (var x = 0; x < 4; x++)
        {
            var p = (y * 4 + x) * 4; coveragePixels[p] = 255;
            coveragePixels[p + 3] = (y % 2 * 2 + x % 2) < visiblePerBlock[y / 2 * 2 + x / 2] ? (byte)255 : (byte)0;
        }
        var coverageOptions = mipOptions with { Format = DdsFormat.Bc1Srgb, Alpha = DdsAlphaPolicy.Cutout, CutoutThreshold = 0.6f };
        var maintained = DdsCodec.DecodeMip(DdsTextureProcessor.Convert(Rgba(coveragePixels), coverageOptions, default), 1).Rgba;
        var unmaintained = DdsCodec.DecodeMip(DdsTextureProcessor.Convert(Rgba(coveragePixels), coverageOptions with { PreserveCutoutCoverage = false }, default), 1).Rgba;
        double Coverage(float[] values) => values.Where((_, i) => i % 4 == 3).Count(v => v >= 0.6f) / (double)(values.Length / 4);
        check(Math.Abs(Coverage(maintained) - 7.0 / 16) < Math.Abs(Coverage(unmaintained) - 7.0 / 16),
            "DDS policy: coverage preservation improves achievable encoded mip coverage on an independent mask");
        var gradient = new byte[64];
        for (var p = 0; p < 16; p++) { gradient[p * 4] = (byte)(p * 17); gradient[p * 4 + 1] = (byte)(255 - p * 17); gradient[p * 4 + 2] = 64; gradient[p * 4 + 3] = 255; }
        foreach (var format in new[] { DdsFormat.Bc1, DdsFormat.Bc2, DdsFormat.Bc3, DdsFormat.Bc4, DdsFormat.Bc5, DdsFormat.Bc7 })
        {
            var values = DdsCodec.DecodeMip(DdsCodec.Encode(Rgba(gradient), format), 0).Rgba;
            var channels = format == DdsFormat.Bc4 ? 1 : format == DdsFormat.Bc5 ? 2 : 3;
            double squared = 0;
            for (var p = 0; p < 16; p++) for (var c = 0; c < channels; c++) squared += Math.Pow(values[p * 4 + c] - gradient[p * 4 + c] / 255.0, 2);
            check(squared / (16 * channels) < 0.02, "DDS metric: independent gradient mean-square error below 0.02 for " + format);
        }
        var compressed = DdsCodec.Encode(Rgba(checker), DdsFormat.Bc7Srgb);
        var retained = DdsTextureProcessor.Convert(compressed, mipOptions with { Format = DdsFormat.Bc7Srgb, Mips = DdsMipPolicy.Preserve }, CancellationToken.None);
        check(retained.SequenceEqual(compressed), "DDS policy: unchanged representation preserves compressed blocks exactly");
        var legacy = DdsTextureProcessor.Convert(Rgba(checker), linear with { Format = DdsFormat.Bc3, Header = DdsHeaderMode.Legacy }, CancellationToken.None);
        check(DdsParser.Parse(legacy, legacy.Length) is { HasDx10Header: false, Format: DdsFormat.Bc3, IsSupported2D: true }, "DDS policy: explicit compatible legacy header validates");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { DdsTextureProcessor.Convert(source, linear, cancellation.Token); check(false, "DDS policy: cancellation before processing"); }
        catch (OperationCanceledException) { check(true, "DDS policy: cancellation before processing"); }
    }

    private static byte[] Rgba(byte[] rgba)
    {
        var bytes = new byte[148 + rgba.Length];
        "DDS "u8.CopyTo(bytes);
        foreach (var pair in new (int Offset, uint Value)[] { (4, 124), (8, 0x1007), (12, 4), (16, 4), (76, 32),
            (80, 4), (84, 0x30315844), (108, 0x1000), (128, 28), (132, 3), (140, 1) })
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(pair.Offset), pair.Value);
        rgba.CopyTo(bytes, 148);
        return bytes;
    }

    private static void Reject(Action action, string name, Action<bool, string> check)
    {
        try { action(); check(false, name); } catch (InvalidDataException) { check(true, name); }
    }
}
