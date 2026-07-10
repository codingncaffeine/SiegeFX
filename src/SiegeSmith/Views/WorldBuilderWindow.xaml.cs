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
    private bool _painting;
    private Point _lastPaint;
    private const double PaintStepPx = 48; // cursor travel between drag-paint steps
    private Point _last;

    private readonly System.Windows.Threading.DispatcherTimer _flyTimer;

    public WorldBuilderWindow(IReadOnlyList<string> tankPaths)
    {
        InitializeComponent();
        _vm = new WorldBuilderViewModel(tankPaths);
        DataContext = _vm;

        // ED-2 — WASD/QE fly while the cursor is over the viewport (Shift =
        // fast, Ctrl = slow). Polled at ~30fps; never steals keys from a
        // focused text box.
        _flyTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = System.TimeSpan.FromMilliseconds(33),
        };
        _flyTimer.Tick += (_, _) => FlyTick();
        _flyTimer.Start();

        Closed += (_, _) =>
        {
            _flyTimer.Stop();
            _vm.Dispose();
        };
    }

    private void FlyTick()
    {
        if (!IsActive || !Viewport.IsMouseOver) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
        float f = (Keyboard.IsKeyDown(Key.W) ? 1 : 0) - (Keyboard.IsKeyDown(Key.S) ? 1 : 0);
        float s = (Keyboard.IsKeyDown(Key.D) ? 1 : 0) - (Keyboard.IsKeyDown(Key.A) ? 1 : 0);
        float v = (Keyboard.IsKeyDown(Key.E) ? 1 : 0) - (Keyboard.IsKeyDown(Key.Q) ? 1 : 0);
        if (f == 0 && s == 0 && v == 0) return;
        _vm.Fly(f, s, v,
            (Keyboard.Modifiers & ModifierKeys.Shift) != 0,
            (Keyboard.Modifiers & ModifierKeys.Control) != 0);
    }

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        // ED-2 — clicking the viewport pulls keyboard focus out of any text
        // box so WASD flying and single-key shortcuts (F, 1-4) work at once.
        Viewport.Focus();
        var p = e.GetPosition(Viewport);
        if (_vm.TrySnapView(p.X, p.Y)) return; // clicked the gizmo — snapped the view, don't orbit

        // ED-5/ED-6 — brush modes (terrain paint / object scatter): click
        // applies once; holding and dragging re-applies every PaintStepPx.
        if (_vm.HasBrush && (Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            if (_vm.TryBrush(p.X, p.Y))
            {
                _painting = true;
                _lastPaint = p;
                Viewport.CaptureMouse();
                return;
            }
        }

        // ED-1b — Ctrl+click toggles pieces in/out of the multi-selection. A
        // Ctrl+click that misses keeps the set and just starts an orbit.
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _vm.TryToggleMultiSelect(p.X, p.Y);
            _dragging = true;
            _last = p;
            Viewport.CaptureMouse();
            return;
        }

        if (_vm.TryGrabObject(p.X, p.Y))       // grabbed a placed object — drag moves it (Shift-drag rotates)
        {
            _movingObject = true;
            _last = p;
            Viewport.CaptureMouse();
            return;
        }
        _vm.ClearMultiSelect();                // plain click on empty space = deselect the set
        _vm.TryPick(p.X, p.Y);                 // click-to-select the node under the cursor (drag still orbits)
        _dragging = true;
        _last = p;
        Viewport.CaptureMouse();
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        _movingObject = false;
        _painting = false;
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

        // ED-5/ED-6 — drag-brush: re-apply every PaintStepPx of travel.
        if (_painting)
        {
            if ((p - _lastPaint).Length >= PaintStepPx && _vm.TryBrush(p.X, p.Y))
                _lastPaint = p;
            return;
        }

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

    /// <summary>SC-UX1 — Scene Outliner click routes to the same selection the
    /// viewport and Inspector use (group headers themselves are not selections).</summary>
    private void OnOutlinerSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not null and not ViewModels.WorldBuilder.OutlineGroup)
            _vm.SelectFromOutliner(e.NewValue);
    }

    // ED-7 — drag a palette entry into the viewport to place it at the drop
    // point. Drag starts only from a real list item (never the scrollbar).
    private Point _paletteDragStart;

    private void OnPaletteMouseDown(object sender, MouseButtonEventArgs e) =>
        _paletteDragStart = e.GetPosition(null);

    private void OnPaletteMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(null) - _paletteDragStart;
        if (System.Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance &&
            System.Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (sender is not System.Windows.Controls.ListBox { SelectedItem: not null } lb) return;
        if (System.Windows.Controls.ItemsControl.ContainerFromElement(lb, e.OriginalSource as DependencyObject)
            is not System.Windows.Controls.ListBoxItem) return;
        DragDrop.DoDragDrop(lb, new DataObject(lb.SelectedItem.GetType(), lb.SelectedItem), DragDropEffects.Copy);
    }

    private void OnViewportDrop(object sender, DragEventArgs e)
    {
        var p = e.GetPosition(Viewport);
        if (e.Data.GetData(typeof(SiegeSmith.Services.PropTemplate)) is SiegeSmith.Services.PropTemplate tpl)
            _vm.DropObjectAt(p.X, p.Y, tpl);
        else if (e.Data.GetData(typeof(SiegeSmith.Services.SnoMeshEntry)) is SiegeSmith.Services.SnoMeshEntry mesh)
            _vm.DropMeshAt(p.X, p.Y, mesh);
    }
}
