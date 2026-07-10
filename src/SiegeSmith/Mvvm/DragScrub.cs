using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SiegeSmith.Mvvm;

/// <summary>A view-model that groups a whole scrub drag into ONE undo step:
/// the behavior calls <see cref="BeginScrubGesture"/> when a drag starts
/// changing values and <see cref="EndScrubGesture"/> on release.</summary>
public interface IScrubGestureSink
{
    void BeginScrubGesture();
    void EndScrubGesture();
}

/// <summary>Blender-style scrubbable numeric fields, attached to any TextBox:
/// <b>drag horizontally on the (unfocused) box to change the value live</b>;
/// a plain click still focuses it for typing. Hold <b>Shift</b> for fine
/// steps (×0.1), <b>Ctrl</b> for coarse (×10). The whole drag is one undo
/// step when the DataContext implements <see cref="IScrubGestureSink"/>.
///
/// Enable with <c>mvvm:DragScrub.Step="0.05"</c> (value change per pixel);
/// optional <c>Min</c>/<c>Max</c> clamp, and <c>Format</c> ("0" for integer
/// fields, default "0.###").</summary>
public static class DragScrub
{
    public static readonly DependencyProperty StepProperty = DependencyProperty.RegisterAttached(
        "Step", typeof(double), typeof(DragScrub), new PropertyMetadata(0d, OnStepChanged));
    public static void SetStep(DependencyObject d, double v) => d.SetValue(StepProperty, v);
    public static double GetStep(DependencyObject d) => (double)d.GetValue(StepProperty);

    public static readonly DependencyProperty MinProperty = DependencyProperty.RegisterAttached(
        "Min", typeof(double), typeof(DragScrub), new PropertyMetadata(double.NegativeInfinity));
    public static void SetMin(DependencyObject d, double v) => d.SetValue(MinProperty, v);
    public static double GetMin(DependencyObject d) => (double)d.GetValue(MinProperty);

    public static readonly DependencyProperty MaxProperty = DependencyProperty.RegisterAttached(
        "Max", typeof(double), typeof(DragScrub), new PropertyMetadata(double.PositiveInfinity));
    public static void SetMax(DependencyObject d, double v) => d.SetValue(MaxProperty, v);
    public static double GetMax(DependencyObject d) => (double)d.GetValue(MaxProperty);

    public static readonly DependencyProperty FormatProperty = DependencyProperty.RegisterAttached(
        "Format", typeof(string), typeof(DragScrub), new PropertyMetadata("0.###"));
    public static void SetFormat(DependencyObject d, string v) => d.SetValue(FormatProperty, v);
    public static string GetFormat(DependencyObject d) => (string)d.GetValue(FormatProperty);

    private sealed class State
    {
        public Point Origin;
        public double StartValue;
        public bool Scrubbing;
    }

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(DragScrub));

    private static void OnStepChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb || (double)e.NewValue <= 0) return;
        tb.PreviewMouseLeftButtonDown += OnDown;
        tb.PreviewMouseMove += OnMove;
        tb.PreviewMouseLeftButtonUp += OnUp;
        tb.LostMouseCapture += OnLostCapture;
        // The ⇔ cursor advertises scrubbability while unfocused; focused = I-beam for editing.
        tb.MouseEnter += (_, _) => tb.Cursor = tb.IsKeyboardFocusWithin ? Cursors.IBeam : Cursors.SizeWE;
        tb.GotKeyboardFocus += (_, _) => tb.Cursor = Cursors.IBeam;
        tb.LostKeyboardFocus += (_, _) => tb.Cursor = Cursors.SizeWE;
        tb.Cursor = Cursors.SizeWE;
    }

    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        var tb = (TextBox)sender;
        if (tb.IsKeyboardFocusWithin) return; // already editing — leave clicks to text selection

        double start = double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : Math.Clamp(0, GetMin(tb), GetMax(tb));
        tb.SetValue(StateProperty, new State { Origin = e.GetPosition(null), StartValue = start });
        tb.CaptureMouse();
        e.Handled = true; // decide click-vs-drag at mouse-up
    }

    private static void OnMove(object sender, MouseEventArgs e)
    {
        var tb = (TextBox)sender;
        if (tb.GetValue(StateProperty) is not State st || !tb.IsMouseCaptured) return;

        var pos = e.GetPosition(null);
        if (!st.Scrubbing)
        {
            if (Math.Abs(pos.X - st.Origin.X) < 3) return; // dead zone — a shaky click is still a click
            st.Scrubbing = true;
            (tb.DataContext as IScrubGestureSink)?.BeginScrubGesture();
        }

        double step = GetStep(tb);
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) step *= 0.1;
        else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) step *= 10;

        double v = Math.Clamp(st.StartValue + (pos.X - st.Origin.X) * step, GetMin(tb), GetMax(tb));
        tb.Text = v.ToString(GetFormat(tb), CultureInfo.InvariantCulture);
        tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); // live — the viewport follows the drag
    }

    private static void OnUp(object sender, MouseButtonEventArgs e)
    {
        var tb = (TextBox)sender;
        if (tb.GetValue(StateProperty) is not State st) return;
        bool scrubbed = st.Scrubbing;
        tb.ClearValue(StateProperty);
        tb.ReleaseMouseCapture();
        if (scrubbed)
        {
            (tb.DataContext as IScrubGestureSink)?.EndScrubGesture();
        }
        else
        {
            // Plain click — hand the box over for typing, ready to overwrite.
            tb.Focus();
            tb.SelectAll();
        }
        e.Handled = true;
    }

    private static void OnLostCapture(object sender, MouseEventArgs e)
    {
        // Safety: capture stolen mid-drag (alt-tab, popup) — end the gesture cleanly.
        var tb = (TextBox)sender;
        if (tb.GetValue(StateProperty) is not State st) return;
        tb.ClearValue(StateProperty);
        if (st.Scrubbing) (tb.DataContext as IScrubGestureSink)?.EndScrubGesture();
    }
}
