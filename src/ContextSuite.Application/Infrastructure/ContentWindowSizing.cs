using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ContextSuite.Application.Infrastructure;

// Shared view behavior: disclosure changes fit the height again after a manual
// resize. Width stays under the user's control; oversized content still scrolls.
internal sealed class ContentWindowSizing
{
    private readonly Window _window;
    private readonly double _minimumHeight;
    private readonly double _maximumHeight;
    private readonly bool _fitContent;
    private readonly bool _fitBelowCurrentTop;
    private readonly bool _initialFitOnly;
    private bool _userPositioned;
    private bool _updating;
    private bool _boundsQueued;

    internal ContentWindowSizing(Window window, bool fitContent = true, bool fitBelowCurrentTop = false, bool initialFitOnly = false)
    {
        _window = window;
        _minimumHeight = window.MinHeight;
        _maximumHeight = window.MaxHeight;
        _fitContent = fitContent;
        _fitBelowCurrentTop = fitBelowCurrentTop;
        _initialFitOnly = initialFitOnly;
        window.SizeToContent = fitContent ? SizeToContent.Height : SizeToContent.Manual;
        window.SourceInitialized += (_, _) =>
        {
            UpdateBounds(false);
            if (_initialFitOnly)
                HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(OnWindowMessage);
        };
        window.StateChanged += (_, _) =>
        {
            if (_initialFitOnly && window.WindowState != WindowState.Normal) ReleaseBounds();
        };
        window.Loaded += (_, _) => FitContent();
        window.SizeChanged += (_, _) => QueueBounds();
        // Recalculate the budget during a move, but do not snap a dragged window
        // back onto its old monitor before it can cross to another one.
        window.LocationChanged += (_, _) => UpdateBounds(false);
        window.DpiChanged += (_, _) => { UpdateBounds(false); QueueBounds(); };
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
        if (_userPositioned || !_fitContent || _window.WindowState != WindowState.Normal) return;
        UpdateBounds(false);
        // WPF switches to Manual when the user drags a resize border.
        _window.SizeToContent = SizeToContent.Height;
        _window.InvalidateMeasure();
        QueueBounds();
    }

    private nint OnWindowMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        // WM_ENTERSIZEMOVE arrives before native title-bar dragging or resizing.
        // Release the temporary fit before any move can trigger a new clamp.
        if (message == 0x0231 || message == 0x0112 && ((long)wParam & 0xfff0) == 0xf030) ReleaseBounds();
        return 0;
    }

    private void ReleaseBounds()
    {
        if (_userPositioned) return;
        _userPositioned = true;
        var height = _window.WindowState == WindowState.Normal ? _window.ActualHeight : _window.RestoreBounds.Height;
        _window.SizeToContent = SizeToContent.Manual;
        if (height > 0) _window.Height = height;
        _window.MinHeight = _minimumHeight;
        _window.MaxHeight = _maximumHeight;
    }

    private void QueueBounds()
    {
        if (_userPositioned || _boundsQueued) return;
        _boundsQueued = true;
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _boundsQueued = false;
            // SizeChanged can precede the final native resize. Check the actual
            // window rectangle after layout, not the old HWND bounds.
            UpdateBounds(true);
        }));
    }

    private void UpdateBounds(bool keepVisible)
    {
        if (_userPositioned || _updating || _window.WindowState != WindowState.Normal) return;
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == 0 || HwndSource.FromHwnd(handle)?.CompositionTarget is not { } target) return;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor)) return;
        var scale = target.TransformFromDevice.M22;
        var availableHeight = (monitor.Work.Bottom - monitor.Work.Top) * scale;
        var margin = _fitBelowCurrentTop ? 8.0 : 0;
        availableHeight = Math.Max(1, availableHeight - 2 * margin);
        var hasBounds = GetWindowRect(handle, out var bounds);
        var maximumHeight = availableHeight;
        if (_fitBelowCurrentTop && hasBounds)
            maximumHeight = Math.Min(availableHeight,
                Math.Max(Math.Min(_minimumHeight, availableHeight), (monitor.Work.Bottom - bounds.Top) * scale - margin));
        _updating = true;
        try
        {
            _window.MinHeight = Math.Min(_minimumHeight, availableHeight);
            _window.MaxHeight = Math.Min(_maximumHeight, maximumHeight);
            // Use a physical-coordinate delta so mixed-DPI monitor origins do
            // not get mistaken for WPF's device-independent window coordinates.
            if (keepVisible && _window.IsLoaded && hasBounds)
            {
                var inset = (int)Math.Ceiling(margin / scale);
                var height = Math.Min(bounds.Bottom - bounds.Top, _window.MaxHeight / scale);
                var top = Math.Clamp(bounds.Top, monitor.Work.Top + inset,
                    Math.Max(monitor.Work.Top + inset, monitor.Work.Bottom - inset - height));
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
