namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// SC-MP-MENU — Multiplayer provider menu's button hit-test + click drain.
/// Mirrors <see cref="DifficultyMenuPanel"/> (identical row geometry).
/// Layout from /ui/interfaces/frontend/multiplayer_provider/multiplayer_provider.gas:
/// - button_matchmaker rect 283,192,519,241 → notify(provider_matchmaker)
///   — ZONEMATCH: rendered DISABLED (service discontinued); excluded from
///   hit-testing entirely so it neither hovers nor clicks.
/// - button_internet   rect 283,268,519,317 → notify(provider_internet)
/// - button_lan        rect 283,342,519,391 → notify(provider_lan)
/// - button_sp_back    rect 360,568,439,597 → notify(multi_player_to_main)
/// </summary>
internal sealed class MultiplayerMenuPanel
{
    public enum Action
    {
        None,
        Internet,
        Network,
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

    // 800×600 reference rects per multiplayer_provider.gas.
    const int NetX  = 283, NetY  = 268, NetW  = 519 - 283, NetH  = 317 - 268;
    const int LanX  = 283, LanY  = 342, LanW  = 519 - 283, LanH  = 391 - 342;
    const int BackX = 360, BackY = 568, BackW = 439 - 360, BackH = 597 - 568;

    Action _hovered = Action.None;
    Action _pressed = Action.None;
    (int X, int Y, int W, int H) _netRect;
    (int X, int Y, int W, int H) _lanRect;
    (int X, int Y, int W, int H) _backRect;

    void Layout(int viewportW, int viewportH)
    {
        float scale = System.MathF.Min(viewportH / 600f, viewportW / 800f);
        int authoredW = (int)System.MathF.Round(800 * scale);
        int authoredH = (int)System.MathF.Round(600 * scale);
        int dx = (viewportW - authoredW) / 2;
        int dy = (viewportH - authoredH) / 2;
        (int X, int Y, int W, int H) Scale(int rx, int ry, int rw, int rh) => (
            dx + (int)System.MathF.Round(rx * scale),
            dy + (int)System.MathF.Round(ry * scale),
            (int)System.MathF.Round(rw * scale),
            (int)System.MathF.Round(rh * scale));
        _netRect  = Scale(NetX,  NetY,  NetW,  NetH);
        _lanRect  = Scale(LanX,  LanY,  LanW,  LanH);
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
            Action.Internet => _netRect,
            Action.Network  => _lanRect,
            Action.Back     => _backRect,
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
        if (Hits(_netRect,  px, py)) return Action.Internet;
        if (Hits(_lanRect,  px, py)) return Action.Network;
        if (Hits(_backRect, px, py)) return Action.Back;
        return Action.None;
    }
}
