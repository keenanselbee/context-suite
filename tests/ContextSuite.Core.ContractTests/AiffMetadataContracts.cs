using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using ContextSuite.Core.Audio;

namespace ContextSuite.Core.ContractTests;

internal static class AiffMetadataContracts
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var common = Common();
        var sound = new byte[8 + 60];
        var ordinary = FileOf(false, ("COMM", common), ("SSND", sound));
        using (var stream = new MemoryStream(FileOf(false, ("SSND", sound), ("COMM", common),
            ("NAME", "A title"u8.ToArray()), ("AUTH", "Author"u8.ToArray()), ("ANNO", "Line 1\nLine 2"u8.ToArray()))))
        {
            stream.Position = 5;
            var inventory = await AiffMetadata.ReadAsync(stream, default);
            inventory.RequireConversionSupport();
            check(inventory.Codec == "pcm_s16be" && inventory.SampleFrames == 10 && inventory.SampleRate == 48000 &&
                inventory.Channels == 3 && stream.Position == 5 && inventory.Tags["author"] == "Author" && inventory.Tags["comment"] == "Line 1\nLine 2",
                "AIFF inventory: sound-first framing, trailing metadata and caller position preserved");
        }
        foreach (var kind in new[] { "MARK", "INST", "COMT", "APPL", "CHAN", "JUNK", "abcd" })
        {
            var result = await Read(FileOf(false, ("COMM", common), (kind, new byte[3]), ("SSND", sound)));
            try { result.RequireConversionSupport(); check(false, "AIFF unsupported " + kind); }
            catch (NotSupportedException) { check(result.UnsupportedChunks.Contains(kind), "AIFF inventory: unhandled " + kind + " cannot disappear"); }
        }
        foreach (var text in new[] { "bad\0text"u8.ToArray(), Encoding.UTF8.GetBytes("Caf\u00e9"), new byte[] { 1 } })
            check(!(await Read(FileOf(false, ("COMM", common), ("SSND", sound), ("NAME", text)))).UnsupportedChunks.IsEmpty,
                "AIFF inventory: ambiguous legacy text is refused");
        await Reject(FileOf(false, ("COMM", common), ("SSND", sound), ("NAME", "One"u8.ToArray()), ("NAME", "Two"u8.ToArray())), "duplicate names");
        await Reject(ordinary.Concat(new byte[] { 0 }).ToArray(), "undeclared trailing bytes");
        await Reject(FileOf(false, ("COMM", common), ("COMM", common), ("SSND", sound)), "duplicate common");
        await Reject(FileOf(false, ("COMM", common), ("SSND", sound), ("SSND", sound)), "duplicate sound");
        await Reject(FileOf(false, ("COMM", common), ("SSND", sound), ("NAME", new byte[AiffMetadata.MaximumTextBytes + 1])), "text budget");
        var many = Enumerable.Range(0, AiffMetadata.MaximumChunks).Select(_ => ("abcd", Array.Empty<byte>()))
            .Prepend(("COMM", common)).Append(("SSND", sound)).ToArray();
        await Reject(FileOf(false, many), "chunk count budget");
        await Reject(FileOf(false, ("COMM", common), ("SSND", new byte[8 + 59])), "partial sample frame");
        var badCount = common.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(badCount.AsSpan(2), 11);
        await Reject(FileOf(false, ("COMM", badCount), ("SSND", sound)), "sample count mismatch");
        foreach (var rateKind in new[] { "negative", "fractional", "nan", "unnormalized", "zero", "too-large" })
        {
            var invalid = common.ToArray();
            switch (rateKind)
            {
                case "negative": invalid[8] |= 128; break;
                case "fractional": invalid[17] = 1; break;
                case "nan": invalid[8] = 127; invalid[9] = 255; break;
                case "unnormalized": invalid[10] &= 127; break;
                case "zero": invalid.AsSpan(8, 10).Clear(); break;
                default: invalid[9] += 5; break;
            }
            await Reject(FileOf(false, ("COMM", invalid), ("SSND", sound)), rateKind + " rate");
        }
        var offsetSound = new byte[8 + 7 + 60]; BinaryPrimitives.WriteUInt32BigEndian(offsetSound, 7);
        check((await Read(FileOf(false, ("COMM", common), ("SSND", offsetSound)))).SampleFrames == 10,
            "AIFF inventory: SSND byte offset is not decoded audio");
        BinaryPrimitives.WriteUInt32BigEndian(offsetSound, 999);
        await Reject(FileOf(false, ("COMM", common), ("SSND", offsetSound)), "out-of-extent sound offset");
        var blocked = sound.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(blocked.AsSpan(4), 512);
        check((await Read(FileOf(false, ("COMM", common), ("SSND", blocked)))).UnsupportedChunks.Contains("SSND/block alignment"),
            "AIFF inventory: block-aligned storage cannot be silently reinterpreted");
        var version = Convert.FromHexString("A2805140");
        foreach (var (name, bits, width, expected) in new[] { ("NONE", 16, 2, "pcm_s16be"), ("sowt", 16, 2, "pcm_s16le"),
            ("fl32", 32, 4, "pcm_f32be"), ("fl64", 64, 8, "pcm_f64be"), ("alaw", 8, 1, "pcm_alaw"), ("ulaw", 16, 1, "pcm_mulaw") })
        {
            var format = Common(bits).Concat(Encoding.ASCII.GetBytes(name)).Concat(new byte[2]).ToArray();
            var result = await Read(FileOf(true, ("FVER", version), ("COMM", format), ("SSND", new byte[8 + 30 * width])));
            check(result.Codec == expected && result.SampleFrames == 10, "AIFF-C inventory: codec-specific storage " + name);
            await Reject(FileOf(true, ("COMM", format), ("SSND", new byte[8 + 30 * width])), "missing AIFC version " + name);
        }
        var ima = Common(4); BinaryPrimitives.WriteUInt32BigEndian(ima.AsSpan(2), 2);
        var imaCommon = ima.Concat("ima4"u8.ToArray()).Concat(new byte[2]).ToArray();
        var imaResult = await Read(FileOf(true, ("FVER", version), ("COMM", imaCommon), ("SSND", new byte[8 + 34 * 3 * 2])));
        check(imaResult.SampleFrames == 128, "AIFF-C inventory: IMA4 packet-count convention retains every decoded frame");
        await Reject(FileOf(true, ("FVER", version), ("COMM", imaCommon), ("SSND", new byte[8 + 34 * 3 * 2 - 1])), "partial IMA4 packet");
        var unknown = Common().Concat("MAC3"u8.ToArray()).Concat(new byte[2]).ToArray();
        await Reject(FileOf(true, ("FVER", version), ("COMM", unknown), ("SSND", sound)), "unsupported AIFF-C codec");
        using (var cancelled = new MemoryStream(ordinary))
        {
            cancelled.Position = 3;
            try { await AiffMetadata.ReadAsync(cancelled, new CancellationToken(true)); check(false, "AIFF cancellation"); }
            catch (OperationCanceledException) { check(cancelled.Position == 3, "AIFF inventory: cancellation restores caller position"); }
        }
        foreach (var codec in new[] { "pcm_s8", "pcm_s16be", "pcm_s24be", "pcm_s32be", "pcm_f32be", "pcm_f64be", "pcm_alaw", "pcm_mulaw", "adpcm_ima_qt" })
        foreach (var target in Enum.GetValues<AudioFormat>())
        {
            var empty = ImmutableDictionary<string, string>.Empty;
            var facts = new AudioProbeFacts("aiff", 1, [new(0, "audio", codec, 48000, 2, null, null, null, "stereo", empty)], empty);
            var plan = AudioConversionPlan.Create(facts, target);
            check(!plan.AlreadyTarget && plan.Target == target && Enum.GetValues<AudioFormat>().Length == 6,
                "AIFF plan: input-only variant uses existing target " + codec + " / " + target);
            if (target == AudioFormat.Wave && codec == "pcm_f64be")
                check(plan.OutputCodec == "pcm_f64le" && plan.RequiresExactSamples, "AIFF float64 retains precision while changing WAV byte order");
            if (codec is "pcm_alaw" or "pcm_mulaw" or "adpcm_ima_qt" && target == AudioFormat.Mp3)
                check(plan.RequiredConsent.HasFlag(AudioConversionConsent.LossyTranscoding), "AIFF compressed source retains lossy-generation consent");
            if (codec == "pcm_alaw" && target == AudioFormat.Flac)
            {
                var declaredBits = facts with { Streams = [facts.Streams[0] with { SampleBits = 8 }] };
                var compressedPlan = AudioConversionPlan.Create(declaredBits, target);
                check(compressedPlan.SampleBits == 24 && !compressedPlan.Notices.Any(notice => notice.Contains("16-bit FLAC")),
                    "AIFF companded source describes its actual 24-bit output precision");
            }
        }

        async Task Reject(byte[] data, string name)
        {
            using var stream = new MemoryStream(data); stream.Position = 2;
            try { await AiffMetadata.ReadAsync(stream, default); check(false, "AIFF refuses " + name); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            { check(stream.Position == 2, "AIFF inventory: rejects " + name + " and restores caller position"); }
        }
    }

    private static async Task<AiffMetadataInventory> Read(byte[] data)
    { using var stream = new MemoryStream(data); return await AiffMetadata.ReadAsync(stream, default); }

    private static byte[] Common(int bits = 16)
    {
        var bytes = Convert.FromHexString("00030000000A0010400EBB80000000000000");
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)bits); return bytes;
    }

    private static byte[] FileOf(bool compressed, params (string Kind, byte[] Data)[] chunks)
    {
        using var output = new MemoryStream(); output.Write("FORM"u8); output.Write(new byte[4]); output.Write(compressed ? "AIFC"u8 : "AIFF"u8);
        foreach (var (kind, data) in chunks)
        {
            output.Write(Encoding.ASCII.GetBytes(kind)); var size = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(size, (uint)data.Length);
            output.Write(size); output.Write(data); if ((data.Length & 1) != 0) output.WriteByte(0);
        }
        var bytes = output.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)bytes.Length - 8); return bytes;
    }
}
