using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using ContextSuite.Core.Audio;

namespace ContextSuite.Application;

// One decision for the pending audio-to-WEM batch. Inspecting an installation or
// reference does not run Wwise or claim that a particular game can use it.
internal sealed class WemEncodingViewModel(
    string? rememberedConsolePath, Func<string, CancellationToken, Task> rememberConsolePath,
    CancellationToken requestToken,
    Func<string, CancellationToken, Task<WwiseInstallation>>? inspectWwise = null,
    Func<CancellationToken, Task<IReadOnlyList<WwiseInstallation>>>? discoverWwise = null,
    Func<string, CancellationToken, Task<WemReference>>? inspectReference = null)
    : INotifyPropertyChanged, IDisposable
{
    private readonly CancellationTokenSource _lifetime = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
    private readonly Func<string, CancellationToken, Task<WwiseInstallation>> _inspectWwise = inspectWwise ?? WwiseInstallation.InspectAsync;
    private readonly Func<CancellationToken, Task<IReadOnlyList<WwiseInstallation>>> _discoverWwise = discoverWwise ??
        (token => WwiseInstallation.DiscoverAsync(token: token));
    private readonly Func<string, CancellationToken, Task<WemReference>> _inspectReference = inspectReference ?? WemReference.InspectAsync;
    private WwiseInstallation? _selectedInstallation;
    private WemReference? _reference;
    private bool _advanced, _busy, _disposed;
    private WemCodec? _advancedCodec;
    private string _message = "Checking installed Wwise versions…";
    public ObservableCollection<WwiseInstallation> Installations { get; } = [];
    public WwiseInstallation? SelectedInstallation
    {
        get => _selectedInstallation;
        set { if (!CanChoose) return; _selectedInstallation = value; Changed(); }
    }
    public WemReference? Reference
    {
        get => _reference;
        private set { _reference = value; Changed(); }
    }
    public bool MatchOriginal
    {
        get => !_advanced;
        set { if (CanChoose && value) { _advanced = false; Changed(); } }
    }
    public bool Advanced
    {
        get => _advanced;
        set { if (CanChoose) { _advanced = value; Changed(); } }
    }
    public bool Pcm16
    {
        get => _advancedCodec == WemCodec.Pcm16;
        set { if (CanChoose && value) { _advancedCodec = WemCodec.Pcm16; Changed(); } }
    }
    public bool Vorbis
    {
        get => _advancedCodec == WemCodec.Vorbis;
        set { if (CanChoose && value) { _advancedCodec = WemCodec.Vorbis; Changed(); } }
    }
    public string InstallationSummary => SelectedInstallation is null ?
        "No inspected Wwise Authoring installation is selected." :
        $"Wwise {SelectedInstallation.ProductVersion} — {SelectedInstallation.ConsolePath}";
    public string ReferenceSummary => Reference is null ? "No original WEM selected." :
        $"{Path.GetFileName(Reference.Path)} — {Reference.Codec}, {Reference.SampleRate:N0} Hz, {Reference.Channels} channel(s)";
    public string Message => _message;
    public bool IsBusy => _busy;
    public bool CanChoose => !_disposed && !_busy && !_lifetime.IsCancellationRequested;
    public bool CanContinue => CanChoose && SelectedInstallation is not null &&
        (_advanced ? _advancedCodec is WemCodec.Pcm16 or WemCodec.Vorbis : Reference is not null);
    public ICommand ContinueCommand => _continueCommand ??= new UiCommand(() => _ = ConfirmAsync(), () => CanContinue);
    private ICommand? _continueCommand;
    public event Action<WemEncodingOptions>? Confirmed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task InitializeAsync()
    {
        if (!CanChoose) return;
        await RunAsync(async token =>
        {
            if (rememberedConsolePath is not null)
            {
                try { Add(await _inspectWwise(rememberedConsolePath, token)); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
                    NotSupportedException or InvalidOperationException or Win32Exception)
                { _message = "The remembered Wwise installation is unavailable. Select another installed version."; }
            }
            foreach (var installation in await _discoverWwise(token)) Add(installation);
            if (SelectedInstallation is null)
                _message = "Install Wwise Authoring with WwiseConsole, or locate an existing WwiseConsole.exe.";
            else
                _message = "Choose an original WEM to match, or select an advanced codec.";
        });
    }

    public Task ChooseWwiseAsync(string path) => RunAsync(async token =>
    {
        var installation = await _inspectWwise(path, token);
        await rememberConsolePath(installation.ConsolePath, token);
        Add(installation);
        _selectedInstallation = installation;
        Changed();
        _message = "Wwise location saved. Choose an original WEM or an advanced codec.";
    });

    public Task ChooseReferenceAsync(string path) => RunAsync(async token =>
    {
        Reference = await _inspectReference(path, token);
        _advanced = false;
        _message = "The original WEM supplies codec, sample rate, and channel count only. Matching these does not reproduce its encoder or guarantee game compatibility.";
    });

    private void Add(WwiseInstallation installation)
    {
        var index = -1;
        for (var candidate = 0; candidate < Installations.Count; candidate++)
            if (string.Equals(Installations[candidate].ConsolePath, installation.ConsolePath, StringComparison.OrdinalIgnoreCase))
            { index = candidate; break; }
        if (index < 0) Installations.Add(installation);
        else Installations[index] = installation;
        if (_selectedInstallation is null ||
            string.Equals(_selectedInstallation.ConsolePath, installation.ConsolePath, StringComparison.OrdinalIgnoreCase))
        { _selectedInstallation = installation; Changed(); }
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (!CanChoose) return;
        _busy = true; Changed();
        try { await action(_lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (OperationCanceledException) { _message = "Inspection was cancelled. Try again."; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            ArgumentException or NotSupportedException or InvalidOperationException or Win32Exception or
            System.Security.Cryptography.CryptographicException)
        { _message = error.Message; }
        finally { _busy = false; Changed(); }
    }

    internal async Task ConfirmAsync()
    {
        if (!CanContinue) return;
        var installation = SelectedInstallation!;
        var reference = _advanced ? null : Reference;
        var codec = _advanced ? _advancedCodec!.Value : reference!.Codec;
        await RunAsync(async token =>
        {
            var currentInstallation = await _inspectWwise(installation.ConsolePath, token);
            if (currentInstallation != installation)
                throw new InvalidDataException("The selected Wwise installation changed. Locate it again before continuing.");
            if (reference is not null && await _inspectReference(reference.Path, token) != reference)
                throw new InvalidDataException("The original WEM changed. Locate it again before continuing.");
            var options = new WemEncodingOptions(installation, codec, reference);
            options.Validate();
            await rememberConsolePath(installation.ConsolePath, token);
            token.ThrowIfCancellationRequested();
            Confirmed?.Invoke(options);
        });
    }

    private void Changed()
    {
        PropertyChanged?.Invoke(this, new(null));
        CommandManager.InvalidateRequerySuggested();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
