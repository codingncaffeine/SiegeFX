using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Modal pause overlay. Owns its own button stack (Resume / Quit), draws a
/// dimming backdrop so the world reads as suspended, and forwards mouse
/// events to its buttons. Caller toggles <see cref="IsOpen"/> off when the
/// resume button fires; quit fires <see cref="QuitRequested"/> for the host
/// to act on (closing the window is the host's job, not the menu's).
///
/// Layout is recomputed each Draw so a window resize re-centers without
/// extra plumbing — buttons are cheap data objects, not GL resources.
/// </summary>
public sealed class PauseMenu
{
    public bool IsOpen { get; set; }
    public bool QuitRequested { get; private set; }

    private const int ButtonW = 200;
    private const int ButtonH = 32;
    private const int ButtonGap = 12;
    private const int Padding   = 18;
    private const int TitleH    = 24;

    private readonly MenuButton _resume = new("Resume", 0, 0, ButtonW, ButtonH);
    private readonly MenuButton _quit   = new("Quit",   0, 0, ButtonW, ButtonH);

    public int PanelWidth  => ButtonW + Padding * 2;
    public int PanelHeight => TitleH + Padding * 2 + ButtonH * 2 + ButtonGap;

    public void Toggle() { IsOpen = !IsOpen; if (!IsOpen) CancelPresses(); }
    public void Close()  { IsOpen = false;  CancelPresses(); }

    private void CancelPresses()
    {
        _resume.CancelPress();
        _quit.CancelPress();
    }

    private void Layout(int viewportW, int viewportH)
    {
        int px = (viewportW - PanelWidth)  / 2;
        int py = (viewportH - PanelHeight) / 2;
        int bx = px + Padding;
        int by = py + TitleH + Padding;
        _resume.X = bx; _resume.Y = by;                   _resume.Width = ButtonW; _resume.Height = ButtonH;
        _quit.X   = bx; _quit.Y   = by + ButtonH + ButtonGap; _quit.Width   = ButtonW; _quit.Height   = ButtonH;
    }

    public void OnMouseMove(int px, int py)
    {
        if (!IsOpen) return;
        _resume.UpdateHover(px, py);
        _quit.UpdateHover(px, py);
    }

    /// <summary>LMB-down at (<paramref name="px"/>, <paramref name="py"/>). Returns
    /// true if the menu consumed the click (suppress click-to-move upstream).</summary>
    public bool OnMouseDown(int px, int py)
    {
        if (!IsOpen) return false;
        // Press lands on at most one button; either way the click is consumed
        // because the menu owns the screen while open.
        _resume.TryPress(px, py);
        _quit.TryPress(px, py);
        return true;
    }

    /// <summary>LMB-up at (<paramref name="px"/>, <paramref name="py"/>). Returns
    /// true if a button fired this frame; mutates <see cref="IsOpen"/> /
    /// <see cref="QuitRequested"/> as appropriate.</summary>
    public bool OnMouseUp(int px, int py)
    {
        if (!IsOpen) return false;
        if (_resume.Release(px, py)) { IsOpen = false; return true; }
        if (_quit.Release(px, py))   { QuitRequested = true; return true; }
        return true; // still consume the up-event so it doesn't fall through
    }

    public void Draw(BarRenderer bars, TextRenderer text, int viewportW, int viewportH)
    {
        if (!IsOpen) return;
        Layout(viewportW, viewportH);

        int px = (viewportW - PanelWidth)  / 2;
        int py = (viewportH - PanelHeight) / 2;

        var dim    = new Vector4(0f, 0f, 0f, 0.55f);
        var panel  = new Vector4(0.08f, 0.08f, 0.10f, 0.94f);
        var title  = new Vector4(0.16f, 0.13f, 0.10f, 1f);
        var border = new Vector4(0.78f, 0.66f, 0.42f, 1f);
        var ink    = new Vector4(0.92f, 0.88f, 0.78f, 1f);

        bars.DrawRect  (viewportW, viewportH, 0, 0, viewportW, viewportH, dim);
        bars.DrawRect  (viewportW, viewportH, px, py, PanelWidth, PanelHeight, panel);
        bars.DrawRect  (viewportW, viewportH, px, py, PanelWidth, TitleH, title);
        bars.DrawBorder(viewportW, viewportH, px, py, PanelWidth, PanelHeight, border);
        bars.DrawBorder(viewportW, viewportH, px, py + TitleH, PanelWidth, 1, border);

        const string heading = "Paused";
        int hw = text.MeasureWidth(heading);
        text.DrawString(viewportW, viewportH, heading, px + (PanelWidth - hw) / 2, py + 5, ink);

        _resume.Draw(bars, text, viewportW, viewportH);
        _quit.Draw  (bars, text, viewportW, viewportH);
    }
}
