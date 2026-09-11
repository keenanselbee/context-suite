using System.Collections.Immutable;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;

internal static class AudioDecisionContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var path = Path.Combine(scratch, "decision.mp3");
        foreach (var action in new[] { "wav", "flac", "mp3", "m4a", "vorbis", "opus" })
        {
            var request = new OperationRequest(Guid.NewGuid(), "convert", action, [path]); request.Validate(false);
            check(request.IsQuickAudioConversion && request.IsQuickAction && !request.IsQuickOptimization,
                "Audio action: explicit quick target " + action);
        }
        var source = new AudioFileSource(Guid.NewGuid(), path, new string('A', 64), 5000,
            new("mp3", 2, [new(0, "audio", "mp3", 44100, 2, null, null, 2, "stereo", ImmutableDictionary<string, string>.Empty)], ImmutableDictionary<string, string>.Empty));
        var plan = AudioConversionBatch.Create(Guid.NewGuid(), [source], AudioFormat.Opus, new("convert", new()));
        var access = new Access(); using var model = new AudioConversionViewModel(plan, access, false);
        check(!model.CanConfirm && model.Heading == "Convert to Opus" && model.FileSummary == "1 audio file" &&
            model.QualityChanges.Contains("quality loss") && model.QualityChanges.Contains("48,000"), "Audio decision: fixed target and required consequences precede confirmation");
        await model.RefreshAccessAsync();
        check(!model.CanConfirm && model.LicenseActionLabel.Contains("Activate") && access.Admissions == 0,
            "Audio decision: expired access gives activation without starting work");
        access.Allowed = true; await model.RefreshAccessAsync();
        check(model.CanConfirm && ReferenceEquals(model.Plan, plan) && model.FileNames == "decision.mp3" && model.OutputSummary.Contains("Originals are kept"),
            "Audio decision: activation refresh retains target, files and copy policy");
        access.Allowed = false; await model.RefreshAccessAsync();
        check(!model.CanConfirm && ReferenceEquals(model.Plan, plan), "Audio decision: deactivation blocks the same plan");
        access.Allowed = true; await model.RefreshAccessAsync();
        ConfirmedAudioConversion? confirmed = null; var confirmations = 0;
        model.Confirmed += value => { confirmed = value; confirmations++; };
        model.ConfirmCommand.Execute(null); model.ConfirmCommand.Execute(null);
        check(confirmations == 1 && confirmed?.AcceptedConsent == plan.RequiredConsent && access.Admissions == 0,
            "Audio decision: Convert accepts only this plan and cannot double-submit; executor owns admission");
        using var precision = new AudioConversionViewModel(AudioConversionBatch.Create(Guid.NewGuid(), [source], AudioFormat.Flac, new("convert", new())), access, false);
        check(precision.QualityChanges.Contains("24-bit") && precision.QualityChanges.Contains("cannot restore"), "Audio decision: precision change does not imply restored quality");
        var many = Enumerable.Range(0, 100).Select(index => source with { ItemId = Guid.NewGuid(), Facts = source.Facts with
        { Container = "wav", Streams = [source.Facts.Streams[0] with { Codec = "pcm_s16le", SampleBits = 16, SampleRate = 8000 + index }] } });
        using var bounded = new AudioConversionViewModel(AudioConversionBatch.Create(Guid.NewGuid(), many, AudioFormat.Opus, new("convert", new())), access, false);
        check(bounded.QualityChanges.Length < 200 && bounded.FileSummary == "100 audio files", "Audio decision: many input rates retain one compact resampling explanation");
        var waiting = new WaitingAccess(); var cancelled = new AudioConversionViewModel(plan, waiting, false);
        var refresh = cancelled.RefreshAccessAsync(); cancelled.Dispose(); await refresh; cancelled.Dispose(); await cancelled.RefreshAccessAsync();
        check(waiting.Cancelled && !cancelled.CanConfirm, "Audio decision: closing cancels pending access and prevents confirmation");
    }
    private class Access : IOperationAccess
    {
        public bool Allowed { get; set; }
        public int Admissions { get; private set; }
        public virtual Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => Task.FromResult(new OperationAccessStatus(Allowed, Allowed ? "Licensed" : "Activate your license to convert."));
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected image admission.");
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => throw new InvalidOperationException("Unexpected optimization admission.");
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedAudioConversion confirmed, CancellationToken token)
        { Admissions++; return Task.FromResult(new OperationAdmission(new(Allowed, "Synthetic status"), confirmed.Plan.BatchId)); }
    }
    private sealed class WaitingAccess : Access
    {
        public bool Cancelled { get; private set; }
        public override async Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default)
        {
            try { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException("Expected cancellation."); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
        }
    }
}
