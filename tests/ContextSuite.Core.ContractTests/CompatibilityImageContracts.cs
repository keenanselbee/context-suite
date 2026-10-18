using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Images;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.ContractTests;

internal static class CompatibilityImageContracts
{
    public static async Task RunAsync(string root, string executable, string iconPath, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Use a fresh input-compatibility directory.");
        Directory.CreateDirectory(root);
        var jfif = Path.Combine(root, "photo.JFIF");
        var icon = Path.Combine(root, "artwork.ico");
        var wrong = Path.Combine(root, "mismatch.jfif");
        File.Copy(iconPath, icon);
        var pixels = Enumerable.Repeat(new byte[] { 30, 90, 180 }, 16 * 8).SelectMany(pixel => pixel).ToArray();
        var source = BitmapSource.Create(16, 8, 96, 96, PixelFormats.Bgr24, null, pixels, 16 * 3);
        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using (var output = File.Create(jfif)) encoder.Save(output);
        File.WriteAllText(wrong, "not JPEG data");
        var before = new[] { jfif, icon, wrong }.ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
        var access = new CountingAccess();
        await using var worker = new WorkerClient(executable, Path.Combine(root, "worker"));
        var publisher = new OutputPublisher(Path.Combine(root, "records"), new NoRecycle());
        await using var view = new MainViewModel(worker,
            new SuiteSettings { Convert = new(ReplaceOriginals: true), PlayCompletionSound = false }, publisher, access);
        view.ConversionRequested += (_, _) => throw new InvalidOperationException("These copy conversions need no routine planner.");
        view.Admit(new(Guid.NewGuid(), "convert", "png", [jfif, icon, wrong]));
        await view.WaitForIdleAsync();
        var rows = view.Rows.ToDictionary(row => row.Path);
        check(access.Admissions == 1 && rows[jfif].Result.State == OperationState.Succeeded && rows[icon].Result.State == OperationState.Succeeded &&
            rows[wrong].Result.State is OperationState.Unsupported or OperationState.Failed,
            "JFIF and ICO share one admission, publish independently, and report malformed JFIF separately");
        check(before.All(pair => SHA256.HashData(File.ReadAllBytes(pair.Key)).SequenceEqual(pair.Value)) &&
            rows[jfif].OutputPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && rows[icon].OutputPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase),
            "JFIF and ICO direct conversion preserves every original despite overwrite settings");
        using (var input = File.OpenRead(icon))
        using (var output = File.OpenRead(rows[icon].OutputPath))
        {
            var frames = new IconBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames;
            var largest = frames.OrderByDescending(frame => frame.PixelWidth * frame.PixelHeight).ThenByDescending(frame => frame.Format.BitsPerPixel).First();
            var actual = new PngBitmapDecoder(output, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            check(actual.PixelWidth == largest.PixelWidth && actual.PixelHeight == largest.PixelHeight && Pixels(actual).SequenceEqual(Pixels(largest)),
                "Windows independently verifies largest embedded ICO frame extraction with alpha");
        }
        using (var input = File.OpenRead(jfif))
        using (var output = File.OpenRead(rows[jfif].OutputPath))
        {
            var expected = new JpegBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var actual = new PngBitmapDecoder(output, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            check(actual.PixelWidth == 16 && actual.PixelHeight == 8 && Pixels(actual).Zip(Pixels(expected)).All(pair => Math.Abs(pair.First - pair.Second) <= 2),
                "Windows independently verifies JFIF decoded color and output size");
        }
        view.RetryFailed();
        await view.WaitForIdleAsync();
        check(Directory.EnumerateFiles(root, "*.png").Count() == 2 && access.Admissions == 1,
            "retrying an invalid JFIF creates no duplicate completed copies or empty admission");
    }

    private static byte[] Pixels(BitmapSource source)
    {
        var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return pixels;
    }

    private sealed class NoRecycle : IFileRecycler
    {
        public Task<RecycleResult> RecycleAsync(string path, FileFingerprint expected, CancellationToken token) =>
            throw new InvalidOperationException("Input copies must not recycle originals.");
    }

    private sealed class CountingAccess : IOperationAccess
    {
        public int Admissions { get; private set; }
        public Task<OperationAccessStatus> ReadAccessAsync(CancellationToken token = default) => Task.FromResult(new OperationAccessStatus(true, "Available"));
        public Task<OperationAdmission> AdmitConversionAsync(ConfirmedImageBatch confirmed, CancellationToken token)
        {
            Admissions++;
            return Task.FromResult(new OperationAdmission(new(true, "Available"), confirmed.Plan.BatchId));
        }
        public Task<OperationAdmission> AdmitOptimizationAsync(ConfirmedPngOptimization confirmed, CancellationToken token) => throw new NotSupportedException();
    }
}
