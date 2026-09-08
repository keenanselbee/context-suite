using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Settings;

namespace ContextSuite.Application;

internal sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore _store;
    private readonly LoadedSettings _loaded;
    private readonly SaveSettingsCommand _saveCommand;
    private int _selectedSection;
    private bool _isSaving;
    private string _message;

    public SettingsViewModel(SettingsStore store, LoadedSettings loaded, string section, bool replacementAvailable)
    {
        _store = store;
        _loaded = loaded;
        Convert = new ToolSettingsEditor(loaded.Settings.Convert);
        Optimize = new ToolSettingsEditor(loaded.Settings.Optimize);
        PlayCompletionSound = loaded.Settings.PlayCompletionSound;
        _selectedSection = section == "optimize" ? 1 : 0;
        _message = loaded.Warning ?? "Preferences apply to future batches. Existing batches keep their settings.";
        CanEdit = loaded.CanSave;
        CanAllowReplacement = CanEdit && replacementAvailable;
        ReplacementNotice = replacementAvailable
            ? "Replacement also requires confirmation for each batch. Quick actions always create copies."
            : "Replacement is unavailable until Windows safety verification is complete. Copies remain the default.";
        _saveCommand = new SaveSettingsCommand(this);
    }

    public ToolSettingsEditor Convert { get; }
    public ToolSettingsEditor Optimize { get; }
    public bool PlayCompletionSound { get; set; }
    public bool CanEdit { get; }
    public bool CanAllowReplacement { get; }
    public string ReplacementNotice { get; }
    public string NamingExample => SelectedSection == 0 ? "gamma - Converted.png   /   gamma - BC7-sRGB.dds" : "gamma - Optimized.webp";
    public int SelectedSection
    {
        get => _selectedSection;
        set { _selectedSection = value; Changed(); Changed(nameof(NamingExample)); }
    }
    public bool IsSaving
    {
        get => _isSaving;
        private set { _isSaving = value; Changed(); _saveCommand.Refresh(); }
    }
    public string Message { get => _message; private set { _message = value; Changed(); } }
    public ICommand SaveCommand => _saveCommand;
    public event Action<LoadedSettings>? Saved;
    public event PropertyChangedEventHandler? PropertyChanged;

    internal async Task SaveAsync()
    {
        if (IsSaving || !CanEdit) return;
        IsSaving = true;
        try
        {
            var settings = new SuiteSettings { Convert = Convert.Capture(), Optimize = Optimize.Capture(), PlayCompletionSound = PlayCompletionSound };
            var result = await _store.SaveAsync(settings, _loaded);
            Message = "Settings saved. Existing batches were not changed.";
            IsSaving = false;
            Saved?.Invoke(result);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            Message = error.Message;
        }
        finally { IsSaving = false; }
    }

    private void Changed([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private sealed class SaveSettingsCommand(SettingsViewModel owner) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => owner.CanEdit && !owner.IsSaving;
        public async void Execute(object? parameter) { await owner.SaveAsync(); }
        public void Refresh() { CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}

internal sealed class ToolSettingsEditor(ToolSettings settings) : INotifyPropertyChanged
{
    public bool AllowReplacingOriginals { get; set; } = settings.AllowReplacingOriginals;
    private string _outputDirectory = settings.OutputDirectory ?? "";
    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (_outputDirectory == value) return;
            _outputDirectory = value;
            PropertyChanged?.Invoke(this, new(nameof(OutputDirectory)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public ToolSettings Capture()
    {
        return new ToolSettings(AllowReplacingOriginals, string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory.Trim());
    }
}
