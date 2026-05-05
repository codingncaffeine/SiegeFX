using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// SC-DIFF Phase C — Difficulty screen's button hit-test + click drain.
/// Mirrors <see cref="CharacterSelectMenuPanel"/> for the cd state.
/// Layout from /ui/interfaces/frontend/difficulty_menu/difficulty_menu.gas:
/// - button_easy   rect 283,192,519,241 → notify(difficulty_easy)
/// - button_medium rect 283,268,519,317 → notify(difficulty_medium)
/// - button_hard   rect 283,342,519,391 → notify(difficulty_hard)
/// - button_diff_back rect 360,568,439,597 → notify(difficulty_back)
/// </summary>
internal sealed class DifficultyMenuPanel
{
    public enum Action
    {
        None,
        Easy,
        Medium,
        Hard,
        Back,
    }

    public bool IsActive { get; set; } = false;

    Action _pending = Action.None;
    public Action ConsumeAction()
    {
        var a = _pending;
        _pending = Action.None;
        return a;
    }

    // 800×600 reference rects per difficulty_menu.gas.
    const int EasyX   = 283, EasyY   = 192, EasyW   = 519 - 283, EasyH   = 241 - 192;
    const int MedX    = 283, MedY    = 268, MedW    = 519 - 283, MedH    = 317 - 268;
    const int HardX   = 283, HardY   = 342, HardW   = 519 - 283, HardH   = 391 - 342;
    const int BackX   = 360, BackY   = 568, BackW   = 439 - 360, BackH   = 597 - 568;

    Action _hovered = Action.None;
    Action _pressed = Action.None;
    int _fontScale = 1;
    (int X, int Y, int W, int H) _easyRect;
    (int X, int Y, int W, int H) _medRect;
    (int X, int Y, int W, int H) _hardRect;
    (int X, int Y, int W, int H) _backRect;

    public int FontScale => _fontScale;

    void Layout(int viewportW, int viewportH)
    {
        float scale = MathF.Min(viewportH / 600f, viewportW / 800f);
        int authoredW = (int)MathF.Round(800 * scale);
        int authoredH = (int)MathF.Round(600 * scale);
        int dx = (viewportW - authoredW) / 2;
        int dy = (viewportH - authoredH) / 2;
        _fontScale = Math.Max(1, (int)MathF.Round(scale));
        (int X, int Y, int W, int H) Scale(int rx, int ry, int rw, int rh) => (
            dx + (int)MathF.Round(rx * scale),
            dy + (int)MathF.Round(ry * scale),
            (int)MathF.Round(rw * scale),
            (int)MathF.Round(rh * scale));
        _easyRect = Scale(EasyX, EasyY, EasyW, EasyH);
        _medRect  = Scale(MedX,  MedY,  MedW,  MedH);
        _hardRect = Scale(HardX, HardY, HardW, HardH);
        _backRect = Scale(BackX, BackY, BackW, BackH);
    }

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
        (int X, int Y, int W, int H) r = act switch
        {
            Action.Easy   => _easyRect,
            Action.Medium => _medRect,
            Action.Hard   => _hardRect,
            Action.Back   => _backRect,
            _ => default,
        };
        if (act == Action.None) { x = y = w = h = 0; hovered = pressed = false; return false; }
        x = r.X; y = r.Y; w = r.W; h = r.H;
        hovered = _hovered == act;
        pressed = _pressed == act;
        return true;
    }

    Action HitTest(int px, int py)
    {
        if (Hits(_easyRect, px, py)) return Action.Easy;
        if (Hits(_medRect,  px, py)) return Action.Medium;
        if (Hits(_hardRect, px, py)) return Action.Hard;
        if (Hits(_backRect, px, py)) return Action.Back;
        return Action.None;
    }
}
