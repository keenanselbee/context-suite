using System.Buffers.Binary;
using System.Security.Cryptography;
using ContextSuite.Core.Audio;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Audio;

// Optional integration candidate. Windows supplies the codec; only the owned
// helper is pinned here. Never redistribute system DLLs or fall back to FFmpeg.
internal sealed class WindowsVoiceDecoder : IDisposable
{
    private const string HelperSha256 = "B03029719B338363193C6111A204B144A23EF52947301D6CA5493267C3D0DBA5";
    private readonly RuntimeDirectoryLeases _directories = new();
    private readonly FileStream _helper;
    private readonly string _executable;
    private bool _available, _disposed;

    internal WindowsVoiceDecoder(string directory)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Windows Voice decoding requires Windows 11 x64.");
        _executable = Path.Combine(RuntimeDirectoryLeases.LocalPath(directory), "WmaVoiceMftProbe.exe");
        try
        {
            _helper = _directories.OpenFileRead(_executable);
            if (Convert.ToHexString(SHA256.HashData(_helper)) != HelperSha256)
                throw new InvalidDataException("Windows Voice helper identity mismatch.");
        }
        catch { _helper?.Dispose(); _directories.Dispose(); throw; }
    }

    internal static void RequireSupport(AsfMetadataInventory inventory, long inputBytes)
    {
        inventory.RequireConversionSupport();
        if (inventory.Codec != "wmavoice" || inventory.Channels != 1 || inventory.SampleBits != 16 ||
            inventory.SampleRate is not (8000 or 11025 or 16000 or 22050))
            throw new NotSupportedException("This WMA Voice profile has no verified decoder policy yet.");
        if (inputBytes is <= 0 or > AudioEncodingAdapter.MaximumFileBytes)
            throw new NotSupportedException("This WMA Voice input exceeds the current decoder limit.");
    }

    internal async Task<long> DecodeWaveAsync(FileStream input, AsfMetadataInventory inventory, string outputPath,
        CancellationToken token)
    {
        RequireSupport(inventory, input.Length);
        await EnsureAvailableAsync(input, Path.GetDirectoryName(outputPath)!, token);
        input.Position = 0;
        using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            65536, FileOptions.Asynchronous);
        try
        {
            var frames = await SeekableMediaProcess.RunAsync(_executable, Path.GetDirectoryName(outputPath)!, input,
                ["--inherited"], (stream, readToken) => CaptureWaveAsync(stream, output, inventory.SampleRate,
                    AudioEncodingAdapter.MaximumDecodedBytes, readToken), token, ExitFailure);
            token.ThrowIfCancellationRequested();
            if (frames == 0) throw new InvalidDataException("Windows Voice returned no sample transport.");
            output.Position = 0;
            await output.WriteAsync(DecodedAudioWave.Header(frames * 8, inventory.SampleRate, 1, "mono"), token);
            return frames;
        }
        catch { output.Dispose(); File.Delete(outputPath); throw; }
    }

    // The stream can split a sample at any byte. Keep only fixed-size buffers,
    // validate every sample, and finalize the WAV header only after child success.
    internal static async Task<long> CaptureWaveAsync(Stream input, Stream output, int rate, long maximumBytes,
        CancellationToken token)
    {
        if (maximumBytes is < 8 or > AudioEncodingAdapter.MaximumDecodedBytes || maximumBytes % 8 != 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var header = new byte[24];
        // Empty stdout can mean startup failed. Let the launcher classify the
        // child exit first; DecodeWaveAsync rejects zero frames after success.
        var received = await input.ReadAsync(header, token);
        if (received == 0) return 0;
        await input.ReadExactlyAsync(header.AsMemory(received), token);
        if (!header.AsSpan(0, 8).SequenceEqual("CSVOICE1"u8) ||
            BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8)) != rate ||
            BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12)) != 1 ||
            BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16)) != 32 ||
            BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20)) != 16)
            throw new InvalidDataException("Windows Voice output disagrees with its validated input profile.");
        await output.WriteAsync(new byte[80], token);
        var buffer = new byte[65536]; var converted = new byte[131072];
        var pending = 0; long total = 0;
        while (true)
        {
            var count = await input.ReadAsync(buffer.AsMemory(pending), token);
            if (count == 0) break;
            var complete = (pending + count) / 4 * 4;
            if (total + complete * 2L > maximumBytes)
                throw new InvalidDataException("Windows Voice decoded audio exceeds the current budget.");
            for (var offset = 0; offset < complete; offset += 4)
            {
                var sample = BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(offset));
                if (!float.IsFinite(sample) || sample < -1 || sample > 32767f / 32768 || sample * 32768 != MathF.Truncate(sample * 32768))
                    throw new InvalidDataException("Windows Voice returned invalid PCM16 samples.");
                BinaryPrimitives.WriteDoubleLittleEndian(converted.AsSpan(offset * 2), sample);
            }
            await output.WriteAsync(converted.AsMemory(0, complete * 2), token);
            total += complete * 2L;
            pending += count - complete;
            buffer.AsSpan(complete, pending).CopyTo(buffer);
        }
        if (pending != 0 || total == 0) throw new InvalidDataException("Windows Voice returned incomplete or empty samples.");
        return total / 8;
    }

    internal async Task EnsureAvailableAsync(FileStream input, string directory, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        token.ThrowIfCancellationRequested();
        if (_available) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var reply = await SeekableMediaProcess.RunAsync(_executable, directory, input,
            ["--availability"], 64, deadline.Token, ExitFailure);
        if (!reply.AsSpan().SequenceEqual("CSVOICE-AVAILABLE-1\n"u8))
            throw new InvalidDataException("Windows Voice availability could not be verified. Originals were kept.");
        _available = true;
    }

    internal static Exception ExitFailure(int exitCode) => unchecked((uint)exitCode) switch
    {
        // Helper initialization failure, missing DLL, missing entry point,
        // or loader initialization failure. Other decoder failures stay distinct.
        3 or 0xc0000135 or 0xc0000139 or 0xc0000142 => new NotSupportedException(
            "Windows media support for WMA Voice could not start. Check Windows media components and try again. Originals were kept."),
        _ => new InvalidDataException("WMA Voice processing failed; no output was admitted.")
    };

    public void Dispose() { _disposed = true; _helper.Dispose(); _directories.Dispose(); }
}
