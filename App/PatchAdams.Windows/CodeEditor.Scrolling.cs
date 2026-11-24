using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PatchAdamsSharp;

internal sealed partial class CodeEditor
{
    private const double VerticalWheelDelta = 120;
    // WPF's physical scrolling uses a 16-DIP line increment.
    private const double VerticalLineSize = 16;
    private DispatcherOperation? verticalScrollOperation;
    private ScrollViewer? pendingVerticalViewer;
    private double pendingVerticalOffset;

    private void InitializeScrolling()
    {
        Unloaded += (_, _) => CancelPendingVerticalScroll();
        TextChanged += (_, _) => CancelPendingVerticalScroll();
        PreviewMouseDown += (_, _) => CancelPendingVerticalScroll();
        PreviewKeyDown += (_, _) => CancelPendingVerticalScroll();
        AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        {
            // Keep accumulating until WPF has actually committed the target, not just until
            // our dispatcher callback runs: more input can arrive before the next layout.
            if (e.OriginalSource == pendingVerticalViewer && verticalScrollOperation is null &&
                Math.Abs(e.VerticalOffset - pendingVerticalOffset) < 0.01)
                pendingVerticalViewer = null;
        }));
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        // Leave modified wheel commands (such as Ctrl+wheel zoom) to WPF.
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None) return;
        var viewer = FindViewer();
        if (viewer is null || viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled ||
            viewer.ScrollableHeight <= 0)
            return;

        e.Handled = true;
        if (e.Delta == 0) return;
        if (pendingVerticalViewer != viewer)
        {
            CancelPendingVerticalScroll();
            pendingVerticalViewer = viewer;
            pendingVerticalOffset = viewer.VerticalOffset;
        }

        var lines = SystemParameters.WheelScrollLines;
        var distance = lines < 0 ? viewer.ViewportHeight : lines * VerticalLineSize;
        // ScrollViewer's default handler uses only the delta's sign, turning tiny touchpad
        // movements into full multi-line steps. Scale by magnitude instead: a normal mouse
        // notch keeps its distance, while high-resolution input moves proportionally.
        pendingVerticalOffset = Math.Clamp(
            pendingVerticalOffset - e.Delta / VerticalWheelDelta * distance, 0, viewer.ScrollableHeight);
        if (verticalScrollOperation is not null) return;
        verticalScrollOperation = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            var target = pendingVerticalOffset;
            verticalScrollOperation = null;
            // WPF defers offset changes. Apply one accumulated target per input burst instead
            // of letting each wheel message calculate from the same stale VerticalOffset.
            if (!viewer.IsLoaded)
                pendingVerticalViewer = null;
            else
            {
                if (target == viewer.VerticalOffset) pendingVerticalViewer = null;
                // Even a target equal to the visible offset must replace an earlier queued
                // request when the user reverses direction before layout has caught up.
                viewer.ScrollToVerticalOffset(target);
            }
        }));
    }

    private void CancelPendingVerticalScroll()
    {
        verticalScrollOperation?.Abort();
        verticalScrollOperation = null;
        pendingVerticalViewer = null;
    }
}
