using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Dds;
using ContextSuite.Private.Dds;

if (args.Length != 1 || !Path.IsPathFullyQualified(args[0])) throw new ArgumentException("Expected absolute scratch directory.");
Directory.CreateDirectory(args[0]);
var passed = 0;
var source = Fixture(DdsFormat.Rgba8, 64);
for (var offset = 148; offset < source.Length; offset += 4) { source[offset] = 255; source[offset + 3] = 255; }
var hash = SHA256.HashData(source);
foreach (var target in new[] { DdsFormat.Bc1, DdsFormat.Bc1Srgb, DdsFormat.Bc2, DdsFormat.Bc2Srgb, DdsFormat.Bc3,
    DdsFormat.Bc3Srgb, DdsFormat.Bc4, DdsFormat.Bc4Snorm, DdsFormat.Bc5, DdsFormat.Bc5Snorm, DdsFormat.Bc7,
    DdsFormat.Bc7Srgb, DdsFormat.R8, DdsFormat.Rg8, DdsFormat.Rgba8, DdsFormat.Rgba8Srgb, DdsFormat.Bgra8, DdsFormat.Bgra8Srgb })
{
    var output = DdsCodec.Encode(source, target);
    var info = DdsParser.Parse(output, output.Length);
    Check(info.Format == target && info.Width == 4 && info.Height == 4 && info.Warnings.IsEmpty, $"native DDS encode and independent layout: {target}");
    var decoded = DdsCodec.DecodeMip(output, 0);
    Check(decoded.Rgba.Where((_, i) => i % 4 == 0).All(v => v > 0.95f), $"native DDS red-channel roundtrip: {target}");
    var restored = DdsCodec.Encode(output, DdsFormat.Rgba8);
    Check(DdsParser.Parse(restored, restored.Length).Format == DdsFormat.Rgba8, $"native DDS decode-to-uncompressed: {target}");
    if (target is DdsFormat.Rgba8 or DdsFormat.Rgba8Srgb)
        Check(source.AsSpan(148).SequenceEqual(output.AsSpan(148)), $"native DDS exact uncompressed pixels: {target}");
}
var bc1 = Fixture(DdsFormat.Bc1, 8);
bc1[148] = 0; bc1[149] = 0xf8; // RGB565 red endpoint, all sixteen 2-bit indices zero.
var red = DdsCodec.DecodeMip(bc1, 0).Rgba;
Check(red[0] == 1 && red[1] == 0 && red[2] == 0 && red[3] == 1, "independent BC1 block decodes to exact opaque red");
var signed = Fixture(DdsFormat.Bc5Snorm, 16);
signed[148] = 127; signed[156] = 129; // Signed endpoints +127 and -127, all indices zero.
var channels = DdsCodec.DecodeMip(signed, 0).Rgba;
Check(channels[0] == 1 && channels[1] == -1, "independent BC5 SNORM retains negative data values");
var chain = new byte[148 + 64 + 16 + 4];
source.AsSpan(0, 148).CopyTo(chain); Write(chain, 8, 0x21007); Write(chain, 28, 3); Write(chain, 108, 0x401008);
source.AsSpan(148).CopyTo(chain.AsSpan(148));
for (var offset = 212; offset < 228; offset += 4) { chain[offset + 1] = 255; chain[offset + 3] = 255; }
chain[230] = 255; chain[231] = 255;
var convertedChain = DdsCodec.Encode(chain, DdsFormat.Bc7);
Check(DdsParser.Parse(convertedChain, convertedChain.Length).MipLevels == 3, "native DDS preserves authored mip count");
Check(DdsCodec.DecodeMip(convertedChain, 1).Rgba[1] > 0.95f && DdsCodec.DecodeMip(convertedChain, 2).Rgba[2] > 0.95f,
    "native DDS converts each authored mip instead of regenerating it");
foreach (var format in new[] { DdsFormat.Bc6HUfloat, DdsFormat.Bc7Typeless })
{
    var rejected = false;
    try { DdsCodec.DecodeMip(Fixture(format, 16), 0); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, $"native DDS rejects unsupported {format} before decode");
}
Check(SHA256.HashData(source).SequenceEqual(hash), "native DDS preserves supplied source bytes");
using (var identity = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ContextSuite.Dds.Engine.json"))))
{
    var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Single(m => m.ModuleName == "ContextSuite.Dds.Native.dll");
    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module.FileName))) == identity.RootElement.GetProperty("nativeSha256").GetString(),
        "DDS actual loaded native module matches recorded candidate hash");
}
TexturePolicyContracts.Run(Check);
DdsAdapterContracts.Run(args[0], Check);
File.WriteAllText(Path.Combine(args[0], "result.txt"), $"Passed {passed} DDS codec contracts.\n");
Console.WriteLine($"Passed {passed} DDS codec contracts.");

void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    passed++; Console.WriteLine("PASS: " + name);
}
static byte[] Fixture(DdsFormat format, int payload)
{
    var bytes = new byte[148 + payload];
    "DDS "u8.CopyTo(bytes); Write(bytes, 4, 124); Write(bytes, 8, 0x1007); Write(bytes, 12, 4); Write(bytes, 16, 4);
    Write(bytes, 76, 32); Write(bytes, 80, 4); Write(bytes, 84, 0x30315844); Write(bytes, 108, 0x1000);
    Write(bytes, 128, (uint)format); Write(bytes, 132, 3); Write(bytes, 140, 1);
    return bytes;
}
static void Write(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
