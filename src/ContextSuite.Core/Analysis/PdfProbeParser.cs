using System.Text.Json;

namespace ContextSuite.Core.Analysis;

public static class PdfProbeParser
{
    public const int MaximumBytes = 1024 * 1024;
    private const int MaximumItems = 4096;

    public static PdfProbeFacts Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumBytes) throw new InvalidDataException("PDF probe size limit exceeded.");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            RejectDuplicateKeys(root);
            if (Required(root, "version").GetInt32() != 2) throw new InvalidDataException("Unsupported PDF probe schema.");
            var pages = Required(root, "pages");
            RequireArray(pages);
            var expectedPage = 1;
            foreach (var page in pages.EnumerateArray())
            {
                if (Required(page, "pageposfrom1").GetInt32() != expectedPage++)
                    throw new InvalidDataException("Conflicting PDF page positions.");
            }
            var form = Required(root, "acroform");
            var hasForms = Required(form, "hasacroform").GetBoolean();
            var needsAppearances = Required(form, "needappearances").GetBoolean();
            var fields = Required(form, "fields");
            RequireArray(fields);
            var signatures = 0;
            foreach (var field in fields.EnumerateArray())
            {
                var type = Required(field, "fieldtype").GetString();
                if (type is null || type.Length > 128) throw new InvalidDataException("Invalid PDF form field type.");
                if (type == "/Sig") signatures++;
            }
            if (!hasForms && (fields.GetArrayLength() != 0 || needsAppearances))
                throw new InvalidDataException("Conflicting PDF form declarations.");
            var attachments = Required(root, "attachments");
            if (attachments.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid PDF attachment inventory.");
            var attachmentCount = attachments.EnumerateObject().Count();
            if (attachmentCount > MaximumItems) throw new InvalidDataException("PDF attachment count limit exceeded.");
            var outlines = CountOutlines(Required(root, "outlines"));
            var facts = new PdfProbeFacts(pages.GetArrayLength(), Required(Required(root, "encrypt"), "encrypted").GetBoolean(),
                hasForms, fields.GetArrayLength(), signatures, attachmentCount, outlines, needsAppearances);
            facts.Validate();
            return facts;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("Invalid structured PDF probe result.", error); }
    }

    private static JsonElement Required(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property))
            throw new InvalidDataException("Incomplete PDF probe result.");
        return property;
    }

    private static void RequireArray(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > MaximumItems)
            throw new InvalidDataException("PDF probe array limit exceeded.");
    }

    private static int CountOutlines(JsonElement array)
    {
        RequireArray(array);
        var count = array.GetArrayLength();
        foreach (var item in array.EnumerateArray())
        {
            count += CountOutlines(Required(item, "kids"));
            if (count > MaximumItems) throw new InvalidDataException("PDF outline count limit exceeded.");
        }
        return count;
    }

    private static void RejectDuplicateKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Ambiguous PDF probe properties.");
                RejectDuplicateKeys(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicateKeys(item);
    }
}
