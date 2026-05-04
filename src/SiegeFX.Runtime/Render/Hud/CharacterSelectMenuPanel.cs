using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 28-CD-FLYOUT — Character Creator screen's bottom-nav buttons
/// (Previous / Next). The spinner-axis buttons (gender / head / face /
/// hair / shirt / pants × left/right) live on the existing
/// <c>CharacterCreatorPanel</c>; this panel only owns the two
/// <c>backbutton.asp</c>-driven nav buttons at the bottom of the
/// character_select screen.
///
/// <para>Layout from /ui/interfaces/frontend/character_select/character_select.gas
/// (already documented in <c>project_siegefx_frontend_gas_layout.md</c>):</para>
/// <list type="bullet">
///   <item><c>button_previous</c>: rect 237,575,338,595 → notify(back_to_single_player)</item>
///   <item><c>button_next</c>: rect 461,575,562,595 → notify(on_change_map)</item>
/// </list>
/// </summary>
internal sealed class CharacterSelectMenuPanel
{
    public enum Action
    {
        None,
        Previous,
        Next,
    }

    public bool IsActive { get; set; } = false;

    Action _pending = Action.None;
    public Action ConsumeAction()
    {
        var a = _pending;
        _pending = Action.None;
        return a;
    }

    // Authored character_select.gas rects (800×600). Y values match
    // the cd-state pose where backbutton is in pn pose — verified
    // against the existing button_exit Y tuning convention.
    const int PrevX = 237, PrevY = 575, PrevW = 338 - 237, PrevH = 595 - 575;
    const int NextX = 461, NextY = 575, NextW = 562 - 461, NextH = 595 - 575;

    Action _hovered = Action.None;
    Action _pressed = Action.None;
    int _fontScale = 1;
    (int X, int Y, int W, int H) _prevRect;
    (int X, int Y, int W, int H) _nextRect;

    void Layout(int viewportW, int viewportH)
    {
        float scale = MathF.Min(viewportH / 600f, viewportW / 800f);
        int authoredW = (int)MathF.Round(800 * scale);
        int authoredH = (int)MathF.Round(600 * scale);
        int dx = (viewportW - authoredW) / 2;
        int dy = (viewportH - authoredH) / 2;
        _fontScale = Math.Max(1, (int)MathF.Round(scale));
        _prevRect = (
            dx + (int)MathF.Round(PrevX * scale),
            dy + (int)MathF.Round(PrevY * scale),
            (int)MathF.Round(PrevW * scale),
            (int)MathF.Round(PrevH * scale));
        _nextRect = (
            dx + (int)MathF.Round(NextX * scale),
            dy + (int)MathF.Round(NextY * scale),
            (int)MathF.Round(NextW * scale),
            (int)MathF.Round(NextH * scale));
    }

    public int FontScale => _fontScale;

    static bool Hits((int X, int Y, int W, int H) r, int px, int py) =>
        px >= r.X && px < r.X + r.W && py >= r.Y && py < r.Y + r.H;

    public void OnMouseMove(int px, int py, int viewportW, int viewportH)
    {
        if (!IsActive) return;
        Layout(viewportW, viewportH);
        _hovered = HitTest(px, py);
    }

    public void OnMouseDown(int px, int py, int viewportW, int viewportH)
    {
        if (!IsActive) return;
        Layout(viewportW, viewportH);
        _pressed = HitTest(px, py);
    }

    public void OnMouseUp(int px, int py, int viewportW, int viewportH)
    {
        if (!IsActive) return;
        Layout(viewportW, viewportH);
        var up = HitTest(px, py);
        if (up != Action.None && up == _pressed) _pending = up;
        _pressed = Action.None;
    }

    public void ClearHover() => _hovered = Action.None;

    public bool TryGetButtonStateAndRect(Action act, int viewportW, int viewportH,
                                         out int x, out int y, out int w, out int h,
                                         out bool hovered, out bool pressed)
    {
        Layout(viewportW, viewportH);
        if (act == Action.Previous)
        {
            x = _prevRect.X; y = _prevRect.Y; w = _prevRect.W; h = _prevRect.H;
            hovered = _hovered == Action.Previous;
            pressed = _pressed == Action.Previous;
            return true;
        }
        if (act == Action.Next)
        {
            x = _nextRect.X; y = _nextRect.Y; w = _nextRect.W; h = _nextRect.H;
            hovered = _hovered == Action.Next;
            pressed = _pressed == Action.Next;
            return true;
        }
        x = y = w = h = 0;
        hovered = pressed = false;
        return false;
    }

    Action HitTest(int px, int py)
    {
        if (Hits(_prevRect, px, py)) return Action.Previous;
        if (Hits(_nextRect, px, py)) return Action.Next;
        return Action.None;
    }
}
