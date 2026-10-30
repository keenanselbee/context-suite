using System.Text.Json.Nodes;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;
using ContextSuite.Private.Images;

namespace ContextSuite.Private.Pdf;

// Change only certified JPEG stream bytes. The unchanged structural oracle then
// compares the complete expected document with qpdf's reopened result.
internal static class PdfImageOptimization
{
    internal sealed record Prepared(byte[] Json, PdfRewriteFacts Expected, int ChangedImages);

    internal static Prepared Prepare(byte[] inventory, ImageOptimizationPreset preset, CancellationToken token)
    {
        var original = PdfRewriteInventory.Read(inventory, token);
        if (preset is not (ImageOptimizationPreset.Balanced or ImageOptimizationPreset.Smallest))
            return new(inventory, original, 0);
        var root = JsonNode.Parse(inventory)!.AsObject();
        if (HasProtectedImageUse(root, token)) return new(inventory, original, 0);
        var objects = root["qpdf"]![1]!.AsObject();
        var changes = 0;
        var inspected = 0;
        long pixels = 0;
        foreach (var entry in objects)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Value?["stream"] is not JsonObject stream || stream["dict"] is not JsonObject dictionary ||
                !Eligible(dictionary)) continue;
            if (++inspected > 128) break;
            var width = dictionary["/Width"]!.GetValue<int>();
            var height = dictionary["/Height"]!.GetValue<int>();
            if (width <= 0 || height <= 0 || width > 16384 || height > 16384 ||
                (long)width * height > 40_000_000 || pixels + (long)width * height > 80_000_000) continue;
            pixels += (long)width * height;
            var bytes = Convert.FromBase64String(stream["data"]!.GetValue<string>());
            try
            {
                var header = ImageContainer.Read(bytes);
                if (header.Format != ImageFormat.Jpeg || header.Width != width || header.Height != height ||
                    header.Depth != 8) continue;
                var grayscale = new JpegLosslessEnvelope(bytes).IsGrayscale;
                if (grayscale != (Text(dictionary["/ColorSpace"]) == "/DeviceGray")) continue;
                var optimized = ImageAdapter.OptimizeImageBytes(bytes, preset, token);
                if (optimized.Bytes.Length >= bytes.Length) continue;
                stream["data"] = Convert.ToBase64String(optimized.Bytes);
                changes++;
            }
            catch (ImageFailureException error) when (error.Failure == ImageFailure.UnsupportedInput)
            {
                // An unsupported embedded representation stays exact; failures
                // validating a candidate are never converted into this outcome.
            }
        }
        if (changes == 0) return new(inventory, original, 0);
        // qpdf 12.4.1's streaming base64 reader requires literal base64
        // characters (not JSON's otherwise equivalent \u002B escape for '+').
        var updated = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(root,
            new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return new(updated, PdfRewriteInventory.Read(updated, token), changes);
    }

    private static bool HasProtectedImageUse(JsonNode? node, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (node is JsonObject dictionary)
        {
            foreach (var property in dictionary)
            {
                // A stream can be used through several aliases. Until all uses
                // have a reviewed role analysis, masks/attachments/transfer and
                // display-color overrides keep every image exact.
                if (property.Key is "/Mask" or "/SMask" or "/TR" or "/TR2" or "/EF" or "/EmbeddedFiles" or
                    "/DefaultRGB" or "/DefaultGray" or "/OutputIntents" ||
                    property.Key == "/Type" && Text(property.Value) == "/EmbeddedFile" ||
                    HasProtectedImageUse(property.Value, token)) return true;
            }
        }
        else if (node is JsonArray array)
            foreach (var item in array)
                if (HasProtectedImageUse(item, token)) return true;
        return false;
    }

    private static bool Eligible(JsonObject dictionary)
    {
        // Masks, alternate images, Decode/DecodeParms, external data, indirect
        // colorspaces, ICC spaces, and unknown keys keep their original bytes.
        if (dictionary.Any(pair => pair.Key is not ("/Type" or "/Subtype" or "/Width" or "/Height" or
            "/BitsPerComponent" or "/ColorSpace" or "/Filter" or "/Interpolate"))) return false;
        return (!dictionary.ContainsKey("/Type") || Text(dictionary["/Type"]) == "/XObject") && Text(dictionary["/Subtype"]) == "/Image" &&
            Text(dictionary["/Filter"]) == "/DCTDecode" &&
            Text(dictionary["/ColorSpace"]) is "/DeviceRGB" or "/DeviceGray" &&
            dictionary["/BitsPerComponent"] is JsonValue depth && depth.TryGetValue<int>(out var bits) && bits == 8 &&
            dictionary["/Width"] is JsonValue width && width.TryGetValue<int>(out _) &&
            dictionary["/Height"] is JsonValue height && height.TryGetValue<int>(out _);
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
