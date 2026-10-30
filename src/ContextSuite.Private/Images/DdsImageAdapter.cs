using ContextSuite.Private.IO;
using System.Buffers.Binary;
using System.Text.Json;
using ContextSuite.Core.Dds;
using ContextSuite.Core.Images;
using ContextSuite.Private.Dds;
using ImageMagick;

namespace ContextSuite.Private.Images;

public sealed partial class ImageAdapter
{
    private static ImageSourceFacts ProbeDds(ImageProbe request, byte[] bytes, CancellationToken token)
    {
        var info = DdsParser.Parse(bytes.AsSpan(0, Math.Min(bytes.Length, 148)), bytes.Length);
        if (!info.IsSupported2D) throw new ImageFailureException(ImageFailure.UnsupportedInput);
        var transparent = false;
        for (uint mip = 0; mip < info.MipLevels; mip++)
        {
            token.ThrowIfCancellationRequested();
            var pixels = DdsCodec.DecodeMip(bytes, mip).Rgba;
            if (info.RawAlphaMode is not (3 or 4)) for (var p = 3; p < pixels.Length; p += 4) transparent |= pixels[p] < 1;
        }
        return new(request.ItemId, Path.GetFullPath(request.SourcePath), HexHash(bytes), bytes.Length, ImageFormat.Dds,
            info.Width, info.Height, 8, 1, transparent, info.IsBlockCompressed,
            info.IsSrgb ? "sRGB declared" : "DDS storage; source color/data interpretation required", [], Texture: info);
    }

    private ImageWorkResult ConvertWithDds(ImageWork request, CancellationToken token)
    {
        request.Options.Validate(); request.Plan.Source.Validate();
        var facts = Probe(new(request.Plan.Source.ItemId, request.Plan.Source.Path), token);
        ValidateSource(request.Plan.Source, facts);
        var expected = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], request.Options, new("convert", new())).Items[0];
        if (!expected.CanExecute || JsonSerializer.Serialize(expected) != JsonSerializer.Serialize(request.Plan))
            throw new InvalidDataException(expected.BlockReason ?? "DDS plan changed before conversion.");
        ValidatePath(request.TemporaryPath);
        if (Path.GetFileName(request.TemporaryPath) != $".context-suite-{facts.ItemId:N}.tmp" ||
            string.Equals(Path.GetFullPath(request.TemporaryPath), facts.Path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DDS may write only the application's reserved temporary file.");
        using var reserved = new ReservedMediaOutput(request.TemporaryPath);
        var output = CreateDdsOutput(facts, request.Options, token);
        if (output.Length > ImageContainer.MaximumFileBytes) throw new ImageFailureException(ImageFailure.ResourceLimit);
        uint width, height, depth;
        if (request.Options.Target == ImageFormat.Dds)
        {
            var header = DdsParser.Parse(output, output.Length);
            if (!header.IsSupported2D || header.Format != request.Options.Texture!.Format)
                throw new ImageFailureException(ImageFailure.ValidationFailed);
            for (uint mip = 0; mip < header.MipLevels; mip++)
            { token.ThrowIfCancellationRequested(); _ = DdsCodec.DecodeMip(output, mip); }
            width = header.Width; height = header.Height; depth = 8;
        }
        else
        {
            var container = ImageContainer.Read(output);
            using var image = Decode(output, container, token);
            if (container.Format != ImageFormat.Png) throw new ImageFailureException(ImageFailure.ValidationFailed);
            width = image.Width; height = image.Height; depth = container.Depth;
        }
        if (width != expected.OutputWidth || height != expected.OutputHeight || depth != expected.OutputDepth)
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        token.ThrowIfCancellationRequested();
        reserved.Write(output); reserved.Flush(true); reserved.Dispose();
        token.ThrowIfCancellationRequested();
        var readBack = ReadSource(request.TemporaryPath, token);
        if (!readBack.AsSpan().SequenceEqual(output)) throw new ImageFailureException(ImageFailure.ValidationFailed);
        return new(new(facts.ItemId, HexHash(readBack), true), width, height, depth, output.Length);
    }

    private byte[] CreateDdsOutput(ImageSourceFacts source, ImageConversionOptions options, CancellationToken token)
    {
        var bytes = ReadSource(source.Path, token);
        if (HexHash(bytes) != source.Sha256) throw new ImageFailureException(ImageFailure.SourceChanged);
        var policy = options.Texture ?? throw new InvalidDataException("Missing DDS policy.");
        if (source.Format != ImageFormat.Dds)
        {
            using var image = Decode(bytes, ImageContainer.Read(bytes), token);
            image.AutoOrient();
            if (policy.Purpose == DdsPurpose.Color)
            {
                if (policy.SourceInterpretation != DdsInterpretation.Srgb)
                    throw new InvalidDataException("Color-managed ordinary images enter DDS processing as sRGB, not untransformed linear pixels.");
                if (image.GetColorProfile() is { } profile) image.TransformColorSpace(profile, ColorProfiles.SRGB);
                image.ColorSpace = ColorSpace.sRGB;
            }
            else if (image.GetColorProfile() is not null)
                throw new InvalidDataException("A profiled image cannot silently be treated as numeric texture data.");
            image.Depth = 8;
            var pixels = image.GetPixels().ToByteArray(PixelMapping.RGBA) ?? throw new InvalidDataException("No source pixels.");
            bytes = RgbaDds(pixels, image.Width, image.Height, policy.Purpose == DdsPurpose.Color);
        }
        if (options.Target == ImageFormat.Dds) return DdsTextureProcessor.Convert(bytes, policy, token);
        if (options.Target != ImageFormat.Png || options.ExtractMip is not { } selected || source.Texture is null || policy.Purpose != DdsPurpose.Color)
            throw new InvalidDataException("DDS export requires color PNG and an explicitly selected mip.");
        if (source.Texture.Format is DdsFormat.Bc4Snorm or DdsFormat.Bc5Snorm)
            throw new InvalidDataException("Signed numeric texture export requires a separate range conversion policy.");
        // Keep decoder precision until after color conversion, then quantize once for PNG.
        var asSrgb = DdsTextureProcessor.Convert(bytes, policy with { Format = DdsFormat.Rgba8Srgb,
            Mips = DdsMipPolicy.Remove, Header = DdsHeaderMode.Dx10, ColorOperation = DdsColorOperation.ConvertValues }, token, selected);
        var mipPixels = DdsCodec.DecodeMip(asSrgb, 0);
        var rgba = FloatToBytes(mipPixels.Rgba);
        using var png = new MagickImage();
        png.ReadPixels(rgba, new PixelReadSettings(mipPixels.Width, mipPixels.Height, StorageType.Char, PixelMapping.RGBA));
        png.SetProfile(ColorProfiles.SRGB); png.ColorSpace = ColorSpace.sRGB; png.Depth = 8;
        png.Settings.SetDefine(MagickFormat.Png, "bit-depth", "8");
        png.Settings.SetDefine(MagickFormat.Png, "color-type", "6");
        return png.ToByteArray(MagickFormat.Png);
    }

    private ImagePreview PreviewWithDds(ImagePreviewRequest request, CancellationToken token)
    {
        var facts = Probe(new(request.Source.ItemId, request.Source.Path), token);
        ValidateSource(request.Source, facts);
        if (request.Options is { } options)
        {
            var plan = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], options, new("convert", new())).Items[0];
            if (!plan.CanExecute) throw new InvalidDataException(plan.BlockReason);
            var encoded = CreateDdsOutput(facts, options, token);
            if (options.Target == ImageFormat.Png)
            {
                using var png = Decode(encoded, ImageContainer.Read(encoded), token);
                png.Resize(new MagickGeometry(512, 512) { Greater = true });
                return DisplayPixels(png, token);
            }
            return TexturePreview(encoded, options.Texture!.IsSrgb, options.Texture.Purpose, token);
        }
        if (facts.Texture is null) throw new InvalidDataException("DDS source preview requires DDS input.");
        return TexturePreview(ReadSource(facts.Path, token), facts.Texture.IsSrgb,
            facts.Texture.IsSrgb ? DdsPurpose.Color : DdsPurpose.Data, token);
    }

    private static ImagePreview TexturePreview(byte[] dds, bool srgb, DdsPurpose purpose, CancellationToken token)
    {
        var pixels = DdsCodec.DecodeMip(dds, 0);
        var info = DdsParser.Parse(dds, dds.Length);
        if (info.Format is DdsFormat.Bc4Snorm or DdsFormat.Bc5Snorm)
            for (var p = 0; p < pixels.Rgba.Length; p += 4)
            {
                pixels.Rgba[p] = pixels.Rgba[p] * 0.5f + 0.5f;
                if (info.Format == DdsFormat.Bc5Snorm) pixels.Rgba[p + 1] = pixels.Rgba[p + 1] * 0.5f + 0.5f;
            }
        if (purpose != DdsPurpose.Color || info.RawAlphaMode == 3)
            for (var p = 3; p < pixels.Rgba.Length; p += 4) pixels.Rgba[p] = 1;
        if (purpose == DdsPurpose.Color && !srgb)
            for (var p = 0; p < pixels.Rgba.Length; p += 4)
                for (var c = 0; c < 3; c++)
                { var value = pixels.Rgba[p + c]; pixels.Rgba[p + c] = value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1 / 2.4f) - 0.055f; }
        using var image = new MagickImage();
        image.ReadPixels(FloatToBytes(pixels.Rgba), new PixelReadSettings(pixels.Width, pixels.Height, StorageType.Char, PixelMapping.RGBA));
        image.ColorSpace = ColorSpace.sRGB;
        image.Resize(new MagickGeometry(512, 512) { Greater = true });
        return DisplayPixels(image, token);
    }

    private static byte[] FloatToBytes(float[] pixels)
    {
        var bytes = new byte[pixels.Length];
        for (var i = 0; i < pixels.Length; i++) bytes[i] = (byte)Math.Clamp((int)MathF.Round(pixels[i] * 255), 0, 255);
        return bytes;
    }

    private static byte[] RgbaDds(byte[] rgba, uint width, uint height, bool srgb)
    {
        if ((ulong)rgba.LongLength != (ulong)width * height * 4) throw new InvalidDataException("Invalid RGBA texture buffer.");
        var bytes = new byte[148 + rgba.Length];
        "DDS "u8.CopyTo(bytes);
        foreach (var field in new (int Offset, uint Value)[] { (4, 124), (8, 0x1007), (12, height), (16, width), (76, 32),
            (80, 4), (84, 0x30315844), (108, 0x1000), (128, (uint)(srgb ? DdsFormat.Rgba8Srgb : DdsFormat.Rgba8)), (132, 3), (140, 1), (144, 1) })
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(field.Offset), field.Value);
        rgba.CopyTo(bytes, 148);
        return bytes;
    }
}
