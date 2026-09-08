using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Settings;

namespace ContextSuite.Application.Infrastructure;

internal sealed record LoadedSettings(SuiteSettings Settings, string? Revision, bool CanSave, string? Warning);

internal sealed class SettingsStore(string path)
{
    private const int MaximumBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path = Path.GetFullPath(path);

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextSuite", "settings.json");

    public async Task<LoadedSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            RejectLinkedAncestors(_path);
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            if (stream.Length > MaximumBytes)
                return new(new(), null, false, "Settings are too large. Defaults are active; the file will not be overwritten.");
            var bytes = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken);
            var revision = Convert.ToHexString(SHA256.HashData(bytes));
            try
            {
                using var json = JsonDocument.Parse(bytes);
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("SchemaVersion", out var version) ||
                    version.ValueKind != JsonValueKind.Number ||
                    !version.TryGetInt32(out var schema) || schema != SuiteSettings.CurrentSchemaVersion)
                    return new(new(), revision, false, "Unsupported settings version. Defaults are active; the file will not be overwritten.");
                var repaired = false;
                var settings = new SuiteSettings
                {
                    Convert = ReadTool(root, "Convert", ref repaired),
                    Optimize = ReadTool(root, "Optimize", ref repaired),
                    PlayCompletionSound = !root.TryGetProperty("PlayCompletionSound", out var sound) || sound.ValueKind != JsonValueKind.False
                };
                return new(settings, revision, true, repaired ? "Some settings were invalid and use safe defaults. Save to apply the repaired values." : null);
            }
            catch (JsonException)
            {
                return new(new(), revision, true, "Settings could not be read. Safe defaults are active; saving replaces the damaged settings.");
            }
        }
        catch (FileNotFoundException) { return new(new(), null, true, null); }
        catch (DirectoryNotFoundException) { return new(new(), null, true, null); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(new(), null, false, "Settings are unavailable. Defaults are active; no settings will be overwritten.");
        }
    }

    public async Task<LoadedSettings> SaveAsync(SuiteSettings settings, LoadedSettings expected,
        CancellationToken cancellationToken = default)
    {
        if (!expected.CanSave || settings.SchemaVersion != SuiteSettings.CurrentSchemaVersion ||
            settings.Convert is null || settings.Optimize is null)
            throw new InvalidDataException("These settings cannot be saved by this version of Context Suite.");
        ValidateDirectory(settings.Convert.OutputDirectory);
        ValidateDirectory(settings.Optimize.OutputDirectory);
        RejectLinkedAncestors(_path);
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        RejectLinkedAncestors(directory);
        RejectLinkedAncestors(_path + ".lock");
        // Serialize cooperating saves. External changes since load are rejected rather than lost.
        await using var lease = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var current = await LoadAsync(cancellationToken);
        if (!current.CanSave || current.Revision != expected.Revision)
            throw new IOException("Settings changed or became unavailable. Close and reopen Settings before saving.");
        var temporary = Path.Combine(directory, $"settings-{Guid.NewGuid():N}.tmp");
        var backup = Path.Combine(directory, $"settings-{Guid.NewGuid():N}.bak");
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinkedAncestors(_path);
            if (current.Revision is null) File.Move(temporary, _path, overwrite: false);
            else File.Replace(temporary, _path, backup, ignoreMetadataErrors: false);
            // Once committed, cancellation must not report a failed save.
            TryDeleteOwnedFile(backup);
            return new(settings, Convert.ToHexString(SHA256.HashData(bytes)), true, null);
        }
        catch (IOException error) when (File.Exists(backup))
        {
            throw new IOException($"Settings publication was interrupted. Previous settings are retained at {backup}.", error);
        }
        finally { TryDeleteOwnedFile(temporary); }
    }

    private static ToolSettings ReadTool(JsonElement root, string name, ref bool repaired)
    {
        if (!root.TryGetProperty(name, out var tool) || tool.ValueKind != JsonValueKind.Object)
        {
            repaired = true;
            return new();
        }
        var allow = false;
        if (tool.TryGetProperty("AllowReplacingOriginals", out var replace) &&
            replace.ValueKind is JsonValueKind.True or JsonValueKind.False) allow = replace.GetBoolean();
        else repaired = true;
        string? directory = null;
        if (tool.TryGetProperty("OutputDirectory", out var output) && output.ValueKind != JsonValueKind.Null)
        {
            if (output.ValueKind == JsonValueKind.String)
            {
                try { directory = output.GetString(); ValidateDirectory(directory); }
                catch (ArgumentException) { directory = null; repaired = true; }
            }
            else repaired = true;
        }
        return new(allow, directory);
    }

    private static void ValidateDirectory(string? directory)
    {
        if (directory is not null && (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory) ||
            directory.IndexOfAny(['\0', '\r', '\n']) >= 0 || directory.Length > 32700))
            throw new ArgumentException("Use a fully qualified output folder or choose the source folder.");
    }

    private static void RejectLinkedAncestors(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Settings cannot be stored through linked paths.");
    }

    private static void TryDeleteOwnedFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
