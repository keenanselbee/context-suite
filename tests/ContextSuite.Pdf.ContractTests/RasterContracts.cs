using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Pdf;
using ContextSuite.Private.Pdf;
using ImageMagick;

internal static class RasterContracts
{
    public static async Task RunAsync(string binary, string fixtures, string reference, string root)
    {
        if (Directory.Exists(root)) throw new IOException("Use a new rendering evidence directory.");
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        var path = Path.Combine(fixtures, "authored original ü.pdf");
        var original = SHA256.HashData(await File.ReadAllBytesAsync(path));
        using var adapter = new PdfRasterAdapter(binary, Path.Combine(root, "scratch"));
        var source = await adapter.ProbeAsync(new(Guid.NewGuid(), path), default);
        Check(source.Document.Pages.Length == 2, "native inspection returns both page geometries");
        for (var index = 0; index < source.Document.Pages.Length; index++)
        {
            var result = await adapter.RenderAsync(source, index, default, transparentBackground: true);
            await File.WriteAllBytesAsync(Path.Combine(root, $"page-{index + 1}.png"), result.Bytes);
            var expected = await File.ReadAllBytesAsync(Path.Combine(reference, $"transparent-{index + 1}.bgra"));
            Check(result.PixelSha256 == Convert.ToHexString(SHA256.HashData(expected)), "page " + (index + 1) + " matches separate PDFium evaluation pixels");
            using var png = new MagickImage(result.Bytes);
            var pixels = png.GetPixels().ToByteArray(PixelMapping.BGRA)!;
            Check(pixels.AsSpan().SequenceEqual(expected) && png.GetProfile("icc") is not null && png.Depth == 8,
                "page " + (index + 1) + " PNG preserves pixels, alpha and color profile");
            Check(PhysicalResolution(result.Bytes) is 5905 or 5906, "page " + (index + 1) + " PNG stores 150 DPI within one pixel per metre");
            if (index == 0) Check(pixels.Where((_, i) => i % 4 == 3).Any(alpha => alpha == 0), "unpainted page areas remain transparent");
            var white = await adapter.RenderAsync(source, index, default);
            using var whitePng = new MagickImage(white.Bytes);
            var whitePixels = whitePng.GetPixels().ToByteArray(PixelMapping.BGRA)!;
            Check(whitePixels.Where((_, i) => i % 4 == 3).All(alpha => alpha == 255), "default paper is fully opaque");
            var maximumError = 0;
            for (var offset = 0; offset < pixels.Length; offset += 4)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    var composited = 255 + (pixels[offset + channel] - 255) * (pixels[offset + 3] / 255.0);
                    maximumError = Math.Max(maximumError, (int)Math.Ceiling(Math.Abs(whitePixels[offset + channel] - composited)));
                }
            }
            Check(maximumError <= 1, "white paper preserves foreground and antialiasing within rounding");
            var whiteReference = await File.ReadAllBytesAsync(Path.Combine(reference, $"source-{index + 1}.bgra"));
            Check(whiteReference.Length == whitePixels.Length && whitePixels.Zip(whiteReference, (a, b) => Math.Abs(a - b)).Max() <= 1,
                "white PNG matches independent white-background rendering within one channel level");
            await File.WriteAllBytesAsync(Path.Combine(root, $"page-{index + 1}-white.png"), white.Bytes);
            var jpeg = await adapter.RenderAsync(source, index, default, format: PdfPageFormat.Jpeg);
            using var jpegImage = new MagickImage(jpeg.Bytes);
            Check(jpeg.Policy == PdfRasterProtocol.JpegPolicy && jpegImage.Format == MagickFormat.Jpeg &&
                jpegImage.Quality == 90 && jpegImage.Width == whitePng.Width && jpegImage.Height == whitePng.Height &&
                jpegImage.IsOpaque && jpegImage.GetProfile("icc") is not null &&
                Math.Abs(jpegImage.Density.X - PdfRasterProtocol.Dpi) <= 1 && Math.Abs(jpegImage.Density.Y - PdfRasterProtocol.Dpi) <= 1,
                "page " + (index + 1) + " JPEG retains fixed quality, geometry, opaque paper and sRGB");
            var jpegPixels = jpegImage.GetPixels().ToByteArray(PixelMapping.BGRA)!;
            double squaredError = 0;
            for (var offset = 0; offset < jpegPixels.Length; offset += 4)
                for (var channel = 0; channel < 3; channel++)
                    squaredError += Math.Pow(jpegPixels[offset + channel] - whitePixels[offset + channel], 2);
            Check(Math.Sqrt(squaredError / (jpegPixels.Length / 4 * 3)) <= 16,
                "page " + (index + 1) + " JPEG stays within the decoded pixel error policy");
        }
        var repeat = await adapter.RenderAsync(source, 0, default, transparentBackground: true);
        Check(repeat.PixelSha256 == Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(reference, "transparent-1.bgra")))),
            "repeat rendering retains the same pixel result");
        foreach (var name in new[] { "encrypted.pdf", "owner-protected.pdf" })
            await Reject(() => adapter.ProbeAsync(new(Guid.NewGuid(), Path.Combine(fixtures, name)), default), "protected document refused: " + name);
        foreach (var name in new[] { "signature-field-canary.pdf", "javascript-canary.pdf" })
        {
            var canary = await adapter.ProbeAsync(new(Guid.NewGuid(), Path.Combine(fixtures, name)), default);
            var rendered = await adapter.RenderAsync(canary, 0, default);
            Check(rendered.Bytes.Length > 0, "visual copy can render without invoking document actions: " + name);
        }
        var oversized = Path.Combine(root, "oversized.pdf");
        using (var file = File.Create(oversized)) file.SetLength(PdfRasterProtocol.MaximumSourceBytes + 1L);
        try { await adapter.ProbeAsync(new(Guid.NewGuid(), oversized), default); throw new Exception("Source bound ignored."); }
        catch (ContextSuite.Core.Images.ImageFailureException error) when (error.Failure == ContextSuite.Core.Images.ImageFailure.ResourceLimit)
        { checks.Add("oversized source classified as a resource limit before hashing or native execution"); }
        using (var file = File.OpenRead(oversized))
        {
            var reply = await ContextSuite.Private.IO.SeekableMediaProcess.RunAsync(Path.Combine(binary, "ContextSuite.PdfRenderer.exe"), root, file,
                ["--inspect"], PdfRasterProtocol.MaximumInspectionBytes, default);
            try { PdfRasterProtocol.ReadInspection(reply); throw new Exception("Native source byte limit was not enforced."); }
            catch (ContextSuite.Core.Images.ImageFailureException error) when (error.Failure == ContextSuite.Core.Images.ImageFailure.ResourceLimit)
            { Check(reply.Length == 32 && file.Length == PdfRasterProtocol.MaximumSourceBytes + 1L, "native host independently returns a complete source-size refusal"); }
        }
        var invalidPdf = Path.Combine(root, "invalid.pdf");
        await File.WriteAllTextAsync(invalidPdf, "%PDF-1.7\nnot a document");
        await Reject(() => adapter.ProbeAsync(new(Guid.NewGuid(), invalidPdf), default), "malformed source refused");
        var deep = root;
        while (deep.Length < 270) deep = Path.Combine(deep, "generated-long-path-ü");
        Directory.CreateDirectory(deep);
        var longSource = Path.Combine(deep, "original.pdf"); File.Copy(path, longSource);
        using (var longAdapter = new PdfRasterAdapter(binary, Path.Combine(deep, "scratch")))
        {
            var longFacts = await longAdapter.ProbeAsync(new(Guid.NewGuid(), longSource), default);
            var longResult = await longAdapter.RenderAsync(longFacts, 0, default, transparentBackground: true);
            Check(longResult.PixelSha256 == repeat.PixelSha256, "long Unicode source and scratch paths retain exact pixels");
        }
        await Reject(() => adapter.RenderAsync(source, -1, default), "negative page refused");
        await Reject(() => adapter.RenderAsync(source, 2, default), "out-of-document page refused");
        await Reject(() => adapter.RenderAsync(source, 0, default, true, PdfPageFormat.Jpeg), "transparent JPEG refused");
        await Reject(() => adapter.RenderAsync(source, 0, default, format: (PdfPageFormat)9), "unknown image format refused");
        await Reject(() => adapter.RenderAsync(source with { Sha256 = new('0', 64) }, 0, default), "changed source digest refused");
        var changed = Path.Combine(root, "changed.pdf"); File.Copy(path, changed);
        var stale = await adapter.ProbeAsync(new(Guid.NewGuid(), changed), default);
        await File.AppendAllTextAsync(changed, "\n");
        await Reject(() => adapter.RenderAsync(stale, 0, default), "changed source length refused");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await adapter.RenderAsync(source, 0, cancelled.Token); throw new Exception("Cancellation ignored."); }
        catch (OperationCanceledException) { checks.Add("pre-cancellation remains cancellation"); }
        try { using var writable = new FileStream(Path.Combine(binary, "pdfium.dll"), FileMode.Open, FileAccess.Write, FileShare.ReadWrite); throw new Exception("Engine lease allowed writing."); }
        catch (IOException) { checks.Add("PDFium identity lease prevents mutation while adapter is active"); }
        foreach (var name in new[] { "qpdf30.dll", "msvcp140_2.dll" })
        {
            try { using var writable = new FileStream(Path.Combine(binary, name), FileMode.Open, FileAccess.Write, FileShare.ReadWrite); throw new Exception("Geometry dependency lease allowed writing."); }
            catch (IOException) { checks.Add("geometry dependency is leased against mutation: " + name); }
        }
        var geometryTampered = Path.Combine(root, "geometry-tampered"); Directory.CreateDirectory(geometryTampered);
        foreach (var dependency in Directory.GetFiles(binary)) File.Copy(dependency, Path.Combine(geometryTampered, Path.GetFileName(dependency)));
        var qpdfFile = Path.Combine(geometryTampered, "qpdf30.dll");
        var qpdfBytes = await File.ReadAllBytesAsync(qpdfFile); qpdfBytes[^1] ^= 1;
        await File.WriteAllBytesAsync(qpdfFile, qpdfBytes);
        try { using var invalid = new PdfRasterAdapter(geometryTampered, Path.Combine(root, "invalid-geometry-scratch")); throw new Exception("Geometry dependency identity ignored."); }
        catch (InvalidDataException) { checks.Add("changed qpdf geometry dependency is refused before native execution"); }
        File.Delete(qpdfFile);
        try { using var invalid = new PdfRasterAdapter(geometryTampered, Path.Combine(root, "missing-geometry-scratch")); throw new Exception("Missing geometry dependency accepted."); }
        catch (FileNotFoundException) { checks.Add("missing qpdf geometry dependency is refused before native execution"); }
        var tampered = Path.Combine(root, "tampered"); Directory.CreateDirectory(tampered);
        File.Copy(Path.Combine(binary, "pdfium.dll"), Path.Combine(tampered, "pdfium.dll"));
        var host = await File.ReadAllBytesAsync(Path.Combine(binary, "ContextSuite.PdfRenderer.exe")); host[^1] ^= 1;
        await File.WriteAllBytesAsync(Path.Combine(tampered, "ContextSuite.PdfRenderer.exe"), host);
        try { using var invalid = new PdfRasterAdapter(tampered, Path.Combine(root, "invalid-scratch")); throw new Exception("Host identity ignored."); }
        catch (InvalidDataException) { checks.Add("modified renderer is rejected before native execution"); }
        Check(SHA256.HashData(await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(original), "original PDF is unchanged across rendering and refusal");
        await File.WriteAllTextAsync(Path.Combine(root, "raster-adapter.json"), JsonSerializer.Serialize(new { source, checks }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} private PDF raster checks. Evidence: {root}");

        void Check(bool passed, string description)
        {
            if (!passed) throw new Exception(description);
            checks.Add(description);
        }
        async Task Reject(Func<Task> action, string description)
        {
            try { await action(); throw new Exception("Unexpected acceptance: " + description); }
            catch (InvalidDataException) { checks.Add(description); }
        }
    }

    private static uint PhysicalResolution(byte[] png)
    {
        for (var offset = 8; offset <= png.Length - 12;)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset)));
            if (png.AsSpan(offset + 4, 4).SequenceEqual("pHYs"u8))
            {
                if (length != 9 || png[offset + 16] != 1 || !png.AsSpan(offset + 8, 4).SequenceEqual(png.AsSpan(offset + 12, 4)))
                    throw new Exception("Invalid physical pixel resolution.");
                return BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8));
            }
            offset = checked(offset + length + 12);
        }
        throw new Exception("Missing physical resolution.");
    }
}
