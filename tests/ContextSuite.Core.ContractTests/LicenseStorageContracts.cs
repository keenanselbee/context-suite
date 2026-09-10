using System.Text;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Licensing;
using ContextSuite.Core.Images;
using ContextSuite.Application;
using ContextSuite.Core.Audio;
using System.Collections.Immutable;

namespace ContextSuite.Core.ContractTests;

internal static class LicenseStorageContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var root = Path.Combine(scratch, "licensing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "license.bin");
        var clock = new Clock();
        var service = new FakeService();
        var store = new LicenseStore(path, LicenseEnvironment.Sandbox);
        var manager = new PaidLicenseManager(service, store, clock);
        const string key = "SYNTHETIC-PRIVATE-KEY-NOT-FOR-POLAR";
        check((await manager.ReadStatusAsync()).State == PaidLicenseState.NotActivated && service.Calls == 0, "license: no stored key means no HTTP");
        check((await manager.ActivateAsync(key)).CanStart && service.Activations == 1, "license: first activation grants access");
        var trialPath = Path.Combine(root, "trial.json");
        IOperationAccess access = new OperationAccess(new(trialPath, clock), manager);
        var facts = new ImageSourceFacts(Guid.NewGuid(), Path.Combine(root, "fixture.png"), new string('A', 64), 100,
            ImageFormat.Png, 10, 10, 8, 1, false, false, "sRGB", []);
        var convert = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], new(ImageFormat.WebP), new("convert", new())).Confirm(false, false, false);
        var optimize = PngOptimizationPlan.Create(Guid.NewGuid(), [facts], new("optimize", new())).Confirm(false, false);
        var paidAdmission = await access.AdmitConversionAsync(convert, default);
        var audio = new AudioFileSource(Guid.NewGuid(), Path.Combine(root, "fixture.flac"), new string('B', 64), 100,
            new("flac", 2, [new(0, "audio", "flac", 48000, 2, 16, null, 2, "stereo", ImmutableDictionary<string, string>.Empty)], ImmutableDictionary<string, string>.Empty));
        var flac = FlacOptimizationPlan.Create(Guid.NewGuid(), [audio], new("optimize", new())).Confirm(false, false);
        var flacAdmission = await access.AdmitOptimizationAsync(flac, default);
        check(flacAdmission.IsAllowed && !File.Exists(trialPath), "license: paid FLAC admission does not start trial");
        check(paidAdmission.IsAllowed && (await access.AdmitOptimizationAsync(optimize, default)).IsAllowed && !File.Exists(trialPath),
            "license: both paid operation paths admit without starting trial");
        check(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path)).Contains(key), "license: DPAPI file contains no plaintext key");
        var record = await store.ReadAsync(default);
        check(record?.Key == key && !record.ToString().Contains(key), "license: protected roundtrip and redacted diagnostics");
        var savedInstallation = record!.InstallationId;
        var reopened = new PaidLicenseManager(service, new(path, LicenseEnvironment.Sandbox), clock);
        check((await reopened.ReadStatusAsync()).CanStart && service.Activations == 1, "license: restart reuses persisted receipt without activation");
        check((await reopened.ActivateAsync(key)).State == PaidLicenseState.RecoveryRequired && service.Activations == 1, "license: repeated activate never consumes another slot");
        await reopened.RefreshAsync();
        check(service.Validations == 0, "license: fresh cache skips online refresh");
        clock.Now = clock.Now.AddDays(1);
        await reopened.RefreshAsync();
        check(service.Validations == 1 && service.LastActivation == record.Receipt!.ActivationId, "license: daily validate reuses exact activation");
        service.ValidationState = LicenseReplyState.Unavailable;
        clock.Now = clock.Now.AddDays(1);
        check((await reopened.RefreshAsync()).CanStart, "license: network outage preserves grace");
        var calls = service.Calls; await reopened.RefreshAsync();
        check(calls == service.Calls, "license: failed background refresh is throttled");
        service.ValidationState = LicenseReplyState.InvalidResponse;
        check((await reopened.RefreshAsync(force: true)).CanStart, "license: malformed service response does not revoke good cache");
        clock.Now = clock.Now.AddDays(29);
        check(!(await reopened.ReadStatusAsync()).CanStart, "license: 30 days since last successful validation expires");
        check(paidAdmission.IsAllowed && !(await access.AdmitOptimizationAsync(optimize, default)).IsAllowed && !File.Exists(trialPath),
            "license: paid expiry preserves old admission and cannot fall back to a new trial");
        check(flacAdmission.IsAllowed && !(await access.AdmitOptimizationAsync(flac, default)).IsAllowed && !File.Exists(trialPath),
            "license: paid FLAC expiry preserves admission and cannot fall back to trial");
        clock.Now = clock.Now.AddDays(-2);
        check(!(await reopened.ReadStatusAsync()).CanStart, "license: persisted expiry observation prevents clock rollback revival");
        service.ValidationState = LicenseReplyState.Granted;
        check((await reopened.RefreshAsync(force: true)).CanStart, "license: explicit online validation recovers corrected clock");
        service.ValidationState = LicenseReplyState.Rejected;
        check((await reopened.RefreshAsync(force: true)).State == PaidLicenseState.Rejected, "license: explicit rejection invalidates paid cache");
        check((await new PaidLicenseManager(service, store, clock).ReadStatusAsync()).State == PaidLicenseState.Rejected, "license: rejection survives restart");
        var beforeRecovery = await File.ReadAllBytesAsync(path);
        await reopened.ForgetAfterPortalRecoveryAsync(false);
        check(Enumerable.SequenceEqual(beforeRecovery, await File.ReadAllBytesAsync(path)), "license: recovery requires acknowledgement");
        await reopened.ForgetAfterPortalRecoveryAsync(true);
        check((await store.ReadAsync(default)) is { State: LicenseRecordState.NotActivated, Key: null } recovered && recovered.InstallationId == savedInstallation,
            "license: explicit recovery removes key but preserves installation identity");
        service.ValidationState = LicenseReplyState.Granted;
        await reopened.ActivateAsync(key);
        await reopened.ForgetAfterPortalRecoveryAsync(true);
        check((await reopened.ReadStatusAsync()).CanStart, "license: active license cannot be forgotten without deactivation");
        service.DeactivationState = LicenseReplyState.Unavailable;
        check((await reopened.DeactivateAsync()).State == PaidLicenseState.RecoveryRequired, "license: lost deactivation reply blocks cache");
        check(!(await new PaidLicenseManager(service, store, clock).ReadStatusAsync()).CanStart, "license: pending deactivation survives restart");
        service.DeactivationState = LicenseReplyState.Deactivated;
        check((await reopened.DeactivateAsync()).State == PaidLicenseState.NotActivated, "license: confirmed deactivation clears local entitlement");
        check((await access.ReadAccessAsync()).CanStart && !File.Exists(trialPath), "license: deactivated installation may use remaining unstarted trial without starting during status read");
        check((await access.AdmitConversionAsync(convert, default)).IsAllowed && File.Exists(trialPath), "license: unactivated confirmed work retains normal trial admission");
        service.ActivationState = LicenseReplyState.Unavailable;
        var activationCount = service.Activations;
        await reopened.ActivateAsync(key); await reopened.ActivateAsync(key);
        check(service.Activations == activationCount + 1 && (await reopened.ReadStatusAsync()).State == PaidLicenseState.RecoveryRequired,
            "license: lost activation reply never auto-retries across calls");
        await reopened.ForgetAfterPortalRecoveryAsync(true);
        service.CancelActivation = true;
        try { await reopened.ActivateAsync(key); throw new InvalidOperationException("Cancellation lost"); }
        catch (OperationCanceledException) { check((await reopened.ReadStatusAsync()).State == PaidLicenseState.RecoveryRequired,
            "license: cancellation after pending journal requires recovery"); }
        service.CancelActivation = false;
        var wrongEnvironment = new PaidLicenseManager(service, new(path, LicenseEnvironment.Production), clock);
        check((await wrongEnvironment.ReadStatusAsync()).State == PaidLicenseState.Unavailable, "license: DPAPI entropy isolates sandbox from production");
        var original = await File.ReadAllBytesAsync(path);
        var damaged = original.ToArray(); damaged[^1] ^= 0xff; await File.WriteAllBytesAsync(path, damaged);
        check((await reopened.ReadStatusAsync()).State == PaidLicenseState.Unavailable, "license: tampered protected storage fails closed");
        check(Enumerable.SequenceEqual(damaged, await File.ReadAllBytesAsync(path)), "license: damaged file is not reset or overwritten");
        await File.WriteAllBytesAsync(path, original);
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await store.SaveAsync(record, cancelled.Token); throw new InvalidOperationException("Cancellation lost"); }
            catch (OperationCanceledException) { check(Enumerable.SequenceEqual(original, await File.ReadAllBytesAsync(path)), "license: cancelled save retains exact prior bytes"); }
        }
        check(!Directory.EnumerateFiles(root, "*.tmp").Any(), "license: no plaintext or interrupted temporary files retained");
        await using (var firstLease = await store.LockAsync(default))
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            try { await using var secondLease = await new LicenseStore(path, LicenseEnvironment.Sandbox).LockAsync(cancellation.Token); throw new InvalidOperationException("Lock not exclusive"); }
            catch (OperationCanceledException) { check(true, "license: independent stores serialize mutations with cancellable exclusive lease"); }
        }

        var concurrentStore = new LicenseStore(Path.Combine(root, "concurrent.bin"), LicenseEnvironment.Sandbox);
        var concurrentService = new FakeService();
        var concurrentManager = new PaidLicenseManager(concurrentService, concurrentStore, clock);
        await concurrentManager.ActivateAsync(key);
        concurrentService.PendingValidation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = concurrentManager.RefreshAsync(force: true);
        await concurrentService.ValidationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        check((await concurrentManager.ReadStatusAsync().WaitAsync(TimeSpan.FromSeconds(2))).CanStart,
            "license: cached admission does not wait for pending network validation");
        await concurrentManager.DeactivateAsync().WaitAsync(TimeSpan.FromSeconds(2));
        concurrentService.PendingValidation.SetResult(new(LicenseReplyState.Granted, "Delayed grant", concurrentService.Receipt));
        check((await refresh).State == PaidLicenseState.NotActivated && (await concurrentManager.ReadStatusAsync()).State == PaidLicenseState.NotActivated,
            "license: late validation cannot resurrect a deactivated installation");
        await concurrentManager.ActivateAsync(key);
        concurrentService.ValidationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        concurrentService.PendingValidation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var model = new LicenseViewModel(concurrentManager))
        {
            await model.LoadAsync();
            check(model.CanAct && !model.Message.Contains(key), "license UI: status never displays the customer key");
            check(!model.CanActivate && model.CanValidate && model.CanDeactivate && !model.CanRecover,
                "license UI: activated key offers validation and transfer, not another activation or recovery");
            check(!model.ShowRecovery, "license UI: active license hides recovery instructions");
            var pending = model.ValidateAsync();
            await concurrentService.ValidationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var count = concurrentService.Validations;
            await model.ValidateAsync();
            check(!model.CanAct && concurrentService.Validations == count, "license UI: busy action suppresses duplicate requests");
            concurrentService.PendingValidation.SetResult(new(LicenseReplyState.Unavailable, "Synthetic outage"));
            await pending;
            check(model.CanAct && model.Message.Contains("offline") && !model.Message.Contains("validated online"),
                "license UI: outage explains cached access without claiming online validation succeeded");
        }
        check((await concurrentManager.ActivateAsync(" ")).CanStart,
            "license: empty input never misreports an existing active license as unactivated");

        var declinedService = new FakeService { ActivationState = LicenseReplyState.ActivationDeclined };
        var declinedStore = new LicenseStore(Path.Combine(root, "declined.bin"), LicenseEnvironment.Sandbox);
        var declinedManager = new PaidLicenseManager(declinedService, declinedStore, clock);
        using (var model = new LicenseViewModel(declinedManager))
        {
            await model.LoadAsync();
            check(model.CanActivate && !model.CanValidate && !model.CanDeactivate && !model.CanRecover,
                "license UI: unactivated installation offers only activation");
            check(!model.ShowRecovery, "license UI: first activation hides recovery instructions");
            await model.ActivateAsync(key);
            check(model.CanActivate && !model.CanRecover && (await declinedStore.ReadAsync(default)) is { State: LicenseRecordState.NotActivated, Key: null },
                "license: definitive refusal clears pending key and allows correction without portal recovery");
            declinedService.ActivationState = LicenseReplyState.Granted;
            await model.ActivateAsync(key);
            check(!model.CanActivate && model.CanValidate && declinedService.Activations == 2,
                "license UI: corrected key can be activated explicitly after refusal");
        }

        var interruptedService = new FakeService { PendingActivation = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var interruptedManager = new PaidLicenseManager(interruptedService,
            new LicenseStore(Path.Combine(root, "interrupted-ui.bin"), LicenseEnvironment.Sandbox), clock);
        var interruptedModel = new LicenseViewModel(interruptedManager);
        await interruptedModel.LoadAsync();
        var activationTask = interruptedModel.ActivateAsync(key);
        await interruptedService.ActivationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        interruptedModel.Dispose(); interruptedModel.Dispose();
        await activationTask.WaitAsync(TimeSpan.FromSeconds(2));
        check(!interruptedModel.CanAct && (await interruptedManager.ReadStatusAsync()).State == PaidLicenseState.RecoveryRequired,
            "license UI: closing during activation cancels request but preserves pending recovery");
        using (var reopenedModel = new LicenseViewModel(interruptedManager))
        {
            await reopenedModel.LoadAsync();
            check(!reopenedModel.CanActivate && !reopenedModel.CanValidate && !reopenedModel.CanDeactivate && reopenedModel.CanRecover,
                "license UI: interrupted activation offers portal recovery without allocating another slot");
            check(reopenedModel.ShowRecovery, "license UI: interrupted activation shows recovery instructions directly");
            await reopenedModel.RecoverAsync(false);
            check(reopenedModel.ShowRecovery, "license UI: unconfirmed recovery keeps instructions visible");
            await reopenedModel.RecoverAsync(true);
            check(!reopenedModel.ShowRecovery && reopenedModel.CanActivate,
                "license UI: confirmed recovery hides instructions and restores activation");
        }
        await CheckOpenPlannersAsync(root, check);
    }

    private static async Task CheckOpenPlannersAsync(string root, Action<bool, string> check)
    {
        var clock = new Clock();
        var trialPath = Path.Combine(root, "planner-trial.json");
        var trial = new LocalTrialStore(trialPath, clock);
        var service = new FakeService();
        var paid = new PaidLicenseManager(service, new LicenseStore(Path.Combine(root, "planner-license.bin"), LicenseEnvironment.Sandbox), clock);
        var access = new OperationAccess(trial, paid);
        var facts = new ImageSourceFacts(Guid.NewGuid(), Path.Combine(root, "planner.png"), new string('B', 64), 100,
            ImageFormat.Png, 10, 10, 8, 1, false, false, "sRGB", []);
        var selection = new[] { new ConversionSelection(facts.ItemId, facts.Path, facts, null) };
        var initial = ImageConversionPlanner.Create(Guid.NewGuid(), [facts], new(ImageFormat.Tga), new("convert", new())).Confirm(true, false, false);
        await trial.AdmitAsync(initial);
        clock.Now = clock.Now.AddDays(4);
        check((await trial.ReadStatusAsync()).State == LocalTrialState.Expired, "open planners: fixture has expired trial");
        var trialBefore = await File.ReadAllBytesAsync(trialPath);
        await using var worker = new WorkerClient(Path.Combine(root, "must-not-start.exe"));
        await using var converter = new ConversionViewModel(worker, access, Guid.NewGuid(), selection, new("convert", new()), false)
            { Target = ImageFormat.Tga, MaximumDimension = "8", WarningsAcknowledged = true };
        var optimizer = new OptimizationViewModel(access, Guid.NewGuid(), selection, new("optimize", new()), false)
            { Preset = PngOptimizationPreset.Balanced };
        await converter.InitializeAsync(); await optimizer.InitializeAsync();
        check(!converter.PreviewExpanded && converter.BeforePreview is null && worker.ProcessId is null && converter.AccessBlocked &&
            converter.Message.Contains("Activate your license"), "open planners: collapsed preview starts no worker and expiry offers activation");
        check(!converter.CanConfirm && !optimizer.CanConfirm, "open planners: expired trial disables both confirmations");
        check(optimizer.LicenseActionLabel == "_Activate license..." && optimizer.TrialMessage.Contains("convert or optimize") &&
            !optimizer.Summary.Contains(" ready"), "open planners: expired optimization offers activation without a misleading ready summary");
        var rows = optimizer.Rows;
        var plan = optimizer.Plan;
        var convertNotifications = 0; var optimizeNotifications = 0;
        converter.ConfirmCommand.CanExecuteChanged += (_, _) => convertNotifications++;
        optimizer.ConfirmCommand.CanExecuteChanged += (_, _) => optimizeNotifications++;
        using var license = new LicenseViewModel(paid);
        await license.LoadAsync();
        await license.ActivateAsync("SYNTHETIC-PLANNER-KEY");
        await converter.RefreshAccessAsync(); await optimizer.InitializeAsync();
        check(converter.CanConfirm && optimizer.CanConfirm && converter.ConfirmCommand.CanExecute(null) && optimizer.ConfirmCommand.CanExecute(null),
            "open planners: license activation enables both existing plans");
        check(convertNotifications > 0 && optimizeNotifications > 0 && converter.TrialMessage.Contains("License active") && optimizer.TrialMessage.Contains("License active"),
            "open planners: access refresh notifies controls and displays paid status");
        check(optimizer.LicenseActionLabel == "_License...", "open planners: activation restores the normal license action");
        check(ReferenceEquals(rows, optimizer.Rows) && ReferenceEquals(plan, optimizer.Plan) && optimizer.Preset == PngOptimizationPreset.Balanced &&
            converter.Target == ImageFormat.Tga && converter.MaximumDimension == "8" && converter.WarningsAcknowledged && worker.ProcessId is null,
            "open planners: activation preserves plans and choices without starting media worker");
        ConfirmedImageBatch? converted = null;
        ConfirmedPngOptimization? optimized = null;
        converter.Confirmed += value => converted = value;
        optimizer.Confirmed += value => optimized = value;
        converter.ConfirmCommand.Execute(null); optimizer.ConfirmCommand.Execute(null);
        check(converted is not null && optimized is not null, "open planners: both enabled commands produce confirmed batches");
        var admitted = await access.AdmitConversionAsync(converted!, default);
        clock.Now = clock.Now.AddDays(31);
        check(converter.CanConfirm && optimizer.CanConfirm && !(await access.AdmitConversionAsync(converted!, default)).IsAllowed &&
            !(await access.AdmitOptimizationAsync(optimized!, default)).IsAllowed && admitted.IsAllowed,
            "open planners: final admission rejects stale paid UI after expiry without invalidating earlier admission");
        await converter.RefreshAccessAsync(); await optimizer.InitializeAsync();
        check(!converter.CanConfirm && !optimizer.CanConfirm, "open planners: expired paid status disables both existing plans");
        await license.ValidateAsync();
        check(license.Message == "License validated online. All future updates included.",
            "license UI: successful explicit validation has a distinct completion message");
        await converter.RefreshAccessAsync(); await optimizer.InitializeAsync();
        check(converter.CanConfirm && optimizer.CanConfirm, "open planners: explicit validation restores both plans");
        service.ValidationState = LicenseReplyState.Rejected;
        await license.ValidateAsync();
        await converter.RefreshAccessAsync(); await optimizer.InitializeAsync();
        check(!converter.CanConfirm && !optimizer.CanConfirm && license.ShowRecovery, "open planners: provider rejection blocks both plans and offers recovery");
        await license.DeactivateAsync();
        await converter.RefreshAccessAsync(); await optimizer.InitializeAsync();
        check(!converter.CanConfirm && !optimizer.CanConfirm && license.CanActivate && !license.ShowRecovery,
            "open planners: deactivation returns to expired trial rather than granting new access");
        check(Enumerable.SequenceEqual(trialBefore, await File.ReadAllBytesAsync(trialPath)) && service.Activations == 1 && worker.ProcessId is null,
            "open planners: paid refresh does not reset trial, repeat activation or run media");
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class FakeService : ILicenseService
    {
        public LicenseEnvironment Environment => LicenseEnvironment.Sandbox;
        public LicenseReplyState ActivationState = LicenseReplyState.Granted, ValidationState = LicenseReplyState.Granted,
            DeactivationState = LicenseReplyState.Deactivated;
        public bool CancelActivation;
        public int Calls, Activations, Validations;
        public Guid LastActivation;
        private readonly LicenseReceipt _receipt = new(Guid.NewGuid(), Guid.NewGuid(), null);
        public LicenseReceipt Receipt => _receipt;
        public TaskCompletionSource<LicenseReply>? PendingValidation;
        public TaskCompletionSource<LicenseReply>? PendingActivation;
        public TaskCompletionSource ActivationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ValidationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<LicenseReply> ActivateAsync(string key, Guid installationId, CancellationToken cancellationToken)
        {
            Calls++; Activations++;
            ActivationStarted.TrySetResult();
            if (PendingActivation is not null) return PendingActivation.Task.WaitAsync(cancellationToken);
            if (CancelActivation) throw new OperationCanceledException();
            return Task.FromResult(new LicenseReply(ActivationState, "Synthetic response", _receipt));
        }
        public Task<LicenseReply> ValidateAsync(string key, Guid installationId, Guid activationId, CancellationToken cancellationToken)
        {
            Calls++; Validations++; LastActivation = activationId;
            ValidationStarted.TrySetResult();
            if (PendingValidation is not null) return PendingValidation.Task.WaitAsync(cancellationToken);
            return Task.FromResult(new LicenseReply(ValidationState, "Synthetic response", _receipt));
        }
        public Task<LicenseReply> DeactivateAsync(string key, Guid activationId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new LicenseReply(DeactivationState, "Synthetic response"));
        }
    }
}
