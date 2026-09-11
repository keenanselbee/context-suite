using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Application;

internal sealed partial class MainViewModel
{
    private async Task ConvertImagesToPdfAsync(OperationRequest request, FileRow row, CancellationToken token)
    {
        if (!worker.HasImagePdfConverter)
        {
            row.ApplyResult(new(row.Path, OperationState.Unsupported, "PDF conversion is unavailable in this build."));
            return;
        }
        var sources = new List<ImageSourceFacts>();
        long sourceBytes = 0, pixels = 0;
        var reviewed = row.ReviewedImagePdf;
        var paths = reviewed?.Pages.Select(page => page.Source.Path).ToArray() ?? row.ImagePdfPaths.ToArray();
        for (var index = 0; index < paths.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            row.ApplyResult(new(row.Path, OperationState.Running, $"Checking image {index + 1} of {paths.Length} for one PDF."));
            try
            {
                var expected = reviewed?.Pages[index].Source;
                var source = await worker.ProbeAsync(new(expected?.ItemId ?? Guid.NewGuid(), paths[index]), token);
                if (expected is not null && (source.Sha256 != expected.Sha256 || source.FileBytes != expected.FileBytes))
                {
                    row.ImagePdfRetryBlocked = true;
                    row.ApplyResult(new(row.Path, OperationState.Failed,
                        $"{Path.GetFileName(paths[index])} changed since page order was reviewed. Start a new Convert > PDF command. All originals are kept."));
                    return;
                }
                var page = ImagePdfPage.Create(source);
                sourceBytes += source.FileBytes; pixels += (long)page.Width * page.Height;
                if (sourceBytes > ImagePdfPlan.MaximumSourceBytes || pixels > ImagePdfPlan.MaximumPixels)
                    throw new NotSupportedException("The selected images exceed the combined PDF budget.");
                sources.Add(source);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or
                InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
            {
                var unsupported = error is NotSupportedException or MediaWorkerException { Failure: ImageFailure.UnsupportedInput };
                row.ApplyResult(new(row.Path, unsupported ? OperationState.Unsupported : OperationState.Failed,
                    $"Could not include {Path.GetFileName(paths[index])}. PDF conversion currently supports static PNG, JPEG, WebP, BMP and TGA images within its size limits. Check this file and try again. No PDF was saved; all originals are kept."));
                return;
            }
        }
        ImagePdfPlan plan;
        try { plan = ImagePdfPlan.Create(request.RequestId, sources, row.Settings); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException)
        {
            row.ApplyResult(new(row.Path, OperationState.Unsupported,
                "These images cannot form one PDF within this build's limits. Select fewer or smaller images without duplicate files. No PDF was saved; all originals are kept."));
            return;
        }
        var confirmed = reviewed is null ? await ConfirmImagePdfOrderAsync(plan, token) : plan.Confirm(true);
        if (confirmed is null)
        {
            row.ApplyResult(new(row.Path, OperationState.Cancelled, "PDF conversion cancelled before confirmation. All originals are kept."));
            return;
        }
        row.ReviewedImagePdf = confirmed.Plan;
        token.ThrowIfCancellationRequested();
        await new ImagePdfExecutor(worker, Publisher!, trial!).ExecuteAsync(confirmed, result =>
        {
            // The row represents the original invocation; publication names the reviewed first image.
            row.ApplyResult(result with { Path = row.Path });
            Summary = result.State == OperationState.Running ? "Creating one PDF from the reviewed images." : Summary;
        }, token);
    }
}
