using System.Runtime.InteropServices;
using ContextSuite.Core.Tiff;

namespace ContextSuite.Private.Tiff;

internal static class WicTiffDecoder
{
    internal static byte[] Read(byte[] bytes, int pageCount, TiffPageFacts page, CancellationToken token)
    {
        page.Validate();
        token.ThrowIfCancellationRequested();
        // Explicit built-in codec and converter CLSIDs: do not discover or load
        // arbitrary registered image decoders based on untrusted file contents.
        var initialized = CoInitializeEx(IntPtr.Zero, 0);
        if (initialized < 0 && initialized != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(initialized);
        IntPtr stream = IntPtr.Zero, decoder = IntPtr.Zero, frame = IntPtr.Zero, converter = IntPtr.Zero;
        try
        {
            stream = SHCreateMemStream(bytes, (uint)bytes.Length);
            if (stream == IntPtr.Zero) throw new OutOfMemoryException("Could not create bounded TIFF stream.");
            var decoderClass = new Guid("b54e85d9-fe23-499f-8b88-6acea713752b");
            var decoderInterface = new Guid("9edde9e7-8dee-47ea-99df-e6faf2ed44bf");
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref decoderClass, IntPtr.Zero, 1, ref decoderInterface, out decoder));
            Marshal.ThrowExceptionForHR(Method<InitializeDecoder>(decoder, 4)(decoder, stream, 0));
            Marshal.ThrowExceptionForHR(Method<FrameCount>(decoder, 12)(decoder, out var count));
            if (count != pageCount) throw new InvalidDataException("Windows TIFF decoder did not retain every directory page.");
            Marshal.ThrowExceptionForHR(Method<GetFrame>(decoder, 13)(decoder, (uint)page.Index, out frame));
            Marshal.ThrowExceptionForHR(Method<GetSize>(frame, 3)(frame, out var width, out var height));
            if (width != page.Width || height != page.Height) throw new InvalidDataException("Decoded TIFF dimensions differ from its directory.");
            var converterClass = new Guid("1a3f11dc-b514-4b17-8c5f-2154513852f1");
            var converterInterface = new Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94");
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref converterClass, IntPtr.Zero, 1, ref converterInterface, out converter));
            var bgra = new Guid("6fddc324-4e03-4bfe-b185-3d77768dc90f");
            Marshal.ThrowExceptionForHR(Method<InitializeConverter>(converter, 8)(converter, frame, ref bgra, 0, IntPtr.Zero, 0, 0));
            var stride = checked(width * 4);
            var pixels = new byte[checked((int)(stride * height))];
            var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                var copy = Method<CopyPixels>(converter, 7);
                for (var y = 0u; y < height; y += 32)
                {
                    token.ThrowIfCancellationRequested();
                    var rows = Math.Min(32u, height - y);
                    var rectangle = new WicRectangle { X = 0, Y = (int)y, Width = (int)width, Height = (int)rows };
                    Marshal.ThrowExceptionForHR(copy(converter, ref rectangle, stride, checked(stride * rows),
                        IntPtr.Add(pinned.AddrOfPinnedObject(), checked((int)(stride * y)))));
                }
            }
            finally { pinned.Free(); }
            token.ThrowIfCancellationRequested();
            return pixels;
        }
        finally
        {
            if (converter != IntPtr.Zero) Marshal.Release(converter);
            if (frame != IntPtr.Zero) Marshal.Release(frame);
            if (decoder != IntPtr.Zero) Marshal.Release(decoder);
            if (stream != IntPtr.Zero) Marshal.Release(stream);
            if (initialized >= 0) CoUninitialize();
        }
    }

    private static T Method<T>(IntPtr instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [StructLayout(LayoutKind.Sequential)]
    private struct WicRectangle { public int X, Y, Width, Height; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int InitializeDecoder(IntPtr self, IntPtr stream, uint cache);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FrameCount(IntPtr self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetFrame(IntPtr self, uint index, out IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetSize(IntPtr self, out uint width, out uint height);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int InitializeConverter(IntPtr self, IntPtr source, ref Guid format, uint dither, IntPtr palette, double threshold, uint translate);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CopyPixels(IntPtr self, ref WicRectangle rectangle, uint stride, uint size, IntPtr buffer);
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr instance);
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();
    [DllImport("shlwapi.dll", ExactSpelling = true)]
    private static extern IntPtr SHCreateMemStream(byte[] bytes, uint length);
}
