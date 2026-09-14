using System.Buffers.Binary;
using System.Collections.Immutable;
using ContextSuite.Core.Audio;

internal static class OggPictureReaderContracts
{
    internal static void Run(Action<bool, string> check)
    {
        var first = FlacDescriptionContracts.Picture("image/png", "Front \u00fc", [0, 1, 255]);
        var second = FlacDescriptionContracts.Picture("image/jpeg", "Back", [255, 216, 255]);
        BinaryPrimitives.WriteUInt32BigEndian(second, 4);
        var list = new VorbisCommentList("Fixture vendor", [new("TITLE", "Retained"), Field(first, "metadata_block_picture"), Field(second)]);
        var pictures = OggPictureComments.Read(list);
        check(pictures.Length == 2 && pictures[0].Data.AsSpan().SequenceEqual(first) && pictures[1].Data.AsSpan().SequenceEqual(second),
            "Ogg picture source: canonical fields retain all bytes, case-insensitive names and order");
        var inventory = new OggMetadataInventory("opus", 48000, 2, 312, 0, 96312, list);
        check(inventory.ConversionTags(pictures).Count == 1 && inventory.ConversionTags(pictures)["title"] == "Retained",
            "Ogg picture source: pictures stay separate from descriptive audio tags");
        Reject(() => (inventory with { OutputGain = 1 }).ConversionTags(pictures), "Opus output gain remains refused");
        Reject(() => (inventory with { Descriptions = list with { Comments = list.Comments.Add(new("R128_TRACK_GAIN", "-12")) } }).ConversionTags(pictures), "gain comments remain refused");
        Reject(() => (inventory with { Descriptions = list with { Comments = list.Comments.Add(new("COVERART", "Extra")) } }).ConversionTags(pictures), "legacy cover art cannot disappear");
        Reject(() => (inventory with { Descriptions = list with { Comments = list.Comments.Add(new("TITLE", "Other")) } }).ConversionTags(pictures), "duplicate audio tags remain refused");
        var blank = new VorbisCommentList("Vendor", []);
        check(OggPictureComments.Read(blank).IsEmpty, "Ogg picture source: ordinary comments do not invent pictures");
        Reject(() => OggPictureComments.Read(blank with { Comments = default }), "uninitialized inventory");
        foreach (var value in new[] { "", "not base64!", " " + Convert.ToBase64String(first), Convert.ToBase64String(first) + "\n", "AA==" })
            Reject(() => Read(value), "empty/malformed/noncanonical picture encoding");
        var canonical = Convert.ToBase64String(FlacDescriptionContracts.Picture("image/png", "Padded!", [255]));
        var padding = canonical.IndexOf('=');
        if (padding < 0) throw new Exception("Expected a padding fixture.");
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        var changed = canonical.ToCharArray(); changed[padding - 1] = alphabet[alphabet.IndexOf(changed[padding - 1]) + 1];
        var noncanonical = new string(changed);
        check(Convert.FromBase64String(noncanonical).AsSpan().SequenceEqual(Convert.FromBase64String(canonical)), "Ogg picture source: authored nonzero pad bits decode to same bytes");
        Reject(() => Read(noncanonical), "nonzero pad bits");
        Reject(() => Read(canonical[..padding]), "missing padding");
        Reject(() => Read(Convert.ToBase64String(FlacDescriptionContracts.Picture("-->", "Linked", "https://example.invalid/cover"u8.ToArray()))), "linked images");
        var reserved = first.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(reserved, 21);
        Reject(() => Read(Convert.ToBase64String(reserved)), "reserved picture type");
        var icon = first.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(icon, 2);
        Reject(() => OggPictureComments.Read(new("Vendor", [Field(icon), Field(icon)])), "duplicate icon types");
        var fields = Enumerable.Repeat(Field(first), FlacDescriptiveMetadata.MaximumPictures).ToImmutableArray();
        check(OggPictureComments.Read(new("Vendor", fields)).Length == 31, "Ogg picture source: maximum picture count retains order even with repeated covers");
        Reject(() => OggPictureComments.Read(new("Vendor", fields.Add(Field(second)))), "picture count bound");
        Reject(() => Read(new string('A', VorbisComments.MaximumPictureTextBytes)), "aggregate field budget includes its name");
        Reject(() => OggPictureComments.Read(new("Vendor", Enumerable.Repeat(new AudioComment("X", "Y"), VorbisComments.MaximumComments + 1).ToImmutableArray())), "total comment count bound");
        foreach (var length in new[] { 0, 1, 7, 20, first.Length - 1 })
            Reject(() => Read(Convert.ToBase64String(first[..length])), "truncated complete picture structure " + length);
        Reject(() => Read(Convert.ToBase64String([..first, 0])), "trailing picture bytes");

        ImmutableArray<FlacMetadataBlock> Read(string value) => OggPictureComments.Read(new("Vendor", [new("METADATA_BLOCK_PICTURE", value)]));
        void Reject(Action action, string name)
        {
            try { action(); check(false, "Ogg picture source refuses " + name); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { check(true, "Ogg picture source refuses " + name); }
        }
    }
    private static AudioComment Field(byte[] data, string name = "METADATA_BLOCK_PICTURE") => new(name, Convert.ToBase64String(data));
}
