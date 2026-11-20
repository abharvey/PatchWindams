using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PatchAdamsSharp;

public partial class MainWindow
{
    private const int WmMouseHWheel = 0x020E;
    private const uint SpiGetWheelScrollChars = 0x006C;
    private const double WheelDelta = 120;
    // Match the 16-DIP line increment used by WPF ScrollViewer.LineLeft/LineRight.
    private const double HorizontalLineSize = 16;
    private readonly Dictionary<ScrollViewer, double> pendingHorizontalOffsets = [];

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(HandleHorizontalWheel);
    }

    private IntPtr HandleHorizontalWheel(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam,
        ref bool handled)
    {
        // WPF handles WM_MOUSEWHEEL, but does not expose WM_MOUSEHWHEEL as a routed event.
        if (message != WmMouseHWheel) return IntPtr.Zero;

        // Native wheel coordinates are signed screen pixels, not WPF device-independent units.
        var coordinates = lParam.ToInt64();
        var screenPoint = new Point(unchecked((short)coordinates), unchecked((short)(coordinates >> 16)));
        DependencyObject? hit = null;
        VisualTreeHelper.HitTest(this,
            element => element is UIElement ui && (!ui.IsVisible || !ui.IsHitTestVisible || !ui.IsEnabled)
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue,
            result =>
            {
                hit = result.VisualHit;
                return HitTestResultBehavior.Stop;
            },
            new PointHitTestParameters(PointFromScreen(screenPoint)));
        for (var element = hit; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is not ScrollViewer viewer ||
                viewer.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled ||
                viewer.ScrollableWidth <= 0)
                continue;

            var delta = unchecked((short)(wParam.ToInt64() >> 16));
            var characters = SystemParametersInfo(SpiGetWheelScrollChars, 0, out var configured, 0)
                ? configured : 3u;
            var distance = characters == uint.MaxValue
                ? viewer.ViewportWidth : characters * HorizontalLineSize;
            var alreadyQueued = pendingHorizontalOffsets.TryGetValue(viewer, out var offset);
            if (!alreadyQueued) offset = viewer.HorizontalOffset;
            // Positive WM_MOUSEHWHEEL deltas scroll right. Keep fractional deltas from touchpads.
            pendingHorizontalOffsets[viewer] = Math.Clamp(
                offset + delta / WheelDelta * distance, 0, viewer.ScrollableWidth);
            if (!alreadyQueued)
            {
                // ScrollToHorizontalOffset is deferred by WPF. Accumulate a burst before applying it
                // so successive messages don't all start from the same stale HorizontalOffset.
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    var target = pendingHorizontalOffsets[viewer];
                    pendingHorizontalOffsets.Remove(viewer);
                    if (viewer.IsLoaded) viewer.ScrollToHorizontalOffset(target);
                }));
            }
            handled = true;
            break;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out uint value, uint flags);
}
