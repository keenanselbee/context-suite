using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Tiff;
using ContextSuite.Core.Svg;
using ContextSuite.Core.Gif;

namespace ContextSuite.Application;

internal sealed partial class MainViewModel
{
    internal async Task<ConfirmedImagePdf?> ConfirmImagePdfOrderAsync(ImagePdfPlan plan, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!plan.NeedsOrderReview) return plan.Confirm(false);
        if (ImagePdfOrderRequested is null) return null;
        using var decision = new ImagePdfOrderViewModel(plan, trial ?? throw new InvalidOperationException("Missing conversion access."));
        Summary = "Review the PDF page order. No files changed by this batch.";
        var confirmed = await ImagePdfOrderRequested(decision, token);
        token.ThrowIfCancellationRequested();
        return confirmed;
    }

    private async Task ExecuteConversionPlansAsync(OperationRequest request, FileRow[] rows, ConfirmedImageBatch? images,
        List<PdfRasterSource> documents, List<TiffSourceFacts> tiffs, List<SvgSourceFacts> svgs, List<GifSourceFacts> gifs, CancellationToken token)
    {
        ConfirmedPdfPageConversion? pdf = null;
        ConfirmedSvgConversion? svg = null;
        ConfirmedGifConversion? gif = null;
        var byId = rows.ToDictionary(row => row.ItemId);
        var tiff = await ConfirmTiffAsync(request, rows, tiffs,
            request.Action == "jpeg" ? TiffTarget.Jpeg : TiffTarget.Png, images?.Plan.Options.MatteRgb, token);
        if (tiffs.Any(source => byId[source.ItemId].Result.State == OperationState.Cancelled))
        {
            foreach (var row in rows.Where(row => row.Result.State is OperationState.Pending or OperationState.Running))
                row.ApplyResult(new(row.Path, OperationState.Cancelled, "Conversion cancelled before execution. Originals kept."));
            return;
        }
        if (documents.Count > 0)
        {
            try { pdf = PdfPageConversionPlan.Create(request.RequestId, documents, rows[0].Settings,
                request.Action == "jpeg" ? PdfPageFormat.Jpeg : PdfPageFormat.Png).Confirm(); }
            catch (InvalidDataException)
            {
                foreach (var document in documents) byId[document.ItemId].ApplyResult(new(document.Path, OperationState.Unsupported,
                    "This PDF selection exceeds the page or image-size limit. Select fewer documents or use smaller PDFs. Originals are kept."));
            }
        }
        if (svgs.Count > 0)
        {
            var target = request.Action == "jpeg" ? ImageFormat.Jpeg : ImageFormat.Png;
            var saved = svgs.Select(source => byId[source.ItemId].SvgOptions).Distinct().ToArray();
            SvgConversionOptions? options;
            if (saved.Length == 1 && saved[0] is not null) options = saved[0];
            else
            {
                if (saved.Any(option => option is not null))
                {
                    foreach (var source in svgs) byId[source.ItemId].BlockSvgResume(
                        "Earlier SVG size choices differ. Retry those files separately; completed copies were kept.");
                    return;
                }
                if (SvgSizeRequested is null)
                {
                    foreach (var row in rows.Where(row => row.Result.State is OperationState.Pending or OperationState.Running))
                        row.ApplyResult(new(row.Path, OperationState.Cancelled, "Choose the output size before converting SVG. Originals kept."));
                    return;
                }
                Summary = "Choose the SVG image size" + (target == ImageFormat.Jpeg ? " and JPEG background" : "") + ". No files changed by this batch.";
                options = await SvgSizeRequested(svgs, target, token);
                token.ThrowIfCancellationRequested();
            }
            if (options is null)
            {
                foreach (var row in rows.Where(row => row.Result.State is OperationState.Pending or OperationState.Running))
                    row.ApplyResult(new(row.Path, OperationState.Cancelled, "Conversion cancelled before choosing output options. Originals kept."));
                return;
            }
            if (options.Target != target)
                throw new InvalidDataException("SVG options do not match the selected output format.");
            foreach (var source in svgs) byId[source.ItemId].RememberSvgOptions(options);
            try { svg = SvgConversionPlan.Create(request.RequestId, svgs, options, rows[0].Settings).Confirm(); }
            catch (InvalidDataException)
            {
                foreach (var source in svgs) byId[source.ItemId].ApplyResult(new(source.Path, OperationState.Unsupported,
                    "This SVG selection exceeds the image size limit. Choose fewer files or a smaller width. Original kept."));
            }
        }
        if (gifs.Count > 0)
        {
            try { gif = GifConversionPlan.Create(request.RequestId, gifs, rows[0].Settings).Confirm(); }
            catch (InvalidDataException)
            {
                foreach (var source in gifs) byId[source.ItemId].ApplyResult(new(source.Path, OperationState.Unsupported,
                    "This GIF selection exceeds the frame or pixel limit. Choose fewer or smaller GIFs. Originals kept."));
            }
        }
        if (images is null && pdf is null && tiff is null && svg is null && gif is null) return;
        token.ThrowIfCancellationRequested();
        var admission = images is not null ? await trial!.AdmitConversionAsync(images, token) :
            pdf is not null ? await trial!.AdmitConversionAsync(pdf, token) :
            tiff is not null ? await trial!.AdmitConversionAsync(tiff, token) :
            svg is not null ? await trial!.AdmitConversionAsync(svg, token) : await trial!.AdmitConversionAsync(gif!, token);
        if (images is not null)
            await new ImageBatchExecutor(worker, Publisher!, trial!).ExecuteAdmittedAsync(images, admission, (item, result) =>
            {
                byId[item.Source.ItemId].ApplyResult(result);
                Summary = $"Converting: {rows.Count(row => row.Result.State is not (OperationState.Pending or OperationState.Running))} of {rows.Length} files finished.";
            }, token);
        if (pdf is not null)
        {
            var completed = documents.SelectMany(document => byId[document.ItemId].CompletedPdfOutputs
                .Select(output => (document.ItemId, output.PageIndex, output.OutputBytes)))
                .ToDictionary(output => (output.ItemId, output.PageIndex), output => output.OutputBytes);
            await new PdfPageConversionExecutor(worker, Publisher!, trial!).ExecuteAdmittedAsync(pdf, admission, (source, index, result) =>
            {
                var row = byId[source.ItemId];
                row.ApplyPdfPage(index, result);
                Summary = $"Converting {row.Name}: {row.SavedPdfPages} of {row.PdfPageCount} page copies saved.";
            }, token, completed);
        }
        if (tiff is not null)
        {
            var completed = tiffs.SelectMany(source => byId[source.ItemId].CompletedTiffOutputs
                .Select(output => (source.ItemId, output.PageIndex, output.OutputBytes)))
                .ToDictionary(output => (output.ItemId, output.PageIndex), output => output.OutputBytes);
            await new TiffConversionExecutor(worker, Publisher!, trial!).ExecuteAdmittedAsync(tiff, admission, (source, index, result) =>
            {
                var row = byId[source.ItemId];
                row.ApplyTiffPage(index, result);
                Summary = $"Converting {row.Name}: {row.SavedTiffPages} of {row.TiffPageCount} outputs saved.";
            }, token, completed);
        }
        if (svg is not null)
            await new SvgConversionExecutor(worker, Publisher!, trial!).ExecuteAdmittedAsync(svg, admission, (source, result) =>
            {
                byId[source.ItemId].ApplyResult(result);
                Summary = $"Converting: {rows.Count(row => row.Result.State is not (OperationState.Pending or OperationState.Running))} of {rows.Length} files finished.";
            }, token);
        if (gif is not null)
            await new GifConversionExecutor(worker, Publisher!).ExecuteAdmittedAsync(gif, admission, byId, row =>
            {
                Summary = $"Extracting {row.Name}: {row.SavedGifFrames} of {row.GifSource!.FrameCount} PNG frames saved. Playback timing is not retained.";
            }, token);
    }
}
