using System.Security.Cryptography;
using ContextSuite.Core.Pdf;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Pdf;

// Independent qpdf reader of the writer's fixed raster-only schema. This is
// output validation, not original-file publication or generic PDF validation.
public sealed class ImagePdfValidator : IDisposable
{
    private readonly string _executable, _scratch, _workingDirectory;
    private readonly List<FileStream> _leases = [];
    private bool _disposed;
    private static readonly Dictionary<string, string> PinnedFiles = new()
    {
        ["ContextSuite.ImagePdfValidator.exe"] = "4BD57D54B4B6BD7C0A56170EEA26CA8DFB7080B429821D513E69829DCD91E073",
        ["qpdf30.dll"] = "36FE5B2023F244E5785D96B8372DBA26B75EA51B63ADF4D4F1E66AD0AA1C8A61",
        ["concrt140.dll"] = "54716F0738AF891F283D213B5C8D11B25896BB8EE3097D301EAE718560CF974E",
        ["msvcp140.dll"] = "7C26614E1D733892C2DEAC7E245CE115504B1D80592DD0A01B08E3E5A55F89CA",
        ["msvcp140_1.dll"] = "206C931BF90FDAD8816DE3B5E2EF80B2BCAA9406C89ECC05FE6FDDFFE251E982",
        ["msvcp140_2.dll"] = "D50D7883F20D1DC6191768D3746F52DD9CAC89C346FFAED5BE1F110C2F34A838",
        ["msvcp140_atomic_wait.dll"] = "3D0CBFAA1BF3EECF5A3F4491D2960EE803CB994F30292C6ADC4A07C498F60E2B",
        ["msvcp140_codecvt_ids.dll"] = "8A65C7596EF2E6938731F5A1058E7E40145B6D97967CC649231A076B9A608D78",
        ["vcruntime140.dll"] = "D1F4225DF2CD877DBF130D5668A021DCE3F94118455FF5EC952061C30AFC9CE7",
        ["vcruntime140_1.dll"] = "A7146C08F89FE5B04541AB507CDB59FF7B44534D4BA3C668A426C6450A03434E"
    };

    public ImagePdfValidator(string binaryDirectory, string scratchDirectory)
    {
        if (!Path.IsPathFullyQualified(binaryDirectory) || !Path.IsPathFullyQualified(scratchDirectory))
            throw new ArgumentException("Absolute validator and scratch paths are required.");
        _executable = Path.Combine(Path.GetFullPath(binaryDirectory), "ContextSuite.ImagePdfValidator.exe");
        _scratch = Path.GetFullPath(scratchDirectory); _workingDirectory = _scratch;
        try
        {
            foreach (var entry in PinnedFiles)
            {
                var lease = MediaFiles.OpenRead(Path.Combine(binaryDirectory, entry.Key)); _leases.Add(lease);
                if (Convert.ToHexString(SHA256.HashData(lease)) != entry.Value) throw new InvalidDataException("Image PDF validator identity mismatch.");
            }
            CheckDirectory(_scratch); Directory.CreateDirectory(_scratch); CheckDirectory(_scratch);
            while (_workingDirectory.Length > 240 && Path.GetDirectoryName(_workingDirectory) is { } parent) _workingDirectory = parent;
        }
        catch { Dispose(); throw; }
    }

    public async Task<string> ValidateAsync(ImagePdfCandidate candidate, ConfirmedImagePdf confirmed, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        _ = confirmed.Plan.Confirm(true);
        return await ValidateAsync(candidate, confirmed.Plan.Pages.Select(RasterPdfPage.FromImagePage).ToArray(), token);
    }

    public async Task<string> ValidateAsync(ImagePdfCandidate candidate, IReadOnlyList<RasterPdfPage> pages, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(pages);
        var expectedPages = pages.ToArray();
        RasterPdfPage.ValidatePages(expectedPages);
        if (candidate?.Bytes is not { Length: > 0 and <= ImagePdfPlan.MaximumOutputBytes } ||
            candidate.Samples.IsDefault || candidate.Samples.Length != expectedPages.Length)
            throw new InvalidDataException("Invalid image PDF candidate or sample inventory.");
        for (var i = 0; i < candidate.Samples.Length; i++)
        {
            if (candidate.Samples[i] is null) throw new InvalidDataException("Missing image PDF sample inventory.");
            candidate.Samples[i].Validate(expectedPages[i], i);
        }
        var expectedHash = Convert.ToHexString(SHA256.HashData(candidate.Bytes));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        CheckDirectory(_scratch);
        var path = Path.Combine(_scratch, "image-pdf-validation-" + Guid.NewGuid().ToString("N") + ".pdf");
        var created = false;
        try
        {
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                created = true;
                await file.WriteAsync(candidate.Bytes, deadline.Token);
            }
            using var input = MediaFiles.OpenRead(path);
            if (input.Length != candidate.Bytes.Length || Convert.ToHexString(await SHA256.HashDataAsync(input, deadline.Token)) != expectedHash)
                throw new InvalidDataException("Image PDF validation snapshot changed.");
            var reply = await SeekableMediaProcess.RunAsync(_executable, _workingDirectory, input, ["--inspect"], ImagePdfValidation.MaximumReplyBytes, deadline.Token);
            ImagePdfValidation.Verify(reply, expectedPages, candidate.Samples);
            MediaFiles.CheckHandle(input, path); input.Position = 0;
            if (Convert.ToHexString(await SHA256.HashDataAsync(input, deadline.Token)) != expectedHash ||
                Convert.ToHexString(SHA256.HashData(candidate.Bytes)) != expectedHash)
                throw new InvalidDataException("Image PDF changed during independent validation.");
            deadline.Token.ThrowIfCancellationRequested();
            return expectedHash;
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new TimeoutException("Image PDF validation exceeded its deadline.", error); }
        finally
        {
            if (created && File.Exists(path))
            {
                CheckDirectory(_scratch);
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked validation snapshot retained.");
                File.Delete(path);
            }
        }
    }

    private static void CheckDirectory(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked validation directories are not allowed.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var lease in _leases) lease.Dispose();
    }
}
