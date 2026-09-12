using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Copies of our passive fixture only: reorder slides, change visibility and add literal notes.
internal static class PowerPointSlideFixtures
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly string[] Cases = ["reordered", "hidden-ends", "notes-excluded", "notes-control"];

    public static (string Name, string Filter, int Pages)[] Create(string directory)
    {
        foreach (var key in Cases.Take(3))
        {
            var path = Path.Combine(directory, "PowerPoint slides " + key + ".pptx");
            File.Copy(Path.Combine(directory, "PowerPoint \u00fc.pptx"), path, false);
            using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
            var presentation = Read("ppt/presentation.xml");
            var list = presentation.Root!.Element(P + "sldIdLst")!;
            if (key == "reordered")
            {
                var slides = list.Elements().ToArray();
                list.ReplaceNodes(new[] { slides[2], slides[0], slides[1] });
            }
            Write("ppt/presentation.xml", presentation);
            var types = Read("[Content_Types].xml");
            for (var index = 1; index <= 3; index++)
            {
                var slide = Read($"ppt/slides/slide{index}.xml");
                slide.Root!.SetAttributeValue("show", key == "hidden-ends" && index != 2 ? "0" : "1");
                slide.Descendants(A + "t").Single().Value = $"SLIDE_{index}_MARKER";
                Write($"ppt/slides/slide{index}.xml", slide);
                if (key != "notes-excluded") continue;

                var notes = new XDocument(new XElement(P + "notes", new XAttribute(XNamespace.Xmlns + "p", P),
                    new XAttribute(XNamespace.Xmlns + "a", A), new XElement(slide.Root.Element(P + "cSld")!),
                    new XElement(P + "clrMapOvr", new XElement(A + "masterClrMapping"))));
                notes.Descendants(A + "t").Single().Value = $"NOTE_{index}_MARKER";
                notes.Descendants(P + "sp").Single().Element(P + "nvSpPr")!.Element(P + "nvPr")!
                    .Add(new XElement(P + "ph", new XAttribute("type", "body"), new XAttribute("idx", "1")));
                notes.Descendants(P + "spPr").Single().Element(A + "xfrm")!.Element(A + "ext")!
                    .SetAttributeValue("cx", "5943600");
                Write($"ppt/notesSlides/notesSlide{index}.xml", notes);
                var relationships = Read($"ppt/slides/_rels/slide{index}.xml.rels");
                var ns = relationships.Root!.Name.Namespace;
                relationships.Root.Add(new XElement(ns + "Relationship", new XAttribute("Id", "notes"),
                    new XAttribute("Type", R + "/notesSlide"), new XAttribute("Target", $"../notesSlides/notesSlide{index}.xml")));
                Write($"ppt/slides/_rels/slide{index}.xml.rels", relationships);
                Write($"ppt/notesSlides/_rels/notesSlide{index}.xml.rels", new XDocument(new XElement(ns + "Relationships",
                    new XElement(ns + "Relationship", new XAttribute("Id", "slide"), new XAttribute("Type", R + "/slide"),
                        new XAttribute("Target", $"../slides/slide{index}.xml")))));
                types.Root!.Add(new XElement(types.Root.Name.Namespace + "Override",
                    new XAttribute("PartName", $"/ppt/notesSlides/notesSlide{index}.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.notesSlide+xml")));
            }
            Write("[Content_Types].xml", types);

            XDocument Read(string part) { using var input = archive.GetEntry(part)!.Open(); return XDocument.Load(input); }
            void Write(string part, XDocument document)
            {
                archive.GetEntry(part)?.Delete();
                using var output = archive.CreateEntry(part).Open();
                document.Save(output);
            }
        }
        // Byte-identical input makes the positive control isolate the export option.
        File.Copy(Path.Combine(directory, "PowerPoint slides notes-excluded.pptx"),
            Path.Combine(directory, "PowerPoint slides notes-control.pptx"), false);
        return Cases.Select(key => ("PowerPoint slides " + key + ".pptx", "impress_pdf_Export", key == "hidden-ends" ? 1 : 3)).ToArray();
    }

    public static SlideObservation Observe(string name, string[] text)
    {
        var key = Path.GetFileNameWithoutExtension(name)["PowerPoint slides ".Length..];
        if (!Cases.Contains(key)) throw new ArgumentException("Unknown authored slide fixture.");
        var slides = text.Select(page => Regex.Matches(page, @"SLIDE_[123]_MARKER").Select(match => match.Value).ToArray()).ToArray();
        var notes = text.Select(page => Regex.Matches(page, @"NOTE_[123]_MARKER").Select(match => match.Value).ToArray()).ToArray();
        int[] order = key == "reordered" ? [3, 1, 2] : key == "hidden-ends" ? [2] : [1, 2, 3];
        var expected = order.Select(index => $"{(key == "notes-control" ? "NOTE" : "SLIDE")}_{index}_MARKER").ToArray();
        var actual = key == "notes-control" ? notes : slides;
        var matches = actual.Length == expected.Length && actual.Select((page, index) => page.SequenceEqual(new[] { expected[index] })).All(match => match)
            && (key == "notes-control" || notes.All(page => page.Length == 0));
        return new(key, slides, notes, matches);
    }

    internal sealed record SlideObservation(string Case, string[][] SlideMarkers, string[][] NoteMarkers, bool Matches);
}
