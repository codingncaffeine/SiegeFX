using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SiegeSmith.ViewModels;
using SiegeSmith.ViewModels.Viewers;

namespace SiegeSmith;

/// <summary>Interaction logic for MainWindow.xaml — the SiegeSmith shell window.</summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnBuildTank(object sender, RoutedEventArgs e) =>
        new BuildTankWindow { Owner = this }.ShowDialog();

    private void OnWorldBuilder(object sender, RoutedEventArgs e)
    {
        var paths = new List<string>();
        if (DataContext is MainViewModel vm)
            foreach (var t in vm.Tanks) paths.Add(t.FullPath);
        new WorldBuilderWindow(paths) { Owner = this }.ShowDialog();
    }

    private void OnProjectFiles(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var folder = vm.ProjectSourceFolder;
        if (string.IsNullOrEmpty(folder))
        {
            MessageBox.Show(this, "Open or create a project first (Project ▸ New / Open Project).",
                "No project open", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new ProjectFilesWindow(folder) { Owner = this }.ShowDialog();
    }

    /// <summary>Once the window is up, offer the locate-install prompt if detection came up empty.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.PromptForInstallIfMissing();
    }

    /// <summary>WPF's TreeView.SelectedItem is read-only, so we push tree selection into the
    /// explorer view-model here. (Search-result selection binds directly via the ListBox.)</summary>
    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel { Explorer: { } explorer })
            explorer.SelectedNode = e.NewValue as TankNodeViewModel;
    }

    /// <summary>Right-clicking selects the node under the cursor first, so the context menu acts on
    /// the clicked item rather than the previously selected one.</summary>
    private void OnTreePreviewRightDown(object sender, MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is TreeViewItem item) { item.IsSelected = true; break; }
    }

    // ── drag a tank file out to Windows (extract-on-drag) ────────
    private Point _dragStart;
    private bool _maybeDrag;

    private void OnTreePreviewLeftDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _maybeDrag = true;
    }

    private void OnTreeMouseMove(object sender, MouseEventArgs e)
    {
        if (!_maybeDrag) return;
        if (e.LeftButton != MouseButtonState.Pressed) { _maybeDrag = false; return; }
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _maybeDrag = false;
        if (DataContext is not MainViewModel { Explorer: { } explorer }) return;
        var path = explorer.PrepareDragOut();
        if (path is null) return;
        var data = new DataObject(DataFormats.FileDrop, new[] { path });
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
    }

    // ── model preview: left-drag to orbit, right-drag to pan, wheel to zoom ──
    private Point _viewportLast;
    private bool _viewportDragging;
    private bool _viewportPanning;

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is IInputElement el)
        {
            _viewportDragging = true;
            _viewportLast = e.GetPosition(el);
            el.CaptureMouse();
        }
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        _viewportDragging = false;
        if (!_viewportPanning) (sender as IInputElement)?.ReleaseMouseCapture();
    }

    private void OnViewportRightDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is IInputElement el)
        {
            _viewportPanning = true;
            _viewportLast = e.GetPosition(el);
            el.CaptureMouse();
        }
    }

    private void OnViewportRightUp(object sender, MouseButtonEventArgs e)
    {
        _viewportPanning = false;
        if (!_viewportDragging) (sender as IInputElement)?.ReleaseMouseCapture();
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if ((!_viewportDragging && !_viewportPanning) || sender is not FrameworkElement fe) return;
        var p = e.GetPosition(fe);
        if (fe.DataContext is ModelInfoViewerViewModel vm)
        {
            if (_viewportPanning) vm.Pan(p.X - _viewportLast.X, p.Y - _viewportLast.Y);
            else vm.Orbit(p.X - _viewportLast.X, p.Y - _viewportLast.Y);
        }
        _viewportLast = p;
    }

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModelInfoViewerViewModel vm })
            vm.Zoom(e.Delta);
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModelInfoViewerViewModel vm })
            vm.SetViewport((int)e.NewSize.Width, (int)e.NewSize.Height);
    }
}
