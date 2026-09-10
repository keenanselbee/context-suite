using ContextSuite.Core.Audio;

internal static class FlacConversionMetadataContracts
{
    public static void Run(Action<bool, string> check)
    {
        var tags = Read("TITLE=Unicode \u00fc \ud83d\ude00", "DESCRIPTION=Line one\r\nLine two\n\tend\\",
            "ALBUMARTIST=Artist", "TRACKNUMBER=2/9", "DISCNUMBER=1", "DISCSUBTITLE=Disc", "ENCODER=Old encoder");
        check(tags.Count == 6 && tags["title"] == "Unicode \u00fc \ud83d\ude00" && tags["comment"] == "Line one\r\nLine two\n\tend\\",
            "FLAC conversion: exact Unicode and multiline values survive inventory");
        check(tags["album_artist"] == "Artist" && tags["track"] == "2/9" && tags["disc"] == "1" && tags["disc_subtitle"] == "Disc",
            "FLAC conversion: comment aliases have explicit canonical names");
        AudioCommentConversion.RequireTags(tags, tags);
        check(true, "FLAC conversion: unchanged descriptive values validate");
        check(AudioCommentConversion.Read(new[] { "ENCODER", "major_brand", "minor_version", "compatible_brands", "handler_name", "vendor_id" }
            .Select(key => new AudioComment(key, "Technical provenance"))).IsEmpty,
            "Audio conversion: raw inventory and native probes share technical provenance exclusions");
        foreach (var changed in new[] { tags.Remove("title"), tags.SetItem("title", "Changed") })
            Reject(() => AudioCommentConversion.RequireTags(tags, changed), "missing or changed output comment");
        foreach (var fields in new[] { new[] { "ARTIST=First", "artist=Second" }, new[] { "DESCRIPTION=One", "COMMENT=Two" },
            new[] { "TRACK=1", "TRACKNUMBER=1" } })
            Reject(() => Read(fields), "duplicate or aliased comments");
        foreach (var key in new[] { "CUESHEET", "METADATA_BLOCK_PICTURE", "COVERART", "COVERARTMIME", "CHAPTER001", "LOOPSTART", "REPLAYGAIN_TRACK_GAIN", "R128_TRACK_GAIN" })
            Reject(() => Read(key + "=Value"), "semantic comment " + key);
        foreach (var field in new[] { "TITLE=contains\0nul", "TITLE=" + new string('x', 4097), new string('k', 129) + "=Value" })
            Reject(() => Read(field), "transport limit");
        Reject(() => Read(Enumerable.Range(0, 129).Select(i => "FIELD" + i + "=Value").ToArray()), "tag count limit");
        var header = FlacMetadata.Parse(FlacMetadataContracts.File((4, FlacDescriptionContracts.Comments("TITLE=Safe"))));
        foreach (byte type in new byte[] { 2, 5, 6, 7 })
            Reject(() => FlacConversionMetadata.Read(header with { Blocks = header.Blocks.Add(new(type, [])) }), "unmapped block " + type);
        check(FlacConversionMetadata.Read(header with { Blocks = header.Blocks.Add(new(1, [])).Add(new(3, [])) })["title"] == "Safe",
            "FLAC conversion: padding and separately validated seek metadata do not change descriptive values");

        static System.Collections.Immutable.ImmutableDictionary<string, string> Read(params string[] fields) =>
            FlacConversionMetadata.Read(FlacMetadata.Parse(FlacMetadataContracts.File((4, FlacDescriptionContracts.Comments(fields)))));
        void Reject(Action action, string name)
        {
            try { action(); check(false, "FLAC conversion rejects " + name); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException) { check(true, "FLAC conversion rejects " + name); }
        }
    }
}
