using System.Collections.Immutable;
using ContextSuite.Core.Images;
using ContextSuite.Core.Dds;
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
        var jfif = facts with { Path = Path.Combine(scratch, "photo.JfIf"), Format = ImageFormat.Jpeg, BitDepth = 8, IsLossy = true };
        foreach (var mode in Enum.GetValues<ImageMetadataMode>())
        {
            var jfifPlan = ImageConversionPlanner.Create(Guid.NewGuid(), [jfif], new(ImageFormat.Png, Metadata: mode),
                new("convert", new(ReplaceOriginals: true)), true);
            check(jfifPlan.HasExecutableItems && !jfifPlan.ReplaceOriginal, "JFIF plan: JPEG alias remains copy-only: " + mode);
            Reject(() => (jfifPlan with { ReplaceOriginal = true }).Confirm(true, true, true), "forged JFIF replacement: " + mode);
        }
        Reject(() => (jfif with { Format = ImageFormat.Png }).Validate(), "JFIF filename with non-JPEG facts");
        check(!ImageConversionPlanner.Create(Guid.NewGuid(), [jfif], new(ImageFormat.Jpeg), settings).HasExecutableItems,
            "JFIF plan: no redundant JPEG re-encoding action");
        var iconOptions = new ImageConversionOptions(ImageFormat.Ico, Metadata: ImageMetadataMode.Automatic);
        var iconSource = facts with { Format = ImageFormat.Ico, Width = 48, Height = 32, BitDepth = 8, HasTransparency = true };
        var extracted = ImageConversionPlanner.Create(Guid.NewGuid(), [iconSource], new(ImageFormat.Png, Metadata: ImageMetadataMode.Automatic),
            new("convert", new(ReplaceOriginals: true)), true);
        check(extracted.CanConfirmQuickAction && !extracted.ReplaceOriginal && extracted.Items[0].OutputWidth == 48 && extracted.Items[0].OutputHeight == 32,
            "ICO extraction preserves largest frame size and alpha on a mandatory copy");
        Reject(() => (extracted with { ReplaceOriginal = true }).Confirm(true, true, true), "forged ICO input replacement");
        check(!ImageConversionPlanner.Create(Guid.NewGuid(), [iconSource], new(ImageFormat.Jpeg, MatteRgb: 0xffffff), settings).HasExecutableItems,
            "ICO extraction does not enable other image destinations");
        var icon = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], iconOptions, new("convert", new(true)), true);
        check(!icon.ReplaceOriginal && icon.CanConfirmQuickAction && icon.Items[0].OutputWidth == 256 && icon.Items[0].OutputHeight == 256,
            "ICO plan: automatic square canvases are quiet, copy-only and capped at 256");
        Reject(() => (icon with { ReplaceOriginal = true }).Confirm(true, true, true), "forged ICO replacement");
        Reject(() => (iconOptions with { MaximumDimension = 64 }).Validate(), "custom ICO size conflicts with fixed automatic sizes");
        check(IconFrameLayout.Create(512, 256).Select(f => f.Size).SequenceEqual(new uint[] { 16, 24, 32, 48, 64, 128, 256 }) &&
            IconFrameLayout.Create(512, 256).All(f => f.Width == f.Size && f.Height == f.Size / 2 && f.Top == f.Size / 4),
            "ICO layout: every standard size, aspect fit and centered transparent padding");
        check(IconFrameLayout.Create(20, 10).SequenceEqual(new IconFrameLayout[] { new(16, 16, 8, 0, 4), new(24, 20, 10, 2, 7) }) &&
            IconFrameLayout.Create(1, 1).SequenceEqual(new IconFrameLayout[] { new(16, 1, 1, 7, 7) }),
            "ICO layout: small artwork never enlarges and stops at the next standard canvas");
        foreach (var format in new[] { ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.WebP, ImageFormat.Bmp, ImageFormat.Tga })
            check(ImageConversionPlanner.Create(Guid.NewGuid(), [facts with { Format = format, HasTransparency = true }], iconOptions, settings).CanConfirmQuickAction,
                "ICO plan: ordinary raster alpha needs no matte: " + format);
        foreach (var format in new[] { ImageFormat.Heic, ImageFormat.Avif, ImageFormat.Ico })
            check(!ImageConversionPlanner.Create(Guid.NewGuid(), [facts with { Format = format }], iconOptions, settings).HasExecutableItems,
                "ICO plan: unsupported input does not gain a conversion pair: " + format);
        var avifOptions = new ImageConversionOptions(ImageFormat.Avif, Quality: ImageConversionOptions.AvifQuality,
            Metadata: ImageMetadataMode.Automatic);
        check(avifOptions.Quality == 95 && avifOptions.Extension == "avif" && avifOptions.IsLossy && avifOptions.SupportsAlpha,
            "AVIF options: fixed quality 95, lossy output, alpha and AVIF extension");
        foreach (var format in new[] { ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.WebP, ImageFormat.Bmp, ImageFormat.Tga })
        {
            var avif = ImageConversionPlanner.Create(Guid.NewGuid(), [facts with { Format = format, BitDepth = 8, HasTransparency = true }],
                avifOptions, new("convert", new(ReplaceOriginals: true)), true);
            check(avif.CanConfirmQuickAction && !avif.ReplaceOriginal && avif.Items[0].CanExecute &&
                avif.Items[0].OutputWidth == facts.Width && avif.Items[0].OutputHeight == facts.Height,
                "AVIF plan: ordinary raster alpha, original dimensions and mandatory quiet copy: " + format);
            Reject(() => (avif with { ReplaceOriginal = true }).Confirm(true, true, true), "forged AVIF replacement: " + format);
        }
        foreach (var format in new[] { ImageFormat.Heic, ImageFormat.Avif, ImageFormat.Ico })
            check(!ImageConversionPlanner.Create(Guid.NewGuid(), [facts with { Format = format }], avifOptions, settings).HasExecutableItems,
                "AVIF plan: unsupported input does not gain a conversion pair: " + format);
        var dds = new DdsInfo(true, 0, (uint)DdsFormat.Rgba8, DdsFormat.Rgba8, facts.Width, facts.Height,
            1, 1, 1, DdsTextureKind.Texture2D, 1, 148, facts.FileBytes, null, []);
        check(!ImageConversionPlanner.Create(Guid.NewGuid(), [facts with { Format = ImageFormat.Dds, Texture = dds }], avifOptions, settings).HasExecutableItems,
            "AVIF plan: DDS does not gain an output pair");
        var preciseAvif = ImageConversionPlanner.Create(Guid.NewGuid(), [facts with { ProfileNames = ["icc"], BitDepth = 16 }], avifOptions, settings);
        check(preciseAvif.CanConfirmQuickAction && preciseAvif.Items[0].OutputDepth == 8 &&
            preciseAvif.Items[0].Warnings.Any(w => w.Code == "precision-reduction" && w.Message.Contains("8-bit SDR", StringComparison.Ordinal)) &&
            preciseAvif.Items[0].Warnings.Any(w => w.Code == "color-profile-conversion" && w.Message.Contains("sRGB", StringComparison.Ordinal)),
            "AVIF plan: automatic copy discloses precision and color transformation without routine prompt");
        var taggedAvifSource = facts with { BitDepth = 8, ProfileNames = ["icc", "exif", "xmp"],
            Resolution = new(11811, 11811, 3, 100) };
        var preserveAvif = ImageConversionPlanner.Create(Guid.NewGuid(), [taggedAvifSource],
            new(ImageFormat.Avif, Quality: ImageConversionOptions.AvifQuality, Metadata: ImageMetadataMode.Preserve), settings);
        check(preserveAvif.Items[0].CanExecute && preserveAvif.Items[0].Warnings.Any(w => w.Code == "metadata-normalization"),
            "AVIF plan: supported EXIF, XMP and resolution use normalized preservation");
        var automaticAvif = ImageConversionPlanner.Create(Guid.NewGuid(), [taggedAvifSource], avifOptions,
            new("convert", new(ReplaceOriginals: true)), true);
        check(!automaticAvif.ReplaceOriginal && automaticAvif.CanConfirmQuickAction,
            "AVIF plan: tagged automatic output stays a quiet copy under overwrite settings");
        Reject(() => (avifOptions with { Quality = 90 }).Validate(), "AVIF quality remains fixed at 95");
        Reject(() => (avifOptions with { WebPLossless = true }).Validate(), "AVIF rejects WebP lossless policy");
        Reject(() => (avifOptions with { ExtractMip = 0 }).Validate(), "AVIF rejects DDS mip extraction");
        Reject(() => (avifOptions with { Texture = new() }).Validate(), "AVIF rejects texture options");
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
        var extra = facts with { BitDepth = 8, HasOtherMetadata = true, ProfileNames = ["icc", "exif", "xmp", "extra"] };
        foreach (var target in new[] { ImageFormat.Jpeg, ImageFormat.WebP, ImageFormat.Bmp, ImageFormat.Tga })
        {
            var automatic = Plan(extra, new(target, Metadata: ImageMetadataMode.Automatic));
            check(automatic.CanConfirmQuickAction && automatic.Confirm(false, false, false).Plan == automatic,
                "automatic metadata: ordinary " + target + " copy needs no acknowledgement");
        }
        var permission = new BatchSettings("convert", new(ReplaceOriginals: true));
        var protectedCopy = ImageConversionPlanner.Create(Guid.NewGuid(), [extra], new(ImageFormat.Tga, Metadata: ImageMetadataMode.Automatic), permission, true);
        check(!protectedCopy.ReplaceOriginal && protectedCopy.CanConfirmQuickAction,
            "automatic metadata: omission overrides replacement with a copy");
        Reject(() => (protectedCopy with { ReplaceOriginal = true }).Confirm(true, true, true), "forged automatic metadata replacement");
        check(!Plan(transparent, new(ImageFormat.Jpeg, Metadata: ImageMetadataMode.Automatic)).CanConfirmQuickAction,
            "automatic metadata: transparency still requires a background");
        var mixedAlpha = ImageConversionPlanner.Create(Guid.NewGuid(), [extra, transparent with { ItemId = Guid.NewGuid() }],
            new(ImageFormat.Jpeg, Metadata: ImageMetadataMode.Automatic), settings);
        check(!mixedAlpha.CanConfirmQuickAction, "automatic metadata: mixed selection does not silently skip a background choice");
        var mixedUnsupported = ImageConversionPlanner.Create(Guid.NewGuid(), [extra, facts with { ItemId = Guid.NewGuid(), UnsupportedReason = "Unsupported color interpretation" }],
            new(ImageFormat.WebP, Metadata: ImageMetadataMode.Automatic), settings);
        check(mixedUnsupported.CanConfirmQuickAction && mixedUnsupported.Items.Count(item => item.CanExecute) == 1,
            "automatic metadata: unsupported file does not block independent valid copies");
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
