using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContextSuite.Core.Licensing;

namespace ContextSuite.Application.Infrastructure;

internal enum LicenseRecordState { NotActivated, ActivationPending, Active, Rejected, DeactivationPending }
internal sealed record LicenseRecord(int Schema, LicenseEnvironment Environment, Guid InstallationId,
    LicenseRecordState State, string? Key = null, LicenseReceipt? Receipt = null,
    DateTimeOffset? VerifiedUtc = null, DateTimeOffset? LastObservedUtc = null, DateTimeOffset? LastRefreshAttemptUtc = null,
    Guid? RefreshId = null)
{
    public override string ToString() => $"License record: {Environment}, {State} (credentials redacted)";
}

// Callers hold a lease for read-modify-write. Background validation releases it
// during HTTP and checks its transaction identifier before saving a response.
// Different Windows sessions cannot concurrently allocate the same local slot.
internal sealed class LicenseStore(string path, LicenseEnvironment environment)
{
    private const int MaximumBytes = 16 * 1024;
    private readonly string _path = Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) :
        throw new ArgumentException("License storage requires an absolute path.");
    private readonly byte[] _entropy = Encoding.UTF8.GetBytes($"ContextSuite.License/1/{environment}");
    private static readonly JsonSerializerOptions JsonOptions = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };

    public async Task<FileStream> LockAsync(CancellationToken cancellationToken)
    {
        RejectLinks(_path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        RejectLinks(_path);
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinks(_path + ".lock");
            try { return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (timer.Elapsed < TimeSpan.FromSeconds(12))
            { await Task.Delay(50, cancellationToken); }
        }
    }

    public async Task<LicenseRecord?> ReadAsync(CancellationToken cancellationToken)
    {
        RejectLinks(_path);
        byte[] encrypted;
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            if (stream.Length is 0 or > MaximumBytes) throw new InvalidDataException("Invalid license storage size.");
            encrypted = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(encrypted, cancellationToken);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        var plain = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.CurrentUser);
        try
        {
            var record = JsonSerializer.Deserialize<LicenseRecord>(plain, JsonOptions) ?? throw new InvalidDataException("Missing license data.");
            Validate(record);
            return record;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public async Task SaveAsync(LicenseRecord record, CancellationToken cancellationToken)
    {
        Validate(record);
        RejectLinks(_path);
        var plain = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plain, _entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        if (encrypted.Length > MaximumBytes) throw new InvalidDataException("License storage exceeds its limit.");
        var temporary = Path.Combine(Path.GetDirectoryName(_path)!, $"license-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(encrypted, cancellationToken);
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinks(_path);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void Validate(LicenseRecord record)
    {
        if (record.Schema != 1 || record.Environment != environment || !Enum.IsDefined(record.State) ||
            record.InstallationId == Guid.Empty || record.Key?.Length > 512 || record.Key?.Any(char.IsControl) == true)
            throw new InvalidDataException("License storage schema or environment is invalid.");
        if (record.State == LicenseRecordState.NotActivated)
        {
            if (record.Key is not null || record.Receipt is not null || record.VerifiedUtc is not null)
                throw new InvalidDataException("Inactive license contains active data.");
            return;
        }
        if (string.IsNullOrWhiteSpace(record.Key)) throw new InvalidDataException("Missing stored key.");
        if (record.State is LicenseRecordState.Active or LicenseRecordState.DeactivationPending &&
            (record.Receipt is null || record.Receipt.LicenseId == Guid.Empty || record.Receipt.ActivationId == Guid.Empty ||
             record.VerifiedUtc is null || record.LastObservedUtc is null))
            throw new InvalidDataException("Incomplete license receipt.");
        if (record.VerifiedUtc is { } verified && (verified < DateTimeOffset.UnixEpoch ||
            verified > DateTimeOffset.MaxValue - PaidLicensePolicy.OfflineGrace || record.LastObservedUtc < verified))
            throw new InvalidDataException("Invalid license timestamps.");
    }

    private static void RejectLinks(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked license storage is not supported.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
