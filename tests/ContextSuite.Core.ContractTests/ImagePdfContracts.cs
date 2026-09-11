using System.Collections.Immutable;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.ContractTests;

internal static class ImagePdfContracts
{
    public static void Run(string scratch, Action<bool, string> check)
    {
        var source = new ImageSourceFacts(Guid.NewGuid(), Path.Combine(scratch, "page.png"), new('A', 64),
            100, ImageFormat.Png, 300, 200, 16, 1, true, false, "sRGB", []);
        var settings = new BatchSettings("convert", new(ReplaceOriginals: true));
        ImagePdfPlan Plan(params ImageSourceFacts[] sources) => ImagePdfPlan.Create(Guid.NewGuid(), sources, settings);
        var single = Plan(source);
        check(!single.NeedsOrderReview && single.Confirm(false).Plan == single, "image PDF: one image needs no order prompt");
        check(single.Output.Mode == OutputMode.SiblingCopy, "image PDF: overwrite preference cannot replace originals");
        check(single.Pages[0] is { WidthPoints: 225, HeightPoints: 150, BitsPerComponent: 16, ColorComponents: 3 },
            "image PDF: absent density defaults to 96 DPI without precision reduction");
        var second = source with { ItemId = Guid.NewGuid(), Path = Path.Combine(scratch, "other.png"), Width = 100 };
        var combined = Plan(second, source);
        Reject(() => combined.Confirm(false), "multiple images require reviewed order");
        check(combined.Confirm(true).Plan.Pages.Select(page => page.Source.ItemId).SequenceEqual(new[] { second.ItemId, source.ItemId }),
            "image PDF: confirmation keeps exact proposed order");
        var reverse = Plan(source, second).Confirm(true);
        check(reverse.Plan.Pages[0].Source == source, "image PDF: reordered plan has the new first page");
        var gray = ImagePdfPage.Create(source with { BitDepth = 8, HasGrayscaleProfile = true });
        check(gray is { BitsPerComponent: 8, ColorComponents: 1 }, "image PDF: grayscale ICC uses one color component");
        foreach (var orientation in Enumerable.Range(1, 8))
        {
            var page = ImagePdfPage.Create(source with { Orientation = (uint)orientation, Resolution = new(300, 200, 2) });
            check(page.Width == (orientation >= 5 ? 200u : 300u) && page.Height == (orientation >= 5 ? 300u : 200u) &&
                page.WidthPoints == 72 && page.HeightPoints == 72, "image PDF: orientation swaps pixels and density together " + orientation);
        }
        var metric = ImagePdfPage.Create(source with { Resolution = new(11811, 11811, 3, 100) });
        check(Math.Abs(metric.WidthPoints - 72) < 0.001, "image PDF: metric density retains physical page size");
        var aspect = ImagePdfPage.Create(source with { Resolution = new(2, 1, 1) });
        check(aspect.WidthPoints == 225 && aspect.HeightPoints == 300, "image PDF: unitless density preserves pixel aspect ratio");
        Reject(() => Plan(), "empty selection");
        Reject(() => Plan(source, source), "duplicate identity");
        Reject(() => Plan(source, source with { ItemId = Guid.NewGuid(), Path = source.Path.ToUpperInvariant() }), "duplicate path");
        Reject(() => ImagePdfPlan.Create(Guid.Empty, [source], settings), "empty batch identity");
        Reject(() => ImagePdfPlan.Create(Guid.NewGuid(), [source], new("optimize", new())), "wrong operation");
        Reject(() => (single with { Pages = default }).Confirm(true), "default page array");
        Reject(() => (single with { Pages = [single.Pages[0] with { WidthPoints = 1 }] }).Confirm(true), "forged page geometry");
        Reject(() => Plan(source with { Sha256 = "invalid" }), "invalid digest");
        Reject(() => Plan(source with { UnsupportedReason = "Unsupported HDR" }), "unsupported image information");
        Reject(() => Plan(source with { FileBytes = 128L * 1024 * 1024 + 1 }), "per-file source budget");
        Reject(() => Plan(source with { Width = 5000, Height = 4000 }), "per-page pixel budget");
        Reject(() => Plan(source with { Resolution = new(1, 1, 2) }), "oversized physical page");
        Reject(() => Plan(source with { Resolution = new(uint.MaxValue, uint.MaxValue, 2) }), "unusable tiny physical page");
        Reject(() => ImagePdfPlan.Create(Guid.NewGuid(), Enumerable.Range(0, 4097).Select(i => source with
            { ItemId = Guid.NewGuid(), Path = Path.Combine(scratch, i + ".png") }), settings), "page count budget");
        Reject(() => ImagePdfPlan.Create(Guid.NewGuid(), Enumerable.Range(0, 5).Select(i => source with
            { ItemId = Guid.NewGuid(), Path = Path.Combine(scratch, i + ".png"), FileBytes = 128L * 1024 * 1024 }), settings), "combined byte budget");
        Reject(() => ImagePdfPlan.Create(Guid.NewGuid(), Enumerable.Range(0, 9).Select(i => source with
            { ItemId = Guid.NewGuid(), Path = Path.Combine(scratch, i + ".png"), Width = 4000, Height = 4000 }), settings), "combined pixel budget");

        void Reject(Action action, string description)
        {
            try { action(); check(false, "image PDF accepted " + description); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            { check(true, "image PDF rejects " + description); }
        }
    }
}
