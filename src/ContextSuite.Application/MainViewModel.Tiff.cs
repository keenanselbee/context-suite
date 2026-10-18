using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Tiff;

namespace ContextSuite.Application;

internal sealed partial class MainViewModel
{
    private static bool IsTiffPath(string path) => Path.GetExtension(path).ToLowerInvariant() is ".tif" or ".tiff";

    private async Task<TiffSourceFacts?> ProbeTiffPdfAsync(FileRow row, CancellationToken token)
    {
        try
        {
            var header = await FileAnalysisReader.ReadAsync(row.Path, token, headerOnly: true);
            if (header.Identity is not { FormatId: "tiff", Basis: IdentificationBasis.Content })
            {
                row.ApplyResult(new(row.Path, OperationState.Unsupported, "This file does not contain TIFF data. Original kept."));
                return null;
            }
            if (!worker.HasTiffPdfConverter)
            {
                row.ApplyResult(new(row.Path, OperationState.Unsupported, "TIFF to PDF conversion is unavailable in this build."));
                return null;
            }
            var source = await worker.ProbeTiffAsync(new(row.ItemId, row.Path), token);
            row.BeginTiff(source, true);
            return source;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            if (row.TiffSourceHash is not null)
                row.BlockTiffResume("The TIFF changed or could not be verified. Start a new Convert command; completed copies were kept.");
            else row.ApplyResult(new(row.Path, OperationState.Failed, "TIFF could not be read. Check the file and retry. Original kept."));
            return null;
        }
    }

    private async Task<ConfirmedTiffConversion?> ConfirmTiffAsync(OperationRequest request, FileRow[] rows,
        IReadOnlyList<TiffSourceFacts> sources, TiffTarget target, uint? rasterMatte, CancellationToken token)
    {
        if (sources.Count == 0) return null;
        var byId = rows.ToDictionary(row => row.ItemId);
        var mattes = sources.Select(source => byId[source.ItemId].TiffMatteRgb).Where(matte => matte is not null).Distinct().ToArray();
        if (mattes.Length > 1)
        {
            foreach (var source in sources) byId[source.ItemId].BlockTiffResume("Earlier TIFF backgrounds differ. Retry these sources separately; completed copies were kept.");
            return null;
        }
        var matte = target == TiffTarget.Jpeg ? mattes.FirstOrDefault() ?? rasterMatte : null;
        TiffConversionPlan plan;
        try { plan = TiffConversionPlan.Create(request.RequestId, sources, new(target, matte), rows[0].Settings); }
        catch (InvalidDataException)
        {
            foreach (var source in sources) byId[source.ItemId].ApplyResult(new(source.Path, OperationState.Unsupported,
                "This TIFF selection exceeds the page or pixel limit. Select fewer or smaller files. Originals kept."));
            return null;
        }
        if (plan.NeedsMatte)
        {
            var transparent = sources.SelectMany(source => source.Pages.Where(page => page.HasTransparency && page.UnsupportedReason is null)
                .Select(page => (Source: source, Page: page))).First();
            if (TiffMatteRequested is null)
            {
                foreach (var source in sources) byId[source.ItemId].ApplyResult(new(source.Path, OperationState.Cancelled,
                    "Choose a background before converting transparent TIFF pages to JPEG."));
                return null;
            }
            var preview = await worker.PreviewTiffAsync(new(transparent.Source, transparent.Page.Index, 0xffffff), token);
            matte = await TiffMatteRequested(transparent.Source, preview, token);
            token.ThrowIfCancellationRequested();
            if (matte is null)
            {
                foreach (var source in sources) byId[source.ItemId].ApplyResult(new(source.Path, OperationState.Cancelled,
                    "TIFF conversion cancelled before choosing a background. Originals kept."));
                return null;
            }
            plan = TiffConversionPlan.Create(request.RequestId, sources, new(target, matte), rows[0].Settings);
        }
        foreach (var source in sources) byId[source.ItemId].TiffMatteRgb = matte;
        try { return plan.Confirm(); }
        catch (InvalidDataException)
        {
            foreach (var source in sources)
            {
                var row = byId[source.ItemId];
                foreach (var page in target == TiffTarget.Pdf ? new[] { -1 } : Enumerable.Range(0, source.Pages.Length))
                {
                    var reason = page < 0 ? source.Pages.FirstOrDefault(item => item.GetUnsupportedReason(target) is not null)?.GetUnsupportedReason(target) :
                        source.Pages[page].GetUnsupportedReason(target);
                    row.ApplyTiffPage(page, new(source.Path, OperationState.Unsupported,
                        reason is null ? "No supported TIFF page can use the selected format. Original kept." :
                            $"Page {(page < 0 ? source.Pages.First(item => item.GetUnsupportedReason(target) is not null).Index : page) + 1}: {reason} Original kept."));
                }
            }
            return null;
        }
    }
}
