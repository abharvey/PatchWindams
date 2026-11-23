using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PatchAdams.Core;
using PatchAdamsSharp;

internal static partial class Program
{
    private static void RunPerPatchFileTests(MainWindow window)
    {
        const string original = "src/target.ts";
        const string alternate = "src/other.ts";
        var baseDirectory = (string)GetField(window, "basePath")!;
        var paths = new[] { "a.patch", "b.patch", "c.patch" }
            .Select(name => Path.Combine(baseDirectory, "..", "per-patch", name)).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(paths[0])!);
        File.WriteAllText(paths[0], ModifiedFile(original, 800, 0));
        File.WriteAllText(paths[1], ModifiedFile(original, 800, 1));
        File.WriteAllText(paths[2], ModifiedFile(alternate, 240, 0));
        var blind = (MenuItem)window.FindName("BlindModeToggle");
        blind.IsChecked = false;
        SetField(window, "filePanelCollapsed", false);
        Invoke(window, "ApplySidePanelLayout");
        ((MenuItem)window.FindName("ScrollLockToggle")).IsChecked = false;
        var testsOnly = (CheckBox)window.FindName("TestsOnlyToggle");
        testsOnly.IsChecked = false;
        var load = (Task)Invoke(window, "LoadPatchFilesAsync", new object[] { paths })!;
        WaitUntil(() => load.IsCompleted, "three per-patch file fixtures load");
        load.GetAwaiter().GetResult();
        Pump();

        var tree = (TreeView)window.FindName("FileTree");
        var caption = (TextBlock)window.FindName("SelectedFileText");
        var editors = (IDictionary)GetField(window, "codeEditors")!;
        RichTextBox Editor(string label) => (RichTextBox)editors[label]!;
        TreeViewItem Row(string path) => (TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(
            tree.Items.Cast<object>().Single(item => item.GetType().GetProperty("Path")!.GetValue(item) as string == path));
        void CheckOpen(string a, string b, string c, string message) =>
            Check(EditorPath(Editor("A")) == a && EditorPath(Editor("B")) == b && EditorPath(Editor("C")) == c, message);
        void DoubleClick(object source)
        {
            tree.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = Control.PreviewMouseDoubleClickEvent, Source = source
            });
            Pump();
        }
        MenuItem? OpenInPatchMenu(TreeViewItem row)
        {
            var constructor = typeof(ContextMenuEventArgs).GetConstructor(Private, null,
                [typeof(object), typeof(bool), typeof(double), typeof(double)], null)!;
            var args = (ContextMenuEventArgs)constructor.Invoke([row, true, 1d, 1d]);
            row.RaiseEvent(args);
            return args.Handled ? null : (MenuItem)tree.ContextMenu.Items[0];
        }
        DragDropEffects Drag(string path, string label, bool drop)
        {
            var target = Editor(label);
            var entry = Row(path).Header;
            SetField(window, "draggedFile", entry);
            try
            {
                // WPF's OLE layer creates these args internally. Route the same preview events
                // from the code editor without moving the machine's mouse.
                var data = new DataObject("PatchAdams.FileTreeRow", path);
                var constructor = typeof(DragEventArgs).GetConstructors(Private).Single();
                var args = (DragEventArgs)constructor.Invoke(
                    [data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, target, new Point(12, 12)]);
                args.RoutedEvent = drop ? DragDrop.PreviewDropEvent : DragDrop.PreviewDragOverEvent;
                target.RaiseEvent(args);
                Check(args.Handled, "internal file drops never reach the external patch loader");
                return args.Effects;
            }
            finally
            {
                SetField(window, "draggedFile", null!);
                Pump();
            }
        }

        Row(original).IsSelected = true;
        CheckOpen(alternate, alternate, alternate, "selecting a row alone does not change any open file");
        DoubleClick(tree);
        CheckOpen(alternate, alternate, alternate, "double-clicking blank tree space does not open a file");
        DoubleClick(Descendants<TextBlock>(Row(original)).Single());
        CheckOpen(original, original, original, "double-click opens the shared file in all three patches");

        var a = Editor("A");
        var b = Editor("B");
        var aScroll = (ScrollViewer)a.Template.FindName("PART_ContentHost", a);
        var bScroll = (ScrollViewer)b.Template.FindName("PART_ContentHost", b);
        aScroll.ScrollToVerticalOffset(aScroll.ScrollableHeight / 3);
        bScroll.ScrollToVerticalOffset(bScroll.ScrollableHeight / 2);
        Pump();
        var aOffset = aScroll.VerticalOffset;
        var bOffset = bScroll.VerticalOffset;
        Check(aOffset > 0 && bOffset > 0, "untouched panes are scrolled before the drop");

        Row(alternate).IsSelected = true;
        Check(Drag(alternate, "A", drop: false) == DragDropEffects.None &&
              Drag(alternate, "A", drop: true) == DragDropEffects.None,
            "an unchanged file exists in A but cannot be dropped there");
        Check(Drag(alternate, "C", drop: false) == DragDropEffects.Copy,
            "C accepts the changed file");
        CheckOpen(original, original, original, "dragging without dropping leaves all files unchanged");
        Check(Drag(alternate, "C", drop: true) == DragDropEffects.Copy, "drop opens the target column");
        CheckOpen(original, original, alternate, "only C opens a different file after the drop");
        Check(ReferenceEquals(a, Editor("A")) && ReferenceEquals(b, Editor("B")) &&
              aScroll.VerticalOffset == aOffset && bScroll.VerticalOffset == bOffset,
            "the other two panes keep their editors and scroll positions");
        Check(caption.Text.Contains(original, StringComparison.Ordinal) &&
              Descendants<TextBlock>((DependencyObject)Editor("C").Parent).Any(text => text.IsVisible && text.Text == alternate),
            "the shared heading and overridden path identify what is open");
        testsOnly.IsChecked = true;
        CheckOpen(original, original, alternate, "filtering out a row does not close its file");
        testsOnly.IsChecked = false;
        Pump();

        var c = Editor("C");
        Check(c.AllowDrop, "the code pane participates in native WPF hit testing for drops");
        var cScroll = (ScrollViewer)c.Template.FindName("PART_ContentHost", c);
        var scrollLock = (MenuItem)window.FindName("ScrollLockToggle");
        var cOffset = cScroll.VerticalOffset;
        scrollLock.IsChecked = true;
        aScroll.ScrollToVerticalOffset(aScroll.ScrollableHeight * 0.8);
        Pump();
        Check(bScroll.VerticalOffset > bOffset && cScroll.VerticalOffset == cOffset,
            "scroll lock follows other panes showing the same path only");
        scrollLock.IsChecked = false;

        var clipboard = Clipboard.GetDataObject();
        try
        {
            Descendants<Button>((DependencyObject)c.Parent).Single(button => (string?)button.ToolTip == "copy code")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Clipboard.GetText().Contains("const other-100 = 100; // candidate 0", StringComparison.Ordinal),
                "copy code uses the overridden file");
        }
        finally
        {
            if (clipboard is null) Clipboard.Clear();
            else Clipboard.SetDataObject(clipboard, true);
        }

        var start = (Button)window.FindName("StartComparisonButton");
        var add = (Button)window.FindName("AddComparisonEvidenceButton");
        SelectPostLines(a, 100, 100);
        WaitUntil(() => start.IsEnabled, "selection in a shared pane generates comparison evidence");
        start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        SelectPostLines(Editor("C"), 100, 100);
        WaitUntil(() => add.IsEnabled, "selection in the overridden pane generates comparison evidence");
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var evidence = (List<ComparisonEvidence>)GetField(window, "draftEvidence")!;
        Check(evidence.Select(item => (item.Label, item.Path)).SequenceEqual([("A", original), ("C", alternate)]),
            "one comparison draft captures evidence from two real patch/file locations");

        var reset = Descendants<Button>((DependencyObject)Editor("C").Parent)
            .Single(button => (string?)button.ToolTip == "Use shared file");
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        CheckOpen(original, original, original, "Use shared file resets only the overridden column");
        Check(ReferenceEquals(a, Editor("A")) && evidence.Count == 2,
            "reset preserves other editors and the comparison draft");

        Drag(alternate, "C", drop: true);
        ((Button)window.FindName("NavigateDraftEvidenceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        CheckOpen(original, original, alternate, "navigating to draft evidence keeps the other paths open");

        Row(alternate).IsSelected = true;
        tree.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(tree),
            Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Pump();
        CheckOpen(alternate, alternate, alternate, "Enter opens the selected file across all patches");
        var openInPatch = OpenInPatchMenu(Row(original));
        Check(openInPatch?.Items.Cast<MenuItem>().Select(item => item.Header).SequenceEqual(["A — a.patch", "B — b.patch"]) == true,
            "the context menu offers only patches that changed the chosen row");
        ((MenuItem)openInPatch!.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Pump();
        CheckOpen(original, alternate, alternate, "the context menu opens just one patch");
        Drag(original, "A", drop: true);
        Drag(original, "B", drop: true);
        CheckOpen(original, original, alternate, "two overrides can open the same file");
        DoubleClick(Row(alternate));
        CheckOpen(alternate, alternate, alternate, "double-clicking the shared path clears all overrides");

        Drag(original, "A", drop: true);
        blind.IsChecked = true;
        Pump();
        CheckOpen(alternate, alternate, alternate, "blind review clears all overrides");
        Check(Drag(original, "A", drop: true) == DragDropEffects.None, "blind review blocks per-patch drops");
        Check(OpenInPatchMenu(Row(original)) is null, "blind review hides the per-patch context menu");
        DoubleClick(Row(original));
        CheckOpen(original, original, original, "shared file activation still works in blind review");
    }

    private static string EditorPath(RichTextBox editor) =>
        (string)editor.Tag.GetType().GetProperty("Path")!.GetValue(editor.Tag)!;
}
