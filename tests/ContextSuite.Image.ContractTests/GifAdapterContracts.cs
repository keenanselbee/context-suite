using System.Security.Cryptography;
using System.Text;
using ContextSuite.Core.Gif;
using ContextSuite.Core.Images;
using ContextSuite.Private.Gif;
using ImageMagick;

internal static class GifAdapterContracts
{
    private static readonly byte[] Palette = [0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255];

    public static void Run(string root, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        using var adapter = new GifAdapter(Path.Combine(root, "engine"));
        var source = Path.Combine(root, "animated.gif");
        var original = Gif(2, 1,
            new Part(0, 0, 1, 1, [1], Delay: 3),
            new Part(1, 0, 1, 1, [2], Disposal: 2, Delay: 5),
            new Part(1, 0, 1, 1, [3], Delay: 7));
        File.WriteAllBytes(source, original);
        var facts = adapter.Probe(new(Guid.NewGuid(), source), default);
        check(facts.FileBytes == original.Length && facts.Sha256 == Hash(original) && facts.Width == 2 && facts.Height == 1 &&
            facts.FrameCount == 3 && facts.DelayCentiseconds.SequenceEqual([3, 5, 7]) && facts.LoopCount == 0,
            "GIF probe binds bytes, geometry, delays and loop count");

        var first = Convert(0);
        CheckPixels(first.Path, [255, 0, 0, 255, 0, 0, 0, 255], "first displayed frame");
        RejectFileWrite(source, "GIF source has a read lease during sequential extraction");
        var second = Convert(1);
        CheckPixels(second.Path, [255, 0, 0, 255, 0, 255, 0, 255], "second displayed frame and offset");
        var third = Convert(2);
        CheckPixels(third.Path, [255, 0, 0, 255, 0, 0, 255, 255], "third displayed frame after background disposal");
        check(File.ReadAllBytes(source).SequenceEqual(original), "GIF source bytes survive extraction");
        using (var write = File.Open(source, FileMode.Open, FileAccess.Write, FileShare.None))
            check(write.Length == original.Length, "GIF source lease closes after final frame");
        check(first.Result.SourceId == facts.ItemId && first.Result.SourceSha256 == facts.Sha256 &&
            first.Result.FrameIndex == 0 && first.Result.Width == facts.Width && first.Result.Height == facts.Height &&
            first.Result.EngineIdentity.Contains("sRGB8", StringComparison.Ordinal),
            "GIF result binds source, requested frame and engine policy");

        // A backwards request reconstructs the compositor from the leased original.
        var repeated = Convert(0);
        CheckPixels(repeated.Path, [255, 0, 0, 255, 0, 0, 0, 255], "backwards frame request restarts safely");
        var repeatedPending = Convert(0);
        CheckPixels(repeatedPending.Path, [255, 0, 0, 255, 0, 0, 0, 255], "same pending frame request restarts safely");
        adapter.Release();
        using (var write = File.Open(source, FileMode.Open, FileAccess.Write, FileShare.None))
            check(write.Length == original.Length, "explicit GIF release closes the source lease");

        var staticPath = Path.Combine(root, "transparent.gif");
        File.WriteAllBytes(staticPath, Gif(1, 1, new Part(0, 0, 1, 1, [0], Transparent: 0, Delay: 8), loop: null));
        var still = adapter.Probe(new(Guid.NewGuid(), staticPath), default);
        check(still.FrameCount == 1 && still.LoopCount is null && still.DelayCentiseconds.SequenceEqual([8]),
            "static transparent GIF probe retains absent loop and delay");
        var transparent = ConvertFor(still, 0);
        CheckPixels(transparent.Path, [0, 0, 0, 0], "transparent static frame");

        Convert(0);
        _ = adapter.Probe(new(Guid.NewGuid(), staticPath), default);
        using (var write = File.Open(source, FileMode.Open, FileAccess.Write, FileShare.None))
            check(write.Length == original.Length, "probing another GIF releases the pending source");

        var occupiedId = Guid.NewGuid();
        var occupiedPath = Path.Combine(root, $".context-suite-{occupiedId:N}.tmp");
        File.WriteAllText(occupiedPath, "occupied");
        Reject(() => adapter.Convert(new(still, 0, occupiedId, occupiedPath), default),
            "occupied reservation");
        check(File.ReadAllText(occupiedPath) == "occupied", "occupied reservation remains intact");
        Convert(0);
        Reject(() => adapter.Convert(new(facts, 1, occupiedId, occupiedPath), default),
            "occupied reservation after a cached frame");
        using (var write = File.Open(source, FileMode.Open, FileAccess.Write, FileShare.None))
            check(write.Length == original.Length, "failed cached conversion releases the source");
        Reject(() => adapter.Convert(new(still, 0, Guid.NewGuid(), source), default),
            "source as output reservation");
        check(File.ReadAllBytes(source).SequenceEqual(original), "invalid reservation does not alter source");
        Convert(0);
        Reject(() => adapter.Convert(new(facts, 1, Guid.NewGuid(), source), default),
            "invalid reservation after a cached frame");
        using (var write = File.Open(source, FileMode.Open, FileAccess.Write, FileShare.None))
            check(write.Length == original.Length, "invalid cached request releases the source");

        var stale = adapter.Probe(new(Guid.NewGuid(), source), default);
        File.AppendAllText(source, "changed");
        RejectSourceChanged(() => ConvertFor(stale, 0), "changed GIF bytes after probe");
        File.WriteAllBytes(source, original);
        var alteredHash = stale with { Sha256 = new string('A', 64) };
        RejectSourceChanged(() => ConvertFor(alteredHash, 0), "incorrect GIF hash");
        var alteredDelay = stale with { DelayCentiseconds = [3, 5, 8] };
        RejectSourceChanged(() => ConvertFor(alteredDelay, 0), "incorrect GIF delay");
        var alteredLoop = stale with { LoopCount = 1 };
        RejectSourceChanged(() => ConvertFor(alteredLoop, 0), "incorrect GIF loop count");

        var corruptPath = Path.Combine(root, "bad-pixels.gif");
        var corrupt = Gif(1, 1, new Part(0, 0, 1, 1, [1]), loop: null);
        corrupt[^4] = 0xff;
        File.WriteAllBytes(corruptPath, corrupt);
        var corruptFacts = adapter.Probe(new(Guid.NewGuid(), corruptPath), default);
        try { ConvertFor(corruptFacts, 0); throw new InvalidOperationException("GIF accepted damaged LZW pixels."); }
        catch (ImageFailureException error) when (error.Failure == ImageFailure.InvalidInput)
        { check(true, "GIF damaged LZW pixels have an invalid-input failure"); }

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Convert(0);
        try { adapter.Convert(Work(stale, 0).Work, cancelled.Token); }
        catch (OperationCanceledException)
        {
            using var write = File.Open(source, FileMode.Open, FileAccess.Write, FileShare.None);
            check(write.Length == original.Length, "pre-cancelled GIF conversion stops and releases cached source");
            return;
        }
        throw new InvalidOperationException("GIF conversion ignored cancellation.");

        (GifWorkResult Result, string Path) Convert(int frame) => ConvertFor(facts, frame);

        (GifWorkResult Result, string Path) ConvertFor(GifSourceFacts input, int frame)
        {
            var reservation = Work(input, frame);
            File.WriteAllBytes(reservation.Path, []);
            var result = adapter.Convert(reservation.Work, default);
            var bytes = File.ReadAllBytes(reservation.Path);
            check(result.Validation.ItemId == reservation.Work.OutputId && result.Validation.MatchesPlan &&
                result.Validation.Sha256 == Hash(bytes) && result.OutputBytes == bytes.Length &&
                bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                "GIF frame output has a validated PNG digest");
            return (result, reservation.Path);
        }

        (GifWork Work, string Path) Work(GifSourceFacts input, int frame)
        {
            var id = Guid.NewGuid();
            var path = Path.Combine(root, $".context-suite-{id:N}.tmp");
            return (new GifWork(input, frame, id, path), path);
        }

        void CheckPixels(string path, byte[] expected, string label)
        {
            using var image = new MagickImage(path, MagickFormat.Png);
            check(image.Depth == 8 && image.GetColorProfile()?.ToByteArray().SequenceEqual(ColorProfiles.SRGB.ToByteArray()) == true &&
                image.GetPixels().ToByteArray(PixelMapping.RGBA)!.SequenceEqual(expected),
                "GIF output exact sRGB8 pixels: " + label);
        }

        void Reject(Action action, string label)
        {
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or ImageFailureException)
            { check(true, "GIF refuses " + label); return; }
            throw new InvalidOperationException("GIF accepted " + label);
        }

        void RejectSourceChanged(Action action, string label)
        {
            try { action(); }
            catch (ImageFailureException error) when (error.Failure == ImageFailure.SourceChanged)
            { check(true, "GIF refuses " + label); return; }
            throw new InvalidOperationException("GIF accepted " + label);
        }

        void RejectFileWrite(string path, string label)
        {
            try { using var write = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None); }
            catch (IOException) { check(true, label); return; }
            throw new InvalidOperationException("GIF source lease did not block mutation.");
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed record Part(int Left, int Top, int Width, int Height, byte[] Pixels,
        int Disposal = 0, int Transparent = -1, int Delay = 0);

    private static byte[] Gif(int width, int height, Part first, Part? second = null, Part? third = null, int? loop = 0)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("GIF89a"));
        writer.Write((ushort)width); writer.Write((ushort)height);
        writer.Write((byte)0x81); writer.Write((byte)0); writer.Write((byte)0);
        writer.Write(Palette);
        if (loop is { } loopCount)
        {
            writer.Write(new byte[] { 0x21, 0xff, 11 });
            writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            writer.Write(new byte[] { 3, 1 }); writer.Write((ushort)loopCount); writer.Write((byte)0);
        }
        foreach (var part in new[] { first, second, third }.OfType<Part>())
        {
            writer.Write(new byte[] { 0x21, 0xf9, 4, (byte)(part.Disposal << 2 | (part.Transparent >= 0 ? 1 : 0)) });
            writer.Write((ushort)part.Delay); writer.Write((byte)Math.Max(0, part.Transparent)); writer.Write((byte)0);
            writer.Write((byte)0x2c);
            writer.Write((ushort)part.Left); writer.Write((ushort)part.Top);
            writer.Write((ushort)part.Width); writer.Write((ushort)part.Height); writer.Write((byte)0);
            writer.Write((byte)2);
            var codes = new List<int>();
            foreach (var pixel in part.Pixels) { codes.Add(4); codes.Add(pixel); }
            codes.Add(5);
            uint bits = 0; var count = 0; var packed = new List<byte>();
            foreach (var code in codes)
            {
                bits |= (uint)code << count; count += 3;
                while (count >= 8) { packed.Add((byte)bits); bits >>= 8; count -= 8; }
            }
            if (count > 0) packed.Add((byte)bits);
            writer.Write((byte)packed.Count); writer.Write(packed.ToArray()); writer.Write((byte)0);
        }
        writer.Write((byte)0x3b);
        return stream.ToArray();
    }
}
