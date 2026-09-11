using System.Buffers.Binary;
using System.Collections.Immutable;
using ContextSuite.Core.Images;
using ContextSuite.Core.Pdf;
using ContextSuite.Core.Settings;

namespace ContextSuite.Core.ContractTests;

internal static class ImagePdfValidationContracts
{
    public static void Run(string scratch, Action<bool, string> check)
    {
        var source = new ImageSourceFacts(Guid.NewGuid(), Path.Combine(scratch, "rgb.png"), new('A', 64), 100,
            ImageFormat.Png, 4, 3, 16, 1, true, false, "sRGB", []);
        var plan = ImagePdfPlan.Create(Guid.NewGuid(), [source], new BatchSettings("convert", new())).Confirm(false);
        var page = plan.Plan.Pages[0];
        ImmutableArray<ImagePdfSamples> expected = [new(0, new('A', 64), new('B', 64), new('C', 64), 500)];
        var reply = new byte[ImagePdfValidation.HeaderBytes + ImagePdfValidation.PageBytes];
        uint[] fields = [0x31564943, 1, 1, 160, 0, 4, 3, 16, 3, 1, 500, 0];
        for (var i = 0; i < fields.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(i * 4), fields[i]);
        BinaryPrimitives.WriteDoubleLittleEndian(reply.AsSpan(48), page.WidthPoints);
        BinaryPrimitives.WriteDoubleLittleEndian(reply.AsSpan(56), page.HeightPoints);
        BinaryPrimitives.WriteUInt64LittleEndian(reply.AsSpan(64), 72);
        BinaryPrimitives.WriteUInt64LittleEndian(reply.AsSpan(72), 24);
        Convert.FromHexString(expected[0].ColorSha256).CopyTo(reply, 80);
        Convert.FromHexString(expected[0].AlphaSha256!).CopyTo(reply, 112);
        Convert.FromHexString(expected[0].ProfileSha256).CopyTo(reply, 144);
        ImagePdfValidation.Verify(reply, plan, expected);
        check(true, "image PDF validation: exact samples, geometry, alpha and ICC accepted");
        foreach (var offset in Enumerable.Range(0, 12).Select(i => i * 4).Concat(new[] { 64, 72, 80, 112, 144 }))
        {
            var modified = (byte[])reply.Clone(); modified[offset] ^= 1;
            Reject(() => ImagePdfValidation.Verify(modified, plan, expected), "mismatched field at " + offset);
        }
        foreach (var number in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0, -1, page.WidthPoints + 0.1 })
        {
            var modified = (byte[])reply.Clone(); BinaryPrimitives.WriteDoubleLittleEndian(modified.AsSpan(48), number);
            Reject(() => ImagePdfValidation.Verify(modified, plan, expected), "invalid physical dimension " + number);
        }
        foreach (var length in new[] { 0, 15, 16, 47, 80, reply.Length - 1 })
            Reject(() => ImagePdfValidation.Verify(reply.AsSpan(0, length), plan, expected), "truncated reply " + length);
        Reject(() => ImagePdfValidation.Verify(reply.Concat(new byte[1]).ToArray(), plan, expected), "trailing bytes");
        Reject(() => ImagePdfValidation.Verify(reply, plan, default), "default sample inventory");
        Reject(() => ImagePdfValidation.Verify(reply, plan, []), "empty sample inventory");
        foreach (var invalid in new[] { expected[0] with { PageIndex = 1 }, expected[0] with { ColorSha256 = "untrusted" },
            expected[0] with { ProfileSha256 = "untrusted" }, expected[0] with { AlphaSha256 = null },
            expected[0] with { ProfileBytes = 0 }, expected[0] with { ProfileBytes = 4 * 1024 * 1024 + 1 } })
            Reject(() => ImagePdfValidation.Verify(reply, plan, [invalid]), "invalid sample expectations");
        var opaque = ImagePdfPlan.Create(Guid.NewGuid(), [source with { HasTransparency = false }], plan.Plan.Settings).Confirm(false);
        var opaqueReply = (byte[])reply.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(opaqueReply.AsSpan(36), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(opaqueReply.AsSpan(72), 0); opaqueReply.AsSpan(112, 32).Clear();
        ImagePdfValidation.Verify(opaqueReply, opaque, [expected[0] with { AlphaSha256 = null }]);
        check(true, "image PDF validation: opaque pages require absent alpha and zero alpha digest");
        opaqueReply[112] = 1;
        Reject(() => ImagePdfValidation.Verify(opaqueReply, opaque, [expected[0] with { AlphaSha256 = null }]), "unexpected opaque-page alpha payload");

        void Reject(Action action, string name)
        {
            try { action(); check(false, "image PDF validation accepted " + name); }
            catch (InvalidDataException) { check(true, "image PDF validation rejects " + name); }
        }
    }
}
