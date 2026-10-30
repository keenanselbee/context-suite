using System.Security.Cryptography;
using ContextSuite.Core.Images;
using ContextSuite.Core.Svg;
using ContextSuite.Private.Svg;
using ImageMagick;

internal static class SvgContracts
{
    internal static async Task RunAsync(string root, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new ArgumentException("Use a fresh SVG adapter directory.");
        Directory.CreateDirectory(root);
        var adapter = new SvgAdapter(Path.Combine(root, "scratch"));
        var cases = new Dictionary<string, string>
        {
            ["solid"] = "<rect width='32' height='16' fill='#336699'/>",
            ["alpha"] = "<rect width='16' height='16' fill='red' opacity='.5'/>",
            ["path"] = "<path d='M0 0 H32 V16 H0 Z' fill='#336699'/>",
            ["css"] = "<style>.paint {fill:#336699}</style><rect class='paint' width='32' height='16'/>",
            ["use"] = "<defs><rect id='r' width='32' height='16' fill='#336699'/></defs><use href='#r'/>",
            ["gradient"] = "<defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect width='32' height='16' fill='url(#g)'/>",
            ["clip"] = "<defs><clipPath id='c'><rect width='16' height='16'/></clipPath></defs><rect width='32' height='16' fill='red' clip-path='url(#c)'/>",
            ["mask"] = "<defs><mask id='m'><rect width='16' height='16' fill='white'/></mask></defs><rect width='32' height='16' fill='red' mask='url(#m)'/>",
            ["filter"] = "<defs><filter id='f'><feGaussianBlur stdDeviation='1'/></filter></defs><rect x='8' y='4' width='16' height='8' fill='red' filter='url(#f)'/>",
            ["font"] = "<text x='0' y='13' font-family='Arial' font-size='12'>Ab</text>",
            ["substitute"] = "<text x='0' y='13' font-family='Missing Fixture Family' font-size='12'>Ab</text>"
        };
        var embedded = PngFixture.Create(1, 1, 8, 6, (_, _) => [65535, 0, 0, 65535]);
        cases["embedded-png"] = $"<image width='32' height='16' href='data:image/png;base64,{Convert.ToBase64String(embedded)}'/>";
        using (var image = new MagickImage())
        {
            image.ReadPixels(new byte[] { 255, 0, 0 }, new PixelReadSettings(1, 1, StorageType.Char, PixelMapping.RGB));
            cases["embedded-jpeg"] = $"<image width='32' height='16' href='data:image/jpeg;base64,{Convert.ToBase64String(image.ToByteArray(MagickFormat.Jpeg))}'/>";
        }
        SvgSourceFacts? ordinary = null;
        foreach (var (name, body) in cases)
        {
            var path = Path.Combine(root, name + ".svg");
            await File.WriteAllTextAsync(path, Wrap(body));
            var hash = Hash(path);
            var facts = await adapter.ProbeAsync(new(Guid.NewGuid(), path), default);
            check(facts.Sha256 == hash && facts.IntrinsicWidth == 32 && facts.IntrinsicHeight == 16, "SVG bounded intrinsic/source facts: " + name);
            var output = await ConvertAsync(facts, new());
            using var decoded = new MagickImage(output);
            var pixels = decoded.GetPixels().ToByteArray(PixelMapping.RGBA)!;
            byte Sample(int x, int y, int channel) => pixels[(y * 32 + x) * 4 + channel];
            check(decoded.Width == 32 && decoded.Height == 16 && decoded.Depth == 8 && decoded.Format == MagickFormat.Png &&
                decoded.GetColorProfile()!.ToByteArray().SequenceEqual(ColorProfiles.SRGB.ToByteArray()), "SVG validated sRGB PNG geometry: " + name);
            if (name is "solid" or "path" or "css" or "use")
                check(Sample(10, 7, 0) == 51 && Sample(10, 7, 1) == 102 && Sample(10, 7, 2) == 153 && Sample(10, 7, 3) == 255,
                    "SVG exact authored interior color: " + name);
            else if (name == "alpha")
                check(Sample(8, 7, 0) == 255 && Sample(8, 7, 3) is 127 or 128 && Sample(24, 7, 3) == 0,
                    "SVG retains partial and full transparency");
            else if (name is "clip" or "mask")
                check(Sample(8, 7, 0) == 255 && Sample(8, 7, 3) == 255 && Sample(24, 7, 3) == 0, "SVG semantic coverage: " + name);
            else if (name == "gradient")
                check(Sample(3, 7, 0) > Sample(28, 7, 0) && Sample(3, 7, 2) < Sample(28, 7, 2) && Sample(16, 7, 3) == 255,
                    "SVG gradient direction and opacity");
            else if (name == "filter")
                check(facts.MaximumSide == 1024 && Sample(16, 7, 3) > Sample(8, 7, 3) && Sample(8, 7, 3) > Sample(1, 7, 3),
                    "SVG blur has bounded dimensions and a graduated edge");
            else if (name.StartsWith("embedded", StringComparison.Ordinal))
                check(Sample(16, 7, 0) > 245 && Sample(16, 7, 1) < 5 && Sample(16, 7, 2) < 5 && Sample(16, 7, 3) == 255,
                    "SVG embedded raster retains red interior: " + name);
            else
                check(pixels.Where((_, index) => index % 4 == 3).Any(value => value != 0) &&
                    (name != "substitute" || facts.FontNotices.Any(notice => notice.Contains("Missing Fixture Family", StringComparison.Ordinal))),
                    "SVG text renders with necessary substitution disclosure: " + name);
            check(Hash(path) == hash, "SVG original bytes retained: " + name);
            var jpeg = await ConvertAsync(facts, new(Target: ImageFormat.Jpeg, MatteRgb: 0xffffff));
            using (var jpg = new MagickImage(jpeg, MagickFormat.Jpeg))
            {
                check(jpg.Width == 32 && jpg.Height == 16 && jpg.IsOpaque && jpg.Quality == 90 &&
                    jpg.GetColorProfile()!.ToByteArray().SequenceEqual(ColorProfiles.SRGB.ToByteArray()),
                    "SVG JPEG retains geometry, sRGB and fixed quality: " + name);
                if (name == "alpha")
                {
                    var rgb = jpg.GetPixels().ToByteArray(PixelMapping.RGB)!;
                    check(rgb[(7 * 32 + 8) * 3] >= 250 && rgb[(7 * 32 + 8) * 3 + 1] is >= 124 and <= 132 &&
                        rgb[(7 * 32 + 24) * 3] >= 250 && rgb[(7 * 32 + 24) * 3 + 1] >= 250,
                        "SVG JPEG composites partial red alpha and fully transparent pixels on the chosen white background");
                }
            }
            if (name == "solid") ordinary = facts;
        }
        var resized = await ConvertAsync(ordinary!, new(97));
        using (var image = new MagickImage(resized))
            check(image.Width == 97 && image.Height == 49, "SVG custom width uses predictable aspect rounding");
        var alphaFacts = await adapter.ProbeAsync(new(Guid.NewGuid(), Path.Combine(root, "alpha.svg")), default);
        foreach (var matte in new uint[] { 0, 0x123456 })
        {
            using var image = new MagickImage(await ConvertAsync(alphaFacts, new(64, ImageFormat.Jpeg, matte)), MagickFormat.Jpeg);
            var rgb = image.GetPixels().ToByteArray(PixelMapping.RGB)!;
            var at = (16 * 64 + 48) * 3;
            check(image.Width == 64 && image.Height == 32 && Enumerable.Range(0, 3).All(channel =>
                Math.Abs(rgb[at + channel] - (int)((matte >> (16 - channel * 8)) & 255)) <= 3),
                "SVG JPEG custom size and chosen black/custom background: " + matte);
        }
        await Refuse(async () => await ConvertAsync(alphaFacts, new(Target: ImageFormat.Jpeg)), "JPEG without explicit matte");
        foreach (var body in new[] { "<script/>", "<foreignObject/>", "<animate/>", "<image href='file:///C:/outside.png'/>",
            "<rect fill='url(https://example.com/image)'/>", "<style>@import 'file.css';</style>", "<path d='bad'/>",
            "<rect onclick='x'/>", "<use id='loop' href='#loop'/>" })
        {
            var path = Path.Combine(root, "refusal-" + Guid.NewGuid().ToString("N") + ".svg");
            await File.WriteAllTextAsync(path, Wrap(body));
            await Refuse(() => adapter.ProbeAsync(new(Guid.NewGuid(), path), default), "unsafe or malformed SVG");
        }
        var dtd = Path.Combine(root, "dtd.svg");
        await File.WriteAllTextAsync(dtd, "<!DOCTYPE svg [<!ENTITY test 'text'>]>" + Wrap(""));
        await Refuse(() => adapter.ProbeAsync(new(Guid.NewGuid(), dtd), default), "DTD");
        var oversized = Path.Combine(root, "large.svg");
        await File.WriteAllTextAsync(oversized, Wrap("") + new string(' ', SvgSourceFacts.MaximumFileBytes));
        await Refuse(() => adapter.ProbeAsync(new(Guid.NewGuid(), oversized), default), "oversized XML");
        await Refuse(async () => await ConvertAsync(ordinary! with { FontFingerprint = new string('F', 64) }, new()), "changed font fingerprint");
        var occupiedId = Guid.NewGuid(); var occupied = Path.Combine(root, $".context-suite-{occupiedId:N}.tmp");
        await File.WriteAllBytesAsync(occupied, [17]);
        await Refuse(() => adapter.ConvertAsync(new(ordinary!, new(), occupiedId, occupied), default), "occupied reservation");
        check(File.ReadAllBytes(occupied).SequenceEqual(new byte[] { 17 }), "SVG leaves occupied reservation unchanged");
        await File.AppendAllTextAsync(ordinary!.Path, "\n");
        await Refuse(async () => await ConvertAsync(ordinary, new()), "changed source");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await adapter.ProbeAsync(new(Guid.NewGuid(), ordinary.Path), cancelled.Token); throw new InvalidOperationException("Cancellation ignored"); }
        catch (OperationCanceledException) { check(true, "SVG pre-cancelled request remains cancellation"); }

        async Task<string> ConvertAsync(SvgSourceFacts facts, SvgConversionOptions options)
        {
            var id = Guid.NewGuid(); var output = Path.Combine(root, $".context-suite-{id:N}.tmp"); using (File.Create(output)) { }
            var result = await adapter.ConvertAsync(new(facts, options, id, output), default);
            check(result.Validation.ItemId == id && result.Validation.MatchesPlan && result.Validation.Sha256 == Hash(output) &&
                result.SourceSha256 == facts.Sha256 && result.FontFingerprint == facts.FontFingerprint && result.Format == options.Target,
                "SVG result binds validated copy, output format and source/font identity");
            return output;
        }
        async Task Refuse(Func<Task> action, string label)
        {
            var rejected = false;
            try { await action(); } catch (Exception error) when (error is InvalidDataException or ImageFailureException) { rejected = true; }
            check(rejected, "SVG refuses " + label);
        }
    }

    private static string Wrap(string body) => "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='16'>" + body + "</svg>";
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
