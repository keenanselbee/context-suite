using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

namespace ContextSuite.Core.Analysis;

public sealed partial record WordBodyFontInspection
{
    private static WordBodyFontInspection InspectBody(XElement body, XElement? styles, XElement? theme, CancellationToken token)
    {
        if (styles is not null && styles.Name != W + "styles" ||
            theme is not null && theme.Name != XName.Get("theme", Drawing))
            throw new InvalidDataException("Unexpected font dependency root.");
        var map = new Dictionary<string, XElement>(StringComparer.Ordinal);
        string? defaultParagraph = null;
        foreach (var style in styles?.Elements(W + "style") ?? [])
        {
            var id = (string?)style.Attribute(W + "styleId");
            if (string.IsNullOrWhiteSpace(id) || id.Length > 253 || !map.TryAdd(id, style) || map.Count > 4096)
                throw new InvalidDataException("Style limit or ambiguous style ID.");
            if ((string?)style.Attribute(W + "type") == "paragraph" && (string?)style.Attribute(W + "default") is "1" or "true" or "on")
            {
                if (defaultParagraph is not null) throw new InvalidDataException("Ambiguous default paragraph style.");
                defaultParagraph = id;
            }
        }
        var defaults = One(One(One(styles, "docDefaults"), "rPrDefault"), "rPr");
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var issues = new SortedSet<string>(StringComparer.Ordinal);
        var runs = 0;
        var resolved = 0;
        // Paragraph-mark revisions can merge following paragraphs and alter their
        // inherited formatting. Do not pretend that independent paragraphs suffice.
        var paragraphRevisions = body.Descendants(W + "pPr").Where(p => !Discarded(p))
            .Elements(W + "rPr").Elements().Any(e =>
                e.Name == W + "del" || e.Name == W + "ins" || e.Name == W + "moveFrom" || e.Name == W + "moveTo");
        if (paragraphRevisions) issues.Add("paragraph-mark-revisions");
        foreach (var element in body.Descendants())
        {
            token.ThrowIfCancellationRequested();
            if (Discarded(element)) continue;
            if (element.Name.NamespaceName != Word || element.Name.LocalName is
                "drawing" or "pict" or "object" or "altChunk" or "subDoc" or "sym" or "fldChar" or "fldSimple" or
                "footnoteReference" or "endnoteReference" or "headerReference" or "footerReference" or "numPr" or "tbl")
                issues.Add("additional-content");
        }
        foreach (var run in body.Descendants(W + "r"))
        {
            token.ThrowIfCancellationRequested();
            if (Discarded(run)) continue;
            var texts = run.Elements(W + "t").ToArray();
            if (texts.Any(text => text.HasElements)) throw new InvalidDataException("Text must be a leaf element.");
            if (!texts.Any(text => text.Value.Length > 0)) continue;
            if (++runs > 8192) throw new InvalidDataException("Text run limit.");
            if (paragraphRevisions || run.Ancestors().TakeWhile(e => e != body).Any(e =>
                e.Name.NamespaceName != Word || e.Name.LocalName is not ("p" or "ins" or "moveTo" or "hyperlink")))
            { issues.Add("unsupported-text-context"); continue; }
            var paragraph = run.Ancestors(W + "p").FirstOrDefault();
            if (paragraph is null || paragraph.Descendants(W + "fldChar").Any(e => !Discarded(e)) ||
                paragraph.Descendants(W + "fldSimple").Any(e => !Discarded(e)))
            { issues.Add("fields-or-missing-paragraph"); continue; }
            var properties = One(run, "rPr");
            var state = new FontState();
            Apply(defaults, state);
            var pPr = One(paragraph, "pPr");
            var paragraphStyle = Value(One(pPr, "pStyle")) ?? defaultParagraph;
            ApplyStyle(paragraphStyle, "paragraph", state);
            ApplyStyle(Value(One(properties, "rStyle")), "character", state);
            Apply(properties, state);
            if (One(pPr, "numPr") is not null || state.Unsupported)
            { issues.Add("unsupported-formatting"); continue; }
            var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var supported = true;
            foreach (var text in texts)
            foreach (var scalar in text.Value.EnumerateRunes())
            {
                token.ThrowIfCancellationRequested();
                // These ranges have an unambiguous Latin slot without East Asian
                // hints. Other scripts need the full locale/charset/shaping rules.
                var slot = state.Complex ? 3 : scalar.Value <= 0x7f ? 0 :
                    state.Hint != "eastAsia" && (scalar.Value is >= 0x80 and <= 0x2ff or >= 0x1e00 and <= 0x1eff or >= 0x2000 and <= 0x2e7f) ? 1 : -1;
                if (slot < 0) { supported = false; issues.Add("script-selection"); continue; }
                var family = Resolve(state.Slots[slot]);
                if (family is null) { supported = false; issues.Add("font-selection-unavailable"); }
                else families.Add(family);
            }
            if (!supported) continue;
            resolved++;
            foreach (var family in families)
            {
                names.Add(family);
                if (names.Count > 64) throw new InvalidDataException("Used family limit.");
            }
        }
        return new(true, runs, resolved, names.Order(StringComparer.Ordinal).ToImmutableArray(), issues.ToImmutableArray(), 0);

        void ApplyStyle(string? id, string kind, FontState state)
        {
            var chain = new List<XElement>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (id is not null)
            {
                token.ThrowIfCancellationRequested();
                if (!seen.Add(id) || chain.Count >= 64 || !map.TryGetValue(id, out var style) ||
                    (string?)style.Attribute(W + "type") != kind)
                { state.Unsupported = true; return; }
                chain.Add(style);
                id = Value(One(style, "basedOn"));
            }
            foreach (var style in chain.AsEnumerable().Reverse())
            {
                if (One(One(style, "pPr"), "numPr") is not null) state.Unsupported = true;
                Apply(One(style, "rPr"), state);
            }
        }

        string? Resolve(FontSelection? selection)
        {
            if (selection is null) return null; // Application defaults are not stored font evidence.
            if (!selection.Theme) return selection.Value;
            var kind = selection.Value switch { "majorAscii" or "majorHAnsi" => "majorFont", "minorAscii" or "minorHAnsi" => "minorFont", _ => null };
            if (kind is null || theme is null) return null;
            XNamespace a = Drawing;
            var latin = theme.Elements(a + "themeElements").Elements(a + "fontScheme").Elements(a + kind).Elements(a + "latin").ToArray();
            return latin.Length == 1 ? Family((string?)latin[0].Attribute("typeface")) : null;
        }
    }

    private static bool Discarded(XElement element) => element.AncestorsAndSelf().Any(e => e.Name.NamespaceName == Word &&
        e.Name.LocalName is "del" or "moveFrom" or "rPrChange" or "pPrChange" or "sectPrChange" or "tblPrChange" or "trPrChange" or "tcPrChange");

    private static void Apply(XElement? properties, FontState state)
    {
        if (properties is null) return;
        if (properties.Elements().Any(e => e.Name.NamespaceName != Word) ||
            properties.Elements().Any(e => e.Name.LocalName is "vanish" or "webHidden" or "specVanish"))
            state.Unsupported = true;
        var fonts = One(properties, "rFonts");
        var slots = new[] { ("ascii", "asciiTheme"), ("hAnsi", "hAnsiTheme"), ("eastAsia", "eastAsiaTheme"), ("cs", "cstheme") };
        for (var index = 0; index < slots.Length; index++)
        {
            var literal = (string?)fonts?.Attribute(W + slots[index].Item1);
            var theme = (string?)fonts?.Attribute(W + slots[index].Item2);
            // Each new layer replaces the prior literal/theme pair for that slot.
            // On one rFonts element the theme attribute takes precedence.
            if (theme is not null) state.Slots[index] = new(Family(theme) ?? throw new InvalidDataException("Empty theme selection."), true);
            else if (literal is not null) state.Slots[index] = new(Family(literal) ?? throw new InvalidDataException("Empty family selection."), false);
        }
        if ((string?)fonts?.Attribute(W + "hint") is { } hint)
        {
            if (hint is not ("default" or "eastAsia" or "cs")) state.Unsupported = true;
            state.Hint = hint;
        }
        foreach (var name in new[] { "cs", "rtl" })
        {
            var element = One(properties, name);
            if (element is null) continue;
            var value = Value(element);
            if (value is not (null or "1" or "true" or "on" or "0" or "false" or "off")) state.Unsupported = true;
            var enabled = value is null or "1" or "true" or "on";
            if (name == "cs") state.Cs = enabled; else state.Rtl = enabled;
        }
    }

    private static string? Family(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length > 128 || value.Any(char.IsControl) || value != value.Trim())
            throw new InvalidDataException("Unsupported family value.");
        return value;
    }

    private sealed record FontSelection(string Value, bool Theme);
    private sealed class FontState
    {
        public FontSelection?[] Slots { get; } = new FontSelection?[4];
        public string? Hint { get; set; }
        public bool Cs { get; set; }
        public bool Rtl { get; set; }
        public bool Complex => Cs || Rtl;
        public bool Unsupported { get; set; }
    }
}
