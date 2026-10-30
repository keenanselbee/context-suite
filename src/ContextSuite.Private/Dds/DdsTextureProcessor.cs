using System.Buffers.Binary;
using ContextSuite.Core.Dds;

namespace ContextSuite.Private.Dds;

public static class DdsTextureProcessor
{
    private sealed record Plane(uint Width, uint Height, float[] Pixels);

    public static byte[] Convert(byte[] source, DdsConversionOptions options, CancellationToken cancellationToken, uint? extractMip = null)
    {
        var info = DdsParser.Parse(source.AsSpan(0, Math.Min(source.Length, 148)), source.Length);
        options.ValidateSource(info, false);
        if (extractMip is { } selected && (selected >= info.MipLevels || options.Mips != DdsMipPolicy.Remove ||
            options.ColorOperation == DdsColorOperation.Reinterpret || options.IsCompressed))
            throw new InvalidDataException("Mip extraction requires an existing level and an uncompressed, single-level value conversion.");
        cancellationToken.ThrowIfCancellationRequested();
        if (options.ColorOperation == DdsColorOperation.Reinterpret)
            return Reinterpret(source, info, options.Format);
        var planes = new List<Plane>();
        var count = options.Mips == DdsMipPolicy.Preserve ? info.MipLevels : 1;
        var sourceSigned = info.Format is DdsFormat.Bc4Snorm or DdsFormat.Bc5Snorm;
        var sourceSrgb = options.SourceInterpretation == DdsInterpretation.Srgb ||
            (options.SourceInterpretation == DdsInterpretation.Unspecified && info.IsSrgb);
        if (options.PreservesPayload(info))
        {
            var unchangedPixels = Reinterpret(source, info, options.Format);
            return options.Header == DdsHeaderMode.Legacy ? LegacyHeader(unchangedPixels, options.Format) : unchangedPixels;
        }
        for (uint mip = 0; mip < count; mip++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decoded = DdsCodec.DecodeMip(source, extractMip ?? mip);
            var pixels = decoded.Rgba;
            if (options.Purpose == DdsPurpose.Color && info.RawAlphaMode == 3)
                for (var p = 3; p < pixels.Length; p += 4) pixels[p] = 1;
            var transparent = false;
            for (var p = 3; p < pixels.Length; p += 4) transparent |= pixels[p] < 1;
            options.ValidateSource(info, transparent && options.Purpose == DdsPurpose.Color);
            for (var p = 0; p < pixels.Length; p += 4)
            {
                if ((p & 0xffff) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (options.Purpose == DdsPurpose.Color)
                {
                    for (var c = 0; c < 3; c++)
                    {
                        if (pixels[p + c] is < 0 or > 1) throw new InvalidDataException("Color processing requires bounded non-HDR color values.");
                        if (sourceSrgb) pixels[p + c] = ToLinear(pixels[p + c]);
                        if (options.Alpha == DdsAlphaPolicy.Flatten)
                        {
                            var matte = ((options.MatteRgb!.Value >> (16 - c * 8)) & 255) / 255f;
                            pixels[p + c] = pixels[p + c] * pixels[p + 3] + ToLinear(matte) * (1 - pixels[p + 3]);
                        }
                    }
                    if (options.Alpha is DdsAlphaPolicy.Flatten or DdsAlphaPolicy.Discard) pixels[p + 3] = 1;
                }
                else if (options.Purpose == DdsPurpose.Normal)
                {
                    var x = sourceSigned ? pixels[p] : pixels[p] * 2 - 1;
                    var y = sourceSigned ? pixels[p + 1] : pixels[p + 1] * 2 - 1;
                    var z = info.Format is DdsFormat.Bc5 or DdsFormat.Bc5Snorm or DdsFormat.Rg8 ?
                        MathF.Sqrt(MathF.Max(0, 1 - x * x - y * y)) : pixels[p + 2] * 2 - 1;
                    if (z < 0) throw new InvalidDataException("BC5 normal maps reconstruct positive Z and cannot retain negative-Z normals. Use an explicit data conversion to keep channels without normal-map interpretation.");
                    Normalize(pixels, p, x, y, z);
                    pixels[p + 3] = 1;
                }
                else if (options.Channels < 4)
                {
                    var red = pixels[p + (int)options.RedChannel];
                    var green = pixels[p + (int)options.GreenChannel];
                    pixels[p] = red; pixels[p + 1] = options.Channels == 2 ? green : 0;
                    pixels[p + 2] = 0; pixels[p + 3] = 1;
                }
                else if (options.Purpose == DdsPurpose.Data && options.Format is (DdsFormat.Bc1 or DdsFormat.Bc1Srgb) && pixels[p + 3] != 1)
                    throw new InvalidDataException("BC1 data output requires an opaque fourth channel: transparent BC1 blocks also discard RGB data. Choose BC3/BC7 when the fourth channel carries data.");
            }
            planes.Add(new(decoded.Width, decoded.Height, pixels));
        }
        if (options.Mips == DdsMipPolicy.Generate)
        {
            var desiredCoverage = Coverage(planes[0].Pixels, options.CutoutThreshold, 1);
            while (planes[^1].Width > 1 || planes[^1].Height > 1)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (planes.Sum(p => p.Pixels.LongLength) + (long)Math.Max(1, planes[^1].Width / 2) * Math.Max(1, planes[^1].Height / 2) * 4 > 40_000_000L * 4)
                    throw new InvalidDataException("Generated DDS mip chain exceeds the bounded pixel budget.");
                var next = Downsample(planes[^1], options, cancellationToken);
                if (options.Alpha == DdsAlphaPolicy.Cutout && options.PreserveCutoutCoverage)
                    AdjustCoverage(next.Pixels, desiredCoverage, options.CutoutThreshold);
                planes.Add(next);
            }
        }
        foreach (var plane in planes)
        for (var p = 0; p < plane.Pixels.Length; p += 4)
        {
            if ((p & 0xffff) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (options.Purpose == DdsPurpose.Color)
            {
                if (options.IsSrgb) for (var c = 0; c < 3; c++) plane.Pixels[p + c] = ToSrgb(plane.Pixels[p + c]);
                if (options.Alpha == DdsAlphaPolicy.Cutout) plane.Pixels[p + 3] = plane.Pixels[p + 3] >= options.CutoutThreshold ? 1 : 0;
            }
            if (options.Purpose == DdsPurpose.Normal && !options.IsSigned)
                for (var c = 0; c < 3; c++) plane.Pixels[p + c] = plane.Pixels[p + c] * 0.5f + 0.5f;
            var minimum = options.IsSigned ? -1f : 0f;
            for (var c = 0; c < options.Channels; c++)
                if (plane.Pixels[p + c] < minimum || plane.Pixels[p + c] > 1)
                    throw new InvalidDataException("The selected output cannot represent these signed/channel values without a separate range conversion.");
        }
        var total = planes.Sum(p => p.Pixels.LongLength);
        if (total > 40_000_000L * 4) throw new InvalidDataException("DDS mip chain exceeds the bounded pixel budget.");
        var allPixels = new float[checked((int)total)];
        var position = 0;
        foreach (var plane in planes) { plane.Pixels.CopyTo(allPixels, position); position += plane.Pixels.Length; }
        cancellationToken.ThrowIfCancellationRequested();
        var alphaMode = options.Purpose == DdsPurpose.Data && options.Channels == 4 ? DdsAlphaMode.Custom :
            options.Purpose != DdsPurpose.Color || options.Alpha is DdsAlphaPolicy.Flatten or DdsAlphaPolicy.Discard ? DdsAlphaMode.Opaque : DdsAlphaMode.Straight;
        var output = DdsCodec.EncodeMipPixels(allPixels, planes[0].Width, planes[0].Height, (uint)planes.Count, options.Format,
            options.CutoutThreshold, options.Quality, alphaMode);
        cancellationToken.ThrowIfCancellationRequested();
        return options.Header == DdsHeaderMode.Legacy ? LegacyHeader(output, options.Format) : output;
    }

    private static Plane Downsample(Plane source, DdsConversionOptions options, CancellationToken token)
    {
        var width = Math.Max(1, source.Width / 2); var height = Math.Max(1, source.Height / 2);
        var pixels = new float[checked((int)(width * height * 4))];
        for (uint y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (uint x = 0; x < width; x++)
            {
                var x0 = (double)x * source.Width / width; var x1 = (double)(x + 1) * source.Width / width;
                var y0 = (double)y * source.Height / height; var y1 = (double)(y + 1) * source.Height / height;
                var destination = checked((int)((y * width + x) * 4));
                double weightSum = 0, alphaSum = 0;
                var alphaAware = options.Purpose == DdsPurpose.Color;
                for (var sy = (uint)y0; sy < Math.Ceiling(y1); sy++)
                for (var sx = (uint)x0; sx < Math.Ceiling(x1); sx++)
                {
                    // Area footprints stay inside the raster; clamp and wrap agree
                    // for this box filter. No outside-edge sampling is performed.
                    var weight = (Math.Min(sx + 1, x1) - Math.Max(sx, x0)) * (Math.Min(sy + 1, y1) - Math.Max(sy, y0));
                    var input = checked((int)((sy * source.Width + sx) * 4));
                    var alpha = source.Pixels[input + 3];
                    for (var c = 0; c < 3; c++) pixels[destination + c] += (float)(source.Pixels[input + c] * weight * (alphaAware ? alpha : 1));
                    alphaSum += alpha * weight; weightSum += weight;
                }
                var rgbWeight = alphaAware ? alphaSum : weightSum;
                if (rgbWeight > 0) for (var c = 0; c < 3; c++) pixels[destination + c] /= (float)rgbWeight;
                pixels[destination + 3] = (float)(alphaSum / weightSum);
                if (options.Purpose == DdsPurpose.Normal)
                    Normalize(pixels, destination, pixels[destination], pixels[destination + 1], pixels[destination + 2]);
            }
        }
        return new(width, height, pixels);
    }

    private static void Normalize(float[] pixels, int offset, float x, float y, float z)
    {
        var length = MathF.Sqrt(x * x + y * y + z * z);
        if (length < 0.00001f) { x = 0; y = 0; z = 1; length = 1; }
        pixels[offset] = x / length; pixels[offset + 1] = y / length; pixels[offset + 2] = z / length;
    }

    private static float Coverage(float[] pixels, float threshold, float scale)
    {
        var visible = 0;
        for (var index = 3; index < pixels.Length; index += 4) if (pixels[index] * scale >= threshold) visible++;
        return visible / (pixels.Length / 4f);
    }

    private static void AdjustCoverage(float[] pixels, float desired, float threshold)
    {
        float low = 0, high = 16, best = 1;
        var bestError = MathF.Abs(Coverage(pixels, threshold, 1) - desired);
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var scale = (low + high) / 2;
            var coverage = Coverage(pixels, threshold, scale);
            var error = MathF.Abs(coverage - desired);
            if (error < bestError) { best = scale; bestError = error; }
            if (coverage < desired) low = scale; else high = scale;
        }
        for (var index = 3; index < pixels.Length; index += 4) pixels[index] = Math.Clamp(pixels[index] * best, 0, 1);
    }

    private static byte[] Reinterpret(byte[] source, DdsInfo info, DdsFormat target)
    {
        var result = info.HasDx10Header ? (byte[])source.Clone() : new byte[source.Length + 20];
        if (!info.HasDx10Header)
        {
            source.AsSpan(0, 128).CopyTo(result); source.AsSpan(128).CopyTo(result.AsSpan(148));
            Write(result, 80, 4); Write(result, 84, 0x30315844);
            result.AsSpan(88, 20).Clear(); Write(result, 132, 3); Write(result, 140, 1);
        }
        Write(result, 128, (uint)target);
        var verified = DdsParser.Parse(result, result.Length);
        if (!verified.IsSupported2D || verified.Format != target) throw new InvalidDataException("Reinterpreted DDS header failed validation.");
        return result;
    }

    private static byte[] LegacyHeader(byte[] dx10, DdsFormat format)
    {
        var result = new byte[dx10.Length - 20];
        dx10.AsSpan(0, 128).CopyTo(result); dx10.AsSpan(148).CopyTo(result.AsSpan(128));
        result.AsSpan(80, 28).Clear();
        var fourCc = format switch
        {
            DdsFormat.Bc1 => 0x31545844u, DdsFormat.Bc2 => 0x33545844u, DdsFormat.Bc3 => 0x35545844u,
            DdsFormat.Bc4 => 0x55344342u, DdsFormat.Bc4Snorm => 0x53344342u,
            DdsFormat.Bc5 => 0x55354342u, DdsFormat.Bc5Snorm => 0x53354342u, _ => 0u
        };
        if (fourCc != 0) { Write(result, 80, 4); Write(result, 84, fourCc); }
        else if (format == DdsFormat.R8) { Write(result, 80, 0x20000); Write(result, 88, 8); Write(result, 92, 255); }
        else if (format is DdsFormat.Rgba8 or DdsFormat.Bgra8)
        {
            Write(result, 80, 0x41); Write(result, 88, 32); Write(result, 96, 0xff00); Write(result, 104, 0xff000000);
            Write(result, 92, format == DdsFormat.Rgba8 ? 255u : 0xff0000u); Write(result, 100, format == DdsFormat.Rgba8 ? 0xff0000u : 255u);
        }
        else throw new InvalidDataException("The selected format has no implemented legacy header.");
        var info = DdsParser.Parse(result, result.Length);
        if (info.Format != format || !info.IsSupported2D) throw new InvalidDataException("Legacy output header failed independent validation.");
        return result;
    }

    private static float ToLinear(float value) => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
    private static float ToSrgb(float value) => value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1 / 2.4f) - 0.055f;
    private static void Write(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
