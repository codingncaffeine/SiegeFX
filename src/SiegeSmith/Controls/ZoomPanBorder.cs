using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SiegeSmith.Controls;

/// <summary>A Border whose child can be panned (left-drag) and zoomed about the cursor (mouse
/// wheel), with double-click to fit. Reusable across previews so "grab and move, wheel to zoom"
/// behaves identically everywhere. The child is transformed (not the border), so the frame and its
/// clip stay put. <see cref="ScaleValue"/> exposes the current zoom for a read-out.</summary>
public sealed class ZoomPanBorder : Border
{
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _translate = new(0, 0);
    private Point _lastDrag;
    private bool _dragging;
    private bool _fitPending = true;

    public static readonly DependencyProperty ScaleValueProperty =
        DependencyProperty.Register(nameof(ScaleValue), typeof(double), typeof(ZoomPanBorder),
            new PropertyMetadata(1.0));

    public double ScaleValue
    {
        get => (double)GetValue(ScaleValueProperty);
        private set => SetValue(ScaleValueProperty, value);
    }

    public ZoomPanBorder()
    {
        Background ??= Brushes.Transparent; // whole area hittable so panning works over empty space
        ClipToBounds = true;
        Focusable = false;
        Loaded += (_, _) => AttachChild();
        SizeChanged += (_, _) => { if (_fitPending) Fit(); };
    }

    private void AttachChild()
    {
        if (Child is UIElement c)
        {
            var g = new TransformGroup();
            g.Children.Add(_scale);
            g.Children.Add(_translate);
            c.RenderTransform = g;
            c.RenderTransformOrigin = new Point(0, 0);
            if (c is FrameworkElement fe) fe.SizeChanged += (_, _) => { if (_fitPending) Fit(); };
        }
        if (_fitPending) Fit();
    }

    /// <summary>Centers the child and scales it to fit (never upscaling past 1:1, so small pixel-art
    /// textures start crisp and the user zooms in deliberately).</summary>
    public void Fit()
    {
        if (Child is not UIElement c) return;
        var s = c.RenderSize;
        if (s.Width <= 0 || s.Height <= 0 || ActualWidth <= 0 || ActualHeight <= 0) return;

        double scale = Math.Min(ActualWidth / s.Width, ActualHeight / s.Height);
        if (scale <= 0 || double.IsInfinity(scale) || double.IsNaN(scale)) scale = 1;
        if (scale > 1) scale = 1;

        _scale.ScaleX = _scale.ScaleY = scale;
        _translate.X = (ActualWidth - s.Width * scale) / 2;
        _translate.Y = (ActualHeight - s.Height * scale) / 2;
        ScaleValue = scale;
        _fitPending = false;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var pos = e.GetPosition(this);
        double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        double target = Math.Clamp(_scale.ScaleX * factor, 0.02, 64);
        factor = target / _scale.ScaleX;
        // Keep the point under the cursor fixed while scaling.
        _translate.X = pos.X - factor * (pos.X - _translate.X);
        _translate.Y = pos.Y - factor * (pos.Y - _translate.Y);
        _scale.ScaleX = _scale.ScaleY = target;
        ScaleValue = target;
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2) { Fit(); e.Handled = true; return; }
        _dragging = true;
        _lastDrag = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var p = e.GetPosition(this);
        _translate.X += p.X - _lastDrag.X;
        _translate.Y += p.Y - _lastDrag.Y;
        _lastDrag = p;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
    }
}
