using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using SiegeSmith.ViewModels.EffectsLab;

namespace SiegeSmith;

/// <summary>SS-FXLAB — the Effects Lab window: edit a SiegeFX effect script,
/// see it live. Non-modal, like the World Builder.</summary>
public partial class EffectsLabWindow : Window
{
    private readonly EffectsLabViewModel _vm;

    public EffectsLabWindow(IReadOnlyList<string> tankPaths)
    {
        InitializeComponent();
        SiegeSmith.Services.WindowPlacement.Track(this, "effectslab");
        _vm = new EffectsLabViewModel(tankPaths);
        DataContext = _vm;
        Closed += (_, _) => _vm.Shutdown();
    }

    // ── preview viewport: left-drag orbit, right-drag pan, wheel zoom ──
    private Point _last;
    private bool _orbiting, _panning;

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not IInputElement el) return;
        _last = e.GetPosition(el);
        if (e.ChangedButton == MouseButton.Left)
        {
            if (_vm.TrySnapView(_last.X, _last.Y)) return; // clicked the triad — snapped, don't orbit
            _orbiting = true;
        }
        else if (e.ChangedButton == MouseButton.Right) _panning = true;
        else return;
        el.CaptureMouse();
    }

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _orbiting = false;
        _panning = false;
        (sender as IInputElement)?.ReleaseMouseCapture();
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not IInputElement el || (!_orbiting && !_panning)) return;
        var p = e.GetPosition(el);
        if (_panning) _vm.Pan(p.X - _last.X, p.Y - _last.Y);
        else _vm.Orbit(p.X - _last.X, p.Y - _last.Y);
        _last = p;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) => _vm.Zoom(e.Delta);

    private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs e) =>
        _vm.SetViewport((int)e.NewSize.Width, (int)e.NewSize.Height);
}
