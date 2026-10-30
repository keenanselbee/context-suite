using System.Globalization;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;
using ContextSuite.Private.Images;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Pdf;

public sealed record ImagePdfCandidate(byte[] Bytes, ImmutableArray<ImagePdfSamples> Samples);

// Fixed raster-only PDF 1.7 candidate. No PDF parser, document scripting,
// external resources or file publication. Independent validation is a separate
// integration gate; successfully serializing bytes is not output approval.
public static class ImagePdfWriter
{
    public static ImagePdfCandidate Create(ConfirmedImagePdf confirmed, ImageAdapter adapter, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        ArgumentNullException.ThrowIfNull(adapter);
        var plan = confirmed.Plan;
        _ = plan.Confirm(true);
        token.ThrowIfCancellationRequested();
        var leases = new List<FileStream>();
        try
        {
            // Keep every original read-only and nonreplaceable for the entire
            // combined serialization, including images not decoded yet.
            foreach (var page in plan.Pages)
            {
                token.ThrowIfCancellationRequested();
                var lease = MediaFiles.OpenRead(page.Source.Path);
                leases.Add(lease);
                if (lease.Length != page.Source.FileBytes) throw new InvalidDataException("An original image changed. Start a new conversion.");
            }
            var pages = plan.Pages.Select(RasterPdfPage.FromImagePage).ToArray();
            var candidate = Create(pages, index =>
            {
                var decoded = adapter.ReadPdfPixels(plan.Pages[index], token);
                return new(RasterPdfPage.FromImagePage(decoded.Page),
                    decoded.Color, decoded.Alpha, decoded.IccProfile);
            }, token);
            for (var i = 0; i < leases.Count; i++)
            {
                MediaFiles.CheckHandle(leases[i], plan.Pages[i].Source.Path);
                leases[i].Position = 0;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[65536];
                int read;
                while ((read = leases[i].Read(buffer)) != 0)
                {
                    token.ThrowIfCancellationRequested();
                    hash.AppendData(buffer.AsSpan(0, read));
                }
                if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(plan.Pages[i].Source.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("An original image changed. Start a new conversion.");
            }
            token.ThrowIfCancellationRequested();
            return candidate;
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }

    public static ImagePdfCandidate Create(IReadOnlyList<RasterPdfPage> pages, Func<int, RasterPdfPixels> readPage, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(pages);
        var expectedPages = pages.ToArray();
        RasterPdfPage.ValidatePages(expectedPages);
        ArgumentNullException.ThrowIfNull(readPage);
        token.ThrowIfCancellationRequested();
        using var output = new BoundedPdfStream();
        var samples = ImmutableArray.CreateBuilder<ImagePdfSamples>(expectedPages.Length);
        var firstObjects = new int[expectedPages.Length];
        var next = 3;
        for (var i = 0; i < firstObjects.Length; i++)
        {
            firstObjects[i] = next;
            next += expectedPages[i].HasTransparency ? 7 : 5;
        }
        var offsets = new long[next];
        Write("%PDF-1.7\n%"); output.Write([0xe2, 0xe3, 0xcf, 0xd3]); Write("\n");
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, $"<< /Type /Pages /Count {expectedPages.Length} /Kids [{string.Join(' ', firstObjects.Select(id => $"{id} 0 R"))}] >>");
        for (var index = 0; index < expectedPages.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var pixels = readPage(index);
            var expected = expectedPages[index];
            var colorBytes = checked((long)expected.Width * expected.Height * expected.ColorComponents * expected.BitsPerComponent / 8);
            var alphaBytes = checked((long)expected.Width * expected.Height * expected.BitsPerComponent / 8);
            if (pixels is null || pixels.Page != expected || pixels.Color is null || pixels.IccProfile is null ||
                (pixels.Alpha is not null) != expected.HasTransparency ||
                pixels.Color.LongLength != colorBytes ||
                pixels.Alpha is not null && pixels.Alpha.LongLength != alphaBytes ||
                pixels.IccProfile.Length is < 132 or > 4 * 1024 * 1024)
                throw new InvalidDataException("Raster PDF page samples do not match their declared layout.");
            var page = pixels.Page;
            samples.Add(new(index, Convert.ToHexString(SHA256.HashData(pixels.Color)),
                pixels.Alpha is null ? null : Convert.ToHexString(SHA256.HashData(pixels.Alpha)),
                Convert.ToHexString(SHA256.HashData(pixels.IccProfile)), pixels.IccProfile.Length));
            var id = firstObjects[index];
            // PDF real numbers do not permit exponential notation.
            var width = page.WidthPoints.ToString("0.################", CultureInfo.InvariantCulture);
            var height = page.HeightPoints.ToString("0.################", CultureInfo.InvariantCulture);
            Object(id, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << /XObject << /Im0 {id + 2} 0 R >> >> /Contents {id + 1} 0 R >>");
            var content = Encoding.ASCII.GetBytes($"q\n{width} 0 0 {height} 0 0 cm\n/Im0 Do\nQ\n");
            RawStream(id + 1, "", content);
            var softMask = pixels.Alpha is null ? "" : $" /SMask {id + 5} 0 R";
            var geometry = $"/Type /XObject /Subtype /Image /Width {page.Width} /Height {page.Height} /BitsPerComponent {page.BitsPerComponent} /Interpolate false";
            CompressedStream(id + 2, id + 3, $"{geometry} /ColorSpace [/ICCBased {id + 4} 0 R]{softMask}", pixels.Color);
            RawStream(id + 4, $"/N {page.ColorComponents} /Alternate /{(page.ColorComponents == 1 ? "DeviceGray" : "DeviceRGB")}", pixels.IccProfile);
            if (pixels.Alpha is not null) CompressedStream(id + 5, id + 6, $"{geometry} /ColorSpace /DeviceGray", pixels.Alpha);
        }
        var xref = output.Position;
        Write($"xref\n0 {offsets.Length}\n0000000000 65535 f \n");
        for (var i = 1; i < offsets.Length; i++)
        {
            if (offsets[i] == 0) throw new InvalidDataException("Incomplete PDF object table.");
            Write(offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }
        Write($"trailer\n<< /Size {offsets.Length} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        token.ThrowIfCancellationRequested();
        return new(output.ToArray(), samples.MoveToImmutable());

        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));
        void Begin(int id) { offsets[id] = output.Position; Write($"{id} 0 obj\n"); }
        void Object(int id, string text) { Begin(id); Write(text + "\nendobj\n"); }
        void RawStream(int id, string dictionary, byte[] bytes)
        {
            Begin(id); Write($"<< {dictionary} /Length {bytes.Length} >>\nstream\n");
            output.Write(bytes); Write("\nendstream\nendobj\n");
        }
        void CompressedStream(int id, int lengthId, string dictionary, byte[] bytes)
        {
            Begin(id); Write($"<< {dictionary} /Filter /FlateDecode /Length {lengthId} 0 R >>\nstream\n");
            var start = output.Position;
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
                for (var offset = 0; offset < bytes.Length; offset += 65536)
                {
                    token.ThrowIfCancellationRequested();
                    zlib.Write(bytes.AsSpan(offset, Math.Min(65536, bytes.Length - offset)));
                }
            var length = output.Position - start;
            Write("\nendstream\nendobj\n");
            Object(lengthId, length.ToString(CultureInfo.InvariantCulture));
        }
    }

    private sealed class BoundedPdfStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            Check(count); base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Check(buffer.Length); base.Write(buffer);
        }
        public override void WriteByte(byte value)
        {
            Check(1); base.WriteByte(value);
        }
        private void Check(int count)
        {
            if (count < 0 || Position + count > ImagePdfPlan.MaximumOutputBytes)
                throw new ImageFailureException(ImageFailure.ResourceLimit);
        }
    }
}
