using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SiegeSmith.ViewModels.ProjectFiles;

namespace SiegeSmith;

/// <summary>Interaction logic for ProjectFilesWindow.xaml — the mod project's loose-file manager.
/// Handles tree selection, inline rename commit, and drag-drop (move within the tree, import from
/// Windows, drag out to Windows).</summary>
public partial class ProjectFilesWindow : Window
{
    private readonly ProjectFilesViewModel _vm;
    private Point _dragStart;
    private bool _maybeDrag;

    public ProjectFilesWindow(string sourceFolder)
    {
        InitializeComponent();
        _vm = new ProjectFilesViewModel(sourceFolder);
        DataContext = _vm;
        Closed += (_, _) => _vm.Dispose();
    }

    private void OnSelectedChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        _vm.Selected = e.NewValue as ProjectNodeViewModel;

    // ── inline rename ───────────────────────────────────────────
    private void OnEditVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.IsVisible) { tb.Focus(); tb.SelectAll(); }
    }

    private void OnEditKey(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ProjectNodeViewModel node }) return;
        if (e.Key == Key.Enter) { _vm.CommitRename(node); e.Handled = true; }
        else if (e.Key == Key.Escape) { node.IsEditing = false; e.Handled = true; }
    }

    private void OnEditLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ProjectNodeViewModel node } && node.IsEditing)
            _vm.CommitRename(node);
    }

    // ── drag-drop (move / import / drag-out) ────────────────────
    private void OnTreeLeftDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _maybeDrag = true;
    }

    private void OnTreeMove(object sender, MouseEventArgs e)
    {
        if (!_maybeDrag || e.LeftButton != MouseButtonState.Pressed) { _maybeDrag = false; return; }
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _maybeDrag = false;
        if (_vm.Selected is not { } node) return;
        var data = new DataObject(DataFormats.FileDrop, new[] { node.FullPath });
        DragDrop.DoDragDrop(Tree, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        bool inside = paths is not null && paths.Any(_vm.IsInsideRoot);
        e.Effects = inside ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnTreeDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        var target = NodeAt(e.OriginalSource as DependencyObject);
        foreach (var p in paths)
        {
            if (_vm.IsInsideRoot(p)) _vm.MoveInto(p, target);
            else _vm.Import(new[] { p }, target);
        }
        e.Handled = true;
    }

    private static ProjectNodeViewModel? NodeAt(DependencyObject? d)
    {
        for (; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is TreeViewItem { DataContext: ProjectNodeViewModel n }) return n;
        return null;
    }
}
