using System.Globalization;
using System.Text;

internal static partial class PdfPageGeometryContracts
{
    private sealed record GeometryFixture(string Name, byte[] Bytes, int Width, int Height, int Rotation = 0, bool Solid = false);

    private static IEnumerable<GeometryFixture> InheritedGeometryFixtures()
    {
        const string media = "/MediaBox [0 0 144 72]";
        const string crop = "/MediaBox [0 0 288 144] /CropBox [72 36 216 108]";
        foreach (var rotation in new[] { 0, 90, 180, 270 })
            yield return new("inherited-crop-rotate-" + rotation,
                PageTree(crop + " /Rotate " + rotation, "", "", Quadrants(288, 144)),
                rotation % 180 == 0 ? 300 : 150, rotation % 180 == 0 ? 150 : 300, rotation / 90);
        yield return new("nearest-parent-boxes", PageTree("/MediaBox [0 0 1000 1000] /CropBox [0 0 900 900] /Rotate 180",
            crop + " /Rotate 90", "", Quadrants(288, 144)), 150, 300, 1);
        yield return new("leaf-overrides-parent", PageTree(crop + " /Rotate 90", "", media + " /CropBox [0 0 144 72] /Rotate 270",
            Quadrants(144, 72)), 150, 300, 3);
        yield return new("leaf-zero-rotation", PageTree(media + " /Rotate 90", "", "/Rotate 0", Quadrants(144, 72)), 300, 150);
        yield return new("inherited-media-default-crop", PageTree(media, "", "", Quadrants(144, 72)), 300, 150);
        yield return new("negative-origin", PageTree("/MediaBox [-72 -36 72 36]", "", "",
            "1 0 0 1 -72 -36 cm\n" + Quadrants(144, 72)), 300, 150);
        yield return new("crop-intersects-media", PageTree(media + " /CropBox [-36 -18 180 90]", "", "", Quadrants(144, 72)), 300, 150);
        foreach (var unit in new[] { 1, 2, 4 })
            yield return new("user-unit-" + unit, PageTree(media, "", "/UserUnit " + unit, Quadrants(144, 72)), 300 * unit, 150 * unit);
        yield return new("user-unit-rotated-crop", PageTree(crop + " /Rotate 90", "", "/UserUnit 2", Quadrants(288, 144)), 300, 600, 1);
        yield return new("user-unit-not-inherited", PageTree(media + " /UserUnit 4", "/UserUnit 8", "", Quadrants(144, 72)), 300, 150);
        yield return new("user-unit-indirect", PageTree(media, "", "/UserUnit 6 0 R", Quadrants(144, 72), "2"), 600, 300);
        yield return new("user-unit-null-default", PageTree(media, "", "/UserUnit null", Quadrants(144, 72)), 300, 150);
        yield return new("user-unit-fraction", PageTree(media, "", "/UserUnit 0.5", "0 1 0 rg 0 0 144 72 re f\n"), 150, 75, Solid: true);
        yield return new("user-unit-maximum-pixels", PageTree("/MediaBox [0 0 960 960]", "", "/UserUnit 2", Quadrants(960, 960)), 4000, 4000);
    }

    private static byte[] PageTree(string parent, string branch, string leaf, string content, string? extraObject = null)
    {
        // Two ancestor levels make nearest-ancestor inheritance observable.
        // UserUnit belongs to the leaf; unlike boxes and Rotate it is not inherited.
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /Resources << >> " + parent + " >>",
            "<< /Type /Pages /Parent 2 0 R /Kids [4 0 R] /Count 1 " + branch + " >>",
            "<< /Type /Page /Parent 3 0 R /Contents 5 0 R " + leaf + " >>",
            "<< /Length " + Encoding.ASCII.GetByteCount(content) + " >>\nstream\n" + content + "endstream"
        };
        if (extraObject is not null) objects.Add(extraObject);
        return GeometryPdf(objects);
    }

    private static byte[] MixedUnitPdf()
    {
        var content = Quadrants(144, 72);
        return GeometryPdf([
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [5 0 R 3 0 R 4 0 R] /Count 3 /MediaBox [0 0 144 72] /Resources << >> /UserUnit 8 >>",
            "<< /Type /Page /Parent 2 0 R /Contents 6 0 R /UserUnit 4 /Rotate 90 >>",
            "<< /Type /Page /Parent 2 0 R /Contents 6 0 R >>",
            "<< /Type /Page /Parent 2 0 R /Contents 6 0 R /UserUnit 2 >>",
            "<< /Length " + Encoding.ASCII.GetByteCount(content) + " >>\nstream\n" + content + "endstream"
        ]);
    }

    private static byte[] GeometryPdf(IReadOnlyList<string> objects)
    {
        using var file = new MemoryStream();
        var offsets = new List<long> { 0 };
        Write("%PDF-1.7\n");
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(file.Position);
            Write((index + 1) + " 0 obj\n" + objects[index] + "\nendobj\n");
        }
        var xref = file.Position;
        Write("xref\n0 " + offsets.Count + "\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write("trailer\n<< /Size " + offsets.Count + " /Root 1 0 R >>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
        return file.ToArray();
        void Write(string text) => file.Write(Encoding.ASCII.GetBytes(text));
    }
}
