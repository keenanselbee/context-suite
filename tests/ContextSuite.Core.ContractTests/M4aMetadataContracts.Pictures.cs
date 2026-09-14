using System.Buffers.Binary;
using System.Security.Cryptography;
using ContextSuite.Core.Audio;

internal static partial class M4aMetadataContracts
{
    private static async Task PicturesAsync(Action<bool, string> check)
    {
        byte[] png = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3]; byte[] jpeg = [255, 216, 255, 4, 5, 6];
        foreach (var split in new[] { false, true })
        {
            var covers = split ? new[] { Box("covr", Data(14, png)), Box("covr", Data(13, jpeg)) } : [Box("covr", Data(14, png), Data(13, jpeg))];
            using var file = new MemoryStream(FileOf(tags: [Text("\u00a9nam", "Retained"), ..covers])); file.Position = 3;
            var inventory = await M4aMetadata.ReadAsync(file, default, true);
            var pictures = FlacDescriptiveMetadata.ReadBlocks(inventory.Pictures).Pictures;
            check(file.Position == 3 && inventory.Tags["title"] == "Retained" && pictures.Length == 2 &&
                pictures[0].MediaType == "image/png" && pictures[1].MediaType == "image/jpeg" &&
                pictures[0].Sha256 == Convert.ToHexString(SHA256.HashData(png)) && pictures[1].Sha256 == Convert.ToHexString(SHA256.HashData(jpeg)),
                "M4A artwork: exact ordered images, types, tags and caller position; separate covr=" + split);
            check(pictures.All(picture => picture.Type == 0 && picture.Description == "" && picture.Width == 0 && picture.Height == 0 && picture.BitsPerPixel == 0 && picture.Colors == 0),
                "M4A artwork: no invented picture designation, description or dimensions " + split);
            foreach (var (block, image) in inventory.Pictures.Zip(new[] { png, jpeg }))
                check(block.Data.AsSpan()[^image.Length..].SequenceEqual(image), "M4A artwork: complete original image bytes " + split);
        }
        using (var basic = new MemoryStream(FileOf()))
            check((await M4aMetadata.ReadAsync(basic, default, true)).Pictures.IsEmpty, "M4A artwork: text-only inventory remains empty");
        using (var maximum = new MemoryStream(FileOf(tags: [Box("covr", Enumerable.Repeat(Data(14, png), 31).ToArray())])))
            check((await M4aMetadata.ReadAsync(maximum, default, true)).Pictures.Length == 31, "M4A artwork: maximum count preserves repeated covers");
        await Reject([Box("covr", Data(14, png))], "explicit preservation required", false);
        await Reject([Box("covr", [])], "empty cover atom");
        await Reject([Box("covr", new byte[7])], "truncated child atom");
        await Reject([Box("covr", Box("data", new byte[7]))], "truncated data header");
        await Reject([Box("covr", Box("url ", "https://example.invalid/cover"u8.ToArray()))], "external or unknown child");
        await Reject([Box("covr", Data(14, png, 1))], "localized picture");
        foreach (var type in new[] { 0U, 1U, 12U, 27U, 0x0100000eU })
            await Reject([Box("covr", Data(type, png))], "unreviewed data type/version " + type);
        foreach (var data in new[] { Data(13, png), Data(14, jpeg), Data(13, []), Data(14, png[..7]) })
            await Reject([Box("covr", data)], "signature or extent mismatch");
        await Reject([Box("covr", Enumerable.Repeat(Data(14, png), 32).ToArray())], "picture count bound");
        var huge = new byte[6 * 1024 * 1024]; png.CopyTo(huge, 0);
        await Reject([Box("covr", Data(14, huge))], "base64 budget includes synthesized fields");
        var half = huge[..(3 * 1024 * 1024)];
        await Reject([Box("covr", Data(14, half)), Box("covr", Data(14, half))], "aggregate budget spans cover atoms");
        await Reject([Box("covr", Data(14, png)), Text("\u00a9nam", "One"), Text("\u00a9nam", "Two")], "duplicate ordinary tags still refused");
        await Reject([Box("covr", Data(14, png)), Text("----", "Extra")], "unsupported metadata still refused");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var cancelled = new MemoryStream(FileOf(tags: [Box("covr", Data(14, png))])); cancelled.Position = 4;
        try { await M4aMetadata.ReadAsync(cancelled, cancellation.Token, true); check(false, "M4A artwork cancellation"); }
        catch (OperationCanceledException) { check(cancelled.Position == 4, "M4A artwork: cancellation restores caller position"); }

        async Task Reject(byte[][] tags, string name, bool preserve = true)
        {
            using var file = new MemoryStream(FileOf(tags: tags)); file.Position = 2;
            try { await M4aMetadata.ReadAsync(file, default, preserve); check(false, "M4A artwork refuses " + name); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            { check(file.Position == 2, "M4A artwork refuses " + name + " and restores position"); }
        }
        static byte[] Data(uint type, byte[] image, uint locale = 0) => Box("data", N(type), N(locale), image);
    }
}
