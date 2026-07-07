using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using SiegeSmith.ViewModels.WorldBuilder;

namespace SiegeSmith;

/// <summary>Interaction logic for WorldBuilderWindow.xaml — the visual terrain-node builder.
/// Owns the view-model (built from the install's tank paths) and forwards viewport mouse input to
/// its orbit camera.</summary>
public partial class WorldBuilderWindow : Window
{
    private readonly WorldBuilderViewModel _vm;
    private bool _dragging;
    private Point _last;

    public WorldBuilderWindow(IReadOnlyList<string> tankPaths)
    {
        InitializeComponent();
        _vm = new WorldBuilderViewModel(tankPaths);
        DataContext = _vm;
        Closed += (_, _) => _vm.Dispose();
    }

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _last = e.GetPosition(Viewport);
        Viewport.CaptureMouse();
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        Viewport.ReleaseMouseCapture();
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(Viewport);
        _vm.Orbit(p.X - _last.X, p.Y - _last.Y);
        _last = p;
    }

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e) => _vm.Zoom(e.Delta);

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e) =>
        _vm.SetViewport((int)e.NewSize.Width, (int)e.NewSize.Height);
}
