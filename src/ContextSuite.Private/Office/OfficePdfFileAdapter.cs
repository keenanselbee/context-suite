using System.Security.Cryptography;
using ContextSuite.Core.Office;
using ContextSuite.Core.Pdf;
using ContextSuite.Private.IO;
using ContextSuite.Private.Pdf;

namespace ContextSuite.Private.Office;

// Validates an acknowledged export independently, then fills an existing empty
// reservation. Final publication and failed-reservation cleanup belong to the app.
public sealed class OfficePdfFileAdapter(PdfProbeAdapter structure, PdfRasterAdapter pages)
{
    public async Task<OfficePdfResult> ValidateAsync(OfficePdfWork work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); work.Validate();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var active = deadline.Token;
        try
        {
            using var original = MediaFiles.OpenRead(work.Export.SourcePath);
            using var candidate = MediaFiles.OpenRead(work.Export.CandidatePath);
            using var output = new ReservedMediaOutput(work.TemporaryPath, OfficeHostProtocol.MaximumOutputBytes);
            var completion = work.Candidate.Completion;
            if (original.Length != work.Export.SourceBytes || candidate.Length != completion.OutputBytes ||
                await Hash(original, active) != work.Export.SourceSha256.ToUpperInvariant() || await Hash(candidate, active) != completion.OutputSha256.ToUpperInvariant())
                throw new InvalidDataException("Office source or candidate differs from its completed export.");
            await OfficeWorkbookValidation.CheckAsync(work.Export, completion, active);
            var count = await structure.InspectOfficeAsync(candidate, active);
            var inspected = await pages.ProbeAsync(new PdfFileProbe(work.Export.ItemId, work.Export.CandidatePath), active);
            inspected.Validate();
            if (inspected.Document.Pages.Length != count || inspected.FileBytes != completion.OutputBytes ||
                inspected.Sha256 != completion.OutputSha256.ToUpperInvariant())
                throw new InvalidDataException("Independent Office PDF readers disagree.");
            if (await Hash(original, active) != work.Export.SourceSha256.ToUpperInvariant() ||
                await Hash(candidate, active) != inspected.Sha256)
                throw new IOException("Office source or output changed during validation.");
            candidate.Position = 0;
            var buffer = new byte[65536]; int read;
            while ((read = await candidate.ReadAsync(buffer, active)) != 0)
            { active.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); }
            output.Flush(true);
            if (output.Length != completion.OutputBytes || await Hash(output, active) != inspected.Sha256)
                throw new IOException("Validated Office PDF changed while copying to its reservation.");
            active.ThrowIfCancellationRequested();
            return new(new(work.Export.ItemId, inspected.Sha256, true), work.Export.SourceBytes, work.Export.SourceSha256,
                output.Length, count, work.Policy, "qpdf 12.4.1; PDFium 8044; " + work.Policy);
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new TimeoutException("Office PDF validation exceeded its deadline.", error); }
    }

    private static async Task<string> Hash(FileStream stream, CancellationToken token)
    {
        stream.Position = 0;
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }
}
