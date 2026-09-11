using System.ComponentModel;
using System.Windows.Input;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Audio;

namespace ContextSuite.Application;

// A decision for one fixed menu target, never an audio workspace or preset editor.
internal sealed class AudioConversionViewModel(AudioConversionBatch plan, IOperationAccess access, bool replacementAvailable)
    : INotifyPropertyChanged, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private OperationAccessStatus? _status;
    private bool _checking = true, _confirmed, _disposed;
    private int _refresh;
    private ICommand? _confirmCommand;
    public AudioConversionBatch Plan { get; } = plan;
    public string TargetName => Plan.Target switch
    { AudioFormat.Wave => "WAV", AudioFormat.Flac => "FLAC", AudioFormat.Mp3 => "MP3", AudioFormat.M4a => "M4A (AAC)", AudioFormat.Vorbis => "Ogg Vorbis", _ => "Opus" };
    public string Heading => "Convert to " + TargetName;
    public string FileSummary => Plan.Items.Count(item => item.CanExecute) == 1 ? "1 audio file" : $"{Plan.Items.Count(item => item.CanExecute)} audio files";
    public string QualityChanges
    {
        get
        {
            var changes = new List<string>();
            if (Plan.RequiredConsent.HasFlag(AudioConversionConsent.LossyTranscoding))
                changes.Add("This audio is already compressed with quality loss. Converting it again can reduce quality further.");
            if (Plan.RequiredConsent.HasFlag(AudioConversionConsent.Resampling))
                changes.Add("Opus output uses 48,000 Hz audio. Files recorded at a different sample rate will be resampled.");
            if (Plan.RequiredConsent.HasFlag(AudioConversionConsent.PrecisionReduction))
                changes.Add("FLAC will store this decoded audio at 24-bit integer precision. Floating-point WAV preserves its decoded precision. A lossless format cannot restore quality lost in an earlier recording.");
            return string.Join("\n\n", changes);
        }
    }
    public string FileNames => string.Join("\n", Plan.Items.Where(item => item.CanExecute).Select(item => Path.GetFileName(item.Source.Path)));
    public string OutputSummary => Plan.ReplaceOriginal ? "After the new file is validated, the original goes to the Recycle Bin." : "Originals are kept. New files use your Convert settings.";
    public string Message => _checking ? "Checking license access…" : _status?.CanStart == true ? "Choose Convert to accept these changes." :
        _status?.Message ?? "License access could not be checked. Open License and try again.";
    public string LicenseActionLabel => _status?.CanStart == false ? "_Activate license…" : "_License…";
    public bool CanConfirm => !_disposed && !_checking && !_confirmed && !_lifetime.IsCancellationRequested && _status?.CanStart == true && Plan.HasExecutableItems;
    public ICommand ConfirmCommand => _confirmCommand ??= new UiCommand(Confirm, () => CanConfirm);
    public event Action<ConfirmedAudioConversion>? Confirmed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task RefreshAccessAsync()
    {
        if (_disposed) return;
        var version = ++_refresh; _checking = true; Changed();
        try
        {
            var status = await access.ReadAccessAsync(_lifetime.Token);
            if (version == _refresh && !_lifetime.IsCancellationRequested) _status = status;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { if (version == _refresh) _status = null; }
        finally { if (version == _refresh) { _checking = false; Changed(); } }
    }
    private void Confirm()
    {
        if (!CanConfirm) return;
        var confirmed = Plan.Confirm(Plan.RequiredConsent, Plan.ReplaceOriginal, replacementAvailable);
        _confirmed = true; Changed(); Confirmed?.Invoke(confirmed);
    }
    private void Changed()
    {
        PropertyChanged?.Invoke(this, new(null)); CommandManager.InvalidateRequerySuggested();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose();
    }
}
