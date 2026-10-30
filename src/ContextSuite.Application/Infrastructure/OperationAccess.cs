using ContextSuite.Core.Images;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Office;
using ContextSuite.Core.Tiff;
using ContextSuite.Core.Svg;
using ContextSuite.Core.Gif;

namespace ContextSuite.Application.Infrastructure;

internal sealed record OperationAccessStatus(bool CanStart, string Message);
internal sealed record OperationAdmission(OperationAccessStatus Status, Guid BatchId)
{
    public bool IsAllowed => Status.CanStart;
}
internal interface IOperationAccess
{
    Task<OperationAccessStatus> ReadAccessAsync(CancellationToken cancellationToken = default);
    Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken cancellationToken);
    Task<OperationAdmission> AdmitConversionAsync(ConfirmedGifConversion confirmed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This access implementation does not admit GIF conversions.");
    Task<OperationAdmission> AdmitConversionAsync(ConfirmedOfficeConversion confirmed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This access implementation does not admit Office conversions.");
    Task<OperationAdmission> AdmitConversionAsync(ConfirmedImagePdf confirmed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This access implementation does not admit combined PDF conversion.");
    Task<OperationAdmission> AdmitConversionAsync(ConfirmedPdfDocx confirmed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This access implementation does not admit PDF-to-Word conversions.");
    Task<OperationAdmission> AdmitConversionAsync(ConfirmedPdfDark confirmed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This access implementation does not admit PDF dark-mode conversions.");
    Task<OperationAdmission> AdmitConversionAsync(ConfirmedPdfPageConversion confirmed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This access implementation does not admit PDF page conversions.");
    Task<OperationAdmission> AdmitConversionAsync(ConfirmedTiffConversion confirmed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This access implementation does not admit TIFF conversions.");
    Task<OperationAdmission> AdmitConversionAsync(ConfirmedSvgConversion confirmed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This access implementation does not admit SVG conversions.");
    Task<OperationAdmission> AdmitConversionAsync(ConfirmedAudioConversion confirmed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This access implementation does not admit audio conversions.");
    Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken cancellationToken);
    Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedOfficePackageOptimization confirmed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This access implementation does not admit document package optimization.");
    Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedLosslessImageOptimization confirmed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This access implementation does not admit lossless image optimization.");
    Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedFlacOptimization confirmed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This access implementation does not admit FLAC operations.");
    Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPdfOptimization confirmed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This access implementation does not admit PDF operations.");
}

// Validates a confirmed batch before execution. Free operation admission has no
// persisted access state, clock, provider, or network dependency.
internal sealed class OperationAdmissionService : IOperationAccess
{
    public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedOfficePackageOptimization confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(confirmed);
        _ = confirmed.Plan.Confirm();
        return Task.FromResult(new OperationAdmission(new(true, "Ready."), confirmed.Plan.BatchId));
    }

    public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedLosslessImageOptimization confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(confirmed);
        _ = confirmed.Plan.Confirm();
        return Task.FromResult(new OperationAdmission(new(true, "Ready."), confirmed.Plan.BatchId));
    }

    public Task<OperationAdmission> AdmitConversionAsync(ConfirmedGifConversion confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(confirmed);
        _ = confirmed.Plan.Confirm();
        return Task.FromResult(new OperationAdmission(new(true, "Ready."), confirmed.Plan.BatchId));
    }
    public Task<OperationAdmission> AdmitConversionAsync(ConfirmedSvgConversion confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(confirmed);
        _ = confirmed.Plan.Confirm();
        return Task.FromResult(new OperationAdmission(new(true, "Ready."), confirmed.Plan.BatchId));
    }
    public Task<OperationAdmission> AdmitConversionAsync(ConfirmedTiffConversion confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(confirmed);
        _ = confirmed.Plan.Confirm();
        return Task.FromResult(new OperationAdmission(new(true, "Ready."), confirmed.Plan.BatchId));
    }
    public Task<OperationAdmission> AdmitConversionAsync(ConfirmedOfficeConversion confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(confirmed);
        _ = confirmed.Plan.Confirm();
        return Task.FromResult(new OperationAdmission(new(true, "Ready."), confirmed.Plan.BatchId));
    }

    public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImagePdf confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(confirmed);
        _ = confirmed.Plan.Confirm(true);
        return Task.FromResult(new OperationAdmission(new(true, "Ready."), confirmed.Plan.BatchId));
    }

    public Task<OperationAdmission> AdmitConversionAsync(ConfirmedPdfDocx confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(confirmed);
        _ = confirmed.Plan.Confirm();
        return Task.FromResult(new OperationAdmission(new(true, "Ready."), confirmed.Plan.BatchId));
    }

    public Task<OperationAdmission> AdmitConversionAsync(ConfirmedPdfDark confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(confirmed);
        _ = confirmed.Plan.Confirm();
        return Task.FromResult(new OperationAdmission(new(true, "Ready."), confirmed.Plan.BatchId));
    }

    public Task<OperationAdmission> AdmitConversionAsync(ConfirmedPdfPageConversion confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(confirmed);
        _ = confirmed.Plan.Confirm();
        return Task.FromResult(new OperationAdmission(new(true, "Ready."), confirmed.Plan.BatchId));
    }

    public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPdfOptimization confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(confirmed);
        if (!confirmed.Plan.HasExecutableItems) throw new InvalidDataException("No valid PDF optimization was confirmed.");
        return Task.FromResult(new OperationAdmission(new(true, "Ready."), confirmed.Plan.BatchId));
    }

    public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OperationAccessStatus(true, "Ready."));
    }

    public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(confirmed);
        if (!confirmed.Plan.HasExecutableItems) throw new InvalidDataException("No valid conversion was confirmed.");
        return Task.FromResult(new OperationAdmission(new(true, "Ready."), confirmed.Plan.BatchId));
    }

    public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(confirmed);
        if (!confirmed.Plan.HasExecutableItems) throw new InvalidDataException("No valid optimization was confirmed.");
        return Task.FromResult(new OperationAdmission(new(true, "Ready."), confirmed.Plan.BatchId));
    }

    public Task<OperationAdmission> AdmitConversionAsync(ConfirmedAudioConversion confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(confirmed);
        if (!confirmed.Plan.HasExecutableItems) throw new InvalidDataException("No valid audio conversion was confirmed.");
        return Task.FromResult(new OperationAdmission(new(true, "Ready."), confirmed.Plan.BatchId));
    }

    public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedFlacOptimization confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(confirmed);
        if (!confirmed.Plan.HasExecutableItems) throw new InvalidDataException("No valid FLAC optimization was confirmed.");
        return Task.FromResult(new OperationAdmission(new(true, "Ready."), confirmed.Plan.BatchId));
    }

    public Task<OperationAdmission> AdmitAsync(ConfirmedImageBatch confirmed, CancellationToken cancellationToken = default) =>
        AdmitConversionAsync(confirmed, cancellationToken);
    public Task<OperationAdmission> AdmitAsync(ConfirmedPngOptimization confirmed, CancellationToken cancellationToken = default) =>
        AdmitOptimizationAsync(confirmed, cancellationToken);
}
