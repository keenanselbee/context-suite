using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextSuite.Core.Pdf;
using ContextSuite.Private.Images;
using ContextSuite.Private.Pdf;
using ImageMagick;

internal static class ImagePdfValidationContracts
{
    public static async Task RunAsync(ImagePdfCandidate candidate, ConfirmedImagePdf plan, ImageAdapter images, string binary, string root)
    {
        Directory.CreateDirectory(root);
        var scratch = Path.Combine(root, "scratch");
        using var validator = new ImagePdfValidator(binary, scratch);
        var checks = new List<string>();
        var hash = Convert.ToHexString(SHA256.HashData(candidate.Bytes));
        Check(await validator.ValidateAsync(candidate, plan, default) == hash, "native qpdf validates all 23 ordered pages against original sample digests");
        Check(!Directory.EnumerateFileSystemEntries(scratch).Any(), "successful validation removes its owned snapshot");
        var edits = new (string Name, int Object, string Before, string After)[] {
            ("catalog action", 1, "/Type /Catalog", "/Type /Catalog /OpenAction 3 0 R"),
            ("catalog metadata", 1, "/Type /Catalog", "/Type /Catalog /Metadata 7 0 R"),
            ("wrong page count", 2, "/Count 23", "/Count 22"),
            ("reordered pages", 2, "3 0 R 8 0 R", "8 0 R 3 0 R"),
            ("repeated page", 2, "3 0 R 8 0 R", "3 0 R 3 0 R"),
            ("wrong parent", 3, "/Parent 2 0 R", "/Parent 1 0 R"),
            ("page rotation", 3, "/Type /Page", "/Type /Page /Rotate 90"),
            ("page crop", 3, "/Type /Page", "/Type /Page /CropBox [0 0 1 1]"),
            ("additional resource", 3, "/Resources <<", "/Resources << /Font << >>"),
            ("extra content operation", 4, "/Im0 Do", "/Im0 Do /Im0 Do"),
            ("wrong content transform", 4, "3 0 0 2.25", "2 0 0 2.25"),
            ("wrong image width", 5, "/Width 4", "/Width 5"),
            ("inflation beyond declared samples", 5, "/Width 4", "/Width 1"),
            ("oversized image declaration", 5, "/Width 4", "/Width 999999"),
            ("reduced precision declaration", 5, "/BitsPerComponent 8", "/BitsPerComponent 4"),
            ("interpolation enabled", 5, "/Interpolate false", "/Interpolate true"),
            ("external image stream", 5, "/Filter /FlateDecode", "/F (file:///external) /Filter /FlateDecode"),
            ("unapproved filter", 5, "/FlateDecode", "/ASCII85Decode"),
            ("unexpected decode parameters", 5, "/Filter /FlateDecode", "/Filter /FlateDecode /DecodeParms << /Predictor 12 >>"),
            ("unexpected image decode", 5, "/Interpolate false", "/Interpolate false /Decode [1 0]"),
            ("changed profile channels", 7, "/N 1", "/N 3"),
            ("changed profile alternate", 7, "/DeviceGray", "/DeviceRGB"),
            ("changed alpha dimensions", 13, "/Width 4", "/Width 5"),
            ("alpha matte", 13, "/ColorSpace /DeviceGray", "/ColorSpace /DeviceGray /Matte [1]"),
            ("alpha color space", 13, "/DeviceGray", "/DeviceRGB"),
        };
        foreach (var edit in edits)
        {
            var mutated = Edit(candidate.Bytes, edit.Object, text => {
                if (!text.Contains(edit.Before, StringComparison.Ordinal)) throw new Exception("Fixture edit target missing: " + edit.Name);
                return text.Replace(edit.Before, edit.After, StringComparison.Ordinal);
            });
            await Reject(candidate with { Bytes = mutated }, plan, edit.Name);
        }
        await Reject(candidate with { Bytes = Edit(candidate.Bytes, 7, text => {
            var start = text.IndexOf("stream\n", StringComparison.Ordinal) + 7;
            return text[..(start + 64)] + (char)(text[start + 64] ^ 1) + text[(start + 65)..];
        }) }, plan, "modified ICC bytes");
        await Reject(candidate with { Bytes = candidate.Bytes[..^12] }, plan, "truncated cross-reference trailer");
        await Reject(candidate with { Bytes = Edit(candidate.Bytes, -1, _ => "99 0 obj\n<< /S /JavaScript /JS (unused) >>\nendobj\n") }, plan, "unreferenced extra object");
        await Reject(candidate with { Samples = candidate.Samples.SetItem(0, candidate.Samples[0] with { ColorSha256 = new('0', 64) }) }, plan, "wrong expected image sample digest");
        await Reject(candidate with { Samples = candidate.Samples.SetItem(1, candidate.Samples[1] with { AlphaSha256 = new('0', 64) }) }, plan, "wrong expected alpha digest");
        await Reject(candidate with { Samples = candidate.Samples.SetItem(0, candidate.Samples[0] with { ProfileSha256 = new('0', 64) }) }, plan, "wrong expected ICC digest");
        await Reject(candidate with { Samples = [] }, plan, "missing expected inventory");
        var reordered = ImagePdfPlan.Create(Guid.NewGuid(), plan.Plan.Pages.Reverse().Select(page => page.Source), plan.Plan.Settings).Confirm(true);
        await Reject(candidate, reordered, "changed reviewed page order");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await validator.ValidateAsync(candidate, plan, cancelled.Token); throw new Exception("Cancellation ignored."); }
            catch (OperationCanceledException) { Check(true, "pre-cancellation remains cancellation"); }
        }
        Check(!Directory.EnumerateFileSystemEntries(scratch).Any(), "failed/cancelled validations remove owned snapshots");
        try { using var writable = new FileStream(Path.Combine(binary, "qpdf30.dll"), FileMode.Open, FileAccess.Write, FileShare.ReadWrite); throw new Exception("Runtime lease allowed writing."); }
        catch (IOException) { Check(true, "validator keeps the verified native runtime immutable"); }
        Check(await validator.ValidateAsync(candidate, plan, default) == hash, "valid candidate still succeeds after refusal cases");
        var physicalPath = Path.Combine(root, "low-density.png");
        using (var image = new MagickImage(plan.Plan.Pages[0].Source.Path))
        {
            image.Resize(96, 96); image.Density = new Density(1, 1, DensityUnit.PixelsPerInch);
            image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
            image.Write(physicalPath, MagickFormat.Png);
        }
        var source = images.Probe(new(Guid.NewGuid(), physicalPath), default);
        var physicalPlan = ImagePdfPlan.Create(Guid.NewGuid(), [source], plan.Plan.Settings).Confirm(false);
        var physical = ImagePdfWriter.Create(physicalPlan, images, default);
        var page = physicalPlan.Plan.Pages[0];
        Check(page.WidthPoints * page.HeightPoints * 150 * 150 / (72 * 72) > 16_000_000 &&
            await validator.ValidateAsync(physical, physicalPlan, default) == Convert.ToHexString(SHA256.HashData(physical.Bytes)),
            "large physical page validates at original sample dimensions without DPI resampling");
        using (var cancellation = new CancellationTokenSource())
        using (var watcher = new FileSystemWatcher(scratch, "image-pdf-validation-*.pdf"))
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.Created += (_, _) => { started.TrySetResult(); cancellation.Cancel(); };
            watcher.EnableRaisingEvents = true;
            var pending = validator.ValidateAsync(candidate, plan, cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try { await pending; throw new Exception("Validation cancellation ignored."); }
            catch (OperationCanceledException) { Check(true, "cancellation after snapshot creation stops validation"); }
        }
        Check(!Directory.EnumerateFileSystemEntries(scratch).Any(), "in-flight cancellation removes its owned snapshot");
        var changing = candidate with { Bytes = (byte[])candidate.Bytes.Clone() };
        using (var watcher = new FileSystemWatcher(scratch, "image-pdf-validation-*.pdf"))
        {
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.Created += (_, _) => { changing.Bytes[0] = (byte)'!'; changed.TrySetResult(); };
            watcher.EnableRaisingEvents = true;
            await Reject(changing, plan, "candidate bytes changed during validation");
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        var deep = root;
        while (deep.Length < 270) deep = Path.Combine(deep, "generated long Unicode ü path");
        using (var longValidator = new ImagePdfValidator(binary, deep))
            Check(await longValidator.ValidateAsync(candidate, plan, default) == hash && !Directory.EnumerateFileSystemEntries(deep).Any(),
                "long local Unicode validation snapshot path works and is cleaned");
        var tampered = Path.Combine(root, "tampered"); Directory.CreateDirectory(tampered);
        var host = File.ReadAllBytes(Path.Combine(binary, "ContextSuite.ImagePdfValidator.exe")); host[^1] ^= 1;
        File.WriteAllBytes(Path.Combine(tampered, "ContextSuite.ImagePdfValidator.exe"), host);
        try { using var invalid = new ImagePdfValidator(tampered, Path.Combine(root, "invalid scratch")); throw new Exception("Modified host accepted."); }
        catch (InvalidDataException) { Check(true, "modified native host is refused before execution"); }
        File.WriteAllText(Path.Combine(root, "image-pdf-validation.json"), JsonSerializer.Serialize(new { checks, sha256 = hash }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} native image-PDF validation checks. Evidence: {root}");

        void Check(bool pass, string name) { if (!pass) throw new Exception(name); checks.Add(name); }
        async Task Reject(ImagePdfCandidate invalid, ConfirmedImagePdf expected, string name)
        {
            try { await validator.ValidateAsync(invalid, expected, default); throw new Exception("Validator accepted " + name); }
            catch (InvalidDataException) { Check(true, "rejects " + name); }
        }
    }

    // Test-only editor of the generated writer's classic xref fixture. Rebuild
    // offsets after a mutation so rejection cannot be credited merely to a
    // stale xref. This is not a general PDF parser or production dependency.
    private static byte[] Edit(byte[] bytes, int objectId, Func<string, string> change)
    {
        var text = Encoding.Latin1.GetString(bytes);
        var xref = text.LastIndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        if (xref == 0) throw new Exception("Fixture has no classic xref.");
        var lines = text[xref..].Split('\n');
        var count = int.Parse(lines[1].Split(' ')[1], CultureInfo.InvariantCulture);
        var offsets = Enumerable.Range(1, count - 1).Select(id => (Id: id, Offset: int.Parse(lines[id + 2][..10], CultureInfo.InvariantCulture)))
            .OrderBy(value => value.Offset).ToArray();
        var objects = new Dictionary<int, string>();
        for (var i = 0; i < offsets.Length; i++)
        {
            var start = offsets[i].Offset; var end = i + 1 < offsets.Length ? offsets[i + 1].Offset : xref;
            objects.Add(offsets[i].Id, text[start..end]);
        }
        if (objectId == -1) { objects.Add(count, change("").Replace("99 0 obj", count + " 0 obj", StringComparison.Ordinal)); count++; }
        else
        {
            var modified = change(objects[objectId]);
            var streamStart = modified.IndexOf("\nstream\n", StringComparison.Ordinal) + 8;
            if (streamStart >= 8)
            {
                var lengthStart = modified.LastIndexOf("/Length ", streamStart, StringComparison.Ordinal) + 8;
                var lengthEnd = modified.IndexOf(" >>", lengthStart, StringComparison.Ordinal);
                if (lengthStart >= 8 && lengthEnd > lengthStart && int.TryParse(modified[lengthStart..lengthEnd], out _))
                {
                    var streamEnd = modified.LastIndexOf("\nendstream", StringComparison.Ordinal);
                    modified = modified[..lengthStart] + (streamEnd - streamStart).ToString(CultureInfo.InvariantCulture) + modified[lengthEnd..];
                }
            }
            objects[objectId] = modified;
        }
        var result = new StringBuilder(text[..offsets[0].Offset]); var table = new int[count];
        foreach (var pair in objects.OrderBy(pair => pair.Key)) { table[pair.Key] = result.Length; result.Append(pair.Value); }
        var startXref = result.Length;
        result.Append("xref\n0 ").Append(count).Append("\n0000000000 65535 f \n");
        for (var id = 1; id < count; id++) result.Append(table[id].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        result.Append("trailer\n<< /Size ").Append(count).Append(" /Root 1 0 R >>\nstartxref\n").Append(startXref).Append("\n%%EOF\n");
        return Encoding.Latin1.GetBytes(result.ToString());
    }
}
