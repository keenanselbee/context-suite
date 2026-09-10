using System.Security.Cryptography;

namespace ContextSuite.Core.Audio;

public sealed record AudioSampleSummary(long Frames, double PeakMagnitude, string Sha256);
public sealed record AudioSampleComparison(long Frames, double MaximumError, double RootMeanSquareError);

// Canonical interleaved little-endian float64 samples. Fixed-size buffers keep
// validation independent of recording length; the caller supplies its byte limit.
public static class AudioSampleValidation
{
    private const int BufferBytes = 65536;

    public static async Task<AudioSampleSummary> CaptureAsync(Stream decoded, Stream reference, int channels,
        long maximumBytes, CancellationToken token)
    {
        ValidateLimits(channels, maximumBytes);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferBytes];
        long bytes = 0;
        double peak = 0;
        int count;
        while ((count = await decoded.ReadAtLeastAsync(buffer, buffer.Length, false, token)) != 0)
        {
            if (count > maximumBytes - bytes || count % sizeof(double) != 0)
                throw new InvalidDataException("Decoded audio exceeds its limit or ends inside a sample.");
            for (var offset = 0; offset < count; offset += sizeof(double))
            {
                var value = BitConverter.Int64BitsToDouble(System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(offset)));
                if (!double.IsFinite(value)) throw new InvalidDataException("Decoded audio contains a non-finite sample.");
                peak = Math.Max(peak, Math.Abs(value));
            }
            await reference.WriteAsync(buffer.AsMemory(0, count), token);
            hash.AppendData(buffer, 0, count);
            bytes += count;
        }
        RequireFrames(bytes, channels);
        return new(bytes / (channels * sizeof(double)), peak, Convert.ToHexString(hash.GetHashAndReset()));
    }

    public static async Task<AudioSampleComparison> CompareAsync(Stream reference, Stream decoded, int channels,
        long maximumBytes, CancellationToken token)
    {
        ValidateLimits(channels, maximumBytes);
        var before = new byte[BufferBytes];
        var after = new byte[BufferBytes];
        long bytes = 0;
        double maximum = 0, squared = 0;
        int count;
        while ((count = await decoded.ReadAtLeastAsync(after, after.Length, false, token)) != 0)
        {
            if (count > maximumBytes - bytes || count % sizeof(double) != 0)
                throw new InvalidDataException("Decoded audio exceeds its limit or ends inside a sample.");
            await reference.ReadExactlyAsync(before.AsMemory(0, count), token);
            for (var offset = 0; offset < count; offset += sizeof(double))
            {
                var expected = BitConverter.Int64BitsToDouble(System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(before.AsSpan(offset)));
                var actual = BitConverter.Int64BitsToDouble(System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(after.AsSpan(offset)));
                var error = Math.Abs(expected - actual);
                if (!double.IsFinite(expected) || !double.IsFinite(actual) || !double.IsFinite(error))
                    throw new InvalidDataException("Audio comparison contains a non-finite sample or error.");
                maximum = Math.Max(maximum, error); squared += error * error;
            }
            bytes += count;
        }
        if (await reference.ReadAsync(before.AsMemory(0, 1), token) != 0)
            throw new InvalidDataException("Encoded audio has fewer decoded samples than its reference.");
        RequireFrames(bytes, channels);
        if (!double.IsFinite(squared)) throw new InvalidDataException("Audio signal error exceeds the comparison range.");
        return new(bytes / (channels * sizeof(double)), maximum, Math.Sqrt(squared / (bytes / sizeof(double))));
    }

    private static void ValidateLimits(int channels, long maximumBytes)
    {
        if (channels is < 1 or > 8 || maximumBytes < channels * sizeof(double)) throw new ArgumentOutOfRangeException(nameof(channels));
    }

    private static void RequireFrames(long bytes, int channels)
    {
        if (bytes == 0 || bytes % (channels * sizeof(double)) != 0)
            throw new InvalidDataException("Decoded audio has no complete stream of sample frames.");
    }
}
