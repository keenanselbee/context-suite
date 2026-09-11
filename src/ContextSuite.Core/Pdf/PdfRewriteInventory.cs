using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ContextSuite.Core.Pdf;

public sealed record PdfRewriteFacts(string PdfVersion, string SemanticSha256, string? OriginalDocumentId, int ReachableObjects, string UnreferencedSha256);

// Consumes complete qpdf JSON v2 with inline stream data, not its field summary.
// This is a structural preservation oracle; it is not signature validation or a
// replacement for independent rendering and broader PDF conformance acceptance.
public static class PdfRewriteInventory
{
    public const int MaximumBytes = 32 * 1024 * 1024;
    public const int MaximumObjects = 32768;
    private static readonly HashSet<string> TrailerStorage = new(StringComparer.Ordinal) { "/Size", "/Prev", "/XRefStm", "/ID" };
    private static readonly HashSet<string> XrefStorage = new(StringComparer.Ordinal) { "/Type", "/W", "/Index", "/Length", "/Filter", "/DecodeParms" };

    public static PdfRewriteFacts Read(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
        => ReadCore(bytes, true, token);

    public static void CheckAdmission(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
    {
        _ = ReadCore(bytes, false, token);
    }

    private static PdfRewriteFacts ReadCore(ReadOnlyMemory<byte> bytes, bool requireData, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (bytes.Length == 0 || bytes.Length > MaximumBytes) throw new InvalidDataException("PDF structural inventory exceeds its byte budget.");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            var nodes = 0;
            Inspect(root);
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 || !root.TryGetProperty("qpdf", out var qpdf) ||
                qpdf.ValueKind != JsonValueKind.Array || qpdf.GetArrayLength() != 2)
                throw new InvalidDataException("Complete qpdf JSON v2 is required.");
            var meta = qpdf[0];
            if (meta.GetProperty("jsonversion").GetInt32() != 2 || meta.GetProperty("pushedinheritedpageresources").GetBoolean() ||
                meta.GetProperty("calledgetallpages").GetBoolean())
                throw new InvalidDataException("PDF inventory schema or preprocessing is unsupported.");
            var version = meta.GetProperty("pdfversion").GetString();
            if (version is not ("1.0" or "1.1" or "1.2" or "1.3" or "1.4" or "1.5" or "1.6" or "1.7" or "2.0"))
                throw new NotSupportedException("PDF version needs a reviewed rewrite policy.");
            var objects = qpdf[1];
            if (objects.ValueKind != JsonValueKind.Object || objects.EnumerateObject().Count() > MaximumObjects + 1)
                throw new InvalidDataException("PDF object inventory exceeds its budget.");
            foreach (var entry in objects.EnumerateObject())
            {
                if (entry.Name != "trailer" && (!entry.Name.StartsWith("obj:", StringComparison.Ordinal) || !IsReference(entry.Name[4..])))
                    throw new InvalidDataException("Invalid PDF object key.");
                var wrapper = entry.Value;
                if (wrapper.ValueKind != JsonValueKind.Object || wrapper.EnumerateObject().Count() != 1)
                    throw new InvalidDataException("Invalid PDF object wrapper.");
                if (wrapper.TryGetProperty("stream", out var stream))
                {
                    if (stream.ValueKind != JsonValueKind.Object || stream.EnumerateObject().Any(property => property.Name is not ("dict" or "data")) ||
                        !stream.TryGetProperty("dict", out var dict) || dict.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("Every PDF stream requires an inline payload and dictionary.");
                    if (dict.EnumerateObject().Any(property => DecodeName(property.Name) == "/F"))
                        throw new NotSupportedException("External PDF streams need a preservation policy.");
                    if (!stream.TryGetProperty("data", out var data))
                    {
                        if (requireData) throw new InvalidDataException("PDF stream payload is missing.");
                        continue;
                    }
                    if (data.ValueKind != JsonValueKind.String) throw new InvalidDataException("Invalid PDF stream payload.");
                    // Validate base64 without retaining another decoded copy.
                    var encoded = data.GetString()!;
                    if (!System.Buffers.Text.Base64.IsValid(encoded)) throw new InvalidDataException("Invalid PDF inline stream encoding.");
                }
                else if (!wrapper.TryGetProperty("value", out _)) throw new InvalidDataException("Invalid PDF value wrapper.");
            }
            var trailer = objects.GetProperty("trailer").GetProperty("value");
            if (trailer.ValueKind != JsonValueKind.Object || !trailer.TryGetProperty("/Root", out var catalog) ||
                catalog.ValueKind != JsonValueKind.String || !IsReference(catalog.GetString()!))
                throw new InvalidDataException("PDF catalog reference is missing.");
            if (trailer.EnumerateObject().Any(property => DecodeName(property.Name) == "/Prev"))
                throw new NotSupportedException("PDF revision history requires inspection before rewriting.");
            string? originalId = null;
            if (trailer.TryGetProperty("/ID", out var ids))
            {
                if (ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() != 2 || ids[0].ValueKind != JsonValueKind.String || ids[1].ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("Invalid PDF document identifiers.");
                originalId = ids[0].GetString();
            }
            var xref = trailer.TryGetProperty("/Type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "/XRef";
            var visited = new Dictionary<string, int>(StringComparer.Ordinal);
            var traversed = 0; long totalCanonicalBytes = 0;
            using var canonical = new MemoryStream();
            using (var writer = new Utf8JsonWriter(canonical, new JsonWriterOptions { MaxDepth = 128 }))
            {
                writer.WriteStartObject();
                foreach (var property in trailer.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    if (TrailerStorage.Contains(property.Name) || xref && XrefStorage.Contains(property.Name)) continue;
                    writer.WritePropertyName(property.Name); Write(property.Value, writer, 0);
                }
                writer.WriteEndObject(); writer.Flush();
            }
            var rooted = new Dictionary<string, int>(visited, StringComparer.Ordinal);
            totalCanonicalBytes = canonical.Length;
            var orphanHashes = new List<string>();
            foreach (var entry in objects.EnumerateObject())
            {
                if (entry.Name == "trailer" || rooted.ContainsKey(entry.Name[4..]) || IsStorageObject(entry.Value)) continue;
                visited = new(rooted, StringComparer.Ordinal);
                using var orphan = new MemoryStream();
                using (var writer = new Utf8JsonWriter(orphan, new JsonWriterOptions { MaxDepth = 128 }))
                { Write(entry.Value, writer, 0); writer.Flush(); }
                totalCanonicalBytes += orphan.Length;
                orphanHashes.Add(Convert.ToHexString(SHA256.HashData(orphan.GetBuffer().AsSpan(0, checked((int)orphan.Length)))));
            }
            orphanHashes.Sort(StringComparer.Ordinal);
            return new(version, Convert.ToHexString(SHA256.HashData(canonical.GetBuffer().AsSpan(0, checked((int)canonical.Length)))), originalId, rooted.Count,
                Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(string.Join("", orphanHashes)))));

            void Inspect(JsonElement element)
            {
                token.ThrowIfCancellationRequested();
                if (++nodes > 1000000) throw new InvalidDataException("PDF structural inventory exceeds its node budget.");
                if (element.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in element.EnumerateObject())
                    {
                        var name = DecodeName(property.Name);
                        if (!names.Add(name)) throw new InvalidDataException("Duplicate PDF inventory key.");
                        if (name is "/Encrypt" or "/ByteRange" or "/SigFlags" or "/Perms" or "/DSS" ||
                            name is "/FT" or "/Type" && property.Value.ValueKind == JsonValueKind.String && DecodeName(property.Value.GetString()!) == "/Sig")
                            throw new NotSupportedException("Encrypted or signature-related PDF information prevents rewriting.");
                        // External stream content must never be resolved or dropped.
                        if (name is "/FFilter" or "/FDecodeParms" || name == "stream" && property.Value.ValueKind == JsonValueKind.Object &&
                            property.Value.TryGetProperty("dict", out var streamDict) && streamDict.TryGetProperty("/F", out _))
                            throw new NotSupportedException("External PDF streams need a preservation policy.");
                        Inspect(property.Value);
                    }
                }
                else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Inspect(item);
            }
            void Write(JsonElement element, Utf8JsonWriter writer, int depth)
            {
                token.ThrowIfCancellationRequested();
                if (++traversed > 1000000 || depth > 64 || totalCanonicalBytes + writer.BytesCommitted + writer.BytesPending > MaximumBytes * 2L)
                    throw new InvalidDataException("PDF reachable graph exceeds its depth or byte budget.");
                if (element.ValueKind == JsonValueKind.String && IsReference(element.GetString()!))
                {
                    var reference = element.GetString()!;
                    writer.WriteStartObject();
                    if (visited.TryGetValue(reference, out var index)) writer.WriteNumber("reference", index);
                    else
                    {
                        index = visited.Count; visited.Add(reference, index);
                        if (!objects.TryGetProperty("obj:" + reference, out var target)) throw new InvalidDataException("PDF graph has an unresolved reference.");
                        writer.WriteNumber("index", index); writer.WritePropertyName("object"); Write(target, writer, depth + 1);
                    }
                    writer.WriteEndObject();
                }
                else if (element.ValueKind == JsonValueKind.Object)
                {
                    writer.WriteStartObject();
                    foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                    { writer.WritePropertyName(property.Name); Write(property.Value, writer, depth + 1); }
                    writer.WriteEndObject();
                }
                else if (element.ValueKind == JsonValueKind.Array)
                { writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) Write(item, writer, depth + 1); writer.WriteEndArray(); }
                else element.WriteTo(writer);
            }
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Invalid PDF structural inventory.", error); }
    }

    public static void RequirePreserved(PdfRewriteFacts before, PdfRewriteFacts after)
    {
        if (before.PdfVersion != after.PdfVersion || before.SemanticSha256 != after.SemanticSha256 || before.UnreferencedSha256 != after.UnreferencedSha256 ||
            before.OriginalDocumentId is not null && before.OriginalDocumentId != after.OriginalDocumentId)
            throw new InvalidDataException("PDF rewrite changed document content, compatibility or identity.");
    }
    private static bool IsStorageObject(JsonElement wrapper)
    {
        if (wrapper.TryGetProperty("stream", out var stream))
        {
            var dict = stream.GetProperty("dict");
            if (!dict.TryGetProperty("/Type", out var type) || type.ValueKind != JsonValueKind.String) return false;
            return type.GetString() switch
            {
                "/ObjStm" => dict.EnumerateObject().All(property => property.Name is "/Type" or "/N" or "/First" or "/Length" or "/Filter" or "/DecodeParms"),
                "/XRef" => dict.EnumerateObject().All(property => property.Name is "/Type" or "/Size" or "/Index" or "/W" or "/Prev" or "/Root" or "/Info" or "/Encrypt" or "/ID" or "/Length" or "/Filter" or "/DecodeParms" or "/XRefStm"),
                _ => false
            };
        }
        var value = wrapper.GetProperty("value");
        if (value.ValueKind == JsonValueKind.Null) return true;
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty("/Linearized", out var linearized) &&
            linearized.ValueKind == JsonValueKind.Number && value.EnumerateObject().All(property =>
                property.Name is "/Linearized" or "/L" or "/H" or "/O" or "/E" or "/N" or "/T" or "/P" &&
                (property.Value.ValueKind == JsonValueKind.Number || property.Name == "/H" && property.Value.ValueKind == JsonValueKind.Array &&
                    property.Value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Number)));
    }
    private static bool IsReference(string value)
    {
        if (value.Length > 25 || !value.EndsWith(" R", StringComparison.Ordinal)) return false;
        var parts = value.Split(' ');
        return parts.Length == 3 && uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id != 0 &&
            uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var generation) && generation <= 65535;
    }
    private static string DecodeName(string value)
    {
        if (!value.StartsWith('/') || !value.Contains('#')) return value;
        var decoded = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '#') { decoded.Append(value[i]); continue; }
            if (i + 2 >= value.Length || !byte.TryParse(value.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                throw new InvalidDataException("Invalid escaped PDF name.");
            decoded.Append((char)code); i += 2;
        }
        return decoded.ToString();
    }
}
