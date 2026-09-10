using System.ComponentModel;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Licensing;

namespace ContextSuite.Application;

internal sealed class LicenseViewModel(PaidLicenseManager manager) : INotifyPropertyChanged, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;
    private bool _disposed;
    private PaidLicenseStatus? _status;
    private string _message = "One purchase unlocks Context Suite and all future updates.";
    public bool CanAct => !_busy && !_disposed;
    public bool CanActivate => CanAct && _status?.State == PaidLicenseState.NotActivated;
    public bool CanValidate => CanAct && _status?.State is PaidLicenseState.Active or PaidLicenseState.Offline or PaidLicenseState.Expired or PaidLicenseState.Unavailable;
    public bool CanDeactivate => CanAct && _status?.State is PaidLicenseState.Active or PaidLicenseState.Offline or PaidLicenseState.Expired or PaidLicenseState.Rejected;
    public bool ShowRecovery => _status?.State is PaidLicenseState.Rejected or PaidLicenseState.RecoveryRequired;
    public bool CanRecover => CanAct && ShowRecovery;
    public string Message => _message;
    public event PropertyChangedEventHandler? PropertyChanged;

    public Task LoadAsync() => RunAsync(() => manager.ReadStatusAsync(_lifetime.Token));
    public Task ActivateAsync(string key) => RunAsync(() => manager.ActivateAsync(key, _lifetime.Token));
    public Task ValidateAsync() => RunAsync(() => manager.RefreshAsync(force: true, _lifetime.Token));
    public Task DeactivateAsync() => RunAsync(() => manager.DeactivateAsync(_lifetime.Token));
    public Task RecoverAsync(bool confirmed) => RunAsync(() => manager.ForgetAfterPortalRecoveryAsync(confirmed, _lifetime.Token));

    private async Task RunAsync(Func<Task<PaidLicenseStatus>> action)
    {
        if (!CanAct) return;
        _busy = true;
        _message = "Checking license…";
        Notify();
        try { _status = await action(); _message = _status.Message; }
        catch (OperationCanceledException) { _message = "License request cancelled. Check the customer portal if an activation change was in progress."; }
        finally { _busy = false; Notify(); }
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new(nameof(CanAct)));
        PropertyChanged?.Invoke(this, new(nameof(Message)));
        foreach (var name in new[] { nameof(CanActivate), nameof(CanValidate), nameof(CanDeactivate), nameof(CanRecover), nameof(ShowRecovery) })
            PropertyChanged?.Invoke(this, new(name));
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel(); _lifetime.Dispose();
    }
}
