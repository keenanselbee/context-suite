using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Application;

// Only page order is editable. The request's source facts and settings stay fixed.
internal sealed class ImagePdfOrderViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ImagePdfPlan _plan;
    private readonly IOperationAccess _access;
    private readonly ObservableCollection<ImagePdfOrderRow> _pages;
    private readonly CancellationTokenSource _lifetime = new();
    private OperationAccessStatus? _status;
    private ImagePdfOrderRow? _selected;
    private string _destination = "";
    private string? _destinationError;
    private bool _checking = true, _confirmed, _disposed;
    private int _refresh;
    public ReadOnlyObservableCollection<ImagePdfOrderRow> Pages { get; }
    public ImagePdfOrderRow? SelectedPage
    {
        get => _selected;
        set { if (value is null || _pages.Contains(value)) { _selected = value; Changed(); } }
    }
    public string FileSummary => $"{Pages.Count} images → one PDF";
    public string Destination => _destination;
    public string Message => _destinationError ?? (_checking ? "Checking license access…" : _status?.CanStart == true ?
        "Ready to create one PDF. All originals are kept." : _status?.Message ?? "License access could not be checked. Open License and try again.");
    public string LicenseActionLabel => _status?.CanStart == false ? "_Activate license…" : "_License…";
    public bool CanEdit => !_disposed && !_confirmed;
    public bool CanConfirm => CanEdit && _destinationError is null && !_checking && _status?.CanStart == true;
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand ConfirmCommand { get; }
    public event Action<ConfirmedImagePdf>? Confirmed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ImagePdfOrderViewModel(ImagePdfPlan plan, IOperationAccess access)
    {
        _ = plan.Confirm(true); // Validate facts, without admitting or executing work.
        _plan = plan; _access = access;
        _pages = new(plan.Pages.Select((page, index) => new ImagePdfOrderRow(page, index + 1)));
        Pages = new(_pages); _selected = _pages[0];
        UpdateDestination();
        MoveUpCommand = new UiCommand(() => Move(-1), () => CanMove(-1));
        MoveDownCommand = new UiCommand(() => Move(1), () => CanMove(1));
        ConfirmCommand = new UiCommand(Confirm, () => CanConfirm);
    }

    private bool CanMove(int offset) => CanEdit && _selected is not null &&
        _pages.IndexOf(_selected) + offset >= 0 && _pages.IndexOf(_selected) + offset < _pages.Count;

    private void Move(int offset)
    {
        if (!CanMove(offset)) return;
        var selected = _selected!;
        var from = _pages.IndexOf(selected); var to = from + offset;
        _pages.Move(from, to);
        _pages[from].Number = from + 1; _pages[to].Number = to + 1;
        _selected = selected; // Selection follows the image, including after WPF collection notifications.
        UpdateDestination();
        Changed();
    }

    private void UpdateDestination()
    {
        try
        {
            _destination = Path.Combine(_plan.Settings.Preferences.OutputDirectory ?? Path.GetDirectoryName(_pages[0].Page.Source.Path)!,
                OutputNames.Create(_pages[0].Page.Source.Path, "convert", "pdf", combinedPdf: Pages.Count > 1));
            _destinationError = null;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        {
            _destination = "Choose a different first image or shorten its filename.";
            _destinationError = "The first image's name cannot be used for this PDF copy. Move another image to page 1, or cancel and rename the image.";
        }
    }

    public async Task RefreshAccessAsync()
    {
        if (_disposed) return;
        var version = ++_refresh; _checking = true; Changed();
        try
        {
            var status = await _access.ReadAccessAsync(_lifetime.Token);
            if (version == _refresh && !_disposed) _status = status;
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { if (version == _refresh) _status = null; }
        finally { if (version == _refresh) { _checking = false; Changed(); } }
    }

    private void Confirm()
    {
        if (!CanConfirm) return;
        var confirmed = ImagePdfPlan.Create(_plan.BatchId, _pages.Select(page => page.Page.Source), _plan.Settings).Confirm(true);
        _confirmed = true; Changed(); Confirmed?.Invoke(confirmed);
    }

    private void Changed()
    {
        PropertyChanged?.Invoke(this, new(null));
        ((UiCommand)MoveUpCommand).Notify(); ((UiCommand)MoveDownCommand).Notify(); ((UiCommand)ConfirmCommand).Notify();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); Changed();
    }
}

internal sealed class ImagePdfOrderRow(ImagePdfPage page, int number) : INotifyPropertyChanged
{
    public ImagePdfPage Page { get; } = page;
    public string Name => Path.GetFileName(Page.Source.Path);
    public string Folder => Path.GetDirectoryName(Page.Source.Path)!;
    public string AccessibleName => $"Page {Number}: {Name}. Folder: {Folder}";
    public int Number
    {
        get => number;
        internal set { number = value; PropertyChanged?.Invoke(this, new(null)); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
