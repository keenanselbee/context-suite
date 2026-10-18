using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class TiffFixtureFactory
{
    private const int Width = 8;
    private const int Height = 4;

    public static async Task CreateAsync(string root)
    {
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException("Use a new empty TIFF fixture directory.");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(root);
                Create(root);
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }

    public static async Task CreateLargeAsync(string path)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                const int dimension = 4000;
                var pixels = new byte[dimension * dimension * 3];
                new Random(7429).NextBytes(pixels);
                var bitmap = BitmapSource.Create(dimension, dimension, 96, 96, PixelFormats.Bgr24, null,
                    pixels, dimension * 3);
                var encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.None };
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(path);
                encoder.Save(output);
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }

    private static void Create(string root)
    {
        var compressions = new (string Name, TiffCompressOption Option)[]
        {
            ("none", TiffCompressOption.None), ("lzw", TiffCompressOption.Lzw),
            ("zip", TiffCompressOption.Zip), ("rle", TiffCompressOption.Rle),
            ("ccitt3", TiffCompressOption.Ccitt3), ("ccitt4", TiffCompressOption.Ccitt4)
        };
        foreach (var kind in new[] { "bw1", "gray8", "bgr24", "bgra32" })
        foreach (var (name, option) in compressions)
        {
            if (name.StartsWith("ccitt", StringComparison.Ordinal) && kind != "bw1") continue;
            var encoder = new TiffBitmapEncoder { Compression = option };
            for (var page = 0; page < 2; page++)
            {
                var metadata = new BitmapMetadata("tiff");
                metadata.SetQuery("/ifd/{ushort=274}", (ushort)(page == 0 ? 1 : 6));
                encoder.Frames.Add(BitmapFrame.Create(Make(kind, page), null, metadata, null));
            }
            using var output = File.Create(Path.Combine(root, $"{kind}-{name}.tif"));
            encoder.Save(output);
        }
        var orientations = new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw };
        for (ushort orientation = 1; orientation <= 8; orientation++)
        {
            var metadata = new BitmapMetadata("tiff");
            metadata.SetQuery("/ifd/{ushort=274}", orientation);
            orientations.Frames.Add(BitmapFrame.Create(Make("bgr24", orientation % 2), null, metadata, null));
        }
        using (var output = File.Create(Path.Combine(root, "orientation-1-to-8.tif"))) orientations.Save(output);
        var icc = new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw };
        var colors = new ReadOnlyCollection<ColorContext>([new ColorContext(PixelFormats.Bgra32)]);
        icc.Frames.Add(BitmapFrame.Create(Make("bgra32", 0), null, null, colors));
        using (var output = File.Create(Path.Combine(root, "icc-bgra.tif"))) icc.Save(output);
    }

    private static BitmapSource Make(string kind, int seed)
    {
        var format = kind switch
        {
            "bw1" => PixelFormats.BlackWhite, "gray8" => PixelFormats.Gray8,
            "bgr24" => PixelFormats.Bgr24, _ => PixelFormats.Bgra32
        };
        var bpp = format.BitsPerPixel;
        var stride = (Width * bpp + 7) / 8;
        var bytes = new byte[stride * Height];
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var offset = y * stride + x * bpp / 8;
            if (kind == "bw1") bytes[y * stride] |= (byte)(((x + y + seed) % 2) << (7 - x));
            else if (kind == "gray8") bytes[offset] = (byte)(x * 23 + y * 17 + seed * 7);
            else
            {
                bytes[offset] = (byte)(x * 20 + seed * 7);
                bytes[offset + 1] = (byte)(y * 50 + seed * 7);
                bytes[offset + 2] = (byte)(x * 10 + y * 15 + seed * 7);
                if (kind == "bgra32") bytes[offset + 3] = (byte)((x + y + seed) % 2 == 0 ? 255 : 80);
            }
        }
        return BitmapSource.Create(Width, Height, 150, 75, format, null, bytes, stride);
    }
}
