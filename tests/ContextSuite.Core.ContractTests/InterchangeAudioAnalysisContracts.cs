using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;

internal static class InterchangeAudioAnalysisContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        Directory.CreateDirectory(scratch);
        foreach (var (name, bytes, id) in new[] { ("plain", Aiff(), "aiff"), ("compressed", Aiff("MAC3"), "aiff"), ("sun", Au(), "au") })
        {
            var result = Read(bytes);
            check(result.Identity.FormatId == id && result.Identity.Basis == IdentificationBasis.Content && result.Identity.Confidence == IdentificationConfidence.Likely &&
                Number(result, "audio.channels") == 2 && Number(result, "audio.sample-bits") == 16 && Number(result, "audio.duration-ms") == 2,
                "interchange audio: bounded declared channels/precision/timing for " + name);
            check(!AudioAnalysis.CanProbe(result, bytes), "interchange audio: header recognition grants no optional worker capability for " + name);
            var prefixes = true;
            for (var length = 0; length < bytes.Length; length++)
            {
                var prefix = HeaderAnalyzer.Analyze("unknown", bytes.AsSpan(0, length), length);
                prefixes &= prefix.InspectedBytes == length && prefix.Identity.Confidence != IdentificationConfidence.Confirmed;
            }
            check(prefixes, "interchange audio: every truncation remains bounded and unconfirmed for " + name);
            var path = Path.Combine(scratch, name + ".png");
            await File.WriteAllBytesAsync(path, bytes);
            var modified = File.GetLastWriteTimeUtc(path);
            var actual = await FileAnalysisReader.ReadAsync(path, default);
            check(actual.Identity.FormatId == id && actual.Warnings.Any(w => w.Contains("different type")) &&
                File.GetLastWriteTimeUtc(path) == modified && SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(SHA256.HashData(bytes)),
                "interchange audio: real reader preserves original and overrides misleading suffix for " + name);
        }
        var aiff = Aiff();
        check(Read(aiff[..38], aiff.Length).Facts.Any(f => f.Id == "audio.sample-frames" && f.Integer == 96) &&
            Read(aiff[..37], aiff.Length).Facts.All(f => f.Id != "audio.channels"),
            "interchange audio: complete Common Chunk supports declarations without reading samples");
        var rate = Read(aiff).Facts.Single(f => f.Id == "audio.sample-rate");
        check(rate.Text == "48000" && rate.Availability == FactAvailability.Derived && Number(Read(aiff), "audio.sample-frames") == 96,
            "interchange audio: AIFF extended rate is explicitly approximate and frames are per channel");
        Convert.FromHexString("4000A000000000000000").CopyTo(aiff, 28);
        check(Read(aiff).Facts.Single(f => f.Id == "audio.sample-rate").Text == "2.5" && Number(Read(aiff), "audio.duration-ms") == 38400,
            "interchange audio: fractional AIFF sample rate is not rounded to an integer");
        foreach (var raw in new[] { "00000000000000000000", "8000A000000000000000", "7FFF8000000000000000", "40000000000000000001", "00000000000000000001", "7FFEFFFFFFFFFFFFFFFF" })
        {
            aiff = Aiff(); Convert.FromHexString(raw).CopyTo(aiff, 28);
            check(Read(aiff).Facts.Single(f => f.Id == "audio.sample-rate").Availability == FactAvailability.Unavailable &&
                Read(aiff).Facts.Single(f => f.Id == "aiff.sample-rate-80").Text == raw,
                "interchange audio: zero/negative/nonfinite/unsupported extended rate retains raw value " + raw);
        }
        check(Read(Aiff("NONE")).Facts.Single(f => f.Id == "audio.codec").Text!.Contains("integer PCM") &&
            Read(Aiff("MAC3")).Facts.Single(f => f.Id == "audio.codec").Text == "Not interpreted",
            "interchange audio: AIFF-C raw compression code does not infer a decoder");
        var aifc = Aiff("NONE"); aifc[38] = 0;
        check(Read(aifc).Facts.Single(f => f.Id == "aiff.compression").Text == "0x004F4E45",
            "interchange audio: nonprintable compression identifiers remain safe hex text");
        aifc = Aiff("NONE"); aifc[42] = 255;
        check(Read(aifc).Facts.All(f => f.Id != "audio.channels"), "interchange audio: out-of-chunk compression name invalidates Common facts");
        foreach (var (offset, value) in new[] { (4, uint.MaxValue), (16, uint.MaxValue), (20, 0xFFFF0060u) })
        {
            aiff = Aiff(); U32(aiff, offset, value);
            check(Read(aiff).Facts.All(f => f.Id != "audio.channels") && !Read(aiff).Warnings.IsEmpty,
                "interchange audio: inconsistent FORM/chunk/channel declarations cannot publish audio properties " + offset);
        }
        aiff = Aiff(); aiff[26] = 0; aiff[27] = 0;
        check(Read(aiff).Facts.All(f => f.Id != "audio.channels"), "interchange audio: zero sample precision is refused");
        var common = Aiff()[12..38];
        check(Read(Form(common.Concat(common).ToArray())).Facts.All(f => f.Id != "audio.channels"),
            "interchange audio: duplicate Common Chunks discard earlier facts");
        check(Number(Read(Form(Chunk("JUNK", [1]).Concat(common).ToArray())), "audio.channels") == 2,
            "interchange audio: odd unknown chunks honor padding before Common");
        var tooMany = Enumerable.Range(0, 256).SelectMany(_ => Chunk("JUNK", [])).Concat(common).ToArray();
        check(Read(Form(tooMany)).Facts.All(f => f.Id != "audio.channels") && Read(Form(tooMany)).Warnings.Any(w => w.Contains("record limit")),
            "interchange audio: record budget stops before a 257th chunk");
        var formOnly = Form([]); "8SVX"u8.CopyTo(formOnly.AsSpan(8));
        check(Read(formOnly).Identity.FormatId != "aiff", "interchange audio: unrelated IFF form is not identified as AIFF");
        var trailing = Aiff().Concat(new byte[4]).ToArray();
        check(Read(trailing).Warnings.Any(w => w.Contains("follow")), "interchange audio: trailing bytes are qualified");

        var au = Au();
        check(Number(Read(au[..24], au.Length), "audio.sample-frames") == 96, "interchange audio: AU PCM timing needs only its fixed header");
        foreach (var (code, bits) in new[] { (2u, 8), (3u, 16), (4u, 24), (5u, 32), (6u, 32), (7u, 64) })
        {
            au = Au(code, bits);
            check(Number(Read(au), "audio.sample-bits") == bits && Number(Read(au), "audio.duration-ms") == 2 &&
                Read(au).Facts.Single(f => f.Id == "audio.codec").Text!.Contains(code >= 6 ? "floating-point" : "integer"),
                "interchange audio: AU fixed-width PCM declaration " + code);
        }
        au = Au(); U32(au, 8, uint.MaxValue);
        check(Read(au).Facts.Single(f => f.Id == "au.data-bytes").Availability == FactAvailability.Derived && Number(Read(au), "audio.duration-ms") == 2,
            "interchange audio: unspecified AU data extent derives from actual file length");
        U32(au, 12, 2); U32(au, 16, 1); U32(au, 20, 1);
        check(Read(au[..24], long.MaxValue).Facts.Single(f => f.Id == "audio.duration-ms").Availability == FactAvailability.Unavailable,
            "interchange audio: enormous unknown extent cannot overflow duration arithmetic");
        foreach (var (offset, value) in new[] { (4, 23u), (4, uint.MaxValue), (8, 100000u), (16, 0u), (20, 0u) })
        {
            au = Au(); U32(au, offset, value);
            check(Read(au).Facts.All(f => f.Id != "audio.channels"), "interchange audio: invalid AU offset/extent/rate/channels " + offset + "/" + value);
        }
        au = Au(); U32(au, 12, 10);
        check(Read(au).Facts.Single(f => f.Id == "audio.duration-ms").Availability == FactAvailability.Unavailable && Number(Read(au), "au.encoding") == 10,
            "interchange audio: DSP/unknown AU encoding remains a raw identifier without timing or execution");
        au = Au(); U32(au, 8, 383);
        check(Read(au).Facts.Single(f => f.Id == "audio.duration-ms").Availability == FactAvailability.Unavailable && Read(au).Warnings.Any(w => w.Contains("whole number")),
            "interchange audio: partial AU sample frame has no duration");
        var random = new Random(81254);
        for (var iteration = 0; iteration < 500; iteration++)
        {
            var mutated = iteration % 2 == 0 ? Aiff() : Au();
            mutated[random.Next(mutated.Length)] ^= (byte)random.Next(1, 256);
            _ = Read(mutated);
        }
        check(true, "interchange audio: 500 deterministic header/payload mutations return bounded results");
    }

    private static FileAnalysis Read(byte[] bytes, long? length = null) => HeaderAnalyzer.Analyze("unknown", bytes, length ?? bytes.Length);
    private static long? Number(FileAnalysis report, string id) => report.Facts.SingleOrDefault(f => f.Id == id)?.Integer;
    private static void U32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);

    private static byte[] Au(uint encoding = 3, int bits = 16)
    {
        var bytes = new byte[24 + 96 * 2 * bits / 8];
        ".snd"u8.CopyTo(bytes); U32(bytes, 4, 24); U32(bytes, 8, (uint)bytes.Length - 24);
        U32(bytes, 12, encoding); U32(bytes, 16, 48000); U32(bytes, 20, 2);
        return bytes;
    }

    private static byte[] Aiff(string? compression = null)
    {
        var common = new byte[compression is null ? 18 : 24];
        BinaryPrimitives.WriteInt16BigEndian(common, 2); U32(common, 2, 96);
        BinaryPrimitives.WriteInt16BigEndian(common.AsSpan(6), 16);
        Convert.FromHexString("400EBB80000000000000").CopyTo(common, 8);
        if (compression is not null) Encoding.ASCII.GetBytes(compression).CopyTo(common, 18);
        return Form(Chunk("COMM", common).Concat(Chunk("SSND", new byte[8 + 384])).ToArray(), compression is not null);
    }

    private static byte[] Form(byte[] chunks, bool compressed = false)
    {
        var bytes = new byte[12 + chunks.Length];
        "FORM"u8.CopyTo(bytes); U32(bytes, 4, (uint)bytes.Length - 8);
        Encoding.ASCII.GetBytes(compressed ? "AIFC" : "AIFF").CopyTo(bytes, 8); chunks.CopyTo(bytes, 12);
        return bytes;
    }

    private static byte[] Chunk(string id, byte[] contents)
    {
        var bytes = new byte[8 + contents.Length + (contents.Length & 1)];
        Encoding.ASCII.GetBytes(id).CopyTo(bytes, 0); U32(bytes, 4, (uint)contents.Length); contents.CopyTo(bytes, 8);
        return bytes;
    }
}
