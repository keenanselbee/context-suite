using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ContextSuite.Core.Images;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Images;

// The separately built static libavif/AOM encoder is loaded only on demand.
// The existing independent HEIF reader remains the output validation engine.
internal sealed class AvifCodec
{
    private const string LibraryName = "ContextSuite.Avif.Native.dll";
    private const string LibrarySha256 = "D31E4A28AE6C9CD73E6CF96A86805BFC22915329D600A73D420E991239C6B77B";
    private static readonly Lazy<AvifCodec> Instance = new(() => new(Path.Combine(AppContext.BaseDirectory, "avif")));
    private readonly RuntimeDirectoryLeases _directories = new();
    private readonly FileStream _lease;
    private readonly nint _library;
    private readonly EncodeNative _encode;
    private readonly LengthNative _length;
    private readonly CopyNative _copy;
    private readonly ReleaseNative _release;

    private AvifCodec(string directory)
    {
        FileStream? lease = null;
        try
        {
            _directories.Open(directory);
            if (!Directory.EnumerateFiles(directory, "*.dll").Select(Path.GetFileName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals([LibraryName]))
                throw new InvalidDataException("AVIF encoder library membership mismatch.");
            var path = Path.Combine(directory, LibraryName);
            lease = _directories.OpenFileRead(path);
            if (Convert.ToHexString(SHA256.HashData(lease)) != LibrarySha256)
                throw new InvalidDataException("AVIF encoder identity mismatch.");
            using (var process = Process.GetCurrentProcess())
                if (process.Modules.Cast<ProcessModule>().Any(module => module.ModuleName.Equals(LibraryName, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("An AVIF encoder is already loaded outside this lifetime.");
            _library = NativeLibrary.Load(path, typeof(AvifCodec).Assembly,
                DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
            using (var process = Process.GetCurrentProcess())
            {
                var modules = process.Modules.Cast<ProcessModule>().Where(module => module.ModuleName.Equals(LibraryName, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (modules.Length != 1 || !string.Equals(modules[0].FileName, path, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Loaded AVIF encoder path differs from the verified payload.");
            }
            if (Marshal.GetDelegateForFunctionPointer<AbiNative>(NativeLibrary.GetExport(_library, "cs_avif_abi"))() != 1)
                throw new InvalidDataException("AVIF encoder ABI mismatch.");
            _encode = Marshal.GetDelegateForFunctionPointer<EncodeNative>(NativeLibrary.GetExport(_library, "cs_avif_encode"));
            _length = Marshal.GetDelegateForFunctionPointer<LengthNative>(NativeLibrary.GetExport(_library, "cs_avif_length"));
            _copy = Marshal.GetDelegateForFunctionPointer<CopyNative>(NativeLibrary.GetExport(_library, "cs_avif_copy"));
            _release = Marshal.GetDelegateForFunctionPointer<ReleaseNative>(NativeLibrary.GetExport(_library, "cs_avif_release"));
            _lease = lease;
        }
        catch
        {
            if (_library != 0) NativeLibrary.Free(_library);
            lease?.Dispose(); _directories.Dispose(); throw;
        }
    }

    internal static void EnsureAvailable()
    {
        try { _ = Instance.Value; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or Win32Exception)
        { throw new ImageFailureException(ImageFailure.EngineUnavailable, error); }
        HeifCodec.EnsureAvailable();
    }

    internal static byte[] Encode(uint width, uint height, byte[] rgba, byte[] icc, byte[] exif, byte[] xmp, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (width is 0 or > 16384 || height is 0 or > 16384 || (ulong)width * height > 40_000_000 ||
            rgba.LongLength != (long)width * height * 4 || icc.Length is < 132 or > 4 * 1024 * 1024 ||
            exif.Length > 4 * 1024 * 1024 || xmp.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("AVIF encoding input exceeds its bounds.");
        EnsureAvailable();
        var codec = Instance.Value;
        Cancel callback = _ => token.IsCancellationRequested ? 1 : 0;
        nint result = 0;
        try
        {
            Check(codec._encode(rgba, (ulong)rgba.Length, (int)width, (int)height, checked((int)width * 4),
                icc, (ulong)icc.Length, exif.Length == 0 ? null : exif, (ulong)exif.Length,
                xmp.Length == 0 ? null : xmp, (ulong)xmp.Length, callback, 0, out result));
            if (result == 0) throw new ImageFailureException(ImageFailure.EngineFailure);
            Check(codec._length(result, out var length));
            if (length is < 16 or > 128 * 1024 * 1024) throw new ImageFailureException(ImageFailure.ResourceLimit);
            var encoded = new byte[(int)length];
            Check(codec._copy(result, encoded, length));
            return encoded;
        }
        finally
        {
            if (result != 0) codec._release(result);
            GC.KeepAlive(callback);
        }

        void Check(int status)
        {
            token.ThrowIfCancellationRequested();
            if (status == 5) throw new OperationCanceledException(token);
            if (status != 0) throw new ImageFailureException(status switch
            { 1 or 2 => ImageFailure.InvalidInput, 3 => ImageFailure.UnsupportedInput, 4 => ImageFailure.ResourceLimit, _ => ImageFailure.EngineFailure });
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AbiNative();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Cancel(nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EncodeNative(byte[] rgba, ulong rgbaBytes, int width, int height, int stride,
        byte[] icc, ulong iccBytes, byte[]? exif, ulong exifBytes, byte[]? xmp, ulong xmpBytes, Cancel cancel, nint context, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LengthNative(nint result, out ulong length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CopyNative(nint result, [Out] byte[] destination, ulong length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ReleaseNative(nint result);
}
