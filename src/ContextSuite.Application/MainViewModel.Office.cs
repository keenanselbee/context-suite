using System.ComponentModel;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Office;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Application;

internal sealed partial class MainViewModel
{
    private OfficeConversionExecutor? _officeExecutor;
    private Task<OfficeRecoveryReport>? _officeRecovery;

    // Extension selects a workflow only. Content preflight and the worker must
    // independently establish the exact supported variant before execution.
    private static bool IsOfficePdfPath(string path) => Path.GetExtension(path).ToLowerInvariant() is ".docx" or ".xlsx" or ".pptx";

    internal Task<OfficeRecoveryReport> BeginOfficeRecovery(CancellationToken token)
    {
        if (officeContextRoot is null) throw new InvalidOperationException("Missing Office context root.");
        return _officeRecovery ??= Task.Run(() => OfficeRecoveryCoordinator.RecoverAsync(officeContextRoot, worker.OfficeEngineDirectory, token));
    }

    private async Task ConvertPdfBatchAsync(OperationRequest request, FileRow[] rows, CancellationToken token)
    {
        var imageRow = rows.FirstOrDefault(row => row.IsImagePdf);
        var officeRows = rows.Where(row => !row.IsImagePdf).ToArray();
        var sources = new List<OfficeConversionSource>();
        if (officeRows.Length != 0)
        {
            if (!worker.HasOfficeConverter || officeContextRoot is null)
                foreach (var row in officeRows) row.ApplyResult(new(row.Path, OperationState.Unsupported, "Office PDF conversion is unavailable in this build."));
            else
            {
                Summary = "Checking interrupted Office work before conversion.";
                var recovery = await BeginOfficeRecovery(_lifetime.Token).WaitAsync(token);
                SetOfficeRecoveryReport(recovery);
                if (recovery.Cancelled || recovery.Incomplete || recovery.Entries.Any(entry => entry.State == OfficeRecoveryState.ReviewRequired))
                    foreach (var row in officeRows) row.ApplyResult(new(row.Path, OperationState.Failed,
                        "Earlier Office work needs review before another document can be converted. See the recovery information. Original kept."));
                else
                    foreach (var row in officeRows)
                    {
                        token.ThrowIfCancellationRequested();
                        row.ApplyResult(new(row.Path, OperationState.Running, "Checking document for PDF conversion"));
                        try
                        {
                            var source = await Task.Run(async () =>
                            {
                                using var input = PublicationFiles.OpenRead(row.Path);
                                if (input.Length is <= 0 or > OfficeHostProtocol.MaximumSourceBytes)
                                    throw new InvalidDataException("The document exceeds the Office input size limit.");
                                var facts = await OfficeSourcePreflight.InspectOpenXmlAsync(row.Path, input, token);
                                if (facts.Refusal is not null) throw new InvalidDataException(facts.Refusal);
                                if (!string.Equals(Path.GetExtension(row.Path), "." + facts.FormatId, StringComparison.OrdinalIgnoreCase))
                                    throw new InvalidDataException("The document contents do not match its file extension. Correct the extension and try again.");
                                var identity = await PublicationFiles.FingerprintAsync(input, token);
                                return new OfficeConversionSource(row.ItemId, row.Path, facts.FormatId!,
                                    facts.FormatId == "xlsx" ? row.OfficeCalculation ?? "" : "none", identity.Length, identity.Sha256);
                            }, token);
                            sources.Add(source);
                        }
                        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception)
                        {
                            row.ApplyResult(new(row.Path, error is InvalidDataException ? OperationState.Unsupported : OperationState.Failed,
                                error is InvalidDataException ? error.Message + " Original kept." : "Could not read this document. Check that it is available and try again. Original kept."));
                        }
                    }
            }
        }
        ConfirmedImagePdf? images = imageRow is null ? null : await PrepareImagesToPdfAsync(request, imageRow, token);
        if (imageRow?.Result.State == OperationState.Cancelled) { CancelUnfinished(); return; }
        if (sources.Any(source => source.Format == "xlsx" && officeRows.Single(row => row.ItemId == source.ItemId).OfficeCalculation is null))
        {
            Summary = "Choose how spreadsheet formulas appear in the PDF. No files changed by this batch.";
            var calculation = OfficeCalculationRequested is null ? null : await OfficeCalculationRequested(token);
            token.ThrowIfCancellationRequested();
            if (calculation is null) { CancelUnfinished(); return; }
            if (calculation is not ("cached" or "recalculate")) throw new InvalidDataException("Invalid spreadsheet calculation choice.");
            for (var index = 0; index < sources.Count; index++)
            {
                var row = officeRows.Single(row => row.ItemId == sources[index].ItemId);
                if (sources[index].Format != "xlsx" || row.OfficeCalculation is not null) continue;
                row.OfficeCalculation = calculation;
                sources[index] = sources[index] with { Calculation = calculation };
            }
        }
        ConfirmedOfficeConversion? office = null;
        if (sources.Count != 0)
        {
            try { office = OfficeConversionPlan.Create(request.RequestId, sources, rows[0].Settings).Confirm(); }
            catch (InvalidDataException)
            {
                foreach (var source in sources) officeRows.Single(row => row.ItemId == source.ItemId).ApplyResult(new(source.Path, OperationState.Unsupported,
                    "This Office selection contains duplicate files or exceeds its size limit. Select fewer documents without duplicates. Originals kept."));
            }
        }
        if (images is null && office is null) return;
        token.ThrowIfCancellationRequested();
        var admission = images is not null ? await trial!.AdmitConversionAsync(images, token) : await trial!.AdmitConversionAsync(office!, token);
        if (images is not null)
            await new ImagePdfExecutor(worker, Publisher!, trial!).ExecuteAdmittedAsync(images, admission, result =>
            {
                imageRow!.ApplyResult(result with { Path = imageRow.Path });
                if (result.State == OperationState.Running) Summary = "Creating one PDF from the reviewed images.";
            }, token);
        if (office is not null)
        {
            _officeExecutor ??= new(worker, Publisher!, trial!, officeContextRoot!);
            var byPath = office.Plan.Sources.ToDictionary(source => source.Path,
                source => officeRows.Single(row => row.ItemId == source.ItemId), StringComparer.OrdinalIgnoreCase);
            await _officeExecutor.ExecuteAdmittedAsync(office, admission, result =>
            {
                byPath[result.Path].ApplyResult(result);
                if (result.State == OperationState.Running) Summary = "Creating PDF: " + Path.GetFileName(result.Path);
            }, token);
        }

        void CancelUnfinished()
        {
            foreach (var row in rows)
                if (row.Result.State is OperationState.Pending or OperationState.Running)
                    row.ApplyResult(new(row.Path, OperationState.Cancelled, "PDF conversion cancelled before execution. Originals kept."));
        }
    }

    private async Task DisposeOfficeAsync()
    {
        if (_officeExecutor is null) return;
        try { await _officeExecutor.DisposeAsync(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            Win32Exception or InvalidOperationException or AggregateException or System.Security.SecurityException)
        {
            // Process exit releases retained leases. The durable ownership record
            // remains available to the next startup's recovery scan.
            Console.Error.WriteLine($"Office cleanup remains pending: {error.GetType().Name}; HRESULT 0x{error.HResult:X8}.");
        }
    }
}
