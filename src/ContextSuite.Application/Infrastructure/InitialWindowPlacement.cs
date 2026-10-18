using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ContextSuite.Application.Infrastructure;

// Explicit opening/disclosure layouts only. Never hooks movement or resizing;
// Windows owns geometry between those user actions.
internal static class InitialWindowPlacement
{
    internal static void Apply(Window window, bool expanded)
    {
        if (window.WindowState != WindowState.Normal) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0 || HwndSource.FromHwnd(handle)?.CompositionTarget is not { } target ||
            !GetWindowRect(handle, out var bounds)) return;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor)) return;
        var scale = target.TransformFromDevice.M22;
        var workHeight = (monitor.Work.Bottom - monitor.Work.Top) * scale;
        var availableHeight = Math.Max(1, workHeight - 16);
        var expandedHeight = Math.Clamp(workHeight * 0.82,
            Math.Min(window.MinHeight, availableHeight), Math.Min(window.MaxHeight, availableHeight));
        var height = expanded ? expandedHeight : Math.Clamp(window.Height,
            Math.Min(window.MinHeight, availableHeight), expandedHeight);
        window.SizeToContent = SizeToContent.Manual;
        window.MinHeight = Math.Min(window.MinHeight, availableHeight);
        window.Height = height;
        // Use a physical delta rather than interpreting a mixed-DPI monitor's
        // desktop origin as a WPF device-independent coordinate.
        // Both disclosure states share the expanded window's starting top.
        var top = monitor.Work.Top + (workHeight - expandedHeight) / (2 * scale);
        window.Top += (top - bounds.Top) * scale;
    }

    internal static void ResizeForDetails(Window window, bool expanded, double expandedHeight)
    {
        if (window.WindowState != WindowState.Normal) return;
        var height = expanded ? expandedHeight : Math.Min(570, window.Height);
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target &&
            GetWindowRect(handle, out var bounds) && GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor))
        {
            var scale = target.TransformFromDevice.M22;
            if (double.IsNaN(height)) height = (monitor.Work.Bottom - monitor.Work.Top) * scale * 0.82;
            // Fit an explicit expansion below its current top when possible.
            // Do not pull a deliberately moved window back onto the monitor.
            var available = (monitor.Work.Bottom - bounds.Top) * scale - 8;
            if (expanded && bounds.Top >= monitor.Work.Top && available >= window.MinHeight)
                height = Math.Min(height, available);
        }
        if (!double.IsFinite(height)) height = 570;
        window.Height = Math.Clamp(height, window.MinHeight, window.MaxHeight);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect bounds);
}
