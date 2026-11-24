using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PatchAdams.Core;

namespace PatchAdamsSharp;

public partial class MainWindow
{
    private const string FileRowDragFormat = "PatchAdams.FileTreeRow";
    private string sharedPath = "";
    private string? pendingFileTreePath;
    private (FileTreeEntry Entry, Point Origin)? fileDragStart;
    private FileTreeEntry? draggedFile;

    private string OpenPath(LoadedColumn column) => column.FileOverride ?? sharedPath;
    private bool HasFileOverrides => columns.Any(column => column.FileOverride is not null);

    private void FileTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        fileDragStart = null;
        if (e.ClickCount == 1 && BlindModeToggle.IsChecked != true &&
            FileTreeRowAt(e.OriginalSource) is { Header: FileTreeEntry entry })
            fileDragStart = (entry, e.GetPosition(this));
    }

    private void FileTree_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            FileTreeRowAt(e.OriginalSource) is not { Header: FileTreeEntry entry }) return;
        OpenSharedFile(entry.Path);
        e.Handled = true;
    }

    private void FileTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || FileTree.SelectedItem is not FileTreeEntry entry) return;
        OpenSharedFile(entry.Path);
        e.Handled = true;
    }

    private TreeViewItem? FileTreeRowAt(object source) => source is DependencyObject element
        ? ItemsControl.ContainerFromElement(FileTree, element) as TreeViewItem
        : null;

    private void FileTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var entry = e.CursorLeft < 0 ? FileTree.SelectedItem as FileTreeEntry
            : FileTreeRowAt(e.OriginalSource)?.Header as FileTreeEntry;
        if (entry is null || BlindModeToggle.IsChecked == true)
        {
            e.Handled = true;
            return;
        }

        var openInPatch = new MenuItem { Header = "Open in patch" };
        foreach (var column in columns.Where(column => CanDropFile(column, entry)))
            openInPatch.Items.Add(CreateMenuItem($"{column.Label} — {column.SourceName}",
                () => OpenDroppedFile(column, entry)));
        openInPatch.IsEnabled = openInPatch.Items.Count > 0;
        FileTree.ContextMenu.Items.Clear();
        FileTree.ContextMenu.Items.Add(openInPatch);
    }

    private void FileDrag_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        fileDragStart = null;

    private void FileDrag_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (fileDragStart is not { } start) return;
        if (e.LeftButton != MouseButtonState.Pressed || BlindModeToggle.IsChecked == true)
        {
            fileDragStart = null;
            return;
        }
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - start.Origin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Origin.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        fileDragStart = null;
        draggedFile = start.Entry;
        var accent = (Brush)FindResource("OneDarkAccent");
        foreach (var (column, surface) in columnSurfaces)
            if (CanDropFile(column, start.Entry))
                surface.BorderBrush = accent;
        try
        {
            DragDrop.DoDragDrop(FileTree, new DataObject(FileRowDragFormat, start.Entry.Path), DragDropEffects.Copy);
        }
        finally
        {
            draggedFile = null;
            foreach (var surface in columnSurfaces.Values)
                surface.BorderBrush = Brushes.Transparent;
        }
        e.Handled = true;
    }

    private void FileDrag_PreviewDragOver(object sender, DragEventArgs e) => HandleFileDrag(e, drop: false);

    private void FileDrag_PreviewDrop(object sender, DragEventArgs e) => HandleFileDrag(e, drop: true);

    private void HandleFileDrag(DragEventArgs e, bool drop)
    {
        if (!e.Data.GetDataPresent(FileRowDragFormat, autoConvert: false)) return;
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (draggedFile is not { } entry || e.Data.GetData(FileRowDragFormat, autoConvert: false) is not string path ||
            path != entry.Path || !e.AllowedEffects.HasFlag(DragDropEffects.Copy))
            return;

        foreach (var (column, surface) in columnSurfaces)
        {
            if (!new Rect(surface.RenderSize).Contains(e.GetPosition(surface)) || !CanDropFile(column, entry))
                continue;
            e.Effects = DragDropEffects.Copy;
            if (drop) OpenDroppedFile(column, entry);
            return;
        }
    }

    private bool CanDropFile(LoadedColumn column, FileTreeEntry entry) =>
        BlindModeToggle.IsChecked != true && !column.IsHidden && columns.Contains(column) &&
        columnSurfaces.ContainsKey(column) && PatchChangeAt(column, entry.Path) is not null;

    private void OpenDroppedFile(LoadedColumn column, FileTreeEntry entry)
    {
        if (!CanDropFile(column, entry)) return;
        SetColumnFile(column, entry.Path);
    }

    private void OpenSharedFile(string path)
    {
        SelectFileTreeRow(path);
        if (path == sharedPath && !HasFileOverrides) return;
        sharedPath = path;
        ClearFileOverrides();
        recentSelections.Clear();
        RenderColumns();
    }

    private void ClearFileOverrides()
    {
        fileDragStart = null;
        foreach (var column in columns)
            column.FileOverride = null;
    }

    private void SetColumnFile(LoadedColumn column, string path)
    {
        if (path == OpenPath(column)) return;
        SetFileOverride(column, path);
        recentSelections.RemoveAll(item => item.Column == column);
        ResetRenderedSelection();
        RenderColumn(column);
        RefreshOpenFileHeaders();
    }

    private void SetFileOverride(LoadedColumn column, string path) =>
        column.FileOverride = path == sharedPath ? null : path;

    private void OpenNavigationFile(LoadedColumn column, string path)
    {
        if (HasFileOverrides)
            SetFileOverride(column, path);
        else
            sharedPath = path;
        SelectFileTreeRow(path);
    }

    private void SelectFileTreeRow(string path)
    {
        pendingFileTreePath = path;
        SynchronizeFileTreeSelection();
    }

    private void SynchronizeFileTreeSelection()
    {
        if (pendingFileTreePath is null) return;
        var entry = visibleFiles.FirstOrDefault(item => item.Path == pendingFileTreePath);
        if (entry is not null &&
            FileTree.ItemContainerGenerator.ContainerFromItem(entry) is TreeViewItem row)
        {
            pendingFileTreePath = null;
            row.IsSelected = true;
        }
    }

    private void TestsOnly_Changed(object sender, RoutedEventArgs e)
    {
        if (IsInitialized) RefreshFileTree();
    }

    private void RefreshFileTree(string? selectedRow = null)
    {
        var selectedRowPath = selectedRow ?? (FileTree.SelectedItem as FileTreeEntry)?.Path ?? sharedPath;
        fileDragStart = null;
        draggedFile = null;
        pendingFileTreePath = null;
        visibleFiles.Clear();
        foreach (var file in files.Where(file => TestsOnlyToggle.IsChecked != true || file.IsTest))
            visibleFiles.Add(file);
        SelectFileTreeRow(visibleFiles.Any(file => file.Path == selectedRowPath) ? selectedRowPath : sharedPath);
    }

    private void RefreshFileEntries()
    {
        files.Clear();
        foreach (var path in columns.SelectMany(column => column.Applied?.Files ?? [])
                     .SelectMany(file => file.Change.Kind == FileChangeKind.Renamed &&
                         file.Change.OldPath is { } oldPath ? new[] { file.Path, oldPath } : [file.Path])
                     .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var statuses = columns.Select(column => $"{BlindDisplayLabel(column.Label)}:{StatusForColumn(column, path)}");
            files.Add(new FileTreeEntry(path, string.Join("  ", statuses), FileChange.IsTestPath(path)));
        }
    }

    private void RefreshOpenFileHeaders()
    {
        var hasOverrides = HasFileOverrides;
        SelectedFileText.Text = hasOverrides ? $"Per-patch files · Shared file: {sharedPath}" : sharedPath;
        SelectedFileText.Visibility = columns.Count > 0 && sharedPath.Length > 0
            ? Visibility.Visible : Visibility.Collapsed;
        FileInteractionHint.Text = BlindModeToggle.IsChecked == true
            ? "Double-click to open in all patches."
            : "Double-click to open in all patches. Drag to open in one patch.";
        foreach (var heading in columnFileHeadings.Values)
            heading.Visibility = hasOverrides ? Visibility.Visible : Visibility.Collapsed;
    }

    private DockPanel CreateColumnFileHeading(LoadedColumn column, string path)
    {
        var heading = new DockPanel { Margin = new Thickness(3, 0, 3, 4) };
        if (column.FileOverride is not null)
        {
            var reset = new Button { Content = "↶", ToolTip = "Use shared file", Padding = new Thickness(4, 0, 4, 0) };
            System.Windows.Automation.AutomationProperties.SetName(reset, "Use shared file");
            reset.Click += (_, _) => SetColumnFile(column, sharedPath);
            DockPanel.SetDock(reset, Dock.Right);
            heading.Children.Add(reset);
        }
        heading.Children.Add(new TextBlock
        {
            Text = path, ToolTip = path, FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        return heading;
    }
}
