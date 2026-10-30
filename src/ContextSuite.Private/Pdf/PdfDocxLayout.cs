using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;

namespace ContextSuite.Private.Pdf;

internal sealed record DocxRun(string Text, string Font, double Size, bool Bold, bool Italic, string Color);
internal sealed record DocxParagraph(IReadOnlyList<DocxRun> Runs, double Baseline = 0, double Leading = 0,
    double LeftInset = 0, double TopInset = 0, string Style = "Normal", bool IsFooter = false);
internal sealed record DocxTable(double[] Widths, IReadOnlyList<IReadOnlyList<DocxParagraph>> Rows, double[] Heights);
internal sealed record DocxPicture(byte[] Bytes, string Extension, double Width, double Height);
internal sealed record DocxRule(double Weight, string Color, string SourceText = "");
internal sealed record DocxBlock(PdfRectangle Bounds, DocxParagraph? Paragraph = null, DocxTable? Table = null, DocxPicture? Picture = null,
    bool NeedsRaster = false, string RasterText = "", int RasterImages = 0, DocxRule? Rule = null);
internal sealed record DocxPage(double Width, double Height, IReadOnlyList<DocxBlock> Blocks);

internal static class PdfDocxLayout
{
    public static DocxPage Extract(Page page, UglyToad.PdfPig.PdfDocument document, CancellationToken token)
    {
        if (page.Letters.Count > 30_000 || page.Paths.Count > 20_000 ||
            page.Width is < 72 or > 1584 || page.Height is < 72 or > 1584)
            throw new ImageFailureException(ImageFailure.ResourceLimit);
        var stamps = StaticStamps(page, document);
        var words = ExtractWords(page, token);
        if (words.Count > 10_000) throw new ImageFailureException(ImageFailure.ResourceLimit);
        var blocks = new List<DocxBlock>();
        ExtractTables(page, words, blocks, token);
        var rasterBounds = new List<PdfRectangle>();
        var images = page.GetImages().ToArray();
        // Keep complex painted regions as figures. Ordinary table rules remain
        // editable borders; labels overlapping artwork stay in the same figure.
        foreach (var path in page.Paths.Where(path => !path.IsClipping && (path.IsStroked || path.IsFilled)))
        foreach (var subpath in path)
        {
            if (subpath.GetBoundingRectangle() is not { } box) continue;
            if (!path.IsFilled && blocks.Any(block => block.Table is not null &&
                box.Left >= block.Bounds.Left - 2 && box.Right <= block.Bounds.Right + 2 &&
                box.Bottom >= block.Bounds.Bottom - 2 && box.Top <= block.Bounds.Top + 2 &&
                (box.Width <= 1 || box.Height <= 1 || subpath.IsDrawnAsRectangle))) continue;
            // Only a single, solid horizontal stroke is a rule. Dashed,
            // compound and curved paths retain the existing figure fallback.
            if (!path.IsFilled && path.IsStroked && subpath.Commands.Count == 2 &&
                subpath.Commands[0] is PdfSubpath.Move && subpath.Commands[1] is PdfSubpath.Line &&
                box.Height < 0.01 && box.Width >= 12 && path.LineWidth is > 0 and <= 4 &&
                (path.LineDashPattern is null || path.LineDashPattern.Value.Array.Count == 0) &&
                (int)path.LineCapStyle == 0)
            {
                var rgb = path.StrokeColor?.ToRGBValues() ?? (r: 0d, g: 0d, b: 0d);
                blocks.Add(new(box, Rule: new(path.LineWidth, $"{Channel(rgb.r):X2}{Channel(rgb.g):X2}{Channel(rgb.b):X2}")));
            }
            else rasterBounds.Add(Expand(box, page, 2));
        }
        foreach (var image in images)
            if (image.MaskImage is not null || image.IsImageMask || Math.Abs(image.BoundingBox.Rotation) > 0.01)
                rasterBounds.Add(Expand(AxisAligned(image.BoundingBox), page, 1));
        AddRasterRegions(page, rasterBounds, words, blocks, images, token);
        // Answer rules are often underscore text on a different baseline from
        // the answers. Segmenters must not join those two overlapping layers.
        var rules = words.Where(word => word.Text.Trim().Length >= 3 && word.Text.Trim().All(character => character == '_')).ToArray();
        foreach (var rule in rules)
        {
            var paragraph = Paragraph([rule]);
            // Word bounds also include the text baseline. Use visible glyph
            // bounds so an underscore's descent does not become stroke weight.
            var box = new PdfRectangle(rule.Letters.Min(letter => letter.BoundingBox.Left),
                rule.Letters.Min(letter => letter.BoundingBox.Bottom), rule.Letters.Max(letter => letter.BoundingBox.Right),
                rule.Letters.Max(letter => letter.BoundingBox.Top));
            if (box.Width >= 30 && box.Height is > 0 and <= 3 && paragraph.Runs.Select(run => run.Color).Distinct().Count() == 1)
                blocks.Add(new(new PdfRectangle(box.Left, box.Centroid.Y, box.Right, box.Centroid.Y),
                    Rule: new(box.Height, paragraph.Runs[0].Color, rule.Text)));
            else blocks.Add(new(box, Paragraph: paragraph));
        }
        words.RemoveAll(rules.Contains);
        if (words.Count > 0)
        {
            var textBlocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
            foreach (var block in UnsupervisedReadingOrderDetector.Instance.Get(textBlocks))
            {
                token.ThrowIfCancellationRequested();
                var pending = new List<TextLine>();
                foreach (var line in block.TextLines.OrderByDescending(line => line.BoundingBox.Top))
                {
                    if (pending.Count > 0 && !ContinuesParagraph(pending[^1], line))
                    { AddParagraph(pending, blocks); pending.Clear(); }
                    pending.Add(line);
                }
                AddParagraph(pending, blocks);
            }
        }
        foreach (var image in images)
        {
            token.ThrowIfCancellationRequested();
            if (blocks.Any(block => block.NeedsRaster && Overlaps(block.Bounds, AxisAligned(image.BoundingBox)))) continue;
            if (image.WidthInSamples <= 0 || image.HeightInSamples <= 0 ||
                (long)image.WidthInSamples * image.HeightInSamples > 16_000_000)
                throw new ImageFailureException(ImageFailure.ResourceLimit);
            // Embedded masks require compositing; do not silently discard them.
            if (image.MaskImage is not null || image.IsImageMask) throw new ImageFailureException(ImageFailure.UnsupportedInput);
            byte[] bytes;
            var extension = "png";
            if (!image.TryGetPng(out bytes!))
            {
                var raw = image.RawMemory;
                if (raw.Length < 3 || raw.Span[0] != 0xff || raw.Span[1] != 0xd8 || raw.Span[2] != 0xff)
                    throw new ImageFailureException(ImageFailure.UnsupportedInput);
                bytes = raw.ToArray(); extension = "jpg";
            }
            if (bytes.Length > 16 * 1024 * 1024) throw new ImageFailureException(ImageFailure.ResourceLimit);
            var bounds = image.BoundingBox;
            if (!double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) || bounds.Width <= 0 || bounds.Height <= 0 ||
                Math.Abs(bounds.Rotation) > 0.01)
                throw new ImageFailureException(ImageFailure.UnsupportedInput);
            blocks.Add(new(bounds, Picture: new(bytes, extension, bounds.Width, bounds.Height)));
        }
        // Stamp crops are overlays, not figure regions: growing them through a
        // checkbox would otherwise consume its entire editable table.
        blocks.AddRange(stamps.Select(stamp => new DocxBlock(Expand(stamp.Rectangle, page, 1), NeedsRaster: true)));
        // Recursive column order: text and pictures follow a clear vertical gutter
        // before top-to-bottom order. Spanning headings split the page into bands.
        return new(page.Width, page.Height, Order(blocks));
    }

    private static List<Word> ExtractWords(Page page, CancellationToken token)
    {
        // PDF text layers can overlap: a completed form's answer and its rule
        // are separate text objects. Nearest-neighbour clustering can weave the
        // underscore glyphs into the answer. Respect baselines and font runs
        // before finding word boundaries instead.
        var words = new List<Word>();
        var ruleLetters = new HashSet<Letter>();
        var pendingRule = new List<Letter>();
        foreach (var letter in page.Letters)
        {
            if (pendingRule.Count > 0 && (letter.Value != "_" ||
                letter.TextSequence != pendingRule[0].TextSequence))
                FlushRule();
            if (letter.Value == "_") pendingRule.Add(letter);
        }
        FlushRule();
        var lines = new List<List<Letter>>();
        foreach (var font in page.Letters.Where(letter => !ruleLetters.Contains(letter)).GroupBy(letter => (letter.FontName, letter.PointSize)))
        {
            token.ThrowIfCancellationRequested();
            List<Letter>? line = null;
            foreach (var letter in font.OrderByDescending(letter => letter.StartBaseLine.Y))
            {
                if (line is null || Math.Abs(line[0].StartBaseLine.Y - letter.StartBaseLine.Y) > 0.75)
                { line = [letter]; lines.Add(line); }
                else line.Add(letter);
            }
        }
        foreach (var line in lines)
        {
            var pending = new List<Letter>();
            foreach (var letter in line.OrderBy(letter => letter.StartBaseLine.X))
            {
                var whitespace = string.IsNullOrWhiteSpace(letter.Value);
                if (pending.Count > 0 && (whitespace || letter.StartBaseLine.X - pending[^1].EndBaseLine.X > Math.Max(0.8, letter.PointSize * 0.2)))
                {
                    words.Add(new(pending.ToArray()));
                    pending.Clear();
                }
                if (!whitespace) pending.Add(letter);
            }
            if (pending.Count > 0) words.Add(new(pending.ToArray()));
        }
        return words;

        void FlushRule()
        {
            if (pendingRule.Count >= 6 && pendingRule[^1].EndBaseLine.X - pendingRule[0].StartBaseLine.X > 30)
            { words.Add(new(pendingRule.ToArray())); ruleLetters.UnionWith(pendingRule); }
            pendingRule.Clear();
        }
    }

    private static Annotation[] StaticStamps(Page page, UglyToad.PdfPig.PdfDocument document)
    {
        var annotations = page.GetAnnotations().ToArray();
        if (annotations.Length > 256) throw new ImageFailureException(ImageFailure.ResourceLimit);
        var stamps = annotations.Where(a => a.Type == AnnotationType.Stamp).ToArray();
        foreach (var annotation in annotations)
        {
            var dictionary = annotation.AnnotationDictionary;
            if (!string.IsNullOrEmpty(annotation.Content) || annotation.Action is not null || annotation.InReplyTo is not null ||
                dictionary.Data.ContainsKey("A") || dictionary.Data.ContainsKey("AA") || dictionary.Data.ContainsKey("IRT") ||
                dictionary.Data.ContainsKey("RC") || dictionary.Data.ContainsKey("OC") ||
                annotation.HasDownAppearance || annotation.HasRollOverAppearance)
                throw new ImageFailureException(ImageFailure.UnsupportedInput);
            if (annotation.Type == AnnotationType.Popup)
            {
                // Only an empty, closed popup owned by an admitted stamp can be
                // omitted. Comments, replies and other interactive data refuse.
                if (annotation.HasNormalAppearance ||
                    dictionary.TryGet(NameToken.Create("Open"), out var open) && open is not BooleanToken { Data: false } ||
                    !dictionary.TryGet(NameToken.Create("Parent"), out var parent) ||
                    !stamps.Any(stamp => parent is IndirectReferenceToken reference &&
                        document.Structure.GetObject(reference.Data).Data.Equals(stamp.AnnotationDictionary) &&
                        stamp.AnnotationDictionary.TryGet(NameToken.Create("Popup"), out var popup) && popup is IndirectReferenceToken popupReference &&
                        document.Structure.GetObject(popupReference.Data).Data.Equals(dictionary)))
                    throw new ImageFailureException(ImageFailure.UnsupportedInput);
                continue;
            }
            var box = annotation.Rectangle;
            if (dictionary.TryGet(NameToken.Create("Popup"), out var ownedPopup) &&
                (ownedPopup is not IndirectReferenceToken popupReference || !annotations.Any(candidate =>
                    candidate.Type == AnnotationType.Popup && document.Structure.GetObject(popupReference.Data).Data.Equals(candidate.AnnotationDictionary))))
                throw new ImageFailureException(ImageFailure.UnsupportedInput);
            if (annotation.Type != AnnotationType.Stamp || !annotation.HasNormalAppearance ||
                (annotation.Flags & ~(AnnotationFlags.Print | AnnotationFlags.ReadOnly | AnnotationFlags.Locked | AnnotationFlags.LockedContents)) != 0 ||
                !double.IsFinite(box.Left) || !double.IsFinite(box.Bottom) || !double.IsFinite(box.Width) || !double.IsFinite(box.Height) ||
                box.Width <= 0 || box.Height <= 0 || box.Left < 0 || box.Bottom < 0 || box.Right > page.Width || box.Top > page.Height)
                throw new ImageFailureException(ImageFailure.UnsupportedInput);
        }
        return stamps;
    }

    private static void AddRasterRegions(Page page, List<PdfRectangle> regions, List<Word> words,
        List<DocxBlock> blocks, IPdfImage[] images, CancellationToken token)
    {
        if (regions.Count > 512) throw new ImageFailureException(ImageFailure.ResourceLimit);
        // Grow through intersecting content before merging, so a figure cannot
        // cut a word/image/table in half or duplicate it as editable content.
        var changed = true;
        while (changed)
        {
            token.ThrowIfCancellationRequested(); changed = false;
            for (var index = 0; index < regions.Count; index++)
            {
                var box = regions[index];
                foreach (var content in words.Select(word => word.BoundingBox)
                    .Concat(images.Select(image => AxisAligned(image.BoundingBox))).Concat(blocks.Select(block => block.Bounds)))
                    if (Overlaps(box, content)) box = Union(box, content);
                box = Expand(box, page, 0);
                if (box != regions[index]) { regions[index] = box; changed = true; }
                for (var other = regions.Count - 1; other > index; other--)
                    if (Overlaps(box, regions[other]))
                    { regions[index] = box = Union(box, regions[other]); regions.RemoveAt(other); changed = true; }
            }
        }
        foreach (var box in regions)
        {
            var included = words.Where(word => Overlaps(box, word.BoundingBox)).ToHashSet();
            var tables = blocks.Where(block => block.Table is not null && Overlaps(box, block.Bounds)).ToArray();
            var rules = blocks.Where(block => block.Rule is not null && Overlaps(box, block.Bounds)).ToArray();
            var text = string.Concat(included.SelectMany(word => word.Letters).Select(letter => letter.Value)) +
                string.Concat(tables.SelectMany(block => block.Table!.Rows).SelectMany(row => row).SelectMany(cell => cell.Runs).Select(run => run.Text)) +
                string.Concat(rules.Select(block => block.Rule!.SourceText));
            words.RemoveAll(included.Contains);
            foreach (var table in tables) blocks.Remove(table);
            foreach (var rule in rules) blocks.Remove(rule);
            blocks.Add(new(box, NeedsRaster: true, RasterText: text,
                RasterImages: images.Count(image => Overlaps(box, AxisAligned(image.BoundingBox)))));
        }
    }

    private static bool Overlaps(PdfRectangle a, PdfRectangle b) => a.Left <= b.Right && a.Right >= b.Left && a.Bottom <= b.Top && a.Top >= b.Bottom;
    private static PdfRectangle Union(PdfRectangle a, PdfRectangle b) => new(Math.Min(a.Left, b.Left), Math.Min(a.Bottom, b.Bottom), Math.Max(a.Right, b.Right), Math.Max(a.Top, b.Top));
    private static PdfRectangle AxisAligned(PdfRectangle box)
    {
        var corners = new[] { box.TopLeft, box.TopRight, box.BottomLeft, box.BottomRight };
        return new(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Max(p => p.X), corners.Max(p => p.Y));
    }
    private static PdfRectangle Expand(PdfRectangle box, Page page, double padding) =>
        new(Math.Clamp(box.Left - padding, 0, page.Width), Math.Clamp(box.Bottom - padding, 0, page.Height),
            Math.Clamp(box.Right + padding, 0, page.Width), Math.Clamp(box.Top + padding, 0, page.Height));

    private static void AddParagraph(List<TextLine> lines, List<DocxBlock> blocks)
    {
        if (lines.Count == 0) return;
        var bounds = new PdfRectangle(lines.Min(line => line.BoundingBox.Left), lines.Min(line => line.BoundingBox.Bottom),
            lines.Max(line => line.BoundingBox.Right), lines.Max(line => line.BoundingBox.Top));
        var paragraph = Paragraph(lines.SelectMany(line => line.Words));
        var baselines = lines.Select(line => line.Words.SelectMany(word => word.Letters).Average(letter => letter.StartBaseLine.Y)).ToArray();
        blocks.Add(new(bounds, Paragraph: paragraph with { Baseline = baselines[0],
            Leading = baselines.Length > 1 ? baselines.Zip(baselines.Skip(1), (a, b) => a - b).Average() : 0 }));
    }

    private static bool ContinuesParagraph(TextLine previous, TextLine next)
    {
        var a = Paragraph(previous.Words); var b = Paragraph(next.Words);
        var size = a.Runs.Max(run => run.Size);
        var gap = a.Baseline - b.Baseline;
        var text = string.Concat(b.Runs.Select(run => run.Text)).TrimStart();
        return Math.Abs(previous.BoundingBox.Left - next.BoundingBox.Left) <= 1.5 &&
            gap >= size * 0.95 && gap <= size * 1.4 &&
            Math.Abs(size - b.Runs.Max(run => run.Size)) <= 0.25 &&
            previous.BoundingBox.Width > size * 20 &&
            !a.Runs.All(run => run.Bold) && !b.Runs.All(run => run.Bold) &&
            !text.StartsWith('\u25a1') && !text.StartsWith('\u2610') &&
            !text.StartsWith('\u2022') && !text.Contains(": ", StringComparison.Ordinal) &&
            !string.Concat(a.Runs.Select(run => run.Text)).TrimEnd().EndsWith(':');
    }

    private static IReadOnlyList<DocxBlock> Order(List<DocxBlock> blocks, int depth = 0)
    {
        if (blocks.Count < 2 || depth >= 12) return blocks.OrderByDescending(b => b.Bounds.Top).ThenBy(b => b.Bounds.Left).ToArray();
        var sorted = blocks.OrderBy(b => b.Bounds.Left).ToArray();
        var right = sorted[0].Bounds.Right;
        for (var index = 1; index < sorted.Length; index++)
        {
            if (sorted[index].Bounds.Left - right > 18)
                return Order(sorted.Take(index).ToList(), depth + 1).Concat(Order(sorted.Skip(index).ToList(), depth + 1)).ToArray();
            right = Math.Max(right, sorted[index].Bounds.Right);
        }
        var topDown = blocks.OrderByDescending(b => b.Bounds.Top).ToArray();
        var bottom = topDown[0].Bounds.Bottom;
        for (var index = 1; index < topDown.Length; index++)
        {
            if (bottom - topDown[index].Bounds.Top > 5)
                return Order(topDown.Take(index).ToList(), depth + 1).Concat(Order(topDown.Skip(index).ToList(), depth + 1)).ToArray();
            bottom = Math.Min(bottom, topDown[index].Bounds.Bottom);
        }
        return topDown;
    }

    private static DocxParagraph Paragraph(IEnumerable<Word> words)
    {
        var runs = new List<DocxRun>();
        foreach (var word in words)
        {
            foreach (var group in word.Letters.Where(letter => !string.IsNullOrWhiteSpace(letter.Value))
                .OrderBy(letter => letter.StartBaseLine.X).ToArray().GroupAdjacent()) runs.Add(group);
            if (runs.Count > 0) runs.Add(runs[^1] with { Text = " " });
        }
        if (runs.Count > 0 && runs[^1].Text == " ") runs.RemoveAt(runs.Count - 1);
        var letters = words.SelectMany(word => word.Letters).ToArray();
        return new(runs, letters.Length == 0 ? 0 : letters.Average(letter => letter.StartBaseLine.Y));
    }

    private static IEnumerable<DocxRun> GroupAdjacent(this IReadOnlyList<Letter> letters)
    {
        DocxRun? pending = null;
        foreach (var letter in letters)
        {
            if (letter.RenderingMode != TextRenderingMode.Fill || Math.Abs(letter.BoundingBox.Rotation) > 0.01)
                throw new ImageFailureException(ImageFailure.UnsupportedInput);
            var text = letter.Value;
            if (text.Contains('\uFFFD') || text.Any(character => char.IsControl(character) && !char.IsWhiteSpace(character)))
                throw new ImageFailureException(ImageFailure.UnsupportedInput);
            var font = letter.FontName ?? "Arial";
            var plus = font.IndexOf('+');
            if (plus == 6) font = font[7..];
            if (font.Length > 128) font = "Arial";
            var size = Math.Clamp(letter.PointSize, 4, 144);
            if (!double.IsFinite(size)) throw new ImageFailureException(ImageFailure.UnsupportedInput);
            var rgb = letter.FillColor.ToRGBValues();
            var color = $"{Channel(rgb.r):X2}{Channel(rgb.g):X2}{Channel(rgb.b):X2}";
            var run = new DocxRun(text, font, size, letter.FontDetails.IsBold || font.Contains("Bold", StringComparison.OrdinalIgnoreCase),
                letter.FontDetails.IsItalic || font.Contains("Italic", StringComparison.OrdinalIgnoreCase) || font.Contains("Oblique", StringComparison.OrdinalIgnoreCase), color);
            if (pending is not null && pending.Font == run.Font && pending.Size == run.Size && pending.Bold == run.Bold && pending.Italic == run.Italic && pending.Color == run.Color)
                pending = pending with { Text = pending.Text + text };
            else { if (pending is not null) yield return pending; pending = run; }
        }
        if (pending is not null) yield return pending;
    }

    private static int Channel(double value) => double.IsFinite(value)
        ? (int)Math.Round(Math.Clamp(value, 0, 1) * 255)
        : throw new ImageFailureException(ImageFailure.UnsupportedInput);

    private static void ExtractTables(Page page, List<Word> words, List<DocxBlock> blocks, CancellationToken token)
    {
        var horizontal = new List<PdfRectangle>();
        var vertical = new List<PdfRectangle>();
        foreach (var path in page.Paths.Where(path => path.IsStroked && !path.IsClipping))
        foreach (var subpath in path)
        {
            if (subpath.GetBoundingRectangle() is not { } box) continue;
            if (box.Height <= 1 && box.Width >= 30) horizontal.Add(box);
            else if (box.Width <= 1 && box.Height >= 12) vertical.Add(box);
            else if (subpath.IsDrawnAsRectangle)
            {
                horizontal.Add(new(box.Left, box.Top, box.Right, box.Top));
                horizontal.Add(new(box.Left, box.Bottom, box.Right, box.Bottom));
                vertical.Add(new(box.Left, box.Bottom, box.Left, box.Top));
                vertical.Add(new(box.Right, box.Bottom, box.Right, box.Top));
            }
        }
        if (horizontal.Count > 512 || vertical.Count > 512) return;
        var groups = new List<List<PdfRectangle>>();
        foreach (var line in horizontal)
        {
            var group = groups.FirstOrDefault(candidate => Math.Abs(candidate[0].Left - line.Left) <= 2 && Math.Abs(candidate[0].Right - line.Right) <= 2);
            if (group is null) groups.Add([line]);
            else group.Add(line);
        }
        foreach (var group in groups)
        {
            token.ThrowIfCancellationRequested();
            var ys = group.Select(line => Math.Round(line.Top, 1)).Distinct().OrderDescending().ToArray();
            if (ys.Length < 3 || ys.Length > 101) continue;
            var left = group.First().Left; var right = group.First().Right;
            var xs = vertical.Where(line => line.Bottom <= ys[^1] + 2 && line.Top >= ys[0] - 2 &&
                line.Left >= left - 2 && line.Left <= right + 2).Select(line => Math.Round(line.Left, 1)).Distinct().Order().ToArray();
            if (xs.Length < 3 || xs.Length > 17 || Math.Abs(xs[0] - left) > 2 || Math.Abs(xs[^1] - right) > 2) continue;
            var bounds = new PdfRectangle(xs[0], ys[^1], xs[^1], ys[0]);
            var cells = new List<IReadOnlyList<DocxParagraph>>();
            var included = new HashSet<Word>();
            for (var row = 0; row < ys.Length - 1; row++)
            {
                var columns = new List<DocxParagraph>();
                for (var col = 0; col < xs.Length - 1; col++)
                {
                    var selected = words.Where(word => word.BoundingBox.Centroid.X >= xs[col] && word.BoundingBox.Centroid.X < xs[col + 1] &&
                        word.BoundingBox.Centroid.Y <= ys[row] && word.BoundingBox.Centroid.Y > ys[row + 1])
                        .OrderByDescending(word => Math.Round(word.BoundingBox.Bottom / 3)).ThenBy(word => word.BoundingBox.Left).ToArray();
                    foreach (var word in selected) included.Add(word);
                    var lines = selected.GroupBy(word => Math.Round(word.Letters.Average(letter => letter.StartBaseLine.Y) / 2))
                        .OrderByDescending(line => line.Key).Select(line => line.OrderBy(word => word.BoundingBox.Left).ToArray()).ToArray();
                    var runs = new List<DocxRun>();
                    foreach (var line in lines)
                    {
                        if (runs.Count > 0) runs.Add(runs[^1] with { Text = "\n" });
                        runs.AddRange(Paragraph(line).Runs);
                    }
                    var baselines = lines.Select(line => line.SelectMany(word => word.Letters).Average(letter => letter.StartBaseLine.Y)).ToArray();
                    var size = runs.Count == 0 ? 10 : runs.Max(run => run.Size);
                    columns.Add(new(runs, Leading: baselines.Length > 1 ? baselines.Zip(baselines.Skip(1), (a, b) => a - b).Min() : size * 1.15,
                        LeftInset: selected.Length == 0 ? 3 : Math.Max(0, selected.Min(word => word.BoundingBox.Left) - xs[col]),
                        TopInset: baselines.Length == 0 ? 0 : Math.Max(0, ys[row] - baselines[0] - size * 0.96)));
                }
                cells.Add(columns);
            }
            words.RemoveAll(included.Contains);
            blocks.Add(new(bounds, Table: new(xs.Zip(xs.Skip(1), (a, b) => b - a).ToArray(), cells,
                ys.Zip(ys.Skip(1), (a, b) => a - b).ToArray())));
        }
    }
}
