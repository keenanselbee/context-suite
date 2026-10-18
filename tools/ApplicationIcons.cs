using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Resize the approved artwork without changing its colors, geometry or alpha.
public static class ApplicationIcons
{
    public static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };

    public static Bitmap Render(string sourceDirectory, int size)
    {
        string name = size <= 48 ? "icon-tri-small.png" : "icon-tri.png";
        using (var source = new Bitmap(Path.Combine(sourceDirectory, name)))
        {
            if (source.Width != source.Height || source.Width < size)
                throw new InvalidDataException("Application artwork must be square and large enough: " + name);
            var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(result))
            using (var attributes = new ImageAttributes())
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                graphics.DrawImage(source, new Rectangle(0, 0, size, size),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            return result;
        }
    }

    public static void WriteIcon(string sourceDirectory, string path)
    {
        var frames = new byte[Sizes.Length][];
        for (int i = 0; i < Sizes.Length; ++i)
        {
            int size = Sizes[i];
            using (var bitmap = Render(sourceDirectory, size))
            using (var stream = new MemoryStream())
            {
                if (size == 256)
                    bitmap.Save(stream, ImageFormat.Png);
                else
                {
                    using (var dib = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                    {
                        int maskStride = ((size + 31) / 32) * 4;
                        dib.Write(40);
                        dib.Write(size);
                        dib.Write(size * 2);
                        dib.Write((ushort)1);
                        dib.Write((ushort)32);
                        dib.Write(0);
                        dib.Write(size * size * 4 + maskStride * size);
                        for (int field = 0; field < 4; ++field) dib.Write(0);
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
                    }
                }
                frames[i] = stream.ToArray();
            }
        }
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)Sizes.Length);
            int offset = 6 + 16 * Sizes.Length;
            for (int i = 0; i < Sizes.Length; ++i)
            {
                writer.Write((byte)(Sizes[i] == 256 ? 0 : Sizes[i]));
                writer.Write((byte)(Sizes[i] == 256 ? 0 : Sizes[i]));
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
