using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace ContextSuite.Application.Infrastructure;

// Shared view behavior: disclosure changes fit the height again after a manual
// resize. Width stays under the user's control; oversized content still scrolls.
internal sealed class ContentWindowSizing
{
    private readonly Window _window;
    private readonly double _minimumHeight;
    private readonly double _maximumHeight;
    private bool _updating;

    internal ContentWindowSizing(Window window)
    {
        _window = window;
        _minimumHeight = window.MinHeight;
        _maximumHeight = window.MaxHeight;
        window.SizeToContent = SizeToContent.Height;
        window.SourceInitialized += (_, _) => UpdateBounds(false);
        window.Loaded += (_, _) => FitContent();
        window.SizeChanged += (_, _) => UpdateBounds(true);
        window.LocationChanged += (_, _) => UpdateBounds(false);
        window.DpiChanged += (_, _) => UpdateBounds(true);
        window.AddHandler(Expander.ExpandedEvent, new RoutedEventHandler(OnDisclosureChanged));
        window.AddHandler(Expander.CollapsedEvent, new RoutedEventHandler(OnDisclosureChanged));
        window.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(OnSelectionChanged));
    }

    private void OnDisclosureChanged(object sender, RoutedEventArgs e)
    {
        FitContent();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource is TabControl) FitContent();
    }

    private void FitContent()
    {
        if (_window.WindowState != WindowState.Normal) return;
        UpdateBounds(false);
        // WPF switches to Manual when the user drags a resize border.
        _window.SizeToContent = SizeToContent.Height;
        _window.InvalidateMeasure();
    }

    private void UpdateBounds(bool keepVisible)
    {
        if (_updating || _window.WindowState != WindowState.Normal) return;
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == 0 || HwndSource.FromHwnd(handle)?.CompositionTarget is not { } target) return;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor)) return;
        var scale = target.TransformFromDevice.M22;
        var availableHeight = (monitor.Work.Bottom - monitor.Work.Top) * scale;
        _updating = true;
        try
        {
            _window.MinHeight = Math.Min(_minimumHeight, availableHeight);
            _window.MaxHeight = Math.Min(_maximumHeight, availableHeight);
            // Use a physical-coordinate delta so mixed-DPI monitor origins do
            // not get mistaken for WPF's device-independent window coordinates.
            if (keepVisible && _window.IsLoaded && GetWindowRect(handle, out var bounds))
            {
                var top = Math.Clamp(bounds.Top, monitor.Work.Top,
                    Math.Max(monitor.Work.Top, monitor.Work.Bottom - (bounds.Bottom - bounds.Top)));
                if (top != bounds.Top) _window.Top += (top - bounds.Top) * scale;
            }
        }
        finally { _updating = false; }
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
