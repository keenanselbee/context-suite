using System.ComponentModel;
using System.Diagnostics;

namespace ContextSuite.Application.Infrastructure;

internal enum OfficeRecoveryState { AlreadyClean, Recovered, ReviewRequired }
internal sealed record OfficeRecoveryEntry(string Path, OfficeRecoveryState State, string? Diagnostic = null);
internal sealed record OfficeRecoveryReport(string Directory, IReadOnlyList<OfficeRecoveryEntry> Entries,
    bool Incomplete = false, bool Cancelled = false)
{
    internal bool NeedsAttention => Incomplete || Entries.Any(entry => entry.State != OfficeRecoveryState.AlreadyClean);
    internal string Notice
    {
        get
        {
            if (!NeedsAttention) return "";
            var recovered = Entries.Count(entry => entry.State == OfficeRecoveryState.Recovered);
            var review = Entries.Count(entry => entry.State == OfficeRecoveryState.ReviewRequired);
            var text = recovered == 0 ? "" : $"Cleaned up {recovered} interrupted Office conversion(s). Run Convert again for those files. ";
            if (review != 0 || Incomplete) text += "Some interrupted Office work still needs review. ";
            return text + "Files and recovery records were kept at " + Directory + ".";
        }
    }
}

// Native recovery and explicitly recorded temporary retirement. Never resumes
// rendering or publishes a PDF. Run off the UI thread before admitting Office work.
internal static class OfficeRecoveryCoordinator
{
    internal const int MaximumDirectoryEntries = 512;
    internal const int MaximumRecords = 256;

    internal static async Task<OfficeRecoveryReport> RecoverAsync(string contextRoot, string runtimeDirectory, CancellationToken token)
    {
        var entries = new List<OfficeRecoveryEntry>();
        if (token.IsCancellationRequested) return new(contextRoot, entries, Cancelled: true);
        try
        {
            var root = PublicationFiles.Normalize(contextRoot);
            if (root != contextRoot || !PublicationFiles.IsLocalNtfs(root))
                throw new InvalidDataException("Office recovery requires its ordinary local context root.");
            FileAttributes attributes;
            try { attributes = File.GetAttributes(root); }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            { return new(root, entries); }
            if ((attributes & FileAttributes.Directory) == 0) throw new InvalidDataException("Office recovery root is not a directory.");
            using var lease = OfficeSandboxOwner.LeaseDirectory(root);
            var children = Directory.EnumerateFileSystemEntries(root).Take(MaximumDirectoryEntries + 1).ToArray();
            if (children.Length > MaximumDirectoryEntries) return new(root, entries, Incomplete: true);
            var records = children.Where(path => path.EndsWith(".ownership", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal).ToArray();
            var names = records.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in children)
            {
                var name = Path.GetFileName(path);
                if (name.StartsWith("office-", StringComparison.Ordinal) && Guid.TryParseExact(name[7..], "N", out var id) &&
                    !names.Contains(id.ToString("N") + ".ownership"))
                    entries.Add(new(path, OfficeRecoveryState.ReviewRequired, "Preparation has no ownership record."));
            }
            var started = Stopwatch.StartNew();
            for (var index = 0; index < records.Length; index++)
            {
                if (token.IsCancellationRequested) return new(root, entries, Incomplete: true, Cancelled: true);
                if (index == MaximumRecords || started.Elapsed > TimeSpan.FromSeconds(30)) return new(root, entries, Incomplete: true);
                entries.Add(await RecoverRecordAsync(records[index], root, runtimeDirectory));
            }
            return new(root, entries);
        }
        catch (Exception error) when (Expected(error))
        {
            entries.Add(new(contextRoot, OfficeRecoveryState.ReviewRequired, Diagnostic(error)));
            return new(contextRoot, entries, Incomplete: true);
        }
    }

    private static async Task<OfficeRecoveryEntry> RecoverRecordAsync(string path, string root, string runtime)
    {
        // Once native cleanup starts, finish the bounded attempt even if shutdown
        // is requested. Cancellation is observed before the next record.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var journal = OfficeOwnershipJournal.Open(path, root, runtime);
                if (journal.Version is not (3 or 4)) throw new InvalidDataException("Legacy Office ownership requires review.");
                if (journal.Changes[^1].Step == OfficeOwnershipStep.RetirementIntent)
                {
                    await OfficeContextPreparation.RetireRecoveredAsync(journal, path);
                    return new(path, OfficeRecoveryState.AlreadyClean);
                }
                if (journal.Changes[^1].Step == OfficeOwnershipStep.ProfileDeleted)
                    return new(path, OfficeRecoveryState.AlreadyClean);
                var owner = await OfficeSandboxOwner.RecoverAsync(journal);
                try
                {
                    await owner.DisposeAsync();
                    return new(path, OfficeRecoveryState.Recovered);
                }
                finally { owner.ReleaseRecoveryLeases(); }
            }
            catch (Exception error) when (attempt < 49 && Sharing(error))
            { await Task.Delay(100).ConfigureAwait(false); }
            catch (Exception error) when (Expected(error))
            { return new(path, OfficeRecoveryState.ReviewRequired, Diagnostic(error)); }
        }
    }

    private static bool Sharing(Exception error) => error is Win32Exception native && native.NativeErrorCode is 32 or 33 ||
        error is IOException && (error.HResult & 0xffff) is 32 or 33;
    private static bool Expected(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or
        Win32Exception or ArgumentException or AggregateException or NotSupportedException or System.Security.SecurityException;
    private static string Diagnostic(Exception error) => $"{error.GetType().Name}; HRESULT 0x{error.HResult:X8}";
}
