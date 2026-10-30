using ContextSuite.Core.Images;

namespace ContextSuite.Private.Images;

internal static class TgaOptimization
{
    internal static byte[] Optimize(byte[] source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var header = ImageContainer.Read(source);
        if (header.Format != ImageFormat.Tga) throw new ImageFailureException(ImageFailure.UnsupportedInput);
        var samples = Decode(source, header, token);
        var start = 18 + source[0];
        var stride = source[16] / 8;
        using var output = new MemoryStream();
        output.Write(source.AsSpan(0, start));
        output.GetBuffer()[2] = 10;
        var width = checked((int)header.Width);
        for (var row = 0; row < header.Height; row++)
        {
            token.ThrowIfCancellationRequested();
            var end = checked(((int)row + 1) * width);
            for (var pixel = (int)row * width; pixel < end;)
            {
                var run = 1;
                while (run < 128 && pixel + run < end && Same(pixel, pixel + run)) run++;
                if (run > 1)
                {
                    output.WriteByte((byte)(128 | (run - 1)));
                    output.Write(samples.AsSpan(pixel * stride, stride));
                }
                else
                {
                    while (run < 128 && pixel + run < end &&
                        !(pixel + run + 1 < end && Same(pixel + run, pixel + run + 1))) run++;
                    output.WriteByte((byte)(run - 1));
                    output.Write(samples.AsSpan(pixel * stride, run * stride));
                }
                pixel += run;
                // Avoid retaining an expanded candidate for incompressible input.
                if (output.Length >= source.Length) return source;
            }
        }
        var footer = source.Length >= 44 && source.AsSpan(source.Length - 18).SequenceEqual("TRUEVISION-XFILE.\0"u8);
        if (footer) output.Write(source.AsSpan(source.Length - 26));
        if (output.Length >= source.Length) return source;
        var candidate = output.ToArray();
        var actual = ImageContainer.Read(candidate);
        if (actual != header || !Decode(candidate, actual, token).AsSpan().SequenceEqual(samples) ||
            !candidate.AsSpan(0, 2).SequenceEqual(source.AsSpan(0, 2)) ||
            !candidate.AsSpan(3, start - 3).SequenceEqual(source.AsSpan(3, start - 3)) ||
            footer && !candidate.AsSpan(candidate.Length - 26).SequenceEqual(source.AsSpan(source.Length - 26)))
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        return candidate;

        bool Same(int left, int right) => samples.AsSpan(left * stride, stride).SequenceEqual(samples.AsSpan(right * stride, stride));
    }

    private static byte[] Decode(byte[] source, ImageContainer header, CancellationToken token)
    {
        // ImageContainer has already checked all packet lengths, attributes,
        // dimensions, ID/footer framing, and unsupported extension offsets.
        var stride = source[16] / 8;
        var length = checked((int)(header.Width * header.Height) * stride);
        var position = 18 + source[0];
        if (source[2] == 2) return source.AsSpan(position, length).ToArray();
        var samples = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            token.ThrowIfCancellationRequested();
            var packet = source[position++];
            var count = (packet & 127) + 1;
            if ((packet & 128) != 0)
            {
                for (var index = 0; index < count; index++)
                    source.AsSpan(position, stride).CopyTo(samples.AsSpan(offset + index * stride));
                position += stride;
            }
            else
            {
                source.AsSpan(position, count * stride).CopyTo(samples.AsSpan(offset));
                position += count * stride;
            }
            offset += count * stride;
        }
        return samples;
    }
}
