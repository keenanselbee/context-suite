using ContextSuite.Core.Settings;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Svg;
using ContextSuite.Core.Transport;
using ContextSuite.Core.Images;

namespace ContextSuite.Core.ContractTests;

internal static class SvgModelContracts
{
    internal static void Run(string scratch, Action<bool, string> check)
    {
        var source = new SvgSourceFacts(Guid.NewGuid(), Path.Combine(scratch, "model.svg"), new string('A', 64), 100,
            32, 16, 4096, 16_000_000, new string('B', 64), []);
        source.Validate();
        check(new SvgConversionOptions().GetSize(source) == new SvgPixelSize(32, 16), "SVG automatic size retains an ordinary viewport");
        check(new SvgConversionOptions(100).GetSize(source) == new SvgPixelSize(100, 50), "SVG custom width keeps aspect ratio");
        check(new SvgConversionOptions().GetSize(source with { IntrinsicWidth = 8000, IntrinsicHeight = 6000 }) == new SvgPixelSize(4096, 3072),
            "SVG automatic size fits a large viewport within the side bound");
        check(new SvgConversionOptions().GetSize(source with { IntrinsicWidth = 4096, IntrinsicHeight = 4096 }) == new SvgPixelSize(4000, 4000),
            "SVG automatic size observes the total pixel bound as well as the side bound");
        var filtered = source with { MaximumSide = 1024, MaximumPixels = 1_000_000, IntrinsicWidth = 2000, IntrinsicHeight = 1000 };
        check(new SvgConversionOptions().GetSize(filtered) == new SvgPixelSize(1024, 512), "SVG filters carry their tighter output budget");
        check(new SvgConversionOptions().GetSize(source with { IntrinsicWidth = 0.5, IntrinsicHeight = 0.5 }) == new SvgPixelSize(1, 1),
            "SVG fractional viewport still produces at least one pixel");
        Reject(() => new SvgConversionOptions(2048).GetSize(filtered), "oversized custom filtered output");
        Reject(() => new SvgConversionOptions(4096).GetSize(source with { IntrinsicHeight = 1_000_000 }), "extreme portrait output");
        foreach (var invalid in new[] { source with { IntrinsicWidth = double.NaN }, source with { IntrinsicHeight = double.PositiveInfinity },
            source with { IntrinsicWidth = 0 }, source with { MaximumPixels = 17_000_000 }, source with { FileBytes = 1048577 },
            source with { FontFingerprint = "unbound" }, source with { FontNotices = ["unsafe\nnotice"] }, source with { FontNotices = default } })
            Reject(invalid.Validate, "invalid or unbounded source facts");
        Reject(() => new SvgConversionOptions(0).Validate(), "zero width");
        Reject(() => new SvgConversionOptions(4097).Validate(), "excessive width");
        Reject(() => new SvgConversionOptions(Target: ImageFormat.Jpeg).Validate(), "JPEG without explicit background");
        Reject(() => new SvgConversionOptions(MatteRgb: 0xffffff).Validate(), "PNG background would lose alpha");
        Reject(() => new SvgConversionOptions(Target: ImageFormat.Jpeg, MatteRgb: 0x1000000).Validate(), "out-of-range background");
        Reject(() => new SvgConversionOptions(Target: ImageFormat.WebP).Validate(), "unadvertised SVG output");
        var jpegOptions = new SvgConversionOptions(100, ImageFormat.Jpeg, 0x123456);
        check(jpegOptions.GetSize(source) == new SvgPixelSize(100, 50) && jpegOptions.Extension == "jpg",
            "SVG JPEG shares proportional sizing and uses the JPEG extension");

        var settings = new BatchSettings("convert", new(ReplaceOriginals: true));
        var plan = SvgConversionPlan.Create(Guid.NewGuid(), [source], new(), settings).Confirm();
        check(plan.Plan.Settings.SelectOutput(false, false, false, false).Mode == OutputMode.SiblingCopy, "SVG confirmed output always retains the source");
        var jpegPlan = SvgConversionPlan.Create(Guid.NewGuid(), [source], jpegOptions, settings).Confirm();
        check(jpegPlan.Plan.Options == jpegOptions && jpegPlan.Plan.Settings.SelectOutput(false, false, false, false).Mode == OutputMode.SiblingCopy,
            "SVG JPEG confirmation captures size, target and explicit matte while retaining originals");
        Reject(() => SvgConversionPlan.Create(Guid.NewGuid(), [source, source], new(), settings), "duplicate source identity");
        Reject(() => SvgConversionPlan.Create(Guid.NewGuid(), [source, source with { ItemId = Guid.NewGuid(), Path = source.Path.ToUpperInvariant() }], new(), settings),
            "duplicate Windows source path");
        Reject(() => (plan.Plan with { Options = new(0) }).Confirm(), "forged options at confirmation");
        var outputId = Guid.NewGuid();
        var work = new SvgWork(source, new(), outputId, Path.Combine(scratch, $".context-suite-{outputId:N}.tmp"));
        work.Validate();
        Reject(() => (work with { TemporaryPath = source.Path }).Validate(), "source path used as output");
        Reject(() => (work with { OutputId = source.ItemId }).Validate(), "source identity used as output identity");
        Reject(() => (work with { TemporaryPath = Path.Combine(scratch, "unreserved.png") }).Validate(), "unreserved output name");
        var probe = new WorkerCommand(1, Guid.NewGuid(), "svg-probe", SvgProbe: new(source.ItemId, source.Path));
        probe.Validate();
        new WorkerCommand(1, Guid.NewGuid(), "svg-convert", SvgWork: work).Validate();
        check(true, "SVG probe and conversion have distinct valid IPC payloads");
        Reject(() => (probe with { SvgWork = work }).Validate(), "mixed SVG probe and work");
        Reject(() => (probe with { Probe = new(source.ItemId, source.Path) }).Validate(), "mixed SVG and raster probe");
        Reject(() => (probe with { TiffProbe = new(source.ItemId, source.Path) }).Validate(), "mixed SVG and TIFF probe");
        foreach (var command in new[] { "capabilities", "shutdown", "image-probe", "image-convert", "audio-file-probe", "pdf-raster-probe", "tiff-probe", "office-export" })
            Reject(() => (probe with { Command = command }).Validate(), "SVG payload on " + command);
        void Reject(Action action, string label)
        {
            var refused = false;
            try { action(); } catch (InvalidDataException) { refused = true; }
            check(refused, "SVG refuses " + label);
        }
    }
}
