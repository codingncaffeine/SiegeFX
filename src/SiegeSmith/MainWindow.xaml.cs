using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SiegeSmith.ViewModels;
using SiegeSmith.ViewModels.Viewers;

namespace SiegeSmith;

/// <summary>Interaction logic for MainWindow.xaml — the SiegeSmith shell window.</summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

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

    // ── model preview: drag to orbit, wheel to zoom ──────────────
    private Point _viewportLast;
    private bool _viewportDragging;

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
        (sender as IInputElement)?.ReleaseMouseCapture();
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (!_viewportDragging || sender is not FrameworkElement fe) return;
        var p = e.GetPosition(fe);
        if (fe.DataContext is ModelInfoViewerViewModel vm)
            vm.Orbit(p.X - _viewportLast.X, p.Y - _viewportLast.Y);
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
