using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using ContextSuite.Core.Audio;

namespace ContextSuite.Core.ContractTests;

internal static class AsfMetadataContracts
{
    private const string Header = "75b22630-668e-11cf-a6d9-00aa0062ce6c", Data = "75b22636-668e-11cf-a6d9-00aa0062ce6c";
    private const string Properties = "8cabdca1-a947-11cf-8ee4-00c00c205365", Stream = "b7dc0791-a9b7-11cf-8ee6-00c00c205365";
    private const string Extension = "5fbf03b5-a92e-11cf-8ee3-00c00c205365", ExtendedContent = "d2d0a440-e307-11d2-97f0-00a0c95ea850";
    private const string Content = "75b22633-668e-11cf-a6d9-00aa0062ce6c", Library = "44231c94-9498-49d1-a141-1d134e457054";
    private static readonly Guid FileId = new("99f0e9f8-4daf-4457-a988-9b75a7eaf5c7");

    internal static async Task RunAsync(Action<bool, string> check)
    {
        var original = FileOf();
        using (var source = new MemoryStream(original))
        {
            source.Position = 7;
            var result = await AsfMetadata.ReadAsync(source, default); result.RequireConversionSupport();
            check(result.Codec == "wmav2" && result.SampleRate == 48000 && result.Channels == 2 && result.Packets == 2 &&
                result.PacketBytes == 128 && result.Tags.IsEmpty && result.Pictures.IsEmpty && source.Position == 7,
                "ASF inventory: one local audio stream with matching packet extent and preserved caller position");
        }
        foreach (var (tag, codec) in new[] { (0x160, "wmav1"), (0x161, "wmav2"), (0x162, "wmapro"), (0x163, "wmalossless"), (0x0a, "wmavoice") })
            check((await Read(FileOf(format: (ushort)tag))).Codec == codec, "ASF inventory: declared codec identification only " + codec);
        const string bitrateId = "7bf875ce-468d-11d1-8d82-006097c9a2b2", streamId = "14e6a5cb-c672-4332-8399-a96952065b5a",
            languageId = "7c4346a9-efe0-4bfc-b229-393ede415c85";
        var streamProperties = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(streamProperties.AsSpan(16), 8000);
        BinaryPrimitives.WriteUInt16LittleEndian(streamProperties.AsSpan(48), 1);
        var languages = new byte[] { 1, 0, 12 }.Concat(Text("en-us")).ToArray();
        var bitrate = new byte[] { 1, 0, 1, 0, 64, 31, 0, 0 };
        byte[] StreamFixture(byte[] streamBody, byte[]? languageBody = null, byte[]? rates = null) =>
            FileOf(extra: Object(bitrateId, rates ?? bitrate), extension: Object(languageId, languageBody ?? languages).Concat(Object(streamId, streamBody)).ToArray());
        var streamInventory = await Read(StreamFixture(streamProperties)); streamInventory.RequireConversionSupport();
        check(streamInventory.Tags["language"] == "en-us", "ASF extended stream language is retained as descriptive metadata");
        foreach (var offset in new[] { 48, 50, 60, 62 })
        {
            var changed = streamProperties.ToArray(); changed[offset] = 2;
            await Reject(StreamFixture(changed), "extended stream identity/language/name/payload " + offset);
        }
        var reservedStream = streamProperties.ToArray(); reservedStream[47] = 128;
        await Reject(StreamFixture(reservedStream), "extended reserved flags");
        await Reject(StreamFixture(streamProperties[..63]), "truncated extended stream");
        await Reject(StreamFixture(streamProperties.Concat(new byte[] { 0 }).ToArray()), "unhandled embedded stream");
        await Reject(StreamFixture(streamProperties, [1, 0, 20, 0]), "truncated language");
        await Reject(StreamFixture(streamProperties, new byte[] { 1, 0, 12 }.Concat(Text("en/us")).ToArray()), "invalid language syntax");
        await Reject(StreamFixture(streamProperties, rates: [1, 0, 2, 0, 64, 31, 0, 0]), "bitrate stream mismatch");
        await Reject(FileOf(extension: Object(languageId, languages)), "unassociated language list");
        var provenance = await Read(FileOf(extra: Object(ExtendedContent, Descriptors(("IsVBR", (ushort)2, new byte[4]),
            ("MediaFoundationVersion", (ushort)0, Text("2.112"))))));
        provenance.RequireConversionSupport();
        check(provenance.Tags.IsEmpty, "ASF encoder version and bitrate mode are validated technical provenance");
        await Reject(FileOf(extra: Object(ExtendedContent, Descriptors(("IsVBR", (ushort)2, [2, 0, 0, 0])))), "invalid bitrate-mode boolean");
        await Reject(FileOf(extra: Object(ExtendedContent, Descriptors(("IsVBR", (ushort)2, [0, 0])))), "truncated bitrate-mode boolean");
        await Reject(FileOf(extra: Object(ExtendedContent, Descriptors(("MediaFoundationVersion", (ushort)3, new byte[4])))), "invalid encoder-version type");
        var legacy = ContentFields("Title \u00fc", "Artist \u4e2d", "Copyright", "Line 1\nLine 2", "");
        var described = await Read(FileOf(extra: Object(Content, legacy)));
        described.RequireConversionSupport();
        check(described.Tags["title"] == "Title \u00fc" && described.Tags["artist"] == "Artist \u4e2d" && described.Tags["comment"] == "Line 1\nLine 2",
            "ASF inventory: content description Unicode and empty rating are retained without fabricated tags");
        var extended = await Read(FileOf(extra: Object(ExtendedContent, Descriptors(("WM/AlbumTitle", (ushort)0, Text("Album")),
            ("WM/TrackNumber", (ushort)3, BitConverter.GetBytes(9u)), ("WM/Year", (ushort)0, Text("2026"))))));
        check(extended.Tags["album"] == "Album" && extended.Tags["track"] == "9" && extended.Tags["date"] == "2026",
            "ASF inventory: documented text and numeric track mappings retain values");
        foreach (var name in new[] { "WM/ReplayGain", "WM/OriginalFilename", "Custom", "WM/Script" })
        {
            var result = await Read(FileOf(extra: Object(ExtendedContent, Descriptors((name, (ushort)0, Text("Keep"))))));
            try { result.RequireConversionSupport(); check(false, "ASF unknown metadata"); }
            catch (NotSupportedException) { check(result.UnsupportedObjects.Any(value => value.Contains(name)), "ASF inventory: unmapped field blocks omission " + name); }
        }
        foreach (var id in new[] { "2211b3fb-bd23-11d2-b4b7-00a0c955fc6e", "298ae614-2622-4c17-b935-dae07ee9289c", "43058533-6981-49e6-9b74-ad12cb86d58c" })
        {
            await Reject(FileOf(extra: Object(id, [])), "protection object " + id);
            await Reject(FileOf(extension: Object(id, [])), "nested protection object " + id);
        }
        await Reject(FileOf(flags: 0x8001), "encrypted stream flag");
        await Reject(FileOf(extra: Object(ExtendedContent, Descriptors(("WM/Protected", (ushort)2, BitConverter.GetBytes(1))))), "protection metadata");
        await Reject(FileOf(extra: Object(ExtendedContent, Descriptors(("DRM_LicenseURL", (ushort)0, Text("https://invalid.example/never-open"))))), "DRM URL metadata");
        await Reject(FileOf(format: 1), "non-WMA codec");
        await Reject(FileOf(flags: 0), "zero stream number");
        await Reject(FileOf(flags: 0x101), "reserved stream flags");
        await Reject(FileOf(fileFlags: 1), "broadcast input");
        await Reject(FileOf(extra: Object(Stream, StreamBody(0x161, 2))), "additional audio stream");
        await Reject(FileOf(extra: Object(Content, legacy).Concat(Object(Content, legacy)).ToArray(), extraCount: 2), "duplicate content object");
        await Reject(FileOf(extra: Object(ExtendedContent, Descriptors(("Title", (ushort)0, Text("One")), ("Title", (ushort)0, Text("Two"))))), "duplicate values");
        await Reject(FileOf(extra: Object(ExtendedContent, Descriptors(("Title", (ushort)0, new byte[] { 65, 0, 0 })))), "odd UTF16 length");
        await Reject(FileOf(extra: Object(ExtendedContent, Descriptors(("Title", (ushort)0, new byte[] { 0, 216, 0, 0 })))), "invalid surrogate");
        await Reject(FileOf(extra: Object(ExtendedContent, Descriptors(("Title", (ushort)0, Encoding.Unicode.GetBytes("not terminated"))))), "unterminated text");
        await Reject(FileOf(extra: Object(ExtendedContent, Descriptors(("Title", (ushort)0, Text("bad\0embedded"))))), "embedded NUL");
        await Reject(FileOf(extra: Object(ExtendedContent, Descriptors(("WM/TrackNumber", (ushort)3, new byte[2])))), "wrong numeric size");
        var wrongSize = original.ToArray(); BinaryPrimitives.WriteUInt64LittleEndian(wrongSize.AsSpan(16), ulong.MaxValue);
        await Reject(wrongSize, "header size overflow");
        var wrongCount = original.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(wrongCount.AsSpan(24), 4097);
        await Reject(wrongCount, "object count budget");
        var truncated = original[..^1]; await Reject(truncated, "truncated data extent");
        var trailing = original.Concat(new byte[] { 1 }).ToArray(); await Reject(trailing, "undeclared trailing data");
        var packetMismatch = original.ToArray(); BinaryPrimitives.WriteUInt64LittleEndian(packetMismatch.AsSpan(86), 3);
        await Reject(packetMismatch, "packet count mismatch");
        var namedStream = LibraryField("WM/AlbumTitle", Text("Album"), 1, 0);
        var streamTags = await Read(FileOf(extension: Object(Library, namedStream)));
        streamTags.RequireConversionSupport(); check(streamTags.Tags["album"] == "Album", "ASF inventory: metadata library names the one audio stream");
        await Reject(FileOf(extension: Object(Library, LibraryField("WM/AlbumTitle", Text("Album"), 2, 0))), "metadata refers to absent stream");
        var language = await Read(FileOf(extension: Object(Library, LibraryField("WM/AlbumTitle", Text("Album"), 1, 1))));
        check(language.UnsupportedObjects.Contains("language-attributed metadata"), "ASF inventory: language attribution cannot be silently flattened");
        var unknown = await Read(FileOf(extra: Object("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", [1, 2, 3])));
        check(!unknown.UnsupportedObjects.IsEmpty, "ASF inventory: unknown header content remains a conversion refusal");
        await Reject(FileOf(extension: Object("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", [1])), "unknown extension object");
        await Reject(FileOf(extra: Object("1efb1a30-0b62-11d0-a39b-00a0c90348f6", [])), "script commands");
        var indexBody = new byte[38]; FileId.TryWriteBytes(indexBody);
        BinaryPrimitives.WriteUInt64LittleEndian(indexBody.AsSpan(16), 10000000);
        BinaryPrimitives.WriteUInt32LittleEndian(indexBody.AsSpan(24), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(indexBody.AsSpan(28), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(indexBody.AsSpan(36), 2);
        var indexed = await Read(WithIndex(indexBody)); indexed.RequireConversionSupport();
        check(indexed.Packets == 2, "ASF inventory: bounded simple index retains audio packet extent");
        foreach (var field in new[] { 0, 16, 24, 28, 32, 36 })
        {
            var broken = indexBody.ToArray(); broken[field] = field == 0 ? (byte)0 : (byte)3;
            if (field == 16) broken.AsSpan(16, 8).Clear();
            await Reject(WithIndex(broken), "invalid simple index field " + field);
        }
        // Minimal image header: parsing/transport only, native image decoding
        // and exact pixels are exercised by the separate adapter fixtures.
        var png = new byte[33]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(8), 13); "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), 2); BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), 1);
        png[24] = 8; png[25] = 6;
        var picture = new byte[] { 3 }.Concat(BitConverter.GetBytes((uint)png.Length)).Concat(Text("image/png")).Concat(Text("Cover \u00fc")).Concat(png).ToArray();
        var pictured = await Read(FileOf(extra: Object(ExtendedContent, Descriptors(("WM/Picture", (ushort)1, picture)))));
        var description = FlacDescriptiveMetadata.ReadBlocks(pictured.Pictures).Pictures.Single();
        check(description.Type == 3 && description.Description == "Cover \u00fc" && description.DataBytes == png.Length,
            "ASF inventory: picture identity, description and original bytes retained");
        foreach (var broken in new[] { picture[..^1], picture.Concat(new byte[] { 0 }).ToArray(), picture[..8] })
            await Reject(FileOf(extra: Object(ExtendedContent, Descriptors(("WM/Picture", (ushort)1, broken)))), "malformed picture extent");
        var invalidPictureType = picture.ToArray(); invalidPictureType[0] = 21;
        await Reject(FileOf(extra: Object(ExtendedContent, Descriptors(("WM/Picture", (ushort)1, invalidPictureType)))), "unknown picture type");
        var manyFields = Descriptors(Enumerable.Range(0, 4096).Select(_ => ("Custom", (ushort)1, Array.Empty<byte>())).ToArray());
        await Reject(FileOf(extra: Object(ExtendedContent, manyFields), extension: Object(Library, namedStream)), "aggregate descriptor budget");
        var empty = ImmutableDictionary<string, string>.Empty;
        AudioProbeFacts Facts(string codec) => new("asf", null, [new(0, "audio", codec, 48000, 2, null, null, null, null, empty)], empty);
        foreach (var codec in new[] { "wmav1", "wmav2", "wmapro" })
            foreach (var target in Enum.GetValues<AudioFormat>().Where(format => format != AudioFormat.Wem))
            {
                var plan = AudioConversionPlan.Create(Facts(codec), target);
                var consent = target == AudioFormat.Wave ? AudioConversionConsent.None : target == AudioFormat.Flac ?
                    AudioConversionConsent.PrecisionReduction : AudioConversionConsent.LossyTranscoding;
                check(!plan.AlreadyTarget && plan.RequiredConsent == consent &&
                    (target != AudioFormat.Wave || plan.OutputCodec == "pcm_f32le" && plan.RequiresExactSamples),
                    "WMA plan: input-only conversion retains quality policy " + codec + " / " + target);
            }
        foreach (var rate in new[] { 8000, 11025, 16000, 22050 })
            foreach (var target in Enum.GetValues<AudioFormat>().Where(format => format != AudioFormat.Wem))
            {
                var facts = new AudioProbeFacts("asf", null, [new(0, "audio", "wmavoice", rate, 1, 16, null, null, "mono", empty)], empty);
                var plan = AudioConversionPlan.Create(facts, target);
                var expected = target is AudioFormat.Wave or AudioFormat.Flac ? AudioConversionConsent.None : AudioConversionConsent.LossyTranscoding;
                if (target == AudioFormat.Opus) expected |= AudioConversionConsent.Resampling;
                check(!plan.AlreadyTarget && plan.RequiredConsent == expected && plan.Channels == 1 &&
                    plan.SampleRate == (target == AudioFormat.Opus ? 48000 : rate), "Voice input-only quality policy " + rate + " / " + target);
            }
        foreach (var bits in new[] { 16, 24 })
            foreach (var target in Enum.GetValues<AudioFormat>().Where(format => format != AudioFormat.Wem))
            {
                var facts = new AudioProbeFacts("asf", null, [new(0, "audio", "wmalossless", 48000, 2, bits, null, null, "stereo", empty)], empty);
                var plan = AudioConversionPlan.Create(facts, target);
                check(!plan.AlreadyTarget && plan.RequiredConsent == AudioConversionConsent.None &&
                    (target is not (AudioFormat.Wave or AudioFormat.Flac) || plan.SampleBits == bits && plan.RequiresExactSamples),
                    "WMA Lossless plan preserves known precision without lossy-source warning " + bits + " / " + target);
            }
        foreach (var codec in new[] { "wmalossless", "wmavoice", "pcm_s16le" })
        {
            try { AudioConversionPlan.Create(Facts(codec), AudioFormat.Wave); check(false, "unverified WMA codec"); }
            catch (NotSupportedException) { check(true, "WMA plan: unverified codec remains refused " + codec); }
        }
        using (var source = new MemoryStream(original))
        {
            source.Position = 3;
            try { await AsfMetadata.ReadAsync(source, new CancellationToken(true)); check(false, "ASF cancellation"); }
            catch (OperationCanceledException) { check(source.Position == 3, "ASF inventory: cancellation restores caller position"); }
        }

        async Task Reject(byte[] bytes, string name)
        {
            using var source = new MemoryStream(bytes); source.Position = 2;
            try { var result = await AsfMetadata.ReadAsync(source, default); result.RequireConversionSupport(); check(false, "ASF refuses " + name); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            { check(source.Position == 2, "ASF inventory: refuses " + name + " and restores caller position"); }
        }
    }

    private static byte[] WithIndex(byte[] body)
    {
        var result = FileOf().Concat(Object("33000890-e5b1-11cf-89f4-00a0c90349cb", body)).ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(70), (ulong)result.Length); return result;
    }

    private static async Task<AsfMetadataInventory> Read(byte[] bytes)
    { using var source = new MemoryStream(bytes); return await AsfMetadata.ReadAsync(source, default); }
    private static byte[] Text(string value) => Encoding.Unicode.GetBytes(value + '\0');
    private static byte[] ContentFields(params string[] fields)
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        foreach (var value in fields) writer.Write((ushort)Text(value).Length);
        foreach (var value in fields) writer.Write(Text(value)); return output.ToArray();
    }
    private static byte[] Descriptors(params (string Name, ushort Type, byte[] Value)[] fields)
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write((ushort)fields.Length);
        foreach (var (name, type, value) in fields)
        { var text = Text(name); writer.Write((ushort)text.Length); writer.Write(text); writer.Write(type); writer.Write((ushort)value.Length); writer.Write(value); }
        return output.ToArray();
    }
    private static byte[] LibraryField(string name, byte[] value, ushort stream, ushort language)
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write((ushort)1); writer.Write(language); writer.Write(stream); writer.Write((ushort)Text(name).Length);
        writer.Write((ushort)0); writer.Write((uint)value.Length); writer.Write(Text(name)); writer.Write(value); return output.ToArray();
    }
    private static byte[] StreamBody(ushort codec, ushort flags)
    {
        var bytes = new byte[72]; new Guid("f8699e40-5b4d-11cf-a8fd-00805f5c442b").TryWriteBytes(bytes);
        new Guid("20fb5700-5b55-11cf-a8fd-00805f5c442b").TryWriteBytes(bytes.AsSpan(16));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 18); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(48), flags);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(54), codec); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(56), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(58), 48000); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(62), 24000);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(66), 1024); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(68), 16); return bytes;
    }
    private static byte[] FileOf(byte[]? extra = null, byte[]? extension = null, ushort format = 0x161, ushort flags = 1, uint fileFlags = 2, uint extraCount = 1)
    {
        var properties = new byte[80]; FileId.TryWriteBytes(properties); BinaryPrimitives.WriteUInt64LittleEndian(properties.AsSpan(32), 2);
        BinaryPrimitives.WriteUInt64LittleEndian(properties.AsSpan(40), 10000000); BinaryPrimitives.WriteUInt32LittleEndian(properties.AsSpan(64), fileFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(properties.AsSpan(68), 128); BinaryPrimitives.WriteUInt32LittleEndian(properties.AsSpan(72), 128);
        var ext = new byte[22]; new Guid("abd3d211-a9ba-11cf-8ee6-00c00c205365").TryWriteBytes(ext);
        BinaryPrimitives.WriteUInt16LittleEndian(ext.AsSpan(16), 6); BinaryPrimitives.WriteUInt32LittleEndian(ext.AsSpan(18), (uint)(extension?.Length ?? 0));
        var children = Object(Properties, properties).Concat(Object(Stream, StreamBody(format, flags)))
            .Concat(Object(Extension, ext.Concat(extension ?? []).ToArray())).Concat(extra ?? []).ToArray();
        var body = new byte[6]; BinaryPrimitives.WriteUInt32LittleEndian(body, 3 + (extra is null ? 0 : extraCount)); body[4] = 1; body[5] = 2;
        var data = new byte[26 + 256]; FileId.TryWriteBytes(data); BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(16), 2); data[24] = 1; data[25] = 1;
        var result = Object(Header, body.Concat(children).ToArray()).Concat(Object(Data, data)).ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(70), (ulong)result.Length); return result;
    }
    private static byte[] Object(string id, byte[] body)
    {
        var result = new byte[24 + body.Length]; new Guid(id).TryWriteBytes(result);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(16), (ulong)result.Length); body.CopyTo(result, 24); return result;
    }
}
