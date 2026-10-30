using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ContextSuite.Application.TestHost;

internal static class WorkflowFixture
{
    public static void Prepare(string root)
    {
        if (!Directory.Exists(root) || Directory.EnumerateFileSystemEntries(root).Any() ||
            !Path.GetFullPath(root).Contains(Path.DirectorySeparatorChar + ".codex-temp" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Workflow requires a fresh isolated test directory.");
        var pixels = new byte[64 * 48 * 3];
        for (var y = 0; y < 48; y++)
        for (var x = 0; x < 64; x++)
        {
            var offset = (y * 64 + x) * 3;
            pixels[offset] = (byte)(x < 32 ? 230 : 35);
            pixels[offset + 1] = (byte)(y < 24 ? 60 : 200);
            pixels[offset + 2] = 90;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(64, 48, 96, 96, PixelFormats.Rgb24, null, pixels, 64 * 3)));
        using var output = new FileStream(Path.Combine(root, "fixture.png"), FileMode.CreateNew);
        encoder.Save(output);
    }
}
