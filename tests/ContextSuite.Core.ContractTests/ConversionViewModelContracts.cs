using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.ContractTests;

internal static class ConversionViewModelContracts
{
    public static async Task RunAsync(string root, WorkerClient worker, ImageSourceFacts alpha, Action<bool, string> check)
    {
        var trialPath = Path.Combine(root, "planner-trial", "trial.json");
        var trial = new LocalTrialStore(trialPath);
        await using var planner = new ConversionViewModel(worker, trial, Guid.NewGuid(),
            [new(alpha.ItemId, alpha.Path, alpha, null)], new("convert", new()), false);
        planner.PreviewExpanded = true;
        await planner.InitializeAsync();
        check(planner.Target is null && !planner.CanConfirm && !File.Exists(trialPath), "conversion VM: explicit target, preview does not admit trial");
        check(planner.BeforePreview?.PixelWidth == alpha.Width && planner.AfterPreview is null, "conversion VM: real worker preview becomes a bounded WPF bitmap");
        planner.Target = ImageFormat.Jpeg;
        planner.WarningsAcknowledged = true;
        check(!planner.CanConfirm, "conversion VM: transparent JPEG requires matte even after acknowledging skips");
        planner.MatteChoice = 1;
        check(!planner.CanConfirm && !planner.WarningsAcknowledged, "conversion VM: matte choice requires fresh acknowledgement");
        planner.WarningsAcknowledged = true;
        check(!planner.CanConfirm, "conversion VM: transparent JPEG requires a current matte preview");
        planner.Target = ImageFormat.Bmp;
        planner.WarningsAcknowledged = true;
        check(planner.NeedsMatte && !planner.HasQuality && !planner.CanConfirm, "conversion VM: BMP exposes matte, hides quality and requires a fresh preview");
        await planner.RefreshPreviewAsync();
        check(planner.CanConfirm, "conversion VM: BMP matte preview enables confirmation");
        planner.Target = ImageFormat.Jpeg;
        planner.WarningsAcknowledged = true;
        await planner.RefreshPreviewAsync();
        check(planner.CanConfirm, "conversion VM: explicit matte preview and consent enable valid plan");
        planner.Quality = "invalid";
        check(!planner.CanConfirm && planner.Message.Contains("Quality", StringComparison.Ordinal), "conversion VM: invalid quality blocks execution");
        planner.Quality = "85";
        check(!planner.WarningsAcknowledged, "conversion VM: corrected options do not reuse stale consent");
        planner.MatteChoice = 3;
        planner.CustomMatte = "not-rgb";
        check(!planner.CanConfirm, "conversion VM: invalid custom RGB is blocked");
        planner.CustomMatte = "#00ff80";
        planner.MaximumDimension = "2";
        planner.WarningsAcknowledged = true;
        check(!planner.CanConfirm, "conversion VM: changed settings cannot reuse a stale matte preview");
        await planner.RefreshPreviewAsync();
        ConfirmedImageBatch? confirmed = null;
        planner.Confirmed += value => confirmed = value;
        planner.ConfirmCommand.Execute(null);
        check(confirmed?.Plan.Options is { MatteRgb: 0x00ff80, Quality: 85, MaximumDimension: 2 } &&
            confirmed.Plan.Items[0].OutputWidth == 2 && !File.Exists(trialPath), "conversion VM: exact options confirmed without preview/VM starting trial");
        planner.MaximumDimension = "0";
        check(!planner.CanConfirm, "conversion VM: zero resize blocked");
        planner.MaximumDimension = "";
        planner.Target = ImageFormat.Png;
        check(!planner.CanConfirm && planner.Rows[0].ProposedOutput == "", "conversion VM: same-format conversion is inapplicable");
        planner.Target = ImageFormat.WebP;
        planner.WebPLossless = true;
        planner.WarningsAcknowledged = true;
        planner.ReplaceOriginal = true;
        planner.ReplacementConfirmed = true;
        planner.WarningsAcknowledged = true;
        check(!planner.CanConfirm, "conversion VM: UI cannot override disabled replacement permission");
        planner.ReplaceOriginal = false;
        planner.WarningsAcknowledged = true;
        check(planner.CanConfirm && planner.OutputNotice.Contains("Originals kept", StringComparison.Ordinal), "conversion VM: safe copy remains available");
        planner.RemoveMetadata = true;
        check(!planner.WarningsAcknowledged, "conversion VM: metadata policy changes clear prior consent");
        planner.MaximumDimension = "1";
        planner.MaximumDimension = "2";
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((planner.AfterPreview is null || planner.IsPreviewBusy) && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        check(planner.AfterPreview?.PixelWidth == 2 && !planner.WarningsAcknowledged && !File.Exists(trialPath),
            "conversion VM: rapid edits automatically render latest preview without stale consent or trial admission");

        await using var iconPlanner = new ConversionViewModel(worker, trial, Guid.NewGuid(),
            [new(alpha.ItemId, alpha.Path, alpha, null)], new("convert", new ToolSettings(ReplaceOriginals: true)), true);
        check(iconPlanner.Formats.Any(format => format.Value == ImageFormat.Avif) == worker.HasAvifEncoder,
            "conversion VM: AVIF choice follows required encoder and decoder availability");
        check(iconPlanner.CanReplaceOriginal && iconPlanner.ReplaceOriginal && iconPlanner.CanResize,
            "conversion VM: stored replacement and ordinary resize remain available before ICO selection");
        iconPlanner.MaximumDimension = "25";
        iconPlanner.Target = ImageFormat.Ico;
        check(!iconPlanner.CanResize && iconPlanner.MaximumDimension == "" && !iconPlanner.CanReplaceOriginal &&
            !iconPlanner.ReplaceOriginal && !iconPlanner.EffectiveReplacement &&
            iconPlanner.OutputNotice.Contains("Originals kept", StringComparison.Ordinal) &&
            iconPlanner.ReplacementNotice.Contains("always creates copies", StringComparison.Ordinal),
            "conversion VM: ICO clears stale resize and shows mandatory copy policy");
        iconPlanner.Target = ImageFormat.WebP;
        check(iconPlanner.CanResize && iconPlanner.CanReplaceOriginal && iconPlanner.ReplaceOriginal && iconPlanner.MaximumDimension == "",
            "conversion VM: leaving ICO restores stored replacement choice without stale resize");
        iconPlanner.Target = ImageFormat.Avif;
        check(!iconPlanner.HasQuality && !iconPlanner.CanReplaceOriginal && !iconPlanner.ReplaceOriginal &&
            !iconPlanner.EffectiveReplacement && iconPlanner.OutputNotice.Contains("Originals kept", StringComparison.Ordinal) &&
            iconPlanner.ReplacementNotice.Contains("always creates copies", StringComparison.Ordinal),
            "conversion VM: AVIF has fixed quality and mandatory copies");
        iconPlanner.Target = ImageFormat.WebP;
        check(iconPlanner.CanReplaceOriginal && iconPlanner.ReplaceOriginal,
            "conversion VM: leaving AVIF restores stored replacement choice");
    }
}
