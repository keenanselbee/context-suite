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
    private int _selectedIndex;

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
    public IReadOnlyList<PngOptimizationPreset> Presets { get; } = [PngOptimizationPreset.Auto, PngOptimizationPreset.Lossless, PngOptimizationPreset.Balanced, PngOptimizationPreset.Smallest];
    public PngOptimizationPreset Preset { get => _preset; set { _preset = value; _confirmed = false; Refresh(); } }
    public string PolicyDescription => Preset switch
    {
        PngOptimizationPreset.Auto => "Auto: balances size and quality with gentle lossy reduction only when worthwhile. Originals and accepted metadata are kept.",
        PngOptimizationPreset.Lossless => "Lossless: pixels, transparency, precision and accepted metadata stay identical.",
        PngOptimizationPreset.Balanced => "Balanced (lossy): slightly reduces RGB precision. Banding is possible; alpha and accepted metadata stay exact.",
        _ => "Smallest (lossy): reduces colors with dark-gradient protection. Fine texture or detail may change. Uses one gentler fallback when needed; transparency and accepted metadata stay exact."
    };
    public string TrialMessage => _status?.Message ?? "Checking trial availability…";
    public bool CanReplaceOriginal => _replacementAvailable && _settings.Preferences.AllowReplacingOriginals && _settings.Preferences.OutputDirectory is null;
    public bool ReplaceOriginal { get => _replace; set { _replace = value; _confirmed = false; Refresh(); } }
    public bool ReplacementConfirmed { get => _confirmed; set { _confirmed = value; Refresh(); } }
    public int SelectedIndex
    {
        get => _selectedIndex;
        set { _selectedIndex = value; Changed(); Changed(nameof(SelectedStatus)); Changed(nameof(SelectedDetails)); }
    }
    private OptimizationRow? SelectedRow => SelectedIndex >= 0 && SelectedIndex < Rows.Count ? Rows[SelectedIndex] : null;
    public string SelectedStatus => SelectedRow?.Status ?? "Select a file to review its plan.";
    public string SelectedDetails => SelectedRow is { } row ?
        $"{row.Name}\n{row.Details}\n{(row.CanExecute ? "Proposed output: " + row.ProposedOutput + (ReplaceOriginal && !row.ForceCopy ? "\nReplace only after validation; retain a backup if recycling fails." : "\nExisting names add (2), (3), and so on.") : row.Status)}" : "";
    public string OutputNotice => ReplaceOriginal ? "Replace originals only after validation. Files needing metadata removal always become separate copies. No smaller result leaves the original unchanged." :
        _settings.Preferences.OutputDirectory is { } directory ?
        $"Save new files in: {directory}" : "Save new files beside their originals, with - Optimized added to the name.";
    public string ReplacementNotice => CanReplaceOriginal ?
        "Replacement needs confirmation for this batch. Originals are recycled only after validation and publication." :
        "Replacement is unavailable. It requires permission in Optimize settings, a supported local location and output beside the source.";
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
                OutputNames.Create(selection.Path, "optimize", "png", replaceSource: ReplaceOriginal && item?.Source.PngFdECRemovalRequired != true)) : "";
            return new OptimizationRow(Path.GetFileName(selection.Path), selection.Facts is { } facts ? $"{facts.Width} × {facts.Height}, {facts.BitDepth}-bit, {facts.FileBytes:N0} bytes" : "",
                selection.Failure ?? item?.BlockReason ?? (item?.Source.PngFdECRemovalRequired == true ?
                    $"{Preset} · separate copy; fdEC metadata removed with a completion warning. Original unchanged." :
                    $"{Preset} · preserve accepted metadata"), path, ready, item?.Source.PngFdECRemovalRequired == true);
        }).ToArray();
        foreach (var name in new[] { nameof(Rows), nameof(Preset), nameof(PolicyDescription), nameof(ReplaceOriginal), nameof(ReplacementConfirmed), nameof(CanConfirm), nameof(TrialMessage), nameof(Summary), nameof(SelectedStatus), nameof(SelectedDetails), nameof(OutputNotice) }) Changed(name);
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

internal sealed record OptimizationRow(string Name, string Details, string Status, string ProposedOutput, bool CanExecute, bool ForceCopy = false);
