using System.Security.Cryptography;
using ContextSuite.Core.Pdf;
using ContextSuite.Private.Pdf;
using ImageMagick;

internal static class RasterPdfReuseContracts
{
    public static async Task RunAsync(string validatorDirectory, string scratch)
    {
        var pages = new[]
        {
            new RasterPdfPage(2, 2, 8, 3, 144, 72, true),
            new RasterPdfPage(1, 2, 8, 3, 72, 144, false)
        };
        var colors = new[] { new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, new byte[] { 20, 30, 40, 50, 60, 70 } };
        var alpha = new byte[] { 0, 127, 255, 64 };
        var profiles = new[] { ColorProfiles.SRGB.ToByteArray(), ColorProfiles.AdobeRGB1998.ToByteArray() };
        var calls = new List<int>();
        var candidate = ImagePdfWriter.Create(pages, index =>
        {
            calls.Add(index);
            return new(pages[index], colors[index], index == 0 ? alpha : null, profiles[index]);
        }, default);
        Check(calls.SequenceEqual([0, 1]), "provider reads each ordered page exactly once");
        Check(candidate.Samples[0].AlphaSha256 == Convert.ToHexString(SHA256.HashData(alpha)) &&
            candidate.Samples[1].AlphaSha256 is null &&
            candidate.Samples[0].ProfileSha256 != candidate.Samples[1].ProfileSha256,
            "samples retain ordered alpha and distinct ICC digests");
        using var validator = new ImagePdfValidator(validatorDirectory, scratch);
        var hash = Convert.ToHexString(SHA256.HashData(candidate.Bytes));
        Check(await validator.ValidateAsync(candidate, pages, default) == hash,
            "independent qpdf validates both ordered raster pages");
        Check(!Directory.EnumerateFileSystemEntries(scratch).Any(), "validation removes its snapshot");
        await RejectAsync(candidate, pages.Reverse().ToArray(), "reordered pages");
        await RejectAsync(candidate with { Samples = candidate.Samples.SetItem(0,
            candidate.Samples[0] with { ColorSha256 = new('0', 64) }) }, pages, "changed source sample digest");
        Reject(() => ImagePdfWriter.Create(pages, index => new(index == 0 ? pages[1] : pages[index],
            colors[index], index == 0 ? alpha : null, profiles[index]), default), "mismatched provider page");
        Reject(() => ImagePdfWriter.Create(pages, index => new(pages[index],
            index == 0 ? colors[index][..^1] : colors[index], index == 0 ? alpha : null, profiles[index]), default), "short color samples");
        Reject(() => ImagePdfWriter.Create(pages, index => new(pages[index], colors[index],
            index == 0 ? alpha[..^1] : null, profiles[index]), default), "short alpha samples");
        Reject(() => ImagePdfWriter.Create(pages, index => new(pages[index], colors[index],
            null, profiles[index]), default), "missing alpha samples");
        Reject(() => ImagePdfWriter.Create(pages, index => new(pages[index], colors[index],
            index == 0 ? alpha : null, index == 0 ? new byte[131] : profiles[index]), default), "short ICC profile");
        Reject(() => ImagePdfWriter.Create(pages, index => new(pages[index], colors[index],
            index == 0 ? alpha : null, index == 0 ? new byte[4 * 1024 * 1024 + 1] : profiles[index]), default), "oversized ICC profile");
        Reject(() => ImagePdfWriter.Create([pages[0] with { Width = 16385 }],
            _ => throw new Exception("Invalid page reached provider."), default), "invalid geometry");
        Reject(() => ImagePdfWriter.Create(Enumerable.Repeat(new RasterPdfPage(4000, 4000, 8, 3, 3000, 3000, false), 9).ToArray(),
            _ => throw new Exception("Over-budget pages reached provider."), default), "aggregate pixel budget");
        Console.WriteLine("Passed 13 reusable raster-PDF checks.");

        async Task RejectAsync(ImagePdfCandidate invalid, IReadOnlyList<RasterPdfPage> expected, string reason)
        {
            try { await validator.ValidateAsync(invalid, expected, default); throw new Exception("Accepted " + reason); }
            catch (InvalidDataException) { }
        }
        static void Reject(Func<ImagePdfCandidate> operation, string reason)
        {
            try { operation(); throw new Exception("Accepted " + reason); }
            catch (InvalidDataException) { }
        }
        static void Check(bool condition, string reason)
        {
            if (!condition) throw new Exception(reason);
        }
    }
}
