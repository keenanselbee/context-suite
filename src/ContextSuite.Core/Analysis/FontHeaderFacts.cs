using System.Buffers.Binary;
using System.Collections.Immutable;

namespace ContextSuite.Core.Analysis;

// Header declarations only: no font installation, outline interpretation or decompression.
internal static class FontHeaderFacts
{
    public static string? Identify(ReadOnlySpan<byte> bytes, long fileBytes)
    {
        if (bytes.StartsWith("OTTO"u8)) return "opentype";
        if (bytes.StartsWith("ttcf"u8)) return "font-collection";
        if (bytes.StartsWith("wOFF"u8)) return "woff";
        if (bytes.StartsWith("wOF2"u8)) return "woff2";
        // The numeric sfnt tag is weak evidence alone. Require a plausible complete
        // header and space in the file for its directory before identifying it.
        if (bytes.Length >= 12 && U32(bytes, 0) == 0x00010000 && U16(bytes, 4) > 0 &&
            12L + 16L * U16(bytes, 4) <= fileBytes) return "truetype";
        return null;
    }

    public static void Add(string id, ReadOnlySpan<byte> bytes, long fileBytes,
        ImmutableArray<AnalysisFact>.Builder facts, ImmutableArray<string>.Builder warnings)
    {
        facts.Add(new("font.name", "Font", "Font name", Availability: FactAvailability.Unavailable));
        facts.Add(new("font.glyphs", "Font", "Validated glyph count", Availability: FactAvailability.Unavailable));
        facts.Add(new("font.license", "Font", "Font license and embedding rights", Availability: FactAvailability.Unavailable));
        var required = id == "woff" ? 44 : id == "woff2" ? 48 : 12;
        if (bytes.Length < required)
        {
            warnings.Add("The font header is incomplete within the inspected bytes. Font properties are unavailable.");
            return;
        }
        if (id == "font-collection")
        {
            var version = U32(bytes, 4);
            facts.Add(new("font.collection-version", "Font", "Raw collection header version", Integer: version));
            if (version is not (0x00010000 or 0x00020000))
            {
                warnings.Add("The font collection header version is unsupported; its remaining fields were not interpreted.");
                return;
            }
            var count = U32(bytes, 8);
            facts.Add(new("font.count", "Font", "Declared fonts in collection", Integer: count));
            var end = 12L + 4L * count + (version == 0x00020000 ? 12 : 0);
            if (count == 0 || end > fileBytes)
                warnings.Add("The font collection declares an empty or out-of-file directory.");
            else if (end > bytes.Length)
                warnings.Add("The font collection directory extends beyond the inspected prefix; member fonts were not checked.");
            return;
        }
        var packaged = id is "woff" or "woff2";
        var flavor = U32(bytes, packaged ? 4 : 0);
        facts.Add(new("font.flavor", "Font", "Declared outline/container flavor", Text: flavor switch
        {
            0x00010000 => "TrueType outlines (declaration only)",
            0x4f54544f => "CFF/CFF2 outlines (declaration only)",
            0x74746366 when id == "woff2" => "Font collection (declaration only)",
            _ => "Uninterpreted 0x" + flavor.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)
        }));
        var tables = U16(bytes, packaged ? 12 : 4);
        facts.Add(new("font.tables", "Font", "Declared font table count", Integer: tables));
        if (tables == 0) warnings.Add("The font header declares no tables.");
        if (!packaged)
        {
            var end = 12L + 16L * tables;
            if (end > fileBytes) warnings.Add("The declared font table directory extends beyond the file.");
            else if (end > bytes.Length) warnings.Add("The font table directory extends beyond the inspected prefix.");
            return;
        }
        var declaredLength = U32(bytes, 8);
        facts.Add(new("font.packaged-bytes", "Font", "Declared packaged size (bytes)", Integer: declaredLength));
        facts.Add(new("font.sfnt-bytes", "Font", "Declared uncompressed size reference (bytes)", Integer: U32(bytes, 16)));
        if (declaredLength != fileBytes) warnings.Add("The packaged font's declared file size does not match the file length.");
        if (U16(bytes, 14) != 0) warnings.Add("The packaged font's reserved header field is nonzero.");
        if (id == "woff")
        {
            var end = 44L + 20L * tables;
            if (end > fileBytes) warnings.Add("The WOFF table directory extends beyond the file.");
            else if (end > bytes.Length) warnings.Add("The WOFF table directory extends beyond the inspected prefix.");
        }
        else
        {
            var compressed = U32(bytes, 20);
            facts.Add(new("font.compressed-bytes", "Font", "Declared compressed data size (bytes)", Integer: compressed));
            if (compressed > fileBytes - 48) warnings.Add("The declared WOFF2 compressed data cannot fit after the header.");
        }
    }

    private static ushort U16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
    private static uint U32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
}
