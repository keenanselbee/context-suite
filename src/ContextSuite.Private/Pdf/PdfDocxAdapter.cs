using System.Security.Cryptography;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;
using ContextSuite.Private.IO;
using UglyToad.PdfPig;
using ImageMagick;

namespace ContextSuite.Private.Pdf;

// Extraction is local and read-only. The app owns naming, access and publication;
// the existing single-worker job supplies the process memory/time boundary.
public sealed class PdfDocxAdapter(PdfRasterAdapter? renderer = null)
{
    public async Task<PdfDocxSource> ProbeAsync(PdfFileProbe request, CancellationToken token)
    {
        request.Validate();
        using var input = MediaFiles.OpenRead(request.Path);
        var hash = await Hash(input, token);
        using var pdf = Open(input);
        var facts = Inspect(pdf, token);
        if (await Hash(input, token) != hash) throw new ImageFailureException(ImageFailure.SourceChanged);
        var source = new PdfDocxSource(request.ItemId, request.Path, hash, input.Length, pdf.NumberOfPages,
            facts.Characters, facts.Images, facts.NeedsOcr);
        source.Validate();
        return source;
    }

    public async Task<PdfDocxResult> ConvertAsync(PdfDocxWork work, CancellationToken token)
    {
        work.Validate();
        using var input = MediaFiles.OpenRead(work.Source.Path);
        if (input.Length != work.Source.FileBytes || await Hash(input, token) != work.Source.Sha256)
            throw new ImageFailureException(ImageFailure.SourceChanged);
        using var pdf = Open(input);
        var facts = Inspect(pdf, token);
        if (pdf.NumberOfPages != work.Source.PageCount || facts.Characters != work.Source.TextCharacters ||
            facts.Images != work.Source.ImageCount || facts.NeedsOcr)
            throw new ImageFailureException(ImageFailure.SourceChanged);
        var pages = new List<DocxPage>();
        var sourceCharacters = new Dictionary<char, int>();
        foreach (var page in pdf.GetPages())
        {
            token.ThrowIfCancellationRequested();
            Count(page.Letters.SelectMany(letter => letter.Value), sourceCharacters);
            pages.Add(PdfDocxLayout.Extract(page, pdf, token));
        }
        var outputCharacters = new Dictionary<char, int>();
        foreach (var block in pages.SelectMany(page => page.Blocks))
        {
            Count(block.RasterText, outputCharacters);
            if (block.Rule is { } rule) Count(rule.SourceText, outputCharacters);
            if (block.Paragraph is { } paragraph) Count(paragraph.Runs.SelectMany(run => run.Text), outputCharacters);
            if (block.Table is { } table)
                Count(table.Rows.SelectMany(row => row).SelectMany(cell => cell.Runs).SelectMany(run => run.Text), outputCharacters);
        }
        if (sourceCharacters.Count != outputCharacters.Count || sourceCharacters.Any(pair => outputCharacters.GetValueOrDefault(pair.Key) != pair.Value))
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        var figures = pages.Sum(page => page.Blocks.Count(block => block.NeedsRaster));
        var figureText = pages.Sum(page => page.Blocks.Sum(block => block.RasterText.Count(character => !char.IsWhiteSpace(character))));
        if (figureText >= facts.Characters) throw new ImageFailureException(ImageFailure.UnsupportedInput);
        var images = pages.Sum(page => page.Blocks.Sum(block => block.Picture is not null ? 1 : block.RasterImages));
        if (images != facts.Images) throw new ImageFailureException(ImageFailure.ValidationFailed);
        if (figures > 0)
        {
            if (renderer is null) throw new ImageFailureException(ImageFailure.UnsupportedInput);
            var raster = await renderer.ProbeAsync(new(work.Source.ItemId, work.Source.Path), token);
            if (raster.Sha256 != work.Source.Sha256 || raster.Document.Pages.Length != pages.Count)
                throw new ImageFailureException(ImageFailure.SourceChanged);
            for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                var page = pages[pageIndex];
                if (!page.Blocks.Any(block => block.NeedsRaster)) continue;
                // Preserve unpainted paper as transparency in localized figures.
                // Actual white PDF paint remains opaque; color-keying would erase it.
                var rendered = await renderer.RenderAsync(raster, pageIndex, token, transparentBackground: true);
                using var bitmap = new MagickImage(rendered.Bytes);
                var blocks = new List<DocxBlock>();
                foreach (var block in page.Blocks)
                {
                    token.ThrowIfCancellationRequested();
                    if (!block.NeedsRaster) { blocks.Add(block); continue; }
                    var x = (int)Math.Floor(block.Bounds.Left * bitmap.Width / page.Width);
                    var y = (int)Math.Floor((page.Height - block.Bounds.Top) * bitmap.Height / page.Height);
                    var width = Math.Min((int)bitmap.Width, (int)Math.Ceiling(block.Bounds.Right * bitmap.Width / page.Width)) - x;
                    var height = Math.Min((int)bitmap.Height, (int)Math.Ceiling((page.Height - block.Bounds.Bottom) * bitmap.Height / page.Height)) - y;
                    if (width <= 0 || height <= 0) throw new ImageFailureException(ImageFailure.UnsupportedInput);
                    using var crop = bitmap.Clone();
                    crop.Crop(new MagickGeometry(x, y, (uint)width, (uint)height)); crop.ResetPage();
                    // Keep the crop on the same pixel grid as the source page;
                    // stretching it back to fractional bounds shifts small marks.
                    var bounds = new UglyToad.PdfPig.Core.PdfRectangle(x * page.Width / bitmap.Width,
                        page.Height - (y + height) * page.Height / bitmap.Height,
                        (x + width) * page.Width / bitmap.Width, page.Height - y * page.Height / bitmap.Height);
                    blocks.Add(block with { Bounds = bounds, Picture = new(crop.ToByteArray(MagickFormat.Png), "png", bounds.Width, bounds.Height) });
                }
                pages[pageIndex] = page with { Blocks = blocks };
            }
        }
        var bytes = PdfDocxWriter.Create(PdfDocxStructure.Apply(pages), token);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        using var output = new ReservedMediaOutput(work.TemporaryPath, PdfDocxWork.MaximumOutputBytes);
        token.ThrowIfCancellationRequested();
        output.Write(bytes); output.Flush(true);
        output.Position = 0;
        if (output.Length != bytes.Length || Convert.ToHexString(await SHA256.HashDataAsync(output, token)) != hash || await Hash(input, token) != work.Source.Sha256)
            throw new ImageFailureException(ImageFailure.ValidationFailed);
        return new(new(work.OutputId, hash, true), work.Source.Sha256, bytes.Length, pdf.NumberOfPages,
            facts.Characters, images, pages.Sum(page => page.Blocks.Count(block => block.Table is not null)),
            work.Policy, "PdfPig 0.1.16; " + work.Policy, figures, figureText);
    }

    private static PdfDocument Open(FileStream input)
    {
        input.Position = 0;
        var document = PdfDocument.Open(input, new ParsingOptions { UseLenientParsing = false, SkipMissingFonts = false });
        if (document.IsEncrypted || document.NumberOfPages is <= 0 or > PdfDocxSource.MaximumPages)
        {
            document.Dispose();
            throw new ImageFailureException(ImageFailure.UnsupportedInput);
        }
        return document;
    }

    private static (int Characters, int Images, bool NeedsOcr) Inspect(PdfDocument document, CancellationToken token)
    {
        var characters = 0; var images = 0; var needsOcr = false;
        foreach (var page in document.GetPages())
        {
            token.ThrowIfCancellationRequested();
            if (page.Letters.Count > 30_000 || page.Paths.Count > 20_000)
                throw new ImageFailureException(ImageFailure.ResourceLimit);
            var text = page.Letters.Sum(letter => letter.Value.Count(character => !char.IsWhiteSpace(character)));
            var pictures = page.NumberOfImages;
            characters += text; images += pictures;
            needsOcr |= text == 0 && (pictures > 0 || page.Paths.Count > 0);
            if (characters > PdfDocxSource.MaximumCharacters || images > PdfDocxSource.MaximumImages)
                throw new ImageFailureException(ImageFailure.ResourceLimit);
        }
        return (characters, images, needsOcr || characters == 0);
    }

    private static void Count(IEnumerable<char> characters, Dictionary<char, int> counts)
    {
        foreach (var character in characters.Where(character => !char.IsWhiteSpace(character)))
            counts[character] = counts.GetValueOrDefault(character) + 1;
    }

    private static async Task<string> Hash(FileStream input, CancellationToken token)
    {
        if (input.Length is <= 0 or > PdfDocxSource.MaximumSourceBytes) throw new ImageFailureException(ImageFailure.ResourceLimit);
        input.Position = 0;
        return Convert.ToHexString(await SHA256.HashDataAsync(input, token));
    }
}
