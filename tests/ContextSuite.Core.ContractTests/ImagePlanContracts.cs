using System.Collections.Immutable;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.ContractTests;

internal static class ImagePlanContracts
{
    public static void Run(string scratch, Action<bool, string> check)
    {
        var facts = new ImageSourceFacts(Guid.NewGuid(), Path.Combine(scratch, "image.png"), new string('A', 64),
            100, ImageFormat.Png, 300, 200, 16, 1, false, false, "sRGB", []);
        var settings = new BatchSettings("convert", new());
        ImageBatchPlan Plan(ImageSourceFacts source, ImageConversionOptions? options = null) =>
            ImageConversionPlanner.Create(Guid.NewGuid(), [source], options ?? new(ImageFormat.WebP), settings);
        var plan = Plan(facts);
        check(plan.Items[0].Warnings.Any(w => w.Code == "precision-reduction"), "image plan: 16-bit reduction requires acknowledgement");
        Reject(() => plan.Confirm(false, false, false), "unacknowledged precision warning");
        var confirmed = plan.Confirm(true, false, false);
        check(ReferenceEquals(confirmed.Plan, plan), "image plan: confirmation retains exact immutable plan");
        var edited = plan with { Options = plan.Options with { MaximumDimension = 50 } };
        Reject(() => edited.Confirm(true, false, false), "stale dimensions after option edit");
        Reject(() => (plan with { Items = [plan.Items[0] with { Warnings = [] }] }).Confirm(true, false, false), "removed warning in forged plan");
        check(!Plan(facts, new(ImageFormat.Png)).HasExecutableItems, "image plan: same format is not conversion");
        Reject(() => Plan(facts, new(ImageFormat.Png)).Confirm(true, false, false), "all-inapplicable batch");
        var transparent = facts with { HasTransparency = true };
        check(!Plan(transparent, new(ImageFormat.Bmp)).HasExecutableItems, "image plan: BMP also requires a matte");
        check(Plan(transparent, new(ImageFormat.Bmp, MatteRgb: 0xffffff)).HasExecutableItems, "image plan: BMP matte enables opaque output");
        check(Plan(transparent, new(ImageFormat.Tga)).HasExecutableItems, "image plan: TGA retains alpha without matte");
        foreach (var target in new[] { ImageFormat.Bmp, ImageFormat.Tga })
        {
            check(!Plan(facts with { ProfileNames = ["exif"] }, new(target)).HasExecutableItems, "image plan: bitmap metadata preservation blocks");
            check(Plan(facts with { ProfileNames = ["exif", "icc"] }, new(target, Metadata: ImageMetadataMode.RemoveDescriptive)).Items[0].Warnings.Any(w => w.Code == "color-profile-conversion"), "image plan: explicit bitmap metadata removal retains a color-transform warning");
            check(!Plan(facts with { Format = target }, new(target)).HasExecutableItems, "image plan: same bitmap format is not conversion");
        }
        check(!Plan(transparent, new(ImageFormat.Jpeg)).HasExecutableItems, "image plan: missing JPEG matte blocks execution");
        check(Plan(transparent, new(ImageFormat.Jpeg, MatteRgb: 0xffffff)).Items[0].Warnings.Any(w => w.Code == "alpha-flattening"),
            "image plan: selected matte is explicit alpha-loss consequence");
        var oriented = Plan(facts with { Orientation = 6 }, new(ImageFormat.WebP, MaximumDimension: 150)).Items[0];
        check(oriented.OutputWidth == 100 && oriented.OutputHeight == 150, "image plan: orientation precedes proportional resize");
        var noUpscale = Plan(facts, new(ImageFormat.WebP, MaximumDimension: 600)).Items[0];
        check(noUpscale.OutputWidth == 300 && noUpscale.OutputHeight == 200, "image plan: maximum dimension never enlarges");
        check(Plan(facts with { Format = ImageFormat.Jpeg, IsLossy = true, BitDepth = 8 }, new(ImageFormat.Png)).Items[0].Warnings
            .Any(w => w.Code == "loss-not-restored"), "image plan: lossless target does not restore lost detail");
        check(!Plan(facts with { ProfileNames = ["unknown-profile"] }).HasExecutableItems, "image plan: unknown metadata never silently discarded");
        check(Plan(facts with { ProfileNames = ["unknown-profile"] }, new(ImageFormat.WebP, Metadata: ImageMetadataMode.RemoveDescriptive))
            .HasExecutableItems, "image plan: explicit metadata removal resolves unsupported metadata");
        var mixed = ImageConversionPlanner.Create(Guid.NewGuid(), [facts, facts with { ItemId = Guid.NewGuid(), Format = ImageFormat.WebP }],
            new(ImageFormat.WebP), settings);
        check(mixed.Items.Count(i => i.CanExecute) == 1, "image plan: mixed batch retains inapplicable rows");
        Reject(() => (plan with { ReplaceOriginal = true }).Confirm(true, true, true), "replacement without saved permission");
        Reject(() => ImageConversionPlanner.Create(Guid.NewGuid(), [facts, facts], new(ImageFormat.WebP), settings), "duplicate item identity");
        Reject(() => Plan(facts with { Width = 16384, Height = 16384 }), "pixel budget");
        Reject(() => Plan(facts with { Sha256 = "untrusted" }), "malformed source digest");
        Reject(() => Plan(facts with { Resolution = new(1, 1, 2, 0) }), "zero resolution divisor");
        Reject(() => Plan(facts with { Resolution = new(0, 1, 2) }), "zero directional resolution");
        check(Plan(facts with { Resolution = new(11811, 11811, 3, 100) }).Items[0].Warnings.Any(w => w.Code == "metadata-normalization"),
            "image plan: physical resolution preservation is disclosed");
        foreach (var options in new[] { new ImageConversionOptions((ImageFormat)999), new(ImageFormat.Jpeg, Quality: 0),
            new(ImageFormat.Png, WebPLossless: true), new(ImageFormat.WebP, MatteRgb: 0), new(ImageFormat.Jpeg, MaximumDimension: 0) })
            Reject(() => Plan(facts, options), "invalid options");

        void Reject(Action action, string name)
        {
            try { action(); }
            catch (InvalidDataException) { check(true, "image plan: rejects " + name); return; }
            throw new InvalidOperationException("FAILED to reject " + name);
        }
    }
}
