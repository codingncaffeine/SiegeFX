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
    private bool _panning;
    private bool _spinning;
    private bool _movingObject;
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
        var p = e.GetPosition(Viewport);
        if (_vm.TrySnapView(p.X, p.Y)) return; // clicked the gizmo — snapped the view, don't orbit
        if (_vm.TryGrabObject(p.X, p.Y))       // grabbed a placed object — drag moves it (Shift-drag rotates)
        {
            _movingObject = true;
            _last = p;
            Viewport.CaptureMouse();
            return;
        }
        _vm.TryPick(p.X, p.Y);                 // click-to-select the node under the cursor (drag still orbits)
        _dragging = true;
        _last = p;
        Viewport.CaptureMouse();
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        _movingObject = false;
        if (!_panning) Viewport.ReleaseMouseCapture();
    }

    private void OnViewportRightDown(object sender, MouseButtonEventArgs e)
    {
        _panning = true;
        _last = e.GetPosition(Viewport);
        Viewport.CaptureMouse();
    }

    private void OnViewportRightUp(object sender, MouseButtonEventArgs e)
    {
        _panning = false;
        if (!_dragging) Viewport.ReleaseMouseCapture();
    }

    private void OnViewportMiddleDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        _spinning = true;
        _last = e.GetPosition(Viewport);
        Viewport.CaptureMouse();
    }

    private void OnViewportMiddleUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        _spinning = false;
        if (!_dragging && !_panning) Viewport.ReleaseMouseCapture();
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Viewport);

        // Middle button held → turntable spin about the vertical axis. Driven off the LIVE button
        // state (not just the middle MouseDown), so it fires even when WPF drops the middle-button
        // down event — that was why the map wouldn't twist.
        if (e.MiddleButton == MouseButtonState.Pressed && !_movingObject)
        {
            if (!_spinning) { _spinning = true; _last = p; return; } // anchor on first move, no jump
            _vm.Spin(p.X - _last.X);
            _last = p;
            return;
        }
        if (_spinning) { _spinning = false; if (!_dragging && !_panning) Viewport.ReleaseMouseCapture(); }

        if (!_dragging && !_panning && !_movingObject) return;
        if (_movingObject)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) _vm.RotateSelectedObject(p.X - _last.X);
            else _vm.MoveSelectedObject(p.X, p.Y);         // slide along the node surface
        }
        else if (_panning) _vm.Pan(p.X - _last.X, p.Y - _last.Y);
        else _vm.Orbit(p.X - _last.X, p.Y - _last.Y);
        _last = p;
    }

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e) => _vm.Zoom(e.Delta);

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e) =>
        _vm.SetViewport((int)e.NewSize.Width, (int)e.NewSize.Height);
}
