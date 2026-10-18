using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Original Compact glyphs on a 24-unit grid. Render each ICO size independently.
public static class CompactIcons
{
    public static readonly string[] Names = {
        "Analyze", "Convert", "Optimize", "ConvertPng", "ConvertJpeg", "ConvertWebp",
        "ConvertBmp", "ConvertTga", "ConvertDds", "ConvertWav", "ConvertFlac",
        "ConvertMp3", "ConvertM4a", "ConvertOgg", "ConvertOpus", "ConvertPdf",
        "ConvertDocx", "ConvertWem", "ConvertIco", "ConvertAvif", "Settings"
    };

    private static void AddPolygon(GraphicsPath path, params float[] coordinates)
    {
        var points = new PointF[coordinates.Length / 2];
        for (int i = 0; i < points.Length; ++i)
            points[i] = new PointF(coordinates[i * 2], coordinates[i * 2 + 1]);
        path.AddPolygon(points);
    }

    private static void DrawDestination(Graphics graphics, Brush brush, string name)
    {
        using (var path = new GraphicsPath(FillMode.Alternate))
        {
            switch (name)
            {
                case "ConvertPng":
                    path.AddRectangle(new RectangleF(2, 3, 20, 18));
                    path.AddRectangle(new RectangleF(5, 6, 14, 12));
                    graphics.FillPath(brush, path);
                    graphics.FillRectangle(brush, 5, 6, 7, 6);
                    graphics.FillRectangle(brush, 12, 12, 7, 6);
                    return;
                case "ConvertJpeg":
                    path.AddRectangle(new RectangleF(2, 4, 20, 16));
                    path.AddRectangle(new RectangleF(5, 7, 14, 10));
                    graphics.FillPath(brush, path);
                    graphics.FillEllipse(brush, 6.2f, 7.2f, 3.6f, 3.6f);
                    Polygon(graphics, brush, 5, 17, 11, 10, 15, 14, 17, 12, 19, 17);
                    return;
                case "ConvertWebp":
                    path.AddArc(2, 2, 8, 8, 180, 90);
                    path.AddArc(14, 2, 8, 8, 270, 90);
                    path.AddArc(14, 14, 8, 8, 0, 90);
                    path.AddArc(2, 14, 8, 8, 90, 90);
                    path.CloseFigure();
                    path.AddEllipse(6, 5, 4, 4);
                    AddPolygon(path, 5, 18, 19, 18, 16, 11, 13, 15, 10, 11);
                    break;
                case "ConvertBmp":
                    path.AddRectangle(new RectangleF(3, 3, 8, 8));
                    path.AddRectangle(new RectangleF(13, 3, 8, 8));
                    path.AddRectangle(new RectangleF(3, 13, 8, 8));
                    foreach (int x in new[] { 13, 18 })
                        foreach (int y in new[] { 13, 18 })
                            path.AddRectangle(new RectangleF(x, y, 3, 3));
                    break;
                case "ConvertTga":
                    AddPolygon(path, 3, 3, 17, 3, 17, 6, 6, 6, 6, 16, 3, 16);
                    path.AddRectangle(new RectangleF(8, 8, 13, 13));
                    AddPolygon(path, 10, 18, 19, 18, 14, 11);
                    break;
                case "ConvertDds":
                    AddPolygon(path, 12, 2, 22, 7, 12, 12, 2, 7);
                    AddPolygon(path, 2, 10, 10.5f, 14.2f, 10.5f, 23, 2, 18);
                    AddPolygon(path, 22, 10, 22, 18, 13.5f, 23, 13.5f, 14.2f);
                    break;
                case "ConvertWav":
                    path.AddRectangle(new RectangleF(2, 10, 3, 4));
                    path.AddRectangle(new RectangleF(6, 5, 3, 14));
                    path.AddRectangle(new RectangleF(10, 2, 4, 20));
                    path.AddRectangle(new RectangleF(15, 7, 3, 10));
                    path.AddRectangle(new RectangleF(19, 10, 3, 4));
                    break;
                case "ConvertFlac":
                    AddPolygon(path, 2, 3, 7, 3, 7, 6, 5, 6, 5, 18, 7, 18, 7, 21, 2, 21);
                    AddPolygon(path, 22, 3, 17, 3, 17, 6, 19, 6, 19, 18, 17, 18, 17, 21, 22, 21);
                    path.AddRectangle(new RectangleF(7, 9, 2.5f, 6));
                    path.AddRectangle(new RectangleF(10.75f, 6, 2.5f, 12));
                    path.AddRectangle(new RectangleF(14.5f, 9, 2.5f, 6));
                    break;
                case "ConvertMp3":
                    Polygon(graphics, brush, 9, 4, 22, 2, 22, 17, 19, 17, 19, 7, 12, 8, 12, 19, 9, 19);
                    graphics.FillEllipse(brush, 2, 16, 10, 6);
                    graphics.FillEllipse(brush, 12, 14, 10, 6);
                    return;
                case "ConvertM4a":
                    path.AddEllipse(2, 2, 20, 20);
                    // One continuous note cutout avoids an overlap filling its center.
                    path.StartFigure();
                    path.AddLines(new[] { new PointF(11, 6), new PointF(17, 6), new PointF(17, 9),
                        new PointF(14, 9), new PointF(14, 15) });
                    path.AddBezier(14, 15, 14, 19, 6, 19, 6, 15);
                    path.AddBezier(6, 15, 6, 12.5f, 9, 11.5f, 11, 12.1f);
                    path.CloseFigure();
                    break;
                case "ConvertOgg":
                    path.AddArc(2, 2, 20, 20, 180, 180);
                    path.AddLine(22, 12, 22, 22);
                    path.AddLine(22, 22, 16, 22);
                    path.AddLine(16, 22, 16, 13);
                    path.AddLine(16, 13, 18, 13);
                    path.AddArc(6, 6, 12, 12, 0, -180);
                    path.AddLine(6, 12, 6, 13);
                    path.AddLine(6, 13, 8, 13);
                    path.AddLine(8, 13, 8, 22);
                    path.AddLine(8, 22, 2, 22);
                    path.CloseFigure();
                    break;
                case "ConvertOpus":
                    AddPolygon(path, 2, 9, 6, 9, 11, 4, 11, 20, 6, 15, 2, 15);
                    path.AddRectangle(new RectangleF(14, 7, 3, 10));
                    path.AddRectangle(new RectangleF(19, 3, 3, 18));
                    break;
                case "ConvertPdf":
                    AddPolygon(path, 4, 2, 14, 2, 20, 8, 20, 22, 4, 22);
                    AddPolygon(path, 14, 4, 14, 9, 19, 9);
                    path.AddRectangle(new RectangleF(7, 11, 5, 5));
                    path.AddRectangle(new RectangleF(14, 11, 3, 2));
                    path.AddRectangle(new RectangleF(14, 15, 3, 2));
                    path.AddRectangle(new RectangleF(7, 19, 10, 1));
                    break;
                case "ConvertDocx":
                    AddPolygon(path, 3, 2, 15, 2, 21, 8, 21, 22, 3, 22);
                    AddPolygon(path, 15, 3, 15, 9, 20, 9);
                    AddPolygon(path, 5, 10, 7.6f, 20, 10.6f, 20, 12, 15.5f,
                        13.4f, 20, 16.4f, 20, 19, 10, 16.2f, 10, 15, 16,
                        13.5f, 12, 10.5f, 12, 9, 16, 7.8f, 10);
                    break;
                case "ConvertWem":
                    // Encoded sound: a compact waveform inside a hexagonal container.
                    AddPolygon(path, 7, 2, 17, 2, 23, 12, 17, 22, 7, 22, 1, 12);
                    path.AddRectangle(new RectangleF(6, 9, 3, 6));
                    path.AddRectangle(new RectangleF(10.5f, 6, 3, 12));
                    path.AddRectangle(new RectangleF(15, 9, 3, 6));
                    break;
                case "ConvertIco":
                    // A compact app window and cursor, independent of the image-frame glyphs.
                    path.AddRectangle(new RectangleF(2, 3, 20, 18));
                    path.AddRectangle(new RectangleF(4.5f, 8, 15, 10.5f));
                    graphics.FillPath(brush, path);
                    Polygon(graphics, brush, 9, 9.5f, 9, 17, 11.5f, 15.2f,
                        13, 19.4f, 15, 18.7f, 13.5f, 14.5f, 16.5f, 14.5f);
                    return;
                case "ConvertAvif":
                    // Four-sided aperture within a broad octagonal image tile.
                    AddPolygon(path, 8, 2, 16, 2, 22, 8, 22, 16,
                        16, 22, 8, 22, 2, 16, 2, 8);
                    AddPolygon(path, 12, 7, 17, 12, 12, 17, 7, 12);
                    break;
                case "Settings":
                    // Draw overlapping slider parts together without alternating cutouts.
                    foreach (int y in new[] { 5, 11, 17 })
                        graphics.FillRectangle(brush, 2, y, 20, 2);
                    graphics.FillRectangle(brush, 6, 2, 4, 8);
                    graphics.FillRectangle(brush, 15, 8, 4, 8);
                    graphics.FillRectangle(brush, 6, 14, 4, 8);
                    return;
                default:
                    throw new ArgumentException("Unknown glyph", "name");
            }
            graphics.FillPath(brush, path);
        }
    }

    private static void Polygon(Graphics graphics, Brush brush, params float[] coordinates)
    {
        var points = new PointF[coordinates.Length / 2];
        for (int i = 0; i < points.Length; ++i)
            points[i] = new PointF(coordinates[i * 2], coordinates[i * 2 + 1]);
        graphics.FillPolygon(brush, points);
    }

    public static Bitmap Render(string name, int size, bool darkMenu)
    {
        using (var large = new Bitmap(size * 4, size * 4, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(large))
        using (var brush = new SolidBrush(darkMenu ? Color.FromArgb(245, 245, 245) : Color.FromArgb(36, 36, 36)))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.ScaleTransform(size * 4f / 24, size * 4f / 24);
            if (name == "Analyze")
            {
                // Cut-outs are transparent, including the lens and chart bars.
                using (var document = new GraphicsPath(FillMode.Alternate))
                {
                    document.AddPolygon(new[] { new PointF(3, 2), new PointF(12, 2),
                        new PointF(17, 7), new PointF(17, 21), new PointF(3, 21) });
                    document.AddRectangle(new RectangleF(6, 10, 2, 6));
                    document.AddRectangle(new RectangleF(9, 7, 2, 9));
                    graphics.FillPath(brush, document);
                }
                // Clear a one-unit separation around the magnifier before drawing it.
                graphics.CompositingMode = CompositingMode.SourceCopy;
                using (var clear = new SolidBrush(Color.Transparent))
                    graphics.FillEllipse(clear, 10, 10, 12, 12);
                graphics.CompositingMode = CompositingMode.SourceOver;
                using (var lens = new GraphicsPath(FillMode.Alternate))
                {
                    lens.AddEllipse(11, 11, 10, 10);
                    lens.AddEllipse(13.5f, 13.5f, 5, 5);
                    graphics.FillPath(brush, lens);
                }
                Polygon(graphics, brush, 18, 19.5f, 19.5f, 18, 23, 21.5f, 21.5f, 23);
            }
            else if (name == "Convert")
            {
                Polygon(graphics, brush, 3, 5.5f, 15, 5.5f, 15, 2.5f, 22, 8, 15, 13.5f, 15, 10.5f, 3, 10.5f);
                Polygon(graphics, brush, 21, 13.5f, 9, 13.5f, 9, 10.5f, 2, 16, 9, 21.5f, 9, 18.5f, 21, 18.5f);
            }
            else if (name == "Optimize")
            {
                // Scale the approved B geometry down 10% about its center so
                // its wide chevrons balance the other menu glyphs.
                graphics.TranslateTransform(1.2f, 1.2f);
                graphics.ScaleTransform(0.9f, 0.9f);
                Polygon(graphics, brush, 2.5f, 4, 10.5f, 12, 2.5f, 20, 0.5f, 17, 5.5f, 12, 0.5f, 7);
                Polygon(graphics, brush, 21.5f, 4, 13.5f, 12, 21.5f, 20, 23.5f, 17, 18.5f, 12, 23.5f, 7);
            }
            else DrawDestination(graphics, brush, name);

            var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var output = Graphics.FromImage(result))
            {
                output.CompositingMode = CompositingMode.SourceCopy;
                output.InterpolationMode = InterpolationMode.HighQualityBicubic;
                output.PixelOffsetMode = PixelOffsetMode.HighQuality;
                output.DrawImage(large, new Rectangle(0, 0, size, size), 0, 0,
                    large.Width, large.Height, GraphicsUnit.Pixel);
            }
            return result;
        }
    }

    public static void WriteIcon(string path, string name, bool darkMenu)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128 };
        var frames = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; ++i)
            using (var bitmap = Render(name, sizes[i], darkMenu))
            using (var stream = new MemoryStream())
            {
                // Explicit 32-bit DIBs preserve alpha in both modern and legacy shell readers.
                // GetHicon/Save can quantize to the display's color depth in headless builds.
                using (var dib = new BinaryWriter(stream))
                {
                    int size = sizes[i];
                    int maskStride = ((size + 31) / 32) * 4;
                    dib.Write(40); // BITMAPINFOHEADER
                    dib.Write(size);
                    dib.Write(size * 2); // Color image plus the one-bit transparency mask.
                    dib.Write((ushort)1);
                    dib.Write((ushort)32);
                    dib.Write(0);
                    dib.Write(size * size * 4 + maskStride * size);
                    dib.Write(0);
                    dib.Write(0);
                    dib.Write(0);
                    dib.Write(0);
                    for (int y = size - 1; y >= 0; --y)
                        for (int x = 0; x < size; ++x)
                            dib.Write(bitmap.GetPixel(x, y).ToArgb());
                    for (int y = size - 1; y >= 0; --y)
                    {
                        var mask = new byte[maskStride];
                        for (int x = 0; x < size; ++x)
                            if (bitmap.GetPixel(x, y).A == 0)
                                mask[x / 8] |= (byte)(128 >> (x % 8));
                        dib.Write(mask);
                    }
                    frames[i] = stream.ToArray();
                }
            }
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; ++i)
            {
                writer.Write((byte)sizes[i]);
                writer.Write((byte)sizes[i]);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(frames[i].Length);
                writer.Write(offset);
                offset += frames[i].Length;
            }
            foreach (var frame in frames) writer.Write(frame);
        }
    }
}
