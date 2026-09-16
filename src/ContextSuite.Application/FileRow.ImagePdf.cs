using System.Collections.Immutable;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Application;

internal sealed partial class FileRow
{
    public bool IsImagePdf => Operation == "convert" && Action == "pdf" && !ImagePdfPaths.IsDefaultOrEmpty;
    internal string? OfficeCalculation { get; set; }
    internal ImmutableArray<string> ImagePdfPaths { get; set; } = [];
    internal Guid ImagePdfRetryId { get; private set; } = Guid.NewGuid();
    internal ImagePdfPlan? ReviewedImagePdf { get; set; }
    internal bool ImagePdfRetryBlocked { get; set; }
    private string ImagePdfDetails => !IsImagePdf ? "" : "\n" + (ReviewedImagePdf is null ? "Selected images:" : "Reviewed page order:") + "\n" +
        string.Join("\n", (ReviewedImagePdf?.Pages.Select(page => page.Source.Path) ?? ImagePdfPaths)
            .Select((path, index) => $"{index + 1}. {path}"));

    internal void ResumeImagePdfFrom(FileRow previous)
    {
        if (!IsImagePdf || !previous.IsImagePdf) return;
        ImagePdfPaths = previous.ImagePdfPaths;
        ImagePdfRetryId = previous.ImagePdfRetryId;
        ReviewedImagePdf = previous.ReviewedImagePdf;
        ImagePdfRetryBlocked = previous.ImagePdfRetryBlocked;
    }
}
