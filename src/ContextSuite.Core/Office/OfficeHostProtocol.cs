using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace ContextSuite.Core.Office;

public static class OfficeHostProtocol
{
    public const string Policy = "office-pdf-final-1";
    public const int MaximumReplyBytes = 36 * 1024;
    public const int MaximumFontReportBytes = 16 * 1024;
    public const long MaximumSourceBytes = 64 * 1024 * 1024;
    public const long MaximumOutputBytes = 128 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static OfficeHostCompletion ReadCompletion(ReadOnlyMemory<byte> bytes, int exitCode, Guid requestId,
        string format, string calculation, long sourceBytes, string sourceSha256)
    {
        if (exitCode != 0 || requestId == Guid.Empty || bytes.Length is <= 0 or > MaximumReplyBytes ||
            format is not ("docx" or "xlsx" or "pptx") ||
            (format == "xlsx" ? calculation is not ("cached" or "recalculate") : calculation != "none") ||
            sourceBytes is <= 0 or > MaximumSourceBytes || !Hash(sourceSha256))
            throw new InvalidDataException("Office owner did not complete a valid request.");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Missing Office completion object.");
            var expected = new HashSet<string>(["version", "requestId", "completed", "format", "policy", "calculation",
                "sourceBytes", "outputBytes", "sourceSha256", "outputSha256", "fontReports"], StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!expected.Remove(property.Name)) throw new InvalidDataException("Unexpected or repeated Office completion field.");
            if (expected.Count != 0 || root.GetProperty("version").GetInt32() != 2 || !root.GetProperty("completed").GetBoolean() ||
                root.GetProperty("requestId").GetString() != requestId.ToString("N") || root.GetProperty("format").GetString() != format ||
                root.GetProperty("policy").GetString() != Policy || root.GetProperty("calculation").GetString() != calculation ||
                root.GetProperty("sourceBytes").GetInt64() != sourceBytes ||
                !string.Equals(root.GetProperty("sourceSha256").GetString(), sourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Office completion does not match the admitted request.");
            var outputBytes = root.GetProperty("outputBytes").GetInt64();
            var outputHash = root.GetProperty("outputSha256").GetString();
            if (outputBytes is <= 0 or > MaximumOutputBytes || !Hash(outputHash))
                throw new InvalidDataException("Invalid Office candidate identity.");
            var fonts = ReadFontReports(root.GetProperty("fontReports"));
            return new(requestId, format, calculation, sourceBytes, outputBytes, sourceSha256.ToUpperInvariant(), outputHash!.ToUpperInvariant(), fonts);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Invalid Office completion reply.", error); }
    }

    private static bool Hash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

    public static void ValidateFontFamilies(ImmutableArray<string> families)
    {
        if (families.IsDefault || families.Length > 64) throw new InvalidDataException("Missing or oversized Office font report.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bytes = 0;
        foreach (var name in families)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl) || !names.Add(name))
                throw new InvalidDataException("Invalid or repeated Office font family.");
            try { bytes += StrictUtf8.GetByteCount(name); }
            catch (EncoderFallbackException error) { throw new InvalidDataException("Invalid Office font name encoding.", error); }
        }
        if (bytes > MaximumFontReportBytes) throw new InvalidDataException("Office font names exceed the report limit.");
    }

    private static ImmutableArray<string> ReadFontReports(JsonElement reports)
    {
        if (reports.ValueKind != JsonValueKind.Array || reports.GetArrayLength() > 4)
            throw new InvalidDataException("Invalid Office font callback count.");
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        foreach (var report in reports.EnumerateArray())
        {
            var hex = report.GetString();
            if (hex is null || hex.Length == 0 || hex.Length % 2 != 0 || hex.Length / 2 > MaximumFontReportBytes - total ||
                !hex.All(char.IsAsciiHexDigit)) throw new InvalidDataException("Invalid Office font callback bytes.");
            total += hex.Length / 2;
            var payload = Convert.FromHexString(hex);
            try { _ = StrictUtf8.GetCharCount(payload); }
            catch (DecoderFallbackException error) { throw new InvalidDataException("Invalid Office font callback encoding.", error); }
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 3 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
                !root.TryGetProperty("fontsmissing", out var fonts) || fonts.ValueKind != JsonValueKind.Array ||
                fonts.GetArrayLength() is < 1 or > 64) throw new InvalidDataException("Invalid Office font callback shape.");
            var current = fonts.EnumerateArray().Select(font => font.GetString()!).ToImmutableArray();
            ValidateFontFamilies(current);
            foreach (var name in current) names.TryAdd(name, name);
        }
        var result = names.Values.Order(StringComparer.Ordinal).ToImmutableArray();
        ValidateFontFamilies(result);
        return result;
    }
}
