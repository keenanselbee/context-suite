using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Audio;

namespace ContextSuite.Private.Audio;

internal static partial class WavePreservationContracts
{
    public static async Task RunAsync(string binary, string fixtures, string evidence, List<string> checks, List<object> reports)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(fixtures, "source.wav"));
        var scratch = Path.Combine(evidence, "wave-preservation-scratch");
        using var adapter = new AudioEncodingAdapter(binary, scratch);
        const AudioConversionConsent consent = AudioConversionConsent.LossyTranscoding | AudioConversionConsent.Resampling | AudioConversionConsent.PrecisionReduction;
        foreach (var target in Enum.GetValues<AudioFormat>().Where(target => target != AudioFormat.Wem))
        {
            var result = await adapter.ConvertAsync(bytes, target, consent, default);
            if (!result.SourceMetadataVerified || !result.MissingTags.IsEmpty || (!result.Plan.AlreadyTarget && result.DecodedFrames != 96000))
                throw new InvalidDataException("WAV conversion did not prove its inventory and tags: " + target);
            checks.Add("WAV inventoried metadata and decoded samples: " + target);
            reports.Add(new { waveTarget = target.ToString(), result.SourceMetadataVerified, result.Facts, result.DecodedFrames });
        }
        var cue = new byte[28]; BinaryPrimitives.WriteUInt32LittleEndian(cue, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(cue.AsSpan(4), 1); "data"u8.CopyTo(cue.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(cue.AsSpan(24), 1000);
        foreach (var extra in new[] { ("cue ", cue), ("smpl", new byte[36]), ("bext", new byte[602]),
            ("iXML", Encoding.UTF8.GetBytes("<BWFXML><NOTE>Keep this note</NOTE></BWFXML>")),
            ("LIST", Info("IENG", Encoding.ASCII.GetBytes("Engineer\0"))),
            ("LIST", Info("IART", Encoding.ASCII.GetBytes("Second artist\0"))),
            ("LIST", Info("ICOP", Encoding.UTF8.GetBytes("Café\0"))) })
        {
            var authored = Append(bytes, extra.Item1, extra.Item2);
            var path = Path.Combine(evidence, "wave-extra-" + checks.Count + ".wav");
            await File.WriteAllBytesAsync(path, authored);
            var before = SHA256.HashData(authored);
            try { await adapter.ConvertAsync(authored, AudioFormat.Flac, consent, default); throw new Exception("Unmapped WAV information was discarded: " + extra.Item1); }
            catch (NotSupportedException)
            {
                if (!SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(before) || Directory.EnumerateFileSystemEntries(scratch).Any())
                    throw new IOException("Refused WAV conversion changed the source or retained scratch.");
                checks.Add("WAV refuses unpreserved information and cleans scratch: " + extra.Item1);
            }
        }
        var damaged = bytes.Concat(new byte[] { 1, 2, 3 }).ToArray();
        try { await adapter.ConvertAsync(damaged, AudioFormat.Mp3, consent, default); throw new Exception("Trailing WAV data was silently omitted."); }
        catch (InvalidDataException) { checks.Add("WAV rejects undeclared trailing bytes before encoding"); }
        if (Directory.EnumerateFileSystemEntries(scratch).Any()) throw new IOException("WAV rejection left owned scratch.");
    }
    private static byte[] Append(byte[] source, string id, byte[] data)
    {
        using var stream = new MemoryStream(); stream.Write(source); stream.Write(Encoding.ASCII.GetBytes(id));
        stream.Write(BitConverter.GetBytes(data.Length)); stream.Write(data); if ((data.Length & 1) != 0) stream.WriteByte(0);
        var result = stream.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)result.Length - 8); return result;
    }
    private static byte[] Info(string id, byte[] data)
    {
        using var stream = new MemoryStream(); stream.Write("INFO"u8); stream.Write(Encoding.ASCII.GetBytes(id));
        stream.Write(BitConverter.GetBytes(data.Length)); stream.Write(data); if ((data.Length & 1) != 0) stream.WriteByte(0); return stream.ToArray();
    }
}
