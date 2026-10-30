using System.Security.Cryptography;
using ContextSuite.Core.Office;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Private.Pdf;

public sealed partial class PdfProbeAdapter
{
    internal async Task<int> InspectOfficeAsync(FileStream source, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        token.ThrowIfCancellationRequested();
        if (source.Length is <= 0 or > OfficeHostProtocol.MaximumOutputBytes)
            throw new InvalidDataException("Office PDF exceeds its output bound.");
        RequireUnlinkedDirectory(_scratch);
        var path = Path.Combine(_scratch, "office-pdf-validation-" + Guid.NewGuid().ToString("N") + ".pdf");
        var created = false;
        try
        {
            source.Position = 0;
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            { created = true; await source.CopyToAsync(output, 65536, token); }
            using var snapshot = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            source.Position = 0;
            var expected = await SHA256.HashDataAsync(source, token);
            if (snapshot.Length != source.Length || !(await SHA256.HashDataAsync(snapshot, token)).AsSpan().SequenceEqual(expected))
                throw new IOException("Office PDF snapshot changed before inspection.");
            var input = NativeSnapshotPath(path);
            var inventory = await Require(["--suppress-recovery", "--json-output=2", "--json-stream-data=none", input], PdfRewriteInventory.MaximumBytes);
            OfficePdfPolicy.CheckObjects(inventory, token);
            await Require(["--suppress-recovery", "--check", input], 65536);
            var count = OfficePdfPolicy.ReadPageCount(await Require(["--suppress-recovery", "--show-npages", input], 32));
            snapshot.Position = 0;
            if (!(await SHA256.HashDataAsync(snapshot, token)).AsSpan().SequenceEqual(expected))
                throw new IOException("Office PDF snapshot changed during inspection.");
            return count;
        }
        finally
        {
            if (created && File.Exists(path))
            {
                RequireUnlinkedDirectory(_scratch);
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked Office PDF snapshot retained.");
                File.Delete(path);
            }
        }
        async Task<byte[]> Require(string[] arguments, int maximum)
        {
            var result = await RunAsync(arguments, maximum, token);
            if (result.ExitCode != 0) throw new InvalidDataException("Office PDF inspection failed or reported warnings.");
            return result.Output;
        }
    }
}
