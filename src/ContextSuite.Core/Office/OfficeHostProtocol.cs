using System.Text.Json;

namespace ContextSuite.Core.Office;

public static class OfficeHostProtocol
{
    public const string Policy = "office-pdf-final-1";
    public const int MaximumReplyBytes = 4096;
    public const long MaximumSourceBytes = 64 * 1024 * 1024;
    public const long MaximumOutputBytes = 128 * 1024 * 1024;

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
                "sourceBytes", "outputBytes", "sourceSha256", "outputSha256"], StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!expected.Remove(property.Name)) throw new InvalidDataException("Unexpected or repeated Office completion field.");
            if (expected.Count != 0 || root.GetProperty("version").GetInt32() != 1 || !root.GetProperty("completed").GetBoolean() ||
                root.GetProperty("requestId").GetString() != requestId.ToString("N") || root.GetProperty("format").GetString() != format ||
                root.GetProperty("policy").GetString() != Policy || root.GetProperty("calculation").GetString() != calculation ||
                root.GetProperty("sourceBytes").GetInt64() != sourceBytes ||
                !string.Equals(root.GetProperty("sourceSha256").GetString(), sourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Office completion does not match the admitted request.");
            var outputBytes = root.GetProperty("outputBytes").GetInt64();
            var outputHash = root.GetProperty("outputSha256").GetString();
            if (outputBytes is <= 0 or > MaximumOutputBytes || !Hash(outputHash))
                throw new InvalidDataException("Invalid Office candidate identity.");
            return new(requestId, format, calculation, sourceBytes, outputBytes, sourceSha256.ToUpperInvariant(), outputHash!.ToUpperInvariant());
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Invalid Office completion reply.", error); }
    }

    private static bool Hash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
}
