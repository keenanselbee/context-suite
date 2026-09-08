using System.Buffers.Binary;
using System.Security.Cryptography;
using ContextSuite.Application;
using ContextSuite.Application.Infrastructure;
using ContextSuite.Core.Dds;
using ContextSuite.Core.Operations;
using ContextSuite.Core.Images;

internal static class DdsContracts
{
    public static async Task RunAsync(string scratch, Action<bool, string> check)
    {
        var planBytes = Fixture(DdsFormat.Rgba8);
        var planInfo = DdsParser.Parse(planBytes, planBytes.Length);
        var facts = new ImageSourceFacts(Guid.NewGuid(), Path.Combine(scratch, "plan.dds"), new string('0', 64), planBytes.Length,
            ImageFormat.Dds, 4, 4, 8, 1, false, false, "Unknown color meaning", [], Texture: planInfo);
        ImageItemPlan Plan(ImageSourceFacts source, ImageConversionOptions options) =>
            ImageConversionPlanner.Create(Guid.NewGuid(), [source], options, new("convert", new())).Items[0];
        var textureOptions = new ImageConversionOptions(ImageFormat.Dds, Texture: new());
        check(!Plan(facts, textureOptions).CanExecute, "DDS plan: UNORM color meaning must be chosen");
        textureOptions = textureOptions with { Texture = new(SourceInterpretation: DdsInterpretation.Srgb) };
        check(Plan(facts, textureOptions).CanExecute && textureOptions.OutputRepresentation?.Suffix == "BC7-sRGB", "DDS plan: explicit color policy and output naming");
        var same = textureOptions with { Texture = new(Format: DdsFormat.Rgba8, SourceInterpretation: DdsInterpretation.Linear) };
        check(Plan(facts, same).Warnings.Any(w => w.Code == "dds-preserved-pixels"), "DDS plan: unchanged payload and requested copy are disclosed");
        var alpha = facts with { HasTransparency = true };
        check(!Plan(alpha, textureOptions with { Texture = textureOptions.Texture! with { Format = DdsFormat.Bc1Srgb } }).CanExecute,
            "DDS plan: smooth transparency cannot silently become BC1 cutout");
        check(!Plan(facts, new(ImageFormat.Png, Texture: new(Format: DdsFormat.Rgba8Srgb, SourceInterpretation: DdsInterpretation.Linear))).CanExecute,
            "DDS plan: PNG export requires an explicit mip selection");
        check(Plan(facts, new(ImageFormat.Png, Texture: new(Format: DdsFormat.Rgba8Srgb, SourceInterpretation: DdsInterpretation.Linear), ExtractMip: 0)).CanExecute,
            "DDS plan: explicit base-level color PNG export");
        var ordinary = facts with { Format = ImageFormat.Png, Texture = null };
        check(!Plan(ordinary, textureOptions with { Texture = textureOptions.Texture! with { SourceInterpretation = DdsInterpretation.Linear } }).CanExecute,
            "DDS plan: ordinary color input cannot falsely claim a linear intermediate");
        check(!Plan(ordinary with { ProfileNames = ["icc"] }, textureOptions).CanExecute, "DDS plan: profile removal needs explicit consent");
        var bc1Bytes = Fixture(DdsFormat.Bc1Srgb);
        var unknownAlpha = (byte[])bc1Bytes.Clone(); Write(unknownAlpha, 144, 0x80000001);
        check(DdsParser.Parse(unknownAlpha, unknownAlpha.Length) is { RawAlphaMode: 0x80000001, IsSupported2D: false },
            "DDS analysis: unknown alpha flag bits are retained verbatim, not masked into a known mode");
        var bc1 = facts with { Texture = DdsParser.Parse(bc1Bytes, bc1Bytes.Length), FileBytes = bc1Bytes.Length, HasTransparency = true, IsLossy = true };
        var retainedBc1 = new ImageConversionOptions(ImageFormat.Dds, Texture: new(Format: DdsFormat.Bc1Srgb));
        check(Plan(bc1, retainedBc1).CanExecute, "DDS plan: unchanged BC1 alpha payload needs no new cutout quantization");
        check(Plan(bc1, retainedBc1 with { Texture = new(Format: DdsFormat.Bc1, ColorOperation: DdsColorOperation.Reinterpret) }).CanExecute,
            "DDS plan: BC1 alpha reinterpretation preserves payload and does not require contradictory cutout options");
        foreach (var format in Enum.GetValues<DdsFormat>().Where(f => f != DdsFormat.Unknown))
        {
            var bytes = Fixture(format);
            var info = DdsParser.Parse(bytes.AsSpan(0, 148), bytes.Length);
            check(info.Format == format && info.Warnings.IsEmpty && info.ExpectedPayloadBytes == (ulong)bytes.Length - 148,
                $"DDS: independent DX10 layout {format}");
        }
        foreach (var pair in new[] { ("DXT1", DdsFormat.Bc1), ("DXT2", DdsFormat.Bc2), ("DXT3", DdsFormat.Bc2),
            ("DXT4", DdsFormat.Bc3), ("DXT5", DdsFormat.Bc3), ("ATI1", DdsFormat.Bc4), ("BC4U", DdsFormat.Bc4),
            ("BC4S", DdsFormat.Bc4Snorm), ("ATI2", DdsFormat.Bc5), ("BC5U", DdsFormat.Bc5), ("BC5S", DdsFormat.Bc5Snorm) })
        {
            var original = Fixture(pair.Item2);
            var bytes = new byte[original.Length - 20];
            original.AsSpan(0, 128).CopyTo(bytes);
            Write(bytes, 84, FourCc(pair.Item1));
            var info = DdsParser.Parse(bytes.AsSpan(0, 128), bytes.Length);
            check(info.Format == pair.Item2 && !info.HasDx10Header && !info.IsSrgb && info.Warnings.IsEmpty,
                $"DDS: legacy {pair.Item1} has no declared sRGB");
            if (pair.Item1 is "DXT2" or "DXT4") check(info.RawAlphaMode == 2 && !info.IsSupported2D, "DDS: legacy premultiplied alpha is explicit");
        }
        check(DdsParser.PayloadSize(DdsFormat.Bc1, 7, 5, 1, 3, 1, false) == 48, "DDS: partial edge blocks and small mips use whole blocks");
        var chain = Fixture(DdsFormat.Bc7, 8, 4);
        Array.Resize(ref chain, 148 + 80);
        Write(chain, 28, 4); Write(chain, 8, 0x21007); Write(chain, 108, 0x401008);
        check(DdsParser.Parse(chain, chain.Length).ExpectedPayloadBytes == 80, "DDS: rectangular full mip-chain accounting");
        var cube = Fixture(DdsFormat.Bc1);
        Array.Resize(ref cube, 148 + 48);
        Write(cube, 112, 0xfe00); Write(cube, 136, 4);
        var cubeInfo = DdsParser.Parse(cube, cube.Length);
        check(cubeInfo.Kind == DdsTextureKind.Cube && cubeInfo.ExpectedPayloadBytes == 48 && !cubeInfo.IsSupported2D && cubeInfo.Warnings.IsEmpty,
            "DDS: complete cube reported without flattening");
        var array = Fixture(DdsFormat.R8);
        Array.Resize(ref array, 148 + 32); Write(array, 140, 2);
        check(DdsParser.Parse(array, array.Length) is { ArraySize: 2, IsSupported2D: false, ExpectedPayloadBytes: 32 }, "DDS: array layout is not ordinary 2D");
        var volume = Fixture(DdsFormat.R8);
        Array.Resize(ref volume, 148 + 64); Write(volume, 24, 4); Write(volume, 132, 4);
        Write(volume, 112, 0x200000); Write(volume, 8, 0x801007);
        check(DdsParser.Parse(volume, volume.Length) is { Kind: DdsTextureKind.Texture3D, Depth: 4, ExpectedPayloadBytes: 64, IsSupported2D: false },
            "DDS: volume depth contributes to payload");
        var unknown = Fixture(DdsFormat.Bc1); Write(unknown, 128, 0xabcdef);
        check(DdsParser.Parse(unknown, unknown.Length) is { Format: DdsFormat.Unknown, RawDxgiFormat: 0xabcdef, ExpectedPayloadBytes: null, IsSupported2D: false },
            "DDS: unknown DXGI identifier is retained");
        foreach (var bad in new[] { Array.Empty<byte>(), new byte[127], new byte[148] })
            Reject(() => DdsParser.Parse(bad, bad.Length), "DDS: incomplete/non-DDS header rejected", check);
        var truncated = Fixture(DdsFormat.Bc7);
        Reject(() => DdsParser.Parse(truncated.AsSpan(0, 128), 128), "DDS: truncated DX10 rejected", check);
        var baseline = Fixture(DdsFormat.Rgba8);
        foreach (var change in new (int Offset, uint Value)[] { (4, 0), (76, 0) })
        {
            var bad = (byte[])baseline.Clone(); Write(bad, change.Offset, change.Value);
            Reject(() => DdsParser.Parse(bad, bad.Length), "DDS: invalid header size rejected", check);
        }
        foreach (var change in new (int Offset, uint Value)[] { (8, 0), (12, 0), (16, 0), (24, 2), (28, 99), (108, 0),
            (112, 0x200000), (132, 0), (132, 2), (136, 8), (140, 0), (144, 7), (144, 8) })
        {
            var bad = (byte[])baseline.Clone(); Write(bad, change.Offset, change.Value);
            var info = DdsParser.Parse(bad, bad.Length);
            check(!info.Warnings.IsEmpty && !info.IsSupported2D, $"DDS: contradictory field {change.Offset}={change.Value} reported safely");
        }
        check(!DdsParser.Parse(baseline, baseline.Length - 1).Warnings.IsEmpty, "DDS: truncated payload reported");
        check(!DdsParser.Parse(baseline, baseline.Length + 1).Warnings.IsEmpty, "DDS: trailing payload reported");
        var huge = Fixture(DdsFormat.Rgba8); Write(huge, 16, uint.MaxValue); Write(huge, 12, uint.MaxValue);
        check(DdsParser.Parse(huge, huge.Length).Warnings.Any(w => w.Contains("overflows", StringComparison.Ordinal)), "DDS: size multiplication cannot overflow silently");
        var path = Path.Combine(scratch, "DDS independent source ü.dds");
        await File.WriteAllBytesAsync(path, baseline);
        var hash = SHA256.HashData(baseline); var timestamp = File.GetLastWriteTimeUtc(path);
        check((await DdsParser.ReadAsync(path, CancellationToken.None)).Format == DdsFormat.Rgba8, "DDS: bounded file reader");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await DdsParser.ReadAsync(path, cancelled.Token); check(false, "DDS: cancelled file analysis"); }
            catch (OperationCanceledException) { check(true, "DDS: cancelled file analysis"); }
        }
        await using (var worker = new WorkerClient(Path.Combine(scratch, "does-not-exist.exe")))
        await using (var viewModel = new MainViewModel(worker))
        {
            var badPath = Path.Combine(scratch, "not-a-texture.dds");
            await File.WriteAllTextAsync(badPath, "not a DDS");
            viewModel.Admit(new(Guid.NewGuid(), "analyze", "open-details", [badPath, path]));
            await viewModel.WaitForIdleAsync();
            check(viewModel.Rows[0].Result.State == OperationState.Unsupported && viewModel.Rows[1].Analysis?.Format == DdsFormat.Rgba8,
                "DDS: mixed Analyze batch continues without launching worker or consuming trial");
            check(viewModel.Rows[1].ResultDetails.Contains("not proof of authored linear", StringComparison.Ordinal),
                "DDS: report distinguishes storage from color meaning");
        }
        var after = await File.ReadAllBytesAsync(path);
        check(hash.SequenceEqual(SHA256.HashData(after)) && File.GetLastWriteTimeUtc(path) == timestamp,
            "DDS: analysis preserves source bytes and write time");
    }

    internal static byte[] Fixture(DdsFormat format, uint width = 4, uint height = 4)
    {
        // Independent layout math: do not use the parser to manufacture its own expectations.
        var id = (uint)format;
        var block = id is >= 70 and <= 84 or >= 94 and <= 99;
        var unit = id is >= 70 and <= 72 or >= 79 and <= 81 ? 8u : 16u;
        var payload = block ? ((width + 3) / 4) * ((height + 3) / 4) * unit : width * height * (format == DdsFormat.R8 ? 1u : format == DdsFormat.Rg8 ? 2u : 4u);
        var bytes = new byte[148 + payload];
        "DDS "u8.CopyTo(bytes); Write(bytes, 4, 124); Write(bytes, 8, 0x1007);
        Write(bytes, 12, height); Write(bytes, 16, width); Write(bytes, 76, 32);
        Write(bytes, 80, 4); Write(bytes, 84, FourCc("DX10")); Write(bytes, 108, 0x1000);
        Write(bytes, 128, id); Write(bytes, 132, 3); Write(bytes, 140, 1);
        return bytes;
    }

    private static void Write(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static uint FourCc(string value) => (uint)value[0] | (uint)value[1] << 8 | (uint)value[2] << 16 | (uint)value[3] << 24;
    private static void Reject(Action action, string name, Action<bool, string> check)
    {
        try { action(); check(false, name); } catch (InvalidDataException) { check(true, name); }
    }
}
