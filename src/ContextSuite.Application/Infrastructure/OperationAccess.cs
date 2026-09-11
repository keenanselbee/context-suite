using ContextSuite.Core.Images;
using ContextSuite.Core.Licensing;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Pdf;

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
    Task<OperationAdmission> AdmitConversionAsync(ConfirmedPdfPageConversion confirmed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This access implementation does not admit PDF page conversions.");
    Task<OperationAdmission> AdmitConversionAsync(ConfirmedAudioConversion confirmed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This access implementation does not admit audio conversions.");
    Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken cancellationToken);
    Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedFlacOptimization confirmed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This access implementation does not admit FLAC operations.");
    Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPdfOptimization confirmed, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This access implementation does not admit PDF operations.");
}

// Media and Explorer never see the key or service. Paid batches bypass trial
// bookkeeping entirely; only an unactivated installation may use its trial.
internal sealed class OperationAccess(LocalTrialStore trial, PaidLicenseManager paid) : IOperationAccess
{
    public async Task<OperationAdmission> AdmitConversionAsync(ConfirmedPdfPageConversion confirmed, CancellationToken cancellationToken)
    {
        _ = confirmed.Plan.Confirm();
        var status = await paid.ReadStatusAsync(cancellationToken);
        if (status.State == PaidLicenseState.NotActivated) return await ((IOperationAccess)trial).AdmitConversionAsync(confirmed, cancellationToken);
        return new(new(status.CanStart, status.Message), confirmed.Plan.BatchId);
    }

    public async Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPdfOptimization confirmed, CancellationToken cancellationToken)
    {
        if (!confirmed.Plan.HasExecutableItems) throw new InvalidDataException("No valid PDF optimization was confirmed.");
        var status = await paid.ReadStatusAsync(cancellationToken);
        if (status.State == PaidLicenseState.NotActivated) return await ((IOperationAccess)trial).AdmitOptimizationAsync(confirmed, cancellationToken);
        return new(new(status.CanStart, status.Message), confirmed.Plan.BatchId);
    }

    public async Task<OperationAccessStatus> ReadAccessAsync(CancellationToken cancellationToken = default)
    {
        var status = await paid.ReadStatusAsync(cancellationToken);
        if (status.State == PaidLicenseState.NotActivated) return await ((IOperationAccess)trial).ReadAccessAsync(cancellationToken);
        return new(status.CanStart, status.Message);
    }

    public async Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken cancellationToken)
    {
        if (!confirmed.Plan.HasExecutableItems) throw new InvalidDataException("No valid conversion was confirmed.");
        var status = await paid.ReadStatusAsync(cancellationToken);
        if (status.State == PaidLicenseState.NotActivated) return await ((IOperationAccess)trial).AdmitConversionAsync(confirmed, cancellationToken);
        return new(new(status.CanStart, status.Message), confirmed.Plan.BatchId);
    }

    public async Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken cancellationToken)
    {
        if (!confirmed.Plan.HasExecutableItems) throw new InvalidDataException("No valid optimization was confirmed.");
        var status = await paid.ReadStatusAsync(cancellationToken);
        if (status.State == PaidLicenseState.NotActivated) return await ((IOperationAccess)trial).AdmitOptimizationAsync(confirmed, cancellationToken);
        return new(new(status.CanStart, status.Message), confirmed.Plan.BatchId);
    }

    public async Task<OperationAdmission> AdmitConversionAsync(ConfirmedAudioConversion confirmed, CancellationToken cancellationToken)
    {
        if (!confirmed.Plan.HasExecutableItems) throw new InvalidDataException("No valid audio conversion was confirmed.");
        var status = await paid.ReadStatusAsync(cancellationToken);
        if (status.State == PaidLicenseState.NotActivated) return await ((IOperationAccess)trial).AdmitConversionAsync(confirmed, cancellationToken);
        return new(new(status.CanStart, status.Message), confirmed.Plan.BatchId);
    }

    public async Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedFlacOptimization confirmed, CancellationToken cancellationToken)
    {
        if (!confirmed.Plan.HasExecutableItems) throw new InvalidDataException("No valid FLAC optimization was confirmed.");
        var status = await paid.ReadStatusAsync(cancellationToken);
        if (status.State == PaidLicenseState.NotActivated) return await ((IOperationAccess)trial).AdmitOptimizationAsync(confirmed, cancellationToken);
        return new(new(status.CanStart, status.Message), confirmed.Plan.BatchId);
    }
}
