using System.Security.Cryptography;
using ContextSuite.Core.Analysis;
using ContextSuite.Core.Office;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Office;

public static class OfficeFileAdapter
{
    public static async Task<OfficeExportCandidate> ExportAsync(string packageDirectory, OfficeExportWork work,
        Guid requestId, CancellationToken token)
    {
        work.Validate();
        if (requestId == Guid.Empty) throw new InvalidDataException("Missing Office export request identity.");
        token.ThrowIfCancellationRequested();
        using var paths = new OfficeDirectoryLeases();
        paths.Open(work.DirectoryPath);
        var names = new HashSet<string>(["input", "output", "profile", "temp"], StringComparer.Ordinal);
        foreach (var entry in Directory.EnumerateFileSystemEntries(work.DirectoryPath))
            if (!names.Remove(Path.GetFileName(entry))) throw new InvalidDataException("Unexpected Office context contents.");
        if (names.Count != 0) throw new InvalidDataException("Incomplete Office context.");
        foreach (var name in new[] { "input", "output", "profile", "temp" })
        {
            var directory = Path.Combine(work.DirectoryPath, name); paths.Open(directory);
            var expected = name == "input" ? new[] { work.SourcePath } : [];
            if (!Directory.EnumerateFileSystemEntries(directory).SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Office requires a fresh isolated context.");
        }
        using var source = MediaFiles.OpenRead(work.SourcePath);
        if (!File.GetAttributes(work.SourcePath).HasFlag(FileAttributes.ReadOnly))
            throw new InvalidDataException("Office requires a read-only source snapshot.");
        var modified = File.GetLastWriteTimeUtc(work.SourcePath);
        if (source.Length != work.SourceBytes || Convert.ToHexString(await SHA256.HashDataAsync(source, token)) != work.SourceSha256.ToUpperInvariant())
            throw new InvalidDataException("Office source differs from its admitted identity.");
        var admission = await OfficeSourcePreflight.InspectOpenXmlAsync(work.SourcePath, source, token);
        if (admission.Refusal is not null || admission.FormatId != work.Format)
            throw new InvalidDataException(admission.Refusal ?? "Office source contents do not match the supported document variant.");
        using var runtime = await OfficeRuntimeLease.OpenAsync(Path.Combine(packageDirectory, "ContextSuite.OfficeHost.exe"),
            Path.Combine(packageDirectory, "runtime"), Path.Combine(packageDirectory, "runtime-files.txt"), token);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windows)) throw new InvalidDataException("The Windows system directory is unavailable.");
        var profile = Path.GetDirectoryName(work.EngineProfilePath)!;
        // Windows appends the native package path to LOCALAPPDATA when creating
        // an AppContainer process. An operation-directory override produces a
        // nonexistent nested profile and can stall debugger-helper initialization.
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        paths.Open(Path.Combine(localApplicationData, "Packages", work.ProfileName, "AC", "Temp"));
        var temp = Path.Combine(work.DirectoryPath, "temp");
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["APPDATA"] = profile, ["LOCALAPPDATA"] = localApplicationData, ["USERPROFILE"] = profile,
            ["TEMP"] = temp, ["TMP"] = temp, ["SYSTEMROOT"] = windows, ["WINDIR"] = windows,
            ["PATH"] = runtime.ProgramDirectory + ";" + Path.Combine(windows, "System32"),
            ["CS_OFFICE_PROFILE_NAME"] = work.ProfileName, ["CS_OFFICE_REQUEST_ID"] = requestId.ToString("N"),
            ["CS_OFFICE_FORMAT"] = work.Format, ["CS_OFFICE_CALCULATION"] = work.Calculation,
            ["CS_OFFICE_SOURCE"] = work.SourcePath, ["CS_OFFICE_OUTPUT"] = work.CandidatePath,
            ["CS_OFFICE_PROFILE"] = work.EngineProfilePath, ["CS_OFFICE_PROGRAM"] = runtime.ProgramDirectory,
            ["CS_OFFICE_SOURCE_IDENTITY"] = OfficeDirectoryLeases.Identity(source.SafeFileHandle),
            ["CS_OFFICE_OUTPUT_IDENTITY"] = OfficeDirectoryLeases.Identity(paths.Open(Path.GetDirectoryName(work.CandidatePath)!)),
            ["CS_OFFICE_PROFILE_IDENTITY"] = OfficeDirectoryLeases.Identity(paths.Open(profile)),
            ["CS_OFFICE_PROGRAM_IDENTITY"] = OfficeDirectoryLeases.Identity(paths.Open(runtime.ProgramDirectory))
        };
        var reply = await OfficeSandboxProcess.RunAsync(runtime.Executable, work.DirectoryPath, environment, token);
        runtime.VerifyMembership(token);
        var completion = OfficeHostProtocol.ReadCompletion(reply, 0, requestId, work.Format, work.Calculation, work.SourceBytes, work.SourceSha256);
        using var candidate = MediaFiles.OpenRead(work.CandidatePath);
        if (candidate.Length != completion.OutputBytes || Convert.ToHexString(await SHA256.HashDataAsync(candidate, token)) != completion.OutputSha256)
            throw new InvalidDataException("Office candidate differs from its completion identity.");
        await OfficeWorkbookValidation.CheckAsync(work, completion, token);
        source.Position = 0;
        if (Convert.ToHexString(await SHA256.HashDataAsync(source, token)) != completion.SourceSha256 || File.GetLastWriteTimeUtc(work.SourcePath) != modified)
            throw new InvalidDataException("Office source changed during export.");
        var result = new OfficeExportCandidate(work.ItemId, completion, work.Policy);
        result.Validate(work, requestId);
        return result;
    }
}
