using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Analysis;

internal static class FlacDescriptionContracts
{
    public static void Run(Action<bool, string> check)
    {
        var comment = Comments("TITLE=Authored ü", "ARTIST=First", "artist=Second", "LYRICS=line one\nline two");
        var picture = Picture("image/png", "Cover ü", [1, 2, 3]);
        var header = FlacMetadata.Parse(FlacMetadataContracts.File((4, comment), (6, picture)));
        var details = FlacDescriptiveMetadata.Read(header);
        check(details.Vendor == "Context Suite fixture" && details.Comments.Length == 4 && details.Comments[2] == new FlacComment("artist", "Second"),
            "FLAC descriptions: duplicate case-insensitive names retain distinct ordered values");
        check(details.Comments[0].Value == "Authored ü" && details.Comments[3].Value.Contains('\n'), "FLAC descriptions: Unicode and multiline values survive");
        check(details.Pictures.Single() is { MediaType: "image/png", Description: "Cover ü", Width: 2, Height: 1, DataBytes: 3, IsLinked: false },
            "FLAC descriptions: picture declarations and payload identity are separate from decoding");
        check(FlacDescriptiveMetadata.Read(FlacMetadata.Parse(FlacMetadataContracts.File((6, Picture("-->", "Linked", "https://example.invalid/cover"u8.ToArray())))))
            .Pictures.Single().IsLinked, "FLAC descriptions: linked artwork is recognized without resolving it");
        Reject(Comments("BROKEN"), 4, "missing value separator");
        Reject(Comments("=empty-name"), 4, "empty name");
        Reject(Comments("Ü=not-ascii-name"), 4, "non-ASCII name");
        Reject(comment[..^1], 4, "truncated field");
        Reject(comment.Concat(new byte[] { 0 }).ToArray(), 4, "trailing field bytes");
        var invalidUtf8 = Comments("TITLE=x"); invalidUtf8[^1] = 0xff;
        Reject(invalidUtf8, 4, "invalid UTF-8");
        var hugeCount = Comments(); BinaryPrimitives.WriteUInt32LittleEndian(hugeCount.AsSpan(hugeCount.Length - 4), uint.MaxValue);
        Reject(hugeCount, 4, "huge comment count");
        Reject(Comments("LYRICS=" + new string('x', FlacDescriptiveMetadata.MaximumTextBytes)), 4, "aggregate text budget");
        Reject(Picture("image/ü", "Bad MIME", [1]), 6, "non-ASCII media type");
        Reject(picture[..^1], 6, "truncated picture data");
        Reject(picture.Concat(new byte[] { 0 }).ToArray(), 6, "trailing picture data");
        var reserved = picture.ToArray(); reserved[3] = 21;
        Reject(reserved, 6, "reserved picture type");
        var icon = picture.ToArray(); icon[3] = 2;
        try { FlacDescriptiveMetadata.Read(FlacMetadata.Parse(FlacMetadataContracts.File((6, icon), (6, icon)))); check(false, "FLAC descriptions: duplicate icons"); }
        catch (InvalidDataException) { check(true, "FLAC descriptions: duplicate icons"); }
        try { FlacDescriptiveMetadata.Read(FlacMetadata.Parse(FlacMetadataContracts.File(Enumerable.Repeat(((byte)6, picture), 32).ToArray()))); check(false, "FLAC descriptions: picture budget"); }
        catch (InvalidDataException) { check(true, "FLAC descriptions: picture budget"); }
        var facts = AudioProbeParser.Parse("""
            {"streams":[{"index":0,"codec_type":"audio","codec_name":"flac","sample_rate":"48000","channels":2,"bits_per_raw_sample":"16"},
            {"index":1,"codec_type":"video","codec_name":"png","disposition":{"attached_pic":1}}],"format":{"format_name":"flac"}}
            """u8.ToArray());
        var plan = AudioConversionPlan.CreateFlacOptimization(facts);
        check(plan is { AlreadyTarget: false, RequiresExactSamples: true, Policy: "flac-lossless-1" }, "FLAC optimization: attached artwork requires raw-preservation path");
        var roundtrip = JsonSerializer.Deserialize<AudioProbeFacts>(JsonSerializer.Serialize(facts))!;
        check(roundtrip.Streams[1].AttachedPicture, "audio facts: attached-picture disposition survives transport serialization");
        var report = AudioAnalysis.AddProbe(HeaderAnalyzer.Analyze("cover.flac", new byte[100], 100), facts, 100);
        check(report.Facts.Any(fact => fact.Id == "audio.probe.stream.1.artwork" && fact.Text == "Embedded artwork") &&
            report.Facts.All(fact => fact.Id != "audio.probe.stream.1.rate"), "audio analysis: artwork is named clearly without irrelevant audio fields");
        try { AudioConversionPlan.Create(facts, AudioFormat.Wave); check(false, "audio conversion: artwork cannot be silently discarded"); }
        catch (NotSupportedException) { check(true, "audio conversion: artwork cannot be silently discarded"); }
        try { AudioConversionPlan.CreateFlacOptimization(facts with { Streams = [facts.Streams[0], facts.Streams[1] with { AttachedPicture = false }] }); check(false, "FLAC optimization: ordinary video is not an attachment"); }
        catch (NotSupportedException) { check(true, "FLAC optimization: ordinary video is not an attachment"); }
        foreach (var disposition in new[] { "2", "-1", "true", "{}" })
        {
            var json = "{\"streams\":[{\"index\":0,\"codec_type\":\"video\",\"disposition\":{\"attached_pic\":" + disposition + "}}],\"format\":{}}";
            try { AudioProbeParser.Parse(Encoding.UTF8.GetBytes(json)); check(false, "audio facts: invalid attachment flag"); }
            catch (InvalidDataException) { check(true, "audio facts: invalid attachment flag"); }
        }
        void Reject(byte[] data, byte type, string name)
        {
            try { FlacDescriptiveMetadata.Read(FlacMetadata.Parse(FlacMetadataContracts.File((type, data)))); check(false, "FLAC descriptions: " + name); }
            catch (InvalidDataException) { check(true, "FLAC descriptions: " + name); }
        }
    }

    private static byte[] Comments(params string[] fields)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        var vendor = Encoding.UTF8.GetBytes("Context Suite fixture"); writer.Write(vendor.Length); writer.Write(vendor); writer.Write(fields.Length);
        foreach (var field in fields) { var bytes = Encoding.UTF8.GetBytes(field); writer.Write(bytes.Length); writer.Write(bytes); }
        return stream.ToArray();
    }

    private static byte[] Picture(string media, string description, byte[] data)
    {
        using var stream = new MemoryStream();
        void Number(uint value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, value); stream.Write(bytes); }
        void Text(string value) { var bytes = Encoding.UTF8.GetBytes(value); Number((uint)bytes.Length); stream.Write(bytes); }
        Number(3); Text(media); Text(description); Number(2); Number(1); Number(24); Number(0); Number((uint)data.Length); stream.Write(data);
        return stream.ToArray();
    }
}
