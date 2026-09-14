using System.Security.Cryptography;
using ContextSuite.Core.Audio;

internal static class AudioSampleContracts
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var samples = Enumerable.Range(0, 20000).Select(index => (index % 401 - 200) / 200.0).ToArray();
        var bytes = samples.SelectMany(BitConverter.GetBytes).ToArray();
        using var reference = new MemoryStream();
        var captured = await AudioSampleValidation.CaptureAsync(new ShortReads(bytes), reference, 2, bytes.Length, default);
        check(captured.Frames == 10000 && captured.PeakMagnitude == 1 && captured.Sha256 == Convert.ToHexString(SHA256.HashData(bytes)) &&
            reference.ToArray().SequenceEqual(bytes), "audio samples: fragmented capture retains all samples and their digest");
        reference.Position = 0;
        var exact = await AudioSampleValidation.CompareAsync(reference, new ShortReads(bytes), 2, bytes.Length, default);
        check(exact.Frames == 10000 && exact.MaximumError == 0 && exact.RootMeanSquareError == 0, "audio samples: streaming exact comparison across multiple buffers");
        var changed = bytes.ToArray();
        BitConverter.GetBytes(samples[^1] + .125).CopyTo(changed, changed.Length - 8);
        reference.Position = 0;
        var difference = await AudioSampleValidation.CompareAsync(reference, new MemoryStream(changed), 2, bytes.Length, default);
        check(difference.MaximumError == .125 && Math.Abs(difference.RootMeanSquareError - .125 / Math.Sqrt(samples.Length)) < 1e-12,
            "audio samples: late sample difference contributes to full-stream error");
        await Reject(() => AudioSampleValidation.CaptureAsync(new MemoryStream(bytes), Stream.Null, 2, bytes.Length - 1, default), "capture byte limit");
        await Reject(() => AudioSampleValidation.CompareAsync(new MemoryStream(bytes), new MemoryStream(bytes), 2, bytes.Length - 1, default), "comparison byte limit");
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var invalid = BitConverter.GetBytes(value);
            await Reject(() => AudioSampleValidation.CaptureAsync(new MemoryStream(invalid), Stream.Null, 1, 8, default), "non-finite source " + value);
            await Reject(() => AudioSampleValidation.CompareAsync(new MemoryStream(new byte[8]), new MemoryStream(invalid), 1, 8, default), "non-finite output " + value);
        }
        await Reject(() => AudioSampleValidation.CaptureAsync(new MemoryStream(new byte[7]), Stream.Null, 1, 8, default), "partial sample");
        await Reject(() => AudioSampleValidation.CaptureAsync(new MemoryStream(new byte[8]), Stream.Null, 2, 16, default), "partial channel frame");
        await Reject(() => AudioSampleValidation.CaptureAsync(new MemoryStream(), Stream.Null, 1, 8, default), "empty decode");
        await Reject(() => AudioSampleValidation.CompareAsync(new MemoryStream(bytes), new MemoryStream(bytes[..^16]), 2, bytes.Length, default), "shorter output");
        await Reject(() => AudioSampleValidation.CompareAsync(new MemoryStream(bytes[..^16]), new MemoryStream(bytes), 2, bytes.Length, default), "longer output");
        try
        {
            await AudioSampleValidation.CompareAsync(new ShortReads(new byte[16]), new ShortReads(new byte[47 * 16]), 2, 47 * 16, default);
            check(false, "audio samples: extra padding has an explicit length-validation failure");
        }
        catch (InvalidDataException exception)
        {
            check(exception.Message == "Encoded audio has more decoded samples than its reference.",
                "audio samples: extra padding has an explicit length-validation failure");
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await AudioSampleValidation.CaptureAsync(new MemoryStream(bytes), Stream.Null, 2, bytes.Length, cancelled.Token); check(false, "audio samples: cancellation"); }
        catch (OperationCanceledException) { check(true, "audio samples: cancellation"); }
        async Task Reject(Func<Task> action, string name)
        {
            try { await action(); check(false, "audio samples: " + name); }
            catch (Exception exception) when (exception is IOException or InvalidDataException) { check(true, "audio samples: " + name); }
        }
    }

    private sealed class ShortReads(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 37)], cancellationToken);
    }
}
