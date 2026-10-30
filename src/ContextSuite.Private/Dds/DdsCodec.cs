using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ContextSuite.Core.Dds;

namespace ContextSuite.Private.Dds;

public static class DdsCodec
{
    private static readonly nint Library = LoadLibrary();
    private static readonly ReadMip Read = Marshal.GetDelegateForFunctionPointer<ReadMip>(NativeLibrary.GetExport(Library, "CsDdsReadMip"));
    private static readonly EncodeImage EncodeNative = Marshal.GetDelegateForFunctionPointer<EncodeImage>(NativeLibrary.GetExport(Library, "CsDdsEncode"));
    private static readonly EncodePixels EncodeFloat = Marshal.GetDelegateForFunctionPointer<EncodePixels>(NativeLibrary.GetExport(Library, "CsDdsEncodePixels"));
    private static readonly FreeImage Free = Marshal.GetDelegateForFunctionPointer<FreeImage>(NativeLibrary.GetExport(Library, "CsDdsFree"));

    public static (uint Width, uint Height, float[] Rgba) DecodeMip(byte[] dds, uint mip)
    {
        var info = Validate(dds);
        if (mip >= info.MipLevels) throw new ArgumentOutOfRangeException(nameof(mip));
        var result = default(NativeResult);
        try
        {
            Marshal.ThrowExceptionForHR(Read(dds, (nuint)dds.Length, mip, out result));
            var width = Math.Max(1, info.Width >> (int)mip); var height = Math.Max(1, info.Height >> (int)mip);
            if (result.Width != width || result.Height != height || result.Length != (nuint)((ulong)width * height * 16) ||
                result.Length > 40_000_000 * 16 || result.Bytes == 0) throw new InvalidDataException("Invalid DDS decoded pixel result.");
            var rgba = new float[checked((int)result.Length / 4)];
            Marshal.Copy(result.Bytes, rgba, 0, rgba.Length);
            if (rgba.Any(value => !float.IsFinite(value))) throw new InvalidDataException("DDS decoder returned non-finite pixels.");
            return (width, height, rgba);
        }
        finally { if (result.Bytes != 0) Free(result.Bytes); }
    }

    // Representation-only primitive. Higher-level plans must explicitly settle
    // color interpretation, channels, alpha and mip policy before invoking it.
    public static byte[] Encode(byte[] sourceDds, DdsFormat target, float threshold = 0.5f, uint quality = 1)
    {
        var source = Validate(sourceDds);
        if (!Enum.IsDefined(target) || !float.IsFinite(threshold) || threshold is < 0 or > 1 || quality > 2)
            throw new ArgumentException("Invalid DDS codec options.");
        var result = default(NativeResult);
        try
        {
            Marshal.ThrowExceptionForHR(EncodeNative(sourceDds, (nuint)sourceDds.Length, (uint)target, threshold, quality, out result));
            if (result.Bytes == 0 || result.Length is < 148 or > 128 * 1024 * 1024) throw new InvalidDataException("Invalid DDS encoder result.");
            var output = new byte[checked((int)result.Length)];
            Marshal.Copy(result.Bytes, output, 0, output.Length);
            var info = Validate(output);
            if (info.Format != target || info.Width != source.Width || info.Height != source.Height || info.MipLevels != source.MipLevels)
                throw new InvalidDataException("DDS output does not match its representation request.");
            return output;
        }
        finally { if (result.Bytes != 0) Free(result.Bytes); }
    }

    public static byte[] EncodeMipPixels(float[] pixels, uint width, uint height, uint mips, DdsFormat format,
        float threshold, uint quality, DdsAlphaMode alphaMode)
    {
        if (pixels.LongLength > 40_000_000L * 4 || pixels.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Invalid or oversized DDS pixel buffer.");
        var result = default(NativeResult);
        try
        {
            Marshal.ThrowExceptionForHR(EncodeFloat(pixels, (nuint)pixels.Length, width, height, mips, (uint)format, threshold, quality, (uint)alphaMode, out result));
            if (result.Bytes == 0 || result.Length is < 148 or > 128 * 1024 * 1024) throw new InvalidDataException("Invalid DDS encoder result.");
            var output = new byte[checked((int)result.Length)];
            Marshal.Copy(result.Bytes, output, 0, output.Length);
            var info = Validate(output);
            if (info.Format != format || info.Width != width || info.Height != height || info.MipLevels != mips || info.RawAlphaMode != (uint)alphaMode)
                throw new InvalidDataException("DDS encoded mip chain differs from its plan.");
            return output;
        }
        finally { if (result.Bytes != 0) Free(result.Bytes); }
    }

    private static DdsInfo Validate(byte[] dds)
    {
        var info = DdsParser.Parse(dds.AsSpan(0, Math.Min(dds.Length, DdsParser.MaximumHeaderBytes)), dds.Length);
        if (!info.IsSupported2D) throw new InvalidDataException("DDS codec requires an admitted typed, non-HDR, ordinary 2D texture.");
        return info;
    }

    private static nint LoadLibrary()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ContextSuite.Dds.Native.dll");
        using var identity = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ContextSuite.Dds.Engine.json")));
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) != identity.RootElement.GetProperty("nativeSha256").GetString())
            throw new InvalidDataException("DDS engine identity mismatch.");
        var library = NativeLibrary.Load(path);
        try
        {
            var abi = Marshal.GetDelegateForFunctionPointer<Abi>(NativeLibrary.GetExport(library, "CsDdsAbi"));
            if (abi() != 1) throw new InvalidDataException("DDS bridge ABI mismatch.");
            return library;
        }
        catch { NativeLibrary.Free(library); throw; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeResult { public nint Bytes; public nuint Length; public uint Width; public uint Height; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint Abi();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReadMip(byte[] bytes, nuint length, uint mip, out NativeResult result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EncodeImage(byte[] bytes, nuint length, uint format, float threshold, uint quality, out NativeResult result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EncodePixels(float[] pixels, nuint count, uint width, uint height, uint mips, uint format, float threshold, uint quality, uint alphaMode, out NativeResult result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FreeImage(nint bytes);
}
