using System.Security.Cryptography;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Images;
using ContextSuite.Core.Office;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Office;

// A bounded positive-risk guard, not a complete Excel rendering validator.
internal static class OfficeWorkbookValidation
{
    internal static async Task CheckAsync(OfficeExportWork work, OfficeHostCompletion completion, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        OfficeHostProtocol.ValidateWorkbookIdentity(work.Format, completion.WorkbookBytes, completion.WorkbookSha256);
        if (work.Format != "xlsx") return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var workbook = MediaFiles.OpenRead(work.WorkbookPath);
            await CheckContentAsync(workbook, work.WorkbookPath, completion, deadline.Token);
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new TimeoutException("Calculated workbook inspection exceeded its deadline.", error); }
    }

    // The caller owns the checked source lease and deadline. Keep the same
    // content path usable with a bounded read stream for interruption checks.
    internal static async Task CheckContentAsync(Stream workbook, string path, OfficeHostCompletion completion, CancellationToken token)
    {
        if (workbook.Length != completion.WorkbookBytes ||
            Convert.ToHexString(await SHA256.HashDataAsync(workbook, token)) != completion.WorkbookSha256!.ToUpperInvariant())
            throw new InvalidDataException("Calculated workbook differs from its completed export.");
        workbook.Position = 0;
        var inspection = await OfficeSourcePreflight.InspectOpenXmlAsync(path, workbook, token);
        if (inspection.FormatId != "xlsx")
            throw new InvalidDataException("Calculated workbook contents do not identify a supported workbook.");
        if (inspection.StoredDates is { EarlyDateCells: > 0 })
            throw new ImageFailureException(ImageFailure.OfficeDateFidelity);
        if (inspection.Refusal is not null) throw new InvalidDataException(inspection.Refusal);
        // Incomplete inspection is not evidence of zero risks. Broader
        // formatting and date fidelity remain separate acceptance gates.
    }
}
