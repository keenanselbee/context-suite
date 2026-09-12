using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Xml;
using ContextSuite.Core.Dds;

namespace ContextSuite.Core.Analysis;

// Identification is bounded evidence, never certification of the complete file.
// These readers do not decompress, execute, follow references or enable conversion.
public static class HeaderAnalyzer
{
    public const int MaximumBytes = 64 * 1024;
    internal const string FilenameOnlyWarning = "The suggested type is based only on the filename. The inspected content did not establish its format.";

    public static FileAnalysis Analyze(string path, ReadOnlySpan<byte> bytes, long fileBytes)
    {
        if (fileBytes < bytes.Length || bytes.Length > MaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(fileBytes));
        var catalog = FileTypeCatalog.Default;
        var facts = ImmutableArray.CreateBuilder<AnalysisFact>();
        var warnings = ImmutableArray.CreateBuilder<string>();
        var evidence = ImmutableArray.CreateBuilder<string>();
        facts.Add(new("file.size", "File", "File size (bytes)", Integer: fileBytes));
        facts.Add(new("file.inspected", "File", "Header bytes inspected", Integer: bytes.Length));
        string? id = null;
        var confidence = IdentificationConfidence.Likely;
        DdsInfo? texture = null;

        if (bytes.StartsWith("DDS "u8))
        {
            id = "dds";
            evidence.Add("DDS signature at the start of the file.");
            try
            {
                texture = DdsParser.Parse(bytes, fileBytes);
                confidence = IdentificationConfidence.Confirmed;
                evidence.Add("DDS header structure parsed; pixel data was not decoded.");
                warnings.AddRange(texture.Warnings);
                AddTextureFacts(texture, facts);
            }
            catch (InvalidDataException)
            { warnings.Add("The DDS header is incomplete or malformed. Texture properties are unavailable."); }
        }
        else if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            id = "png";
            evidence.Add("PNG signature at the start of the file; complete image validity was not checked.");
            if (bytes.Length >= 33 && BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) == 13 && bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
            {
                var width = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]);
                var height = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]);
                if (width is > 0 and <= int.MaxValue && height is > 0 and <= int.MaxValue)
                {
                    facts.Add(new("image.width", "Image", "Declared width", Integer: width));
                    facts.Add(new("image.height", "Image", "Declared height", Integer: height));
                }
                else warnings.Add("The PNG header declares invalid dimensions.");
                facts.Add(new("png.depth", "Image", "Declared sample depth", Integer: bytes[24]));
                facts.Add(new("png.color-type", "Image", "Raw color type", Integer: bytes[25]));
                facts.Add(new("image.transparency", "Image", "Transparency", Availability: FactAvailability.Unavailable));
                facts.Add(new("image.animation", "Image", "Animation", Availability: FactAvailability.Unavailable));
                // Even color types without alpha may carry a tRNS chunk; do not infer opacity.
            }
            else warnings.Add("A complete PNG image header was not found in the inspected bytes.");
        }
        else if (bytes.StartsWith(new byte[] { 0xff, 0xd8, 0xff }) || bytes.StartsWith("GIF87a"u8) ||
                 bytes.StartsWith("GIF89a"u8) || bytes.StartsWith("BM"u8) ||
                 bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            id = bytes[0] == 0xff ? "jpeg" : bytes[0] == 'G' ? "gif" : bytes[0] == 'B' ? "bmp" : "webp";
            evidence.Add("Image signature and available header declarations inspected. Pixel data, later frames and metadata were not validated; dimensions are not adjusted for display orientation.");
            ImageHeaderFacts.Add(id, bytes, fileBytes, facts, warnings);
        }
        else if (bytes.StartsWith("%PDF-"u8))
        {
            id = "pdf";
            evidence.Add("PDF signature at the start of the file; document objects were not parsed.");
            if (bytes.Length >= 8 && bytes[5] is >= (byte)'0' and <= (byte)'9' && bytes[6] == '.' && bytes[7] is >= (byte)'0' and <= (byte)'9')
                facts.Add(new("pdf.header-version", "Document", "Header version", Text: Encoding.ASCII.GetString(bytes.Slice(5, 3))));
            else warnings.Add("The PDF version header is incomplete or malformed.");
            facts.Add(new("document.pages", "Document", "Page count", Availability: FactAvailability.Unavailable));
            facts.Add(new("document.encryption", "Document", "Encryption", Availability: FactAvailability.Unavailable));
        }
        else if (bytes.StartsWith("PK\x03\x04"u8) || bytes.StartsWith("PK\x05\x06"u8) || bytes.StartsWith("PK\x07\x08"u8))
        {
            id = "zip";
            evidence.Add("ZIP record signature; archive directory and embedded document type were not checked.");
        }
        else if (bytes.StartsWith(new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 }))
        {
            id = "ole";
            evidence.Add("Compound-file signature; contained streams and application type were not checked.");
        }
        else if (bytes.StartsWith("MZ"u8))
        {
            id = "mz";
            evidence.Add("MZ executable signature; the file was not executed.");
            if (bytes.Length >= 64)
            {
                var offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes[60..]);
                if (offset >= 64 && offset <= bytes.Length - 24 && bytes.Slice((int)offset, 4).SequenceEqual("PE\0\0"u8))
                {
                    id = "pe";
                    evidence.Add("PE signature and COFF header found at the declared offset; image sections were not validated.");
                    facts.Add(new("pe.machine", "Program", "Raw machine identifier", Integer: BinaryPrimitives.ReadUInt16LittleEndian(bytes[((int)offset + 4)..])));
                    facts.Add(new("pe.sections", "Program", "Declared section count", Integer: BinaryPrimitives.ReadUInt16LittleEndian(bytes[((int)offset + 6)..])));
                }
                else evidence.Add("A PE header was not found within the inspected bytes; other executable types remain possible.");
            }
        }
        else if (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            id = "wave";
            evidence.Add("RIFF/WAVE header declarations were inspected within fixed limits. Audio samples were not decoded or validated.");
            AudioHeaderFacts.AddWave(bytes, fileBytes, facts, warnings);
        }
        else if (bytes.Length >= 12 && bytes.StartsWith("FORM"u8) &&
            (bytes.Slice(8, 4).SequenceEqual("AIFF"u8) || bytes.Slice(8, 4).SequenceEqual("AIFC"u8)))
        {
            id = "aiff";
            evidence.Add("FORM/AIFF or AIFF-C signature and bounded Common Chunk declarations inspected. Sound data, compression, version chunks, instruments, loops and metadata were not validated.");
            AudioHeaderFacts.AddAiff(bytes, fileBytes, facts, warnings);
        }
        else if (bytes.StartsWith(".snd"u8))
        {
            id = "au";
            evidence.Add("AU signature and fixed header declarations inspected. Annotation content and audio samples were not interpreted or validated.");
            AudioHeaderFacts.AddAu(bytes, fileBytes, facts, warnings);
        }
        else if (bytes.StartsWith("fLaC"u8))
        {
            id = "flac";
            evidence.Add("FLAC signature and available STREAMINFO declarations inspected. Audio frames and metadata contents were not validated.");
            AudioHeaderFacts.AddFlac(bytes, fileBytes, facts, warnings);
        }
        else if (bytes.StartsWith("OggS"u8))
        {
            id = "ogg";
            evidence.Add("Ogg page signature; the contained codecs were not identified.");
        }
        else if (FontHeaderFacts.Identify(bytes, fileBytes) is { } font)
        {
            id = font;
            evidence.Add("Font signature and available header declarations inspected. Table contents, names, glyphs, compression and embedding rights were not validated; the font was not installed or rendered.");
            FontHeaderFacts.Add(id, bytes, fileBytes, facts, warnings);
        }
        else if (TryText(bytes, fileBytes > bytes.Length, out var encoding, out var text))
        {
            id = "text";
            evidence.Add("The inspected bytes resemble text. Other encodings or application-specific structures may also fit.");
            facts.Add(new("text.encoding", "Text", "Possible encoding", Text: encoding, Availability: FactAvailability.Derived));
            if (fileBytes == bytes.Length && TryStructuredText(text, facts, warnings) is { } structured)
            {
                id = structured;
                confidence = IdentificationConfidence.Confirmed;
                evidence.Add("The complete sampled file parsed as " + structured.ToUpperInvariant() +
                    " structure within the analysis limits. Application-specific meaning was not validated.");
            }
        }

        var hints = catalog.FindByName(path);
        var basis = id is null ? IdentificationBasis.Unknown : IdentificationBasis.Content;
        if (!hints.IsEmpty)
        {
            evidence.Add($"The filename suggests {string.Join(" or ", hints.Select(hint => hint.Name))}; this is only a filename hint.");
            // Text is a container for many languages and formats, not contradictory evidence.
            // A readable source/config file can retain its useful filename explanation,
            // explicitly labeled as such; no language-specific parse is claimed.
            if (id == "text" && hints.Any(hint => hint.TextCompatible))
            {
                var textHints = hints.Where(hint => hint.TextCompatible).ToImmutableArray();
                if (textHints.Length == 1 && textHints[0].Id != "text")
                {
                    id = textHints[0].Id;
                    basis = IdentificationBasis.Filename;
                    evidence.Add("Text is compatible with this filename hint, but the specific format or programming language was not validated.");
                }
                else if (textHints.Length > 1)
                {
                    confidence = IdentificationConfidence.Ambiguous;
                    evidence.Add("Several text formats share this filename; the language or document type remains ambiguous.");
                }
            }
            if (id is null && fileBytes != 0)
            {
                warnings.Add(FilenameOnlyWarning);
                if (hints.Length == 1) { id = hints[0].Id; basis = IdentificationBasis.Filename; }
                else confidence = IdentificationConfidence.Ambiguous;
            }
            else if (id is not null && hints.All(hint => hint.Id != id) &&
                !(id is "text" or "json" or "xml" && hints.Any(hint => hint.TextCompatible)))
            {
                warnings.Add(id is "zip" or "ole"
                    ? "The container alone does not confirm the document type suggested by the filename."
                    : "The filename suggests a different type from the inspected content.");
            }
        }
        if (bytes.Length < fileBytes) evidence.Add($"Only the first {bytes.Length:N0} bytes were inspected.");
        if (id is null)
        {
            evidence.Add(fileBytes == 0 ? "The file is empty; no content signature is available." : "No supported content signature identified this file.");
            return new(path, fileBytes, new(null, fileBytes == 0 ? "Empty file" : "Unknown file type", "Unknown",
                "Its usual purpose could not be established from the available evidence.",
                confidence == IdentificationConfidence.Ambiguous ? confidence : IdentificationConfidence.Unknown, evidence.ToImmutable()),
                facts.ToImmutable(), warnings.ToImmutable(), bytes.Length, FilenameHints: hints);
        }
        var description = catalog.Get(id);
        return new(path, fileBytes, new(id, description.Name, description.Family, description.CommonUses, confidence, evidence.ToImmutable(), basis),
            facts.ToImmutable(), warnings.ToImmutable(), bytes.Length, texture, hints);
    }

    private static bool TryText(ReadOnlySpan<byte> bytes, bool partial, out string encoding, out string text)
    {
        encoding = "";
        text = "";
        if (bytes.IsEmpty) return false;
        Encoding decoder;
        var skip = 0;
        if (bytes.StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) { decoder = new UTF32Encoding(false, false, true); skip = 4; encoding = "UTF-32 little-endian BOM"; }
        else if (bytes.StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) { decoder = new UTF32Encoding(true, false, true); skip = 4; encoding = "UTF-32 big-endian BOM"; }
        else if (bytes.StartsWith(new byte[] { 0xff, 0xfe })) { decoder = new UnicodeEncoding(false, false, true); skip = 2; encoding = "UTF-16 little-endian BOM"; }
        else if (bytes.StartsWith(new byte[] { 0xfe, 0xff })) { decoder = new UnicodeEncoding(true, false, true); skip = 2; encoding = "UTF-16 big-endian BOM"; }
        else
        {
            decoder = new UTF8Encoding(false, true);
            if (bytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) { skip = 3; encoding = "UTF-8 BOM"; }
            else encoding = "UTF-8 compatible (ASCII is also possible)";
        }
        try
        {
            // Do not turn a multi-byte character split at the sampling limit into a failure.
            var characters = new char[bytes.Length];
            decoder.GetDecoder().Convert(bytes[skip..], characters, !partial, out _, out var count, out _);
            for (var index = 0; index < count; index++)
                if (char.IsControl(characters[index]) && characters[index] is not ('\r' or '\n' or '\t' or '\f')) return false;
            text = new string(characters, 0, count);
            return count > 0 || skip > 0;
        }
        catch (DecoderFallbackException) { return false; }
    }

    private static string? TryStructuredText(string text, ImmutableArray<AnalysisFact>.Builder facts,
        ImmutableArray<string>.Builder warnings)
    {
        var start = text.AsSpan().TrimStart();
        if (start.IsEmpty) return null;
        if (start[0] is '{' or '[')
        {
            try
            {
                using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
                var root = document.RootElement;
                facts.Add(new("json.root", "Data", "JSON root", Text: root.ValueKind.ToString()));
                facts.Add(root.ValueKind == JsonValueKind.Array
                    ? new("json.items", "Data", "Top-level array items", Integer: root.GetArrayLength())
                    : new("json.properties", "Data", "Top-level property entries", Integer: root.EnumerateObject().Count()));
                return "json";
            }
            catch (JsonException) { return null; }
        }
        if (start[0] != '<') return null;
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = MaximumBytes, MaxCharactersFromEntities = 1,
                IgnoreComments = true, IgnoreProcessingInstructions = true
            });
            string? root = null, namespaceUri = null;
            long elements = 0;
            while (reader.Read())
            {
                if (reader.Depth > 32) throw new XmlException("XML nesting exceeds the analysis limit.");
                if (reader.NodeType != XmlNodeType.Element) continue;
                elements++;
                if (root is null) { root = reader.LocalName; namespaceUri = reader.NamespaceURI; }
            }
            if (root is null) return null;
            facts.Add(new("xml.root", "Data", "XML root element", Text: root));
            facts.Add(new("xml.namespace", "Data", "XML root namespace", Text: namespaceUri));
            facts.Add(new("xml.elements", "Data", "XML elements", Integer: elements));
            return "xml";
        }
        catch (XmlException)
        {
            if (start.StartsWith("<?xml", StringComparison.Ordinal) || start.StartsWith("<!DOCTYPE", StringComparison.Ordinal))
                warnings.Add("XML details were not read: the document is incomplete, malformed, uses a DTD, or exceeds the analysis limits. External references are never resolved.");
            return null;
        }
    }

    private static void AddTextureFacts(DdsInfo texture, ImmutableArray<AnalysisFact>.Builder facts)
    {
        facts.Add(new("dds.header", "Texture", "Header", Text: texture.HasDx10Header ? "DX10 extended" : "Legacy DDS"));
        facts.Add(new("dds.fourcc", "Texture", "Raw FourCC", Text: $"0x{texture.RawFourCc:X8}"));
        facts.Add(new("dds.dxgi", "Texture", "Raw DXGI identifier", Integer: texture.RawDxgiFormat));
        facts.Add(new("dds.format", "Texture", "Format", Text: texture.Format.ToString()));
        facts.Add(new("dds.kind", "Texture", "Structure", Text: texture.Kind.ToString()));
        facts.Add(new("image.width", "Image", "Width", Integer: texture.Width));
        facts.Add(new("image.height", "Image", "Height", Integer: texture.Height));
        facts.Add(new("dds.depth", "Texture", "Depth", Integer: texture.Depth));
        facts.Add(new("dds.array", "Texture", "Array count", Integer: texture.ArraySize));
        facts.Add(new("dds.mips", "Texture", "Mip levels", Integer: texture.MipLevels));
        facts.Add(new("dds.alpha", "Texture", "Raw alpha mode", Integer: texture.RawAlphaMode));
        facts.Add(new("dds.alpha-name", "Texture", "Alpha mode", Text: Enum.IsDefined((DdsAlphaMode)texture.RawAlphaMode)
            ? ((DdsAlphaMode)texture.RawAlphaMode).ToString() : "Unrecognized"));
        facts.Add(new("dds.color", "Texture", "Color interpretation", Text: texture.IsSrgb ? "sRGB explicitly declared" : texture.IsTypeless
            ? "Typeless; typed interpretation required" : "No sRGB declaration; not proof of authored linear color or texture purpose"));
        facts.Add(texture.ExpectedPayloadBytes is { } size
            ? new("dds.payload", "Texture", "Expected payload (bytes)", Text: size.ToString(), Availability: FactAvailability.Derived)
            : new("dds.payload", "Texture", "Expected payload", Availability: FactAvailability.Unavailable));
    }
}
