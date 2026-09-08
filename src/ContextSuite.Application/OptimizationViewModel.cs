using System.ComponentModel;
using System.Runtime.CompilerServices;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Application;

internal sealed class OptimizationViewModel : INotifyPropertyChanged
{
    private readonly LocalTrialStore _trial;
    private readonly Guid _batchId;
    private readonly IReadOnlyList<ConversionSelection> _selection;
    private readonly BatchSettings _settings;
    private readonly bool _replacementAvailable;
    private LocalTrialStatus? _status;
    private bool _replace, _confirmed;
    private PngOptimizationPreset _preset;

    public OptimizationViewModel(LocalTrialStore trial, Guid batchId, IReadOnlyList<ConversionSelection> selection,
        BatchSettings settings, bool replacementAvailable)
    {
        _trial = trial; _batchId = batchId; _selection = selection; _settings = settings;
        _replacementAvailable = replacementAvailable;
        ConfirmCommand = new UiCommand(Confirm, () => CanConfirm);
        Refresh();
    }

    public PngOptimizationPlan? Plan { get; private set; }
    public IReadOnlyList<OptimizationRow> Rows { get; private set; } = [];
    public UiCommand ConfirmCommand { get; }
    public IReadOnlyList<PngOptimizationPreset> Presets { get; } = Enum.GetValues<PngOptimizationPreset>();
    public PngOptimizationPreset Preset { get => _preset; set { _preset = value; _confirmed = false; Refresh(); } }
    public string PolicyDescription => Preset switch
    {
        PngOptimizationPreset.Lossless => "Lossless: pixels, transparency, precision and accepted metadata stay identical.",
        PngOptimizationPreset.Balanced => "Balanced (lossy): slightly reduces RGB precision. Banding is possible; alpha and accepted metadata stay exact.",
        _ => "Smallest (lossy): stronger RGB precision reduction. Banding may be visible, especially in dark gradients; alpha and accepted metadata stay exact."
    };
    public string TrialMessage => _status?.Message ?? "Checking trial availability…";
    public bool CanReplaceOriginal => _replacementAvailable && _settings.Preferences.AllowReplacingOriginals && _settings.Preferences.OutputDirectory is null;
    public bool ReplaceOriginal { get => _replace; set { _replace = value; _confirmed = false; Refresh(); } }
    public bool ReplacementConfirmed { get => _confirmed; set { _confirmed = value; Refresh(); } }
    public bool CanConfirm => Plan?.HasExecutableItems == true && _status?.State is LocalTrialState.NotStarted or LocalTrialState.Active &&
        (!ReplaceOriginal || (CanReplaceOriginal && ReplacementConfirmed));
    public string Summary => $"{Rows.Count(row => row.CanExecute)} ready; {Rows.Count(row => !row.CanExecute)} unsupported or unreadable. Only smaller verified results are saved.";
    public event Action<ConfirmedPngOptimization>? Confirmed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _status = await _trial.ReadStatusAsync(cancellationToken);
        Refresh();
    }

    private void Refresh()
    {
        var sources = _selection.Where(item => item.Facts is not null).Select(item => item.Facts!).ToArray();
        Plan = sources.Length == 0 ? null : PngOptimizationPlan.Create(_batchId, sources, _settings, ReplaceOriginal, Preset);
        Rows = _selection.Select(selection =>
        {
            var item = Plan?.Items.FirstOrDefault(item => item.Source.ItemId == selection.ItemId);
            var ready = item?.CanExecute == true;
            var path = ready ? Path.Combine(_settings.Preferences.OutputDirectory ?? Path.GetDirectoryName(selection.Path)!,
                OutputNames.Create(selection.Path, "optimize", "png", replaceSource: ReplaceOriginal)) : "";
            return new OptimizationRow(Path.GetFileName(selection.Path), selection.Facts is { } facts ? $"{facts.Width} × {facts.Height}, {facts.BitDepth}-bit, {facts.FileBytes:N0} bytes" : "",
                selection.Failure ?? item?.BlockReason ?? $"{Preset} · preserve accepted metadata", path, ready);
        }).ToArray();
        foreach (var name in new[] { nameof(Rows), nameof(Preset), nameof(PolicyDescription), nameof(ReplaceOriginal), nameof(ReplacementConfirmed), nameof(CanConfirm), nameof(TrialMessage), nameof(Summary) }) Changed(name);
        ConfirmCommand.Notify();
    }

    private void Confirm()
    {
        if (CanConfirm) Confirmed?.Invoke(Plan!.Confirm(ReplacementConfirmed, _replacementAvailable));
    }

    private void Changed([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

internal sealed record OptimizationRow(string Name, string Details, string Status, string ProposedOutput, bool CanExecute);
