using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;
using ContextSuite.Core.Dds;

namespace ContextSuite.Application;

internal sealed record ConversionSelection(Guid ItemId, string Path, ImageSourceFacts? Facts, string? Failure);
internal sealed record FormatChoice(string Label, ImageFormat Value);

internal sealed partial class ConversionViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly WorkerClient _worker;
    private readonly LocalTrialStore _trial;
    private readonly Guid _batchId;
    private readonly BatchSettings _settings;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _previewCancellation;
    private Task? _previewTask;
    private Task _scheduledPreview = Task.CompletedTask;
    private CancellationTokenSource? _previewDelay;
    private bool _initialized;
    private ImageBatchPlan? _plan;
    private ImageFormat? _target;
    private string _quality = "90", _maximumDimension = "", _customMatte = "FFFFFF";
    private int _matteChoice, _selectedIndex;
    private bool _webPLossless, _removeMetadata, _replaceOriginal, _warningsAcknowledged, _replacementConfirmed, _previewBusy;
    private int _revision;
    private bool _hasMattePreview;
    private string _message = "Choose a target format to prepare this batch.", _warnings = "", _previewMessage = "Select a file and refresh the preview.";
    private LocalTrialStatus? _trialStatus;
    private BitmapSource? _before, _after;

    public ConversionViewModel(WorkerClient worker, LocalTrialStore trial, Guid batchId,
        IReadOnlyList<ConversionSelection> selection, BatchSettings settings, bool replacementAvailable)
    {
        _worker = worker; _trial = trial; _batchId = batchId; _settings = settings;
        CanReplaceOriginal = replacementAvailable && settings.Preferences.AllowReplacingOriginals && settings.Preferences.OutputDirectory is null;
        Rows = new(selection.Select(item => new ConversionRow(item)));
        _ddsMips = selection.Any(item => item.Facts?.Texture is not null) ? DdsMipPolicy.Preserve : DdsMipPolicy.Generate;
        _selectedIndex = Math.Max(0, selection.ToList().FindIndex(s => s.Facts?.HasTransparency == true));
        ConfirmCommand = new UiCommand(Confirm, () => CanConfirm);
        PreviewCommand = new UiCommand(() => _previewTask = RefreshPreviewAsync(), () => !IsPreviewBusy && SelectedSource is not null);
        RefreshPlan();
    }

    public IReadOnlyList<FormatChoice> Formats { get; } = [new("PNG — lossless", ImageFormat.Png), new("JPEG", ImageFormat.Jpeg), new("WebP", ImageFormat.WebP), new("BMP — 24-bit RGB", ImageFormat.Bmp), new("TGA — true color / alpha", ImageFormat.Tga), new("DDS — game texture", ImageFormat.Dds)];
    public IReadOnlyList<string> MatteChoices { get; } = ["Choose a background", "White", "Black", "Custom RGB"];
    public ObservableCollection<ConversionRow> Rows { get; }
    public ImageFormat? Target { get => _target; set { if (Set(ref _target, value)) RefreshPlan(); } }
    public string Quality { get => _quality; set { if (Set(ref _quality, value)) RefreshPlan(); } }
    public string MaximumDimension { get => _maximumDimension; set { if (Set(ref _maximumDimension, value)) RefreshPlan(); } }
    public bool WebPLossless { get => _webPLossless; set { if (Set(ref _webPLossless, value)) RefreshPlan(); } }
    public bool RemoveMetadata { get => _removeMetadata; set { if (Set(ref _removeMetadata, value)) RefreshPlan(); } }
    public int MatteChoice { get => _matteChoice; set { if (Set(ref _matteChoice, value)) RefreshPlan(); } }
    public string CustomMatte { get => _customMatte; set { if (Set(ref _customMatte, value)) RefreshPlan(); } }
    public bool ReplaceOriginal { get => _replaceOriginal; set { if (Set(ref _replaceOriginal, value)) RefreshPlan(); } }
    public bool WarningsAcknowledged { get => _warningsAcknowledged; set { if (Set(ref _warningsAcknowledged, value)) Changed(nameof(CanConfirm)); } }
    public bool ReplacementConfirmed { get => _replacementConfirmed; set { if (Set(ref _replacementConfirmed, value)) Changed(nameof(CanConfirm)); } }
    public int SelectedIndex
    {
        get => _selectedIndex;
        set { if (Set(ref _selectedIndex, value)) { InvalidatePreview(); _before = null; Changed(nameof(BeforePreview)); Changed(nameof(SelectedExplanation)); Changed(nameof(SelectedFileDetails)); SchedulePreview(); } }
    }
    public bool IsJpeg => Target == ImageFormat.Jpeg;
    public bool NeedsMatte => Target is ImageFormat.Jpeg or ImageFormat.Bmp || (IsDdsWorkflow && DdsAlpha == DdsAlphaPolicy.Flatten);
    public bool IsWebP => Target == ImageFormat.WebP;
    public bool HasQuality => IsJpeg || (IsWebP && !WebPLossless);
    public bool HasCustomMatte => NeedsMatte && MatteChoice == 3;
    public bool CanReplaceOriginal { get; }
    public bool HasWarnings => _warnings.Length != 0;
    public bool IsPreviewBusy => _previewBusy;
    public bool CanConfirm => _plan?.HasExecutableItems == true && !_previewBusy &&
        _trialStatus?.State is LocalTrialState.NotStarted or LocalTrialState.Active &&
        (!HasWarnings || WarningsAcknowledged) && (!ReplaceOriginal || (CanReplaceOriginal && ReplacementConfirmed)) &&
        (!IsDdsWorkflow || _hasDdsPreview) &&
        (_hasMattePreview || _plan.Options.SupportsAlpha || !_plan.Items.Any(item => item.CanExecute && item.Source.HasTransparency));
    public string Message => _message;
    public string SelectedExplanation => SelectedIndex >= 0 && SelectedIndex < Rows.Count ?
        $"{Rows[SelectedIndex].Name}: {Rows[SelectedIndex].Status}" : "Select a row for its full explanation and preview.";
    public string SelectedFileDetails => SelectedIndex >= 0 && SelectedIndex < Rows.Count ?
        Rows[SelectedIndex].Details + Environment.NewLine + Rows[SelectedIndex].ProposedOutput : "";
    public string Warnings => _warnings;
    public string PreviewMessage => _previewMessage;
    public string TrialMessage => _trialStatus?.Message ?? "Checking local trial status…";
    public string OutputNotice => ReplaceOriginal
        ? "Publish the converted file, then move its original to the Recycle Bin. A cleanup failure retains the original and is reported. No permanent-delete fallback."
        : IsDdsTarget ? "Keep originals. New names include the texture representation, such as ‘ - BC7-sRGB’; collisions get (2), (3), and so on. Proposed paths are finalized safely at publication." :
            "Keep originals. New names use ‘ - Converted’; existing names get (2), (3), and so on. Proposed paths below are finalized safely at publication.";
    public string ReplacementNotice => CanReplaceOriginal ? "Replacement is optional and requires consent for this batch." :
        "Replacement is unavailable: it requires permission in Settings, source-folder output, and a verified Windows/NTFS location. Copies remain available.";
    public BitmapSource? BeforePreview => _before;
    public BitmapSource? AfterPreview => _after;
    public UiCommand ConfirmCommand { get; }
    public UiCommand PreviewCommand { get; }
    public event Action<ConfirmedImageBatch>? Confirmed;
    public event PropertyChangedEventHandler? PropertyChanged;
    private ImageSourceFacts? SelectedSource => SelectedIndex >= 0 && SelectedIndex < Rows.Count ? Rows[SelectedIndex].Selection.Facts : null;

    public async Task InitializeAsync()
    {
        try
        {
            _trialStatus = await _trial.ReadStatusAsync(_lifetime.Token);
            Changed(nameof(TrialMessage)); Changed(nameof(CanConfirm));
            _previewTask = RefreshPreviewAsync();
            await _previewTask;
            _initialized = true;
        }
        catch (OperationCanceledException) { }
    }

    private ImageConversionOptions? Options()
    {
        if (Target is not { } target) return null;
        uint quality = 90;
        if (HasQuality && (!uint.TryParse(Quality, NumberStyles.None, CultureInfo.InvariantCulture, out quality) || quality is < 1 or > 100))
            throw new InvalidDataException("Quality must be a whole number from 1 to 100.");
        uint? maximum = null;
        if (!string.IsNullOrWhiteSpace(MaximumDimension))
        {
            if (!uint.TryParse(MaximumDimension, NumberStyles.None, CultureInfo.InvariantCulture, out var dimension) || dimension is 0 or > 16384)
                throw new InvalidDataException("Maximum dimension must be 1–16,384 pixels, or blank to keep the size.");
            maximum = dimension;
        }
        uint? matte = NeedsMatte ? MatteChoice switch { 1 => 0xffffff, 2 => 0, _ => null } : null;
        if (HasCustomMatte)
        {
            var text = CustomMatte.Trim().TrimStart('#');
            if (text.Length != 6 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                throw new InvalidDataException("Enter a six-digit RGB color, such as FFFFFF for white.");
            matte = rgb;
        }
        return new(target, quality, IsWebP && WebPLossless, maximum, IsDdsWorkflow ? null : matte,
            RemoveMetadata ? ImageMetadataMode.RemoveDescriptive : ImageMetadataMode.Preserve,
            IsDdsWorkflow ? TextureOptions(matte) : null, IsDdsExport ? SelectedMip() : null);
    }

    private void RefreshPlan()
    {
        InvalidatePreview();
        _hasMattePreview = false;
        _hasDdsPreview = false;
        _warningsAcknowledged = false; _replacementConfirmed = false;
        _plan = null; _warnings = "";
        try
        {
            var options = Options();
            var sources = Rows.Where(r => r.Selection.Facts is not null).Select(r => r.Selection.Facts!).ToArray();
            if (options is not null && sources.Length != 0)
                _plan = ImageConversionPlanner.Create(_batchId, sources, options, _settings, ReplaceOriginal);
            var plans = _plan?.Items.ToDictionary(i => i.Source.ItemId);
            foreach (var row in Rows)
                row.Update(plans?.GetValueOrDefault(row.Selection.ItemId), options, _settings, ReplaceOriginal);
            if (_plan is null) _message = sources.Length == 0 ? "No supported images could be read. Originals are unchanged." : "Choose a target format to prepare this batch.";
            else
            {
                var executable = _plan.Items.Count(i => i.CanExecute);
                var skipped = Rows.Count - executable;
                _message = $"{executable} ready to convert; {skipped} will be skipped. Review the per-file plan below.";
                var warnings = _plan.Items.Where(i => i.CanExecute).SelectMany(i => i.Warnings).Select(w => w.Message).Distinct().ToList();
                if (!options!.SupportsAlpha && _plan.Items.Any(item => item.CanExecute && item.Source.HasTransparency))
                    warnings.Add("Refresh the preview of a transparent image with this background before converting.");
                if (skipped != 0) warnings.Add($"{skipped} selected file(s) cannot use this plan and will be skipped.");
                _warnings = string.Join(Environment.NewLine, warnings.Select(w => "• " + w));
            }
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        {
            _plan = null; _message = error.Message;
            foreach (var row in Rows) row.Update(null, null, _settings, false);
        }
        foreach (var property in new[] { nameof(IsDdsWorkflow), nameof(IsDdsTarget), nameof(IsDdsExport), nameof(CanResize), nameof(IsJpeg), nameof(NeedsMatte), nameof(IsWebP), nameof(HasQuality), nameof(HasCustomMatte), nameof(Message),
            nameof(Warnings), nameof(HasWarnings), nameof(WarningsAcknowledged), nameof(ReplacementConfirmed), nameof(CanConfirm), nameof(OutputNotice), nameof(SelectedExplanation), nameof(SelectedFileDetails) }) Changed(property);
        SchedulePreview();
    }

    private void SchedulePreview()
    {
        if (!_initialized || _lifetime.IsCancellationRequested) return;
        _previewDelay?.Cancel();
        var delay = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _previewDelay = delay;
        var previous = _scheduledPreview;
        _scheduledPreview = RunAsync();
        async Task RunAsync()
        {
            try
            {
                await Task.Delay(300, delay.Token);
                await previous;
                if (_previewTask is not null) await _previewTask;
                delay.Token.ThrowIfCancellationRequested();
                _previewTask = RefreshPreviewAsync();
                await _previewTask;
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (_previewDelay == delay) _previewDelay = null;
                delay.Dispose();
            }
        }
    }

    private void InvalidatePreview()
    {
        _revision++;
        _previewCancellation?.Cancel();
        _after = null;
        _previewMessage = "Preview updates automatically. It is capped at 512 pixels and does not create a file or start the trial.";
        Changed(nameof(AfterPreview)); Changed(nameof(PreviewMessage));
    }

    internal async Task RefreshPreviewAsync()
    {
        if (_previewBusy || SelectedSource is not { } source || _lifetime.IsCancellationRequested) return;
        var revision = _revision;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _previewCancellation = cancellation; _previewBusy = true;
        Changed(nameof(IsPreviewBusy)); Changed(nameof(CanConfirm));
        try
        {
            _previewMessage = "Rendering preview in the media worker…"; Changed(nameof(PreviewMessage));
            var before = await _worker.PreviewAsync(new(source), cancellation.Token);
            var options = Options();
            ImagePreview? after = null;
            var planned = _plan?.Items.FirstOrDefault(i => i.Source.ItemId == source.ItemId);
            if (options is not null && planned?.CanExecute == true)
                after = await _worker.PreviewAsync(new(source, options), cancellation.Token);
            if (revision != _revision) return;
            _before = Bitmap(before); _after = after is null ? null : Bitmap(after);
            if (after is not null && source.HasTransparency && options?.SupportsAlpha == false) _hasMattePreview = true;
            if (after is not null && options?.Texture is not null) _hasDdsPreview = true;
            _previewMessage = after is null ? "Original preview. Choose an applicable target and any required background to preview the conversion." :
                "Before / after — color-managed, reduced-size preview. Compression at full resolution may look different. Transparent areas show the neutral preview background.";
            if (IsDdsWorkflow) _previewMessage = after is null ?
                "DDS before preview: declared sRGB color or raw channels when interpretation is unknown. Choose a valid texture policy and refresh before converting." :
                "DDS after preview decodes the actual proposed output, then reduces it to 512 pixels. Color follows the selected interpretation. Data/normal views show channels, not a lit material; signed channels map -1..1 to 0..1. Before uses the source declaration, or raw channels if unknown.";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or
            System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            if (revision == _revision) { _before = null; _after = null; _previewMessage = "Preview unavailable: " + error.Message; }
        }
        finally
        {
            _previewCancellation = null; _previewBusy = false;
            foreach (var property in new[] { nameof(IsPreviewBusy), nameof(CanConfirm), nameof(BeforePreview), nameof(AfterPreview), nameof(PreviewMessage) }) Changed(property);
        }
    }

    private static BitmapSource Bitmap(ImagePreview preview)
    {
        var bitmap = BitmapSource.Create((int)preview.Width, (int)preview.Height, 96, 96,
            PixelFormats.Bgra32, null, preview.BgraPixels, checked((int)preview.Width * 4));
        bitmap.Freeze();
        return bitmap;
    }

    private void Confirm()
    {
        if (!CanConfirm || _plan is null) return;
        try { Confirmed?.Invoke(_plan.Confirm(WarningsAcknowledged, ReplacementConfirmed, CanReplaceOriginal)); }
        catch (InvalidDataException error) { _message = error.Message; Changed(nameof(Message)); }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Changed(name); return true;
    }

    private void Changed([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new(name));
        ConfirmCommand?.Notify(); PreviewCommand?.Notify();
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await _scheduledPreview;
        if (_previewTask is not null) await _previewTask;
        _lifetime.Dispose();
    }

    public void CancelPreparation() { _lifetime.Cancel(); }
}

internal sealed class ConversionRow(ConversionSelection selection) : INotifyPropertyChanged
{
    public ConversionSelection Selection { get; } = selection;
    public string Name => Path.GetFileName(Selection.Path);
    public string Details => Selection.Facts is { } facts ? $"{facts.Format} · {facts.Width} × {facts.Height} · {facts.BitDepth}-bit · {facts.ColorDescription}" : Selection.Path;
    public string Status { get; private set; } = selection.Failure ?? "Choose a target format";
    public string ProposedOutput { get; private set; } = "";
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(ImageItemPlan? plan, ImageConversionOptions? options, BatchSettings settings, bool replace)
    {
        Status = Selection.Failure ?? plan?.BlockReason ?? (plan is null ? "Choose a target format" : $"Ready · {plan.OutputWidth} × {plan.OutputHeight} · {plan.OutputDepth}-bit");
        ProposedOutput = plan?.CanExecute == true && options is not null ? Path.Combine(settings.Preferences.OutputDirectory ?? Path.GetDirectoryName(Selection.Path)!,
            OutputNames.Create(Selection.Path, "convert", options.Extension, representation: options.OutputRepresentation, replaceSource: replace)) : "";
        PropertyChanged?.Invoke(this, new(nameof(Status))); PropertyChanged?.Invoke(this, new(nameof(ProposedOutput)));
    }
}

internal sealed class UiCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public void Execute(object? parameter) { if (canExecute()) execute(); }
    public void Notify() { CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
}
