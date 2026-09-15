using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Core.Office;

// Generated PDF output policy, not a general-purpose PDF safety certificate.
// Structural checks and a separate page reader must both succeed before copying.
public static class OfficePdfPolicy
{
    public const string Policy = "office-pdf-validated-1";
    public const int MaximumPages = 4096;

    public static int ReadPageCount(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is <= 0 or > 32 || !int.TryParse(Encoding.ASCII.GetString(bytes).Trim(),
            NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count is <= 0 or > MaximumPages)
            throw new InvalidDataException("Office PDF page count is unavailable or exceeds its limit.");
        return count;
    }

    public static void CheckObjects(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
    {
        // Includes all objects, even unreachable ones, and refuses aliases,
        // unresolved references, encryption, signatures and external streams.
        PdfRewriteInventory.CheckAdmission(bytes, token);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
        var qpdf = document.RootElement.GetProperty("qpdf");
        if (qpdf[0].GetProperty("pdfversion").GetString() != "1.7")
            throw new InvalidDataException("Office PDF does not match the fixed export version.");
        var objects = qpdf[1];
        Inspect(objects);

        void Inspect(JsonElement value)
        {
            token.ThrowIfCancellationRequested();
            if (value.ValueKind == JsonValueKind.Array)
            { foreach (var item in value.EnumerateArray()) Inspect(item); return; }
            if (value.ValueKind != JsonValueKind.Object) return;
            foreach (var property in value.EnumerateObject())
            {
                var name = Decode(property.Name);
                if (name is "/AcroForm" or "/XFA" or "/JavaScript" or "/JS" or "/AA" or "/EmbeddedFiles" or "/EF" or
                    "/RichMediaContent" or "/RichMediaSettings" or "/Collection")
                    throw new InvalidDataException("Office PDF contains unexpected interactive or embedded content.");
                if (name == "/OpenAction") Destination(Resolve(property.Value));
                if (name == "/S" && property.Value.ValueKind == JsonValueKind.String && Decode(property.Value.GetString()!) is
                    "/JavaScript" or "/Launch" or "/SubmitForm" or "/ResetForm" or "/ImportData" or "/GoToE" or
                    "/Rendition" or "/Movie" or "/Sound" or "/Trans" or "/SetOCGState" or "/Hide")
                    throw new InvalidDataException("Office PDF contains an unexpected action.");
                if (name is "/Type" or "/Subtype" && property.Value.ValueKind == JsonValueKind.String &&
                    Decode(property.Value.GetString()!) is "/EmbeddedFile" or "/RichMedia" or "/Movie" or "/Sound" or
                    "/Screen" or "/Widget" or "/FileAttachment" or "/3D")
                    throw new InvalidDataException("Office PDF contains an unexpected annotation or embedded object.");
                Inspect(property.Value);
            }
            var type = Name(value, "/Type"); var subtype = Name(value, "/Subtype");
            if (type == "/Action") Action(value);
            if ((type == "/Annot" || subtype == "/Link" || Property(value, "/Title", out _) && Property(value, "/Parent", out _)) &&
                Property(value, "/A", out var action)) Action(Resolve(action));
        }
        JsonElement Resolve(JsonElement value)
        {
            for (var depth = 0; depth < 32; depth++)
            {
                if (value.ValueKind != JsonValueKind.String || !objects.TryGetProperty("obj:" + value.GetString(), out var wrapper)) return value;
                if (!wrapper.TryGetProperty("value", out value)) throw new InvalidDataException("Unexpected PDF stream reference.");
            }
            throw new InvalidDataException("Office PDF reference chain exceeds its limit.");
        }
        void Action(JsonElement action)
        {
            if (action.ValueKind != JsonValueKind.Object || Name(action, "/S") is not ("/GoTo" or "/GoToR" or "/URI") ||
                action.EnumerateObject().Any(property => Decode(property.Name) is not ("/Type" or "/S" or "/D" or "/URI" or "/IsMap" or "/F" or "/NewWindow")))
                throw new InvalidDataException("Office PDF hyperlink action is outside the fixed output policy.");
        }
        void Destination(JsonElement destination)
        {
            // An opening page destination is ordinary layout, not an automatic action.
            if (destination.ValueKind == JsonValueKind.String && destination.GetString() is { } text &&
                (text.StartsWith("u:", StringComparison.Ordinal) || text.StartsWith("b:", StringComparison.Ordinal))) return;
            if (destination.ValueKind != JsonValueKind.Array || destination.GetArrayLength() is < 2 or > 6 ||
                Name(Resolve(destination[0]), "/Type") != "/Page" || destination[1].ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Office PDF opening destination is invalid.");
            var count = Decode(destination[1].GetString()!) switch
            { "/Fit" or "/FitB" => 2, "/FitH" or "/FitV" or "/FitBH" or "/FitBV" => 3, "/XYZ" => 5, "/FitR" => 6, _ => 0 };
            if (count != destination.GetArrayLength() || destination.EnumerateArray().Skip(2).Any(item => item.ValueKind != JsonValueKind.Null &&
                (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var number) || !double.IsFinite(number))))
                throw new InvalidDataException("Office PDF opening destination is outside its bounds.");
        }
    }

    private static string? Name(JsonElement value, string key) => Property(value, key, out var field) &&
        field.ValueKind == JsonValueKind.String ? Decode(field.GetString()!) : null;
    private static bool Property(JsonElement value, string key, out JsonElement field)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
                if (Decode(property.Name) == key) { field = property.Value; return true; }
        field = default; return false;
    }
    private static string Decode(string name) => Regex.Replace(name, "#([0-9A-Fa-f]{2})",
        match => ((char)byte.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString(), RegexOptions.CultureInvariant);
}
