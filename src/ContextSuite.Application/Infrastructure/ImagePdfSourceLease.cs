using System.Collections.Immutable;
using ContextSuite.Core.Pdf;

namespace ContextSuite.Application.Infrastructure;

internal sealed record PublicationSource(Guid ItemId, string Path, FileFingerprint Fingerprint);

internal sealed class ImagePdfSourceLease : IDisposable
{
    private readonly List<FileStream> _streams = [];
    public ImmutableArray<PublicationSource> Sources { get; private set; }

    public static async Task<ImagePdfSourceLease> OpenAsync(ConfirmedImagePdf confirmed, CancellationToken token)
    {
        _ = confirmed.Plan.Confirm(true);
        var lease = new ImagePdfSourceLease();
        try
        {
            var sources = ImmutableArray.CreateBuilder<PublicationSource>(confirmed.Plan.Pages.Length);
            foreach (var page in confirmed.Plan.Pages)
            {
                token.ThrowIfCancellationRequested();
                var source = page.Source;
                var path = PublicationFiles.Normalize(source.Path);
                var stream = PublicationFiles.OpenRead(path); lease._streams.Add(stream);
                var fingerprint = await PublicationFiles.FingerprintAsync(stream, token);
                if (fingerprint.Length != source.FileBytes || fingerprint.Sha256 != source.Sha256)
                    throw new InvalidDataException("An original image changed after the PDF page order was reviewed.");
                sources.Add(new(source.ItemId, path, fingerprint));
            }
            lease.Sources = sources.MoveToImmutable();
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    public async Task VerifyAsync(CancellationToken token)
    {
        for (var index = 0; index < Sources.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var source = Sources[index];
            PublicationFiles.RejectLinks(source.Path);
            if (await PublicationFiles.FingerprintAsync(_streams[index], token) != source.Fingerprint ||
                !await PublicationFiles.MatchesAsync(source.Path, source.Fingerprint))
                throw new InvalidDataException("An original image changed before the combined PDF could be saved.");
        }
    }

    public void Dispose()
    {
        foreach (var stream in _streams) stream.Dispose();
    }
}
