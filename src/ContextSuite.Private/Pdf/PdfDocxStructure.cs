namespace ContextSuite.Private.Pdf;

internal static class PdfDocxStructure
{
    public static IReadOnlyList<DocxPage> Apply(IReadOnlyList<DocxPage> pages)
    {
        var paragraphs = pages.SelectMany(page => page.Blocks).Where(block => block.Paragraph is not null).ToArray();
        var bodySize = paragraphs.SelectMany(block => block.Paragraph!.Runs)
            .GroupBy(run => Math.Round(run.Size, 1)).OrderByDescending(group => group.Sum(run => run.Text.Length))
            .Select(group => group.Key).FirstOrDefault(11);
        var headings = paragraphs.Where(block => IsHeading(block.Paragraph!, bodySize))
            .Select(block => block.Paragraph!.Runs.Max(run => run.Size)).Where(size => size < bodySize * 1.65)
            .Distinct().OrderDescending().ToArray();
        return pages.Select((page, pageIndex) => page with
        {
            Blocks = page.Blocks.Select(block =>
            {
                if (block.Paragraph is not { } paragraph) return block;
                var text = Text(paragraph);
                // Repeated bottom-margin text with an isolated occurrence is
                // strong evidence of a footer. Apply that evidence to matching
                // pages even when a form's final rule is close to the footer.
                var footer = text.Length is > 0 and <= 160 && block.Bounds.Top < page.Height * 0.1 &&
                    pages.Where((_, otherIndex) => otherIndex != pageIndex).Any(other =>
                        Math.Abs(other.Width - page.Width) < 1 && Math.Abs(other.Height - page.Height) < 1 &&
                        other.Blocks.Any(candidate => candidate.Paragraph is { } repeated && Text(repeated) == text &&
                            Math.Abs(candidate.Bounds.Left - block.Bounds.Left) < 2 &&
                            Math.Abs(candidate.Bounds.Bottom - block.Bounds.Bottom) < 2 &&
                            repeated.Runs.Select(run => (run.Font, run.Size, run.Italic)).SequenceEqual(
                                paragraph.Runs.Select(run => (run.Font, run.Size, run.Italic))) &&
                            (IsIsolated(page, block) || IsIsolated(other, candidate))));
                var style = "Normal";
                if (footer) style = "Footer";
                else if (IsHeading(paragraph, bodySize))
                {
                    var size = paragraph.Runs.Max(run => run.Size);
                    style = size >= bodySize * 1.65 && pageIndex == 0 && block.Bounds.Top > page.Height * 0.75
                        ? "Title" : "Heading" + Math.Clamp(Array.IndexOf(headings, size) + 1, 1, 3);
                }
                return block with { Paragraph = paragraph with { Style = style, IsFooter = footer } };
            }).ToArray()
        }).ToArray();
    }

    private static bool IsHeading(DocxParagraph paragraph, double bodySize)
    {
        var runs = paragraph.Runs.Where(run => !string.IsNullOrWhiteSpace(run.Text)).ToArray();
        return runs.Length > 0 && Text(paragraph).Length <= 160 &&
            runs.All(run => run.Size > bodySize + 0.5) &&
            (runs.All(run => run.Bold) || runs.All(run => run.Size >= bodySize * 1.65));
    }

    private static bool IsIsolated(DocxPage page, DocxBlock block) => !page.Blocks.Any(other =>
        other != block && other.Rule is null && other.Bounds.Bottom >= block.Bounds.Top &&
        other.Bounds.Bottom - block.Bounds.Top < 24);

    private static string Text(DocxParagraph paragraph) => string.Concat(paragraph.Runs.Select(run => run.Text));
}
