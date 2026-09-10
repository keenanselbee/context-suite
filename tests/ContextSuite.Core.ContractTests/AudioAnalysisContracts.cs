using System.Buffers.Binary;
using ContextSuite.Core.Analysis;

internal static class AudioAnalysisContracts
{
    public static void Run(string scratch, Action<bool, string> check)
    {
        var wave = Wave();
        var result = Read(wave);
        check(result.Identity is { FormatId: "wave", Confidence: IdentificationConfidence.Likely } &&
            Number(result, "audio.channels") == 2 && Number(result, "audio.sample-rate") == 48000 &&
            Number(result, "audio.sample-bits") == 16 && Number(result, "wave.first-data-frames") == 480 &&
            Number(result, "wave.first-data-duration-ms") == 10,
            "audio analysis: WAVE PCM timing derives from declared frame count without decoding");
        check(Read(wave[..44], wave.Length).Facts.Any(fact => fact.Id == "wave.first-data-duration-ms") &&
            Read(wave[..30], wave.Length).Facts.All(fact => fact.Id != "audio.sample-rate"),
            "audio analysis: unread data may have declared duration; incomplete fmt cannot supply properties");
        var junk = Wave(junk: true);
        check(Number(Read(junk), "audio.channels") == 2,
            "audio analysis: WAVE unknown odd-size chunk respects padding before fmt");
        var inconsistent = Wave(); BinaryPrimitives.WriteUInt32LittleEndian(inconsistent.AsSpan(28), 1);
        check(Read(inconsistent).Facts.All(fact => fact.Id != "audio.channels") && !Read(inconsistent).Warnings.IsEmpty,
            "audio analysis: contradictory PCM byte rate cannot produce trusted timing");
        var huge = Wave(); BinaryPrimitives.WriteUInt32LittleEndian(huge.AsSpan(16), uint.MaxValue);
        check(Read(huge).Facts.All(fact => fact.Id != "audio.channels") && !Read(huge).Warnings.IsEmpty,
            "audio analysis: hostile WAVE chunk length cannot overflow or allocate");
        var beyond = Wave(); BinaryPrimitives.WriteUInt32LittleEndian(beyond.AsSpan(4), uint.MaxValue);
        check(Read(beyond).Facts.All(fact => fact.Id != "audio.channels"),
            "audio analysis: RIFF beyond the actual file is incomplete");
        var shortData = Wave(); Array.Resize(ref shortData, shortData.Length - 1);
        check(Read(shortData).Facts.All(fact => fact.Id != "wave.first-data-duration-ms"),
            "audio analysis: truncated WAVE payload cannot claim declared duration");
        var compressed = Wave(); BinaryPrimitives.WriteUInt16LittleEndian(compressed.AsSpan(20), 2);
        check(Read(compressed).Facts.All(fact => fact.Id != "wave.first-data-duration-ms") &&
            Read(compressed).Facts.Single(fact => fact.Id == "audio.sample-bits").Availability == FactAvailability.Unavailable,
            "audio analysis: compressed WAVE byte rates do not imply PCM duration or precision");
        var extended = Wave(extensible: true);
        check(Number(Read(extended), "audio.sample-bits") == 32 && Number(Read(extended), "audio.valid-bits") == 24 &&
            Number(Read(extended), "wave.channel-mask") == 3,
            "audio analysis: extensible WAVE separates valid precision, container width and speaker mask");
        extended[59] ^= 1;
        check(Read(extended).Facts.All(fact => fact.Id != "wave.first-data-duration-ms"),
            "audio analysis: unknown full subformat GUID cannot masquerade as PCM");
        extended = Wave(extensible: true); extended[38] = 33;
        check(Read(extended).Facts.All(fact => fact.Id != "audio.valid-bits"),
            "audio analysis: precision cannot exceed sample container width");
        var floating = Wave(extensible: true); floating[44] = 3; floating[38] = 32;
        check(Read(floating).Facts.Single(fact => fact.Id == "audio.codec").Text == "IEEE floating-point PCM",
            "audio analysis: IEEE floating point is distinguished from integer PCM");

        var flac = Flac();
        result = Read(flac);
        check(result.Identity is { FormatId: "flac", Confidence: IdentificationConfidence.Likely } &&
            Number(result, "audio.channels") == 2 && Number(result, "audio.sample-rate") == 48000 &&
            Number(result, "audio.sample-bits") == 24 && Number(result, "audio.sample-frames") == 96000 &&
            Number(result, "audio.duration-ms") == 2000,
            "audio analysis: FLAC STREAMINFO declarations remain distinct from validated audio");
        check(result.Facts.Single(fact => fact.Id == "flac.md5-present").Boolean == false &&
            result.Facts.Single(fact => fact.Id == "flac.metadata-list-complete").Boolean == true,
            "audio analysis: zero FLAC checksum is not claimed verified");
        result = Read(Flac(samples: 0));
        check(result.Facts.Single(fact => fact.Id == "audio.sample-frames").Availability == FactAvailability.Unknown &&
            result.Facts.Single(fact => fact.Id == "audio.duration-ms").Availability == FactAvailability.Unavailable,
            "audio analysis: FLAC zero total means unknown rather than empty audio");
        result = Read(Flac(rate: 0));
        check(result.Facts.Single(fact => fact.Id == "audio.duration-ms").Integer is null && !result.Warnings.IsEmpty,
            "audio analysis: FLAC zero rate may represent non-audio and has no duration");
        result = Read(Flac(rate: 1, samples: 0xfffffffffUL));
        check(Number(result, "audio.duration-ms") == 68719476735000,
            "audio analysis: maximum FLAC sample count does not overflow timing");
        foreach (var offset in new[] { 4, 7, 8, 9, 10, 11, 20 })
        {
            var malformed = Flac();
            if (offset == 4) malformed[offset] = 0x81;
            else if (offset == 7) malformed[offset] = 33;
            else if (offset == 20) { malformed[20] &= 0xfe; malformed[21] &= 0x0f; }
            else { malformed[offset is 8 or 9 ? 8 : 10] = 0; malformed[offset is 8 or 9 ? 9 : 11] = 0; }
            check(Read(malformed).Facts.All(fact => fact.Id != "audio.channels"),
                "audio analysis: invalid FLAC STREAMINFO field rejected at " + offset);
        }
        var metadata = Flac(); metadata[4] = 0;
        metadata = metadata.Concat(new byte[] { 4, 0, 0, 0, 0x86, 0, 0, 0 }).ToArray();
        result = Read(metadata);
        check(Number(result, "flac.comment-blocks") == 1 && Number(result, "flac.picture-blocks") == 1 &&
            result.Facts.All(fact => !fact.Id.StartsWith("audio.tags")),
            "audio analysis: FLAC metadata block observations do not validate embedded content");
        metadata[^1] = 255;
        check(Read(metadata).Facts.Single(fact => fact.Id == "flac.metadata-list-complete").Boolean == false,
            "audio analysis: oversized FLAC metadata cannot imply absent unobserved artwork");
        var repeated = Flac(); repeated[4] = 0;
        repeated = repeated.Concat(Flac()[4..]).ToArray();
        check(Read(repeated).Warnings.Any(warning => warning.Contains("malformed")),
            "audio analysis: second FLAC STREAMINFO is malformed");
        var many = Flac(); many[4] = 0;
        many = many.Concat(Enumerable.Range(0, 300).SelectMany(_ => new byte[] { 1, 0, 0, 0 })).ToArray();
        check(Read(many).Facts.Single(fact => fact.Id == "flac.metadata-list-complete").Boolean == false,
            "audio analysis: zero-length FLAC blocks still consume the record budget");

        foreach (var fixture in new[] { wave, junk, Wave(extensible: true), flac, metadata })
        {
            for (var length = 0; length < Math.Min(fixture.Length, 90); length++)
            {
                Read(fixture[..length]);
                Read(fixture[..length], fixture.Length);
            }
        }
        var random = new Random(8241);
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var mutated = (attempt % 2 == 0 ? wave : flac).ToArray();
            for (var changes = 0; changes < 4; changes++) mutated[random.Next(mutated.Length)] = (byte)random.Next(256);
            Read(mutated);
        }
        check(true, "audio analysis: truncated headers and deterministic hostile mutations complete without parser exceptions");
        Directory.CreateDirectory(scratch);
        File.WriteAllBytes(Path.Combine(scratch, "audio-header-silence.wav"), wave);
        File.WriteAllBytes(Path.Combine(scratch, "audio-header-streaminfo-only.flac"), flac);
    }

    private static FileAnalysis Read(byte[] bytes, long? length = null)
    {
        return HeaderAnalyzer.Analyze("fixture.unknown", bytes, length ?? bytes.Length);
    }

    private static long? Number(FileAnalysis result, string id)
    {
        return result.Facts.Single(fact => fact.Id == id).Integer;
    }

    private static byte[] Wave(bool extensible = false, bool junk = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(0); writer.Write("WAVE"u8);
        if (junk) { writer.Write("JUNK"u8); writer.Write(1); writer.Write((byte)7); writer.Write((byte)0); }
        writer.Write("fmt "u8); writer.Write(extensible ? 40 : 16);
        writer.Write((ushort)(extensible ? 0xfffe : 1)); writer.Write((ushort)2); writer.Write(48000);
        writer.Write(extensible ? 384000 : 192000); writer.Write((ushort)(extensible ? 8 : 4));
        writer.Write((ushort)(extensible ? 32 : 16));
        if (extensible)
        {
            writer.Write((ushort)22); writer.Write((ushort)24); writer.Write(3);
            writer.Write(new Guid("00000001-0000-0010-8000-00aa00389b71").ToByteArray());
        }
        writer.Write("data"u8); writer.Write(1920); writer.Write(new byte[1920]);
        var bytes = stream.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length - 8);
        return bytes;
    }

    private static byte[] Flac(uint rate = 48000, ulong samples = 96000)
    {
        var bytes = new byte[42]; "fLaC"u8.CopyTo(bytes); bytes[4] = 128; bytes[7] = 34;
        bytes[8] = 0x10; bytes[10] = 0x10;
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(18), ((ulong)rate << 44) | (1UL << 41) | (23UL << 36) | samples);
        return bytes;
    }
}
