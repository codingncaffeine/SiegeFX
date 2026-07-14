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

    private WorldBuilderWindow? _worldBuilder;

    private void OnWorldBuilder(object sender, RoutedEventArgs e)
    {
        // SC-UX1 — the World Builder is a full editor, not a dialog: open it
        // NON-modal so the studio shell (tank explorer, viewers) stays usable
        // alongside; re-activate the existing window instead of stacking.
        if (_worldBuilder is { IsLoaded: true })
        {
            _worldBuilder.Activate();
            return;
        }
        var paths = new List<string>();
        if (DataContext is MainViewModel vm)
            foreach (var t in vm.Tanks) paths.Add(t.FullPath);
        _worldBuilder = new WorldBuilderWindow(paths) { Owner = this };
        _worldBuilder.Closed += (_, _) => _worldBuilder = null;
        _worldBuilder.Show();
    }

    private EffectsLabWindow? _effectsLab;

    private void OnEffectsLab(object sender, RoutedEventArgs e)
    {
        // SS-FXLAB — same shape as the World Builder: a full non-modal tool
        // window, re-activated instead of stacked.
        if (_effectsLab is { IsLoaded: true })
        {
            _effectsLab.Activate();
            return;
        }
        var paths = new List<string>();
        if (DataContext is MainViewModel vm)
            foreach (var t in vm.Tanks) paths.Add(t.FullPath);
        _effectsLab = new EffectsLabWindow(paths) { Owner = this };
        _effectsLab.Closed += (_, _) => _effectsLab = null;
        _effectsLab.Show();
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

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        var ver = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "dev";
        MessageBox.Show(this,
            $"SiegeSmith v{ver} — early alpha\n\n" +
            "The Dungeon Siege modding studio and world builder,\n" +
            "built on the SiegeFX engine's own readers and writers —\n" +
            "what the editor shows is what the engine loads.\n\n" +
            "Not yet field-tested — expect rough edges,\n" +
            "and please report what breaks.\n\n" +
            "Manual: github.com/codingncaffeine/SiegeFX/wiki/SiegeSmith\n" +
            "Project: github.com/codingncaffeine/SiegeFX",
            "About SiegeSmith", MessageBoxButton.OK, MessageBoxImage.Information);
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
    private bool _viewportSpinning;

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        var p = e.GetPosition(fe);
        if (fe.DataContext is ModelInfoViewerViewModel vm && vm.TrySnapView(p.X, p.Y))
            return; // clicked the gizmo — snapped the view, don't orbit
        _viewportDragging = true;
        _viewportLast = p;
        fe.CaptureMouse();
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

    private void OnViewportMiddleDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || sender is not IInputElement el) return;
        _viewportSpinning = true;
        _viewportLast = e.GetPosition(el);
        el.CaptureMouse();
    }

    private void OnViewportMiddleUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        _viewportSpinning = false;
        if (!_viewportDragging && !_viewportPanning) (sender as IInputElement)?.ReleaseMouseCapture();
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        var p = e.GetPosition(fe);
        var vm = fe.DataContext as ModelInfoViewerViewModel;

        // Middle held → turntable spin about the vertical axis, driven off the live button state so it
        // fires even when WPF drops the middle-button down event.
        if (e.MiddleButton == MouseButtonState.Pressed)
        {
            if (!_viewportSpinning) { _viewportSpinning = true; _viewportLast = p; return; }
            vm?.Spin(p.X - _viewportLast.X);
            _viewportLast = p;
            return;
        }
        if (_viewportSpinning)
        {
            _viewportSpinning = false;
            if (!_viewportDragging && !_viewportPanning) (sender as IInputElement)?.ReleaseMouseCapture();
        }

        if (!_viewportDragging && !_viewportPanning) return;
        if (vm is not null)
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
