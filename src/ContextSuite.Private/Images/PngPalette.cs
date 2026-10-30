using System.Buffers.Binary;
using ContextSuite.Core.Images;

namespace ContextSuite.Private.Images;

internal static class PngPalette
{
    internal const string ExecutableSha256 = "6F268233B46CE3A84FED6C085D3FBF5310E8D6F4E890AE1B323CDCF21D700F1E";
    internal const string Method = "Palette (lossy)";

    internal static bool CanUse(byte[] rgba, int width, int height, CancellationToken cancellationToken)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 9_000_000 || rgba.Length != checked(width * height * 4)) return false;
        var hidden = new HashSet<int>(); var partial = new HashSet<byte>(); var opaque = false;
        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (i % 65536 == 0) cancellationToken.ThrowIfCancellationRequested();
            var alpha = rgba[i + 3];
            if (alpha == 0) hidden.Add(rgba[i] << 16 | rgba[i + 1] << 8 | rgba[i + 2]);
            else if (alpha == 255) opaque = true;
            else partial.Add(alpha);
            if (hidden.Count + partial.Count >= 128) return false;
        }
        return opaque;
    }

    internal static async Task<byte[]> GenerateAsync(byte[] rgba, int width, int height, CancellationToken cancellationToken)
    {
        if (!CanUse(rgba, width, height, cancellationToken)) throw new ImageFailureException(ImageFailure.UnsupportedInput);
        var input = new byte[checked(rgba.Length + 8)];
        BinaryPrimitives.WriteInt32LittleEndian(input, width);
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(4), height);
        rgba.CopyTo(input, 8);
        var output = await PngOptimizer.RunAsync(input, "ContextSuite.Palette.exe", ExecutableSha256, rgba.Length, [], cancellationToken);
        if (output.Length != rgba.Length) throw new ImageFailureException(ImageFailure.ValidationFailed);
        var colors = new HashSet<uint>();
        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (i % 65536 == 0) cancellationToken.ThrowIfCancellationRequested();
            if (rgba[i + 3] != output[i + 3] || (rgba[i + 3] == 0 && !rgba.AsSpan(i, 3).SequenceEqual(output.AsSpan(i, 3))))
                throw new ImageFailureException(ImageFailure.ValidationFailed);
            colors.Add(BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(i, 4)));
            if (colors.Count > 256) throw new ImageFailureException(ImageFailure.ValidationFailed);
        }
        return output;
    }
}
