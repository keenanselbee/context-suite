using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

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
        List<PdfRasterSource> documents, CancellationToken token)
    {
        ConfirmedPdfPageConversion? pdf = null;
        var byId = rows.ToDictionary(row => row.ItemId);
        if (documents.Count > 0)
        {
            try { pdf = PdfPageConversionPlan.Create(request.RequestId, documents, rows[0].Settings).Confirm(); }
            catch (InvalidDataException)
            {
                foreach (var document in documents) byId[document.ItemId].ApplyResult(new(document.Path, OperationState.Unsupported,
                    "This PDF selection exceeds the page or image-size limit. Select fewer documents or use smaller PDFs. Originals are kept."));
            }
        }
        if (images is null && pdf is null) return;
        token.ThrowIfCancellationRequested();
        var admission = images is not null ? await trial!.AdmitConversionAsync(images, token) : await trial!.AdmitConversionAsync(pdf!, token);
        if (images is not null)
            await new ImageBatchExecutor(worker, Publisher!, trial!).ExecuteAdmittedAsync(images, admission, (item, result) =>
            {
                byId[item.Source.ItemId].ApplyResult(result);
                Summary = $"Converting: {rows.Count(row => row.Result.State is not (OperationState.Pending or OperationState.Running))} of {rows.Length} files finished.";
            }, token);
        if (pdf is not null)
        {
            var completed = documents.SelectMany(document => byId[document.ItemId].CompletedPdfPages.Select(index => (document.ItemId, index))).ToHashSet();
            await new PdfPageConversionExecutor(worker, Publisher!, trial!).ExecuteAdmittedAsync(pdf, admission, (source, index, result) =>
            {
                var row = byId[source.ItemId];
                row.ApplyPdfPage(index, result);
                Summary = $"Converting {row.Name}: {row.SavedPdfPages} of {row.PdfPageCount} page copies saved.";
            }, token, completed);
        }
    }
}
