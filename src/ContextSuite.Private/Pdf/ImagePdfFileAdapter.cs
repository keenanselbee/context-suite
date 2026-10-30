using System.Collections.Immutable;
using System.Security.Cryptography;
using ContextSuite.Core.Pdf;
using ContextSuite.Private.Images;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Pdf;

public sealed class ImagePdfFileAdapter(ImageAdapter images, ImagePdfValidator validator)
{
    public async Task<ImagePdfResult> ConvertAsync(ImagePdfWork work, CancellationToken token)
    {
        work.Validate();
        var leases = new List<FileStream>();
        try
        {
            foreach (var page in work.Plan.Pages)
            {
                token.ThrowIfCancellationRequested();
                var lease = MediaFiles.OpenRead(page.Source.Path); leases.Add(lease);
                if (lease.Length != page.Source.FileBytes) throw new InvalidDataException("An original image changed after planning.");
            }
            using var output = new ReservedMediaOutput(work.TemporaryPath, ImagePdfPlan.MaximumOutputBytes);
            var confirmed = work.Plan.Confirm(work.PageOrderReviewed);
            var candidate = ImagePdfWriter.Create(confirmed, images, token);
            var hash = await validator.ValidateAsync(candidate, confirmed, token);
            token.ThrowIfCancellationRequested();
            output.Write(candidate.Bytes); output.Flush(true); output.Position = 0;
            if (output.Length != candidate.Bytes.Length || Convert.ToHexString(await SHA256.HashDataAsync(output, token)) != hash)
                throw new InvalidDataException("Combined PDF reservation changed after validation.");
            for (var index = 0; index < leases.Count; index++)
            {
                var source = work.Plan.Pages[index].Source;
                MediaFiles.CheckHandle(leases[index], source.Path); leases[index].Position = 0;
                if (Convert.ToHexString(await SHA256.HashDataAsync(leases[index], token)) != source.Sha256)
                    throw new InvalidDataException("An original image changed during combined PDF conversion.");
            }
            return new(new(work.OutputId, hash, true), work.Plan.BatchId, work.Plan.Pages.Length,
                work.Plan.Pages.Select(page => page.Source.Sha256).ToImmutableArray(), candidate.Bytes.Length,
                work.Policy, "Context Suite raster PDF 1; qpdf 12.4.1 validation");
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }
}
