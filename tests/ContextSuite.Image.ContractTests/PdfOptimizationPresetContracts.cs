using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;
using ContextSuite.Private.Images;
using ContextSuite.Private.Pdf;
using ImageMagick;

internal static class PdfOptimizationPresetContracts
{
    internal static async Task RunAsync(string root, string engine, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        _ = new ImageAdapter(Path.Combine(root, "image-scratch"));
        using var image = new MagickImage(PngFixture.Create(257, 193, 8, 2, (x, y) =>
        {
            var level = 50 + x / 8 + y / 8 + (x * 13 + y * 7) % 3;
            return [(ushort)(level * 257), (ushort)(level * 257), (ushort)(level * 257)];
        }));
        image.ColorType = ColorType.TrueColor;
        image.Settings.ColorType = ColorType.TrueColor;
        image.Quality = 100;
        image.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "1x1");
        image.Settings.SetDefine(MagickFormat.Jpeg, "optimize-coding", false);
        using var encoded = new MemoryStream();
        image.Write(encoded, MagickFormat.Jpeg);
        var original = Fixture(encoded.ToArray());
        var sourcePath = Path.Combine(root, "original.pdf");
        await File.WriteAllBytesAsync(sourcePath, original);
        var inventory = await Inventory(sourcePath);
        using var adapter = new PdfProbeAdapter(engine, Path.Combine(root, "pdf-scratch"));
        foreach (var preset in Enum.GetValues<ImageOptimizationPreset>())
        {
            var prepared = PdfImageOptimization.Prepare(inventory, preset, default);
            await File.WriteAllBytesAsync(Path.Combine(root, preset + "-expected.json"), prepared.Json);
            var lossy = preset is ImageOptimizationPreset.Balanced or ImageOptimizationPreset.Smallest;
            check(prepared.ChangedImages == (lossy ? 1 : 0), $"PDF {preset}: changes only one eligible shared JPEG stream");
            var restored = JsonNode.Parse(prepared.Json)!;
            restored["qpdf"]![1]!["obj:5 0 R"]!["stream"]!["data"] =
                JsonNode.Parse(inventory)!["qpdf"]![1]!["obj:5 0 R"]!["stream"]!["data"]!.DeepClone();
            PdfRewriteInventory.RequirePreserved(PdfRewriteInventory.Read(inventory),
                PdfRewriteInventory.Read(JsonSerializer.SerializeToUtf8Bytes(restored)));
            check(true, $"PDF {preset}: certified image payload is the only semantic inventory change");
            var result = await adapter.OptimizeAsync(original, preset, default);
            var unchangedSource = await File.ReadAllBytesAsync(sourcePath);
            check(result.Policy == PdfOptimizationPlan.PolicyFor(preset) &&
                (lossy ? result.Bytes.Length < original.Length : result.Bytes.Length <= original.Length) &&
                original.SequenceEqual(unchangedSource),
                $"PDF {preset}: smaller or unchanged validated result with unchanged source and exact policy");
            var path = Path.Combine(root, preset + ".pdf");
            await File.WriteAllBytesAsync(path, result.Bytes);
            var actual = await Inventory(path);
            PdfRewriteInventory.RequirePreserved(prepared.Expected, PdfRewriteInventory.Read(actual));
            check(true, $"PDF {preset}: complete expected graph survives qpdf update and rewrite");
            if (lossy)
            {
                Refuse(() => PdfRewriteInventory.RequirePreserved(PdfRewriteInventory.Read(inventory), PdfRewriteInventory.Read(actual)),
                    $"PDF {preset}: original exact-content oracle detects image change");
                var modified = JsonNode.Parse(prepared.Json)!;
                modified["qpdf"]![1]!["obj:6 0 R"]!["value"]!["/BaseFont"] = "/Courier";
                Refuse(() => PdfRewriteInventory.RequirePreserved(prepared.Expected,
                    PdfRewriteInventory.Read(JsonSerializer.SerializeToUtf8Bytes(modified))),
                    $"PDF {preset}: changing a font remains a preservation failure");
            }
        }
        foreach (var protectedKey in new[] { "/SMask", "/Mask", "/EF", "/TR", "/TR2", "/DefaultRGB", "/DefaultGray", "/OutputIntents" })
        {
            var variant = JsonNode.Parse(inventory)!;
            variant["qpdf"]![1]!["obj:1 0 R"]!["value"]![protectedKey] = "5 0 R";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(variant);
            check(PdfImageOptimization.Prepare(bytes, ImageOptimizationPreset.Smallest, default).ChangedImages == 0,
                "PDF image use: inverse " + protectedKey + " reference keeps all image samples exact");
        }
        var wrongColor = JsonNode.Parse(inventory)!;
        wrongColor["qpdf"]![1]!["obj:5 0 R"]!["stream"]!["dict"]!["/ColorSpace"] = "/DeviceGray";
        check(PdfImageOptimization.Prepare(JsonSerializer.SerializeToUtf8Bytes(wrongColor), ImageOptimizationPreset.Smallest, default).ChangedImages == 0,
            "PDF image admission: declared color space must match JPEG component count");
        foreach (var protectedKey in new[] { "/Mask", "/SMask", "/Decode", "/DecodeParms", "/Alternates", "/Metadata" })
        {
            var variant = JsonNode.Parse(inventory)!;
            variant["qpdf"]![1]!["obj:5 0 R"]!["stream"]!["dict"]![protectedKey] = JsonValue.Create("/Unsupported");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(variant);
            var prepared = PdfImageOptimization.Prepare(bytes, ImageOptimizationPreset.Smallest, default);
            check(prepared.ChangedImages == 0 && prepared.Json.SequenceEqual(bytes),
                "PDF image admission: keeps " + protectedKey + " stream unchanged");
        }
        var signed = JsonNode.Parse(inventory)!;
        signed["qpdf"]![1]!["obj:1 0 R"]!["value"]!["/SigFlags"] = 3;
        Refuse(() => PdfImageOptimization.Prepare(JsonSerializer.SerializeToUtf8Bytes(signed), ImageOptimizationPreset.Balanced, default),
            "PDF image admission: signature-related graph refused before image processing");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var stopped = false;
        try { await adapter.OptimizeAsync(original, ImageOptimizationPreset.Smallest, cancelled.Token); }
        catch (OperationCanceledException) { stopped = true; }
        check(stopped && !Directory.EnumerateFileSystemEntries(Path.Combine(root, "pdf-scratch")).Any(),
            "PDF preset cancellation preserves input and leaves no scratch snapshots");

        void Refuse(Action action, string label)
        {
            var refused = false;
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { refused = true; }
            check(refused, label);
        }

        async Task<byte[]> Inventory(string path)
        {
            var start = new ProcessStartInfo(Path.Combine(engine, "qpdf.exe")) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "--suppress-recovery", "--json-output=2", "--decode-level=generalized", "--json-stream-data=inline", path })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidDataException(await error);
            return Encoding.UTF8.GetBytes(await output);
        }
    }

    private static byte[] Fixture(byte[] jpeg)
    {
        var objects = new List<byte[]>
        {
            Bytes("<< /Type /Catalog /Pages 2 0 R >>"),
            Bytes("<< /Type /Pages /Kids [3 0 R 7 0 R] /Count 2 >>"),
            Bytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 250] /Resources << /XObject << /Im 5 0 R >> /Font << /F 6 0 R >> >> /Contents 4 0 R >>"),
            Stream(Bytes("q 257 0 0 193 10 10 cm /Im Do Q\nBT /F 12 Tf 10 225 Td (Searchable text remains exact) Tj ET\n"), ""),
            Stream(jpeg, "/Type /XObject /Subtype /Image /Width 257 /Height 193 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode "),
            Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"),
            Bytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 250] /Resources << /XObject << /Im 5 0 R >> /Font << /F 6 0 R >> >> /Contents 4 0 R >>")
        };
        using var output = new MemoryStream();
        output.Write(Bytes("%PDF-1.4\n"));
        var offsets = new List<long>();
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position);
            output.Write(Bytes($"{index + 1} 0 obj\n"));
            output.Write(objects[index]);
            output.Write(Bytes("\nendobj\n"));
        }
        var xref = output.Position;
        output.Write(Bytes($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets) output.Write(Bytes($"{offset:D10} 00000 n \n"));
        output.Write(Bytes($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return output.ToArray();

        static byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);
        static byte[] Stream(byte[] data, string dictionary)
        {
            using var stream = new MemoryStream();
            stream.Write(Bytes($"<< {dictionary}/Length {data.Length} >>\nstream\n"));
            stream.Write(data);
            stream.Write(Bytes("\nendstream"));
            return stream.ToArray();
        }
    }
}
