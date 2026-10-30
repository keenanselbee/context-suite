using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.ComponentModel;
using System.Diagnostics;
using ContextSuite.Core.Images;
using ContextSuite.Private.IO;

namespace ContextSuite.Private.Images;

// Loaded only on a worker decode request. Packaging/menu adoption is separate.
internal sealed class HeifCodec
{
    private static readonly Lazy<HeifCodec> Instance = new(() => new(Path.Combine(AppContext.BaseDirectory, "heif")));
    private readonly List<FileStream> _leases = [];
    private readonly RuntimeDirectoryLeases _directories = new();
    private readonly nint _library;
    private readonly DecodeNative _decode;
    private readonly GetInfo _info;
    private readonly CopyBlock _copy;
    private readonly Release _release;
    private static readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ContextSuite.Heif.Native.dll"] = "798A9EC3D940E013B9C9B07A54A9B1F525513C35353AD8E9AB9BC2738C12E013",
        ["heif.dll"] = "709AA42349807BDD790E36AC05C163C577C02260ADD1955AF5820DAD6EA0BE4A",
        ["libde265.dll"] = "E95DFC0405C9DE6C22B730BDFE8A01B20AA1F2B8F44C99FB3D5B7A9398C8F78C"
    };

    private HeifCodec(string directory)
    {
        try
        {
            _directories.Open(directory);
            var selected = LocalLibraryManifest.Read(directory, "ContextSuite.Heif.Native.dll", Files, _directories, _leases);
            if (!Directory.EnumerateFiles(directory, "*.dll").Select(Path.GetFileName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(selected.Keys))
                throw new InvalidDataException("HEIF engine library membership mismatch.");
            foreach (var entry in selected)
            {
                var path = Path.Combine(directory, entry.Key);
                var lease = _directories.OpenFileRead(path);
                _leases.Add(lease);
                if (Convert.ToHexString(SHA256.HashData(lease)) != entry.Value) throw new InvalidDataException("HEIF engine identity mismatch.");
            }
            using (var process = Process.GetCurrentProcess())
                if (process.Modules.Cast<ProcessModule>().Any(module => selected.ContainsKey(module.ModuleName)))
                    throw new InvalidDataException("A HEIF dependency is already loaded outside this decoder lifetime.");
            _library = NativeLibrary.Load(Path.Combine(directory, "ContextSuite.Heif.Native.dll"), typeof(HeifCodec).Assembly,
                DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
            using (var process = Process.GetCurrentProcess())
                foreach (var name in selected.Keys)
                {
                    var modules = process.Modules.Cast<ProcessModule>().Where(module => string.Equals(module.ModuleName, name, StringComparison.OrdinalIgnoreCase)).ToArray();
                    // Extra declared dependencies may be delay-loaded or unused.
                    if (modules.Length == 0 && !Files.ContainsKey(name)) continue;
                    if (modules.Length != 1 || !string.Equals(modules[0].FileName, Path.Combine(directory, name), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Loaded HEIF dependency path differs from the verified payload.");
                }
            _decode = Marshal.GetDelegateForFunctionPointer<DecodeNative>(NativeLibrary.GetExport(_library, "cs_heif_decode"));
            _info = Marshal.GetDelegateForFunctionPointer<GetInfo>(NativeLibrary.GetExport(_library, "cs_heif_info"));
            _copy = Marshal.GetDelegateForFunctionPointer<CopyBlock>(NativeLibrary.GetExport(_library, "cs_heif_copy"));
            _release = Marshal.GetDelegateForFunctionPointer<Release>(NativeLibrary.GetExport(_library, "cs_heif_release"));
        }
        catch
        {
            if (_library != 0) NativeLibrary.Free(_library);
            foreach (var lease in _leases) lease.Dispose();
            _directories.Dispose();
            throw;
        }
        // Native library and immutable file leases live for the sequential worker lifetime.
    }

    internal sealed record Decoded(NativeInfo Info, byte[] Pixels, byte[] Icc, byte[] Exif, byte[] Xmp);

    internal static void EnsureAvailable()
    {
        try { _ = Instance.Value; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or Win32Exception)
        {
            throw new ImageFailureException(ImageFailure.EngineUnavailable, error);
        }
    }

    internal static Decoded Decode(byte[] source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Length is < 12 or > ImageContainer.MaximumFileBytes) throw new InvalidDataException("HEIF input size exceeds its bounds.");
        EnsureAvailable();
        var codec = Instance.Value;
        Cancel callback = _ => cancellationToken.IsCancellationRequested ? 1 : 0;
        nint result = 0;
        try
        {
            Check(codec._decode(source, (ulong)source.Length, callback, 0, out result));
            if (result == 0) throw new InvalidDataException("The HEIF decoder returned no result.");
            Check(codec._info(result, out var info, (uint)Marshal.SizeOf<NativeInfo>()));
            if (info.Abi != 3 || info.Width is 0 or > 16384 || info.Height is 0 or > 16384 ||
                (ulong)info.Width * info.Height > 40_000_000 || info.Bits is not (8 or 10 or 12) ||
                info.IccBytes > 4 * 1024 * 1024 || info.ExifBytes > 4 * 1024 * 1024 || info.XmpBytes > 4 * 1024 * 1024)
                throw new InvalidDataException("HEIF decoded information exceeds its bounds.");
            return new(info, Copy(0, checked(info.Width * info.Height * 8)), Copy(1, info.IccBytes), Copy(2, info.ExifBytes), Copy(3, info.XmpBytes));

            byte[] Copy(uint block, uint length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = new byte[checked((int)length)];
                Check(codec._copy(result, block, bytes, length));
                return bytes;
            }
        }
        finally
        {
            if (result != 0) codec._release(result);
            GC.KeepAlive(callback);
        }

        void Check(int status)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (status == 5) throw new OperationCanceledException(cancellationToken);
            if (status != 0) throw new ImageFailureException(status switch
            {
                1 or 2 => ImageFailure.InvalidInput,
                3 => ImageFailure.UnsupportedInput,
                4 => ImageFailure.ResourceLimit,
                _ => ImageFailure.EngineFailure
            });
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeInfo
    {
        public uint Abi, Width, Height, Bits, Primaries, Transfer, Matrix, FullRange;
        public uint ItemType, Flags, IccBytes, ExifBytes, XmpBytes, Codec;
        public uint EffectivePrimaries, EffectiveTransfer, EffectiveMatrix, EffectiveFullRange;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Cancel(nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DecodeNative(byte[] bytes, ulong length, Cancel callback, nint context, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetInfo(nint result, out NativeInfo info, uint size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CopyBlock(nint result, uint block, [Out] byte[] bytes, ulong length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Release(nint result);
}
