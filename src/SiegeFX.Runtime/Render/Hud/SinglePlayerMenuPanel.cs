using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 27-SP-FLYOUT — the Single Player sub-menu the FrontendScene
/// drops into after the user clicks SINGLE PLAYER on the main menu.
/// Shipped DS1 layout: <c>/ui/interfaces/frontend/single_player/single_player.gas</c>
/// authors three buttons — NEW GAME, LOAD GAME, BACK — with the first
/// two reusing the top two slots of the main-menu button column and
/// BACK replacing the EXIT slot at the bottom. The wood-button visuals
/// come from menubars.asp's mm2sp end-pose subsets (the column's
/// remaining 3 rows park their bones off-Z); this panel only owns
/// hit-testing for the screen rects + the click → action pipeline.
///
/// <para>Like <see cref="MainMenuPanel"/>, this is a thin click model
/// over the shared frontend chrome. Layout uses the same authored
/// 800×600 reference space and same height-driven uniform scale so
/// the rects line up with menubars.asp's posed button rows.</para>
/// </summary>
internal sealed class SinglePlayerMenuPanel
{
    public enum Action
    {
        None,
        NewGame,
        LoadGame,
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

    // SP submenu rects in 800×600 reference space. The menubars panel
    // SLIDES DOWN when going from MM state to SP state — that's why
    // these Y values aren't the same as MainMenuPanel's slot 1+2 gas
    // values (132/206). At MM state pose, slot 1's wood button center
    // sits at mesh Y ≈ 0.605 → screen Y ≈ 281 (gas Y ≈ 156). At SP
    // state pose, slot 1's wood button center is at mesh Y ≈ 0.144 →
    // screen Y ≈ 478 (gas Y ≈ 266) on a height-constrained widescreen
    // viewport. Slot 2 likewise drops from gas Y ≈ 230 (MM) to gas
    // Y ≈ 341 (SP). Receipts: `siegefx asp trace-pose menubars.asp
    // menubars_mm2sp.prs 1.0` for subsets 4+5 (MenuLogBASE2 / 1
    // chrome at SP pose).
    static readonly (int X, int Y, int W, int H, string Label, Action OnClick) [] Buttons =
    {
        (280, 236, 237, 60, "START NEW GAME", Action.NewGame),
        (280, 311, 237, 60, "LOAD GAME",      Action.LoadGame),
    };

    Action _hovered = Action.None;
    Action _pressed = Action.None;
    int _fontScale = 1;
    (int X, int Y, int W, int H)[] _rects = new (int, int, int, int)[Buttons.Length];
    (int X, int Y, int W, int H) _backRect;

    // Back button rect — matches MainMenuPanel's EXIT rect by construction
    // so the wood button stays visually anchored at the same screen
    // position across the Main → SP transition.
    const int BackX = 359, BackY = 570, BackW = 79, BackH = 46;

    void Layout(int viewportW, int viewportH)
    {
        float scale = MathF.Min(viewportH / 600f, viewportW / 800f);
        int authoredW = (int)MathF.Round(800 * scale);
        int authoredH = (int)MathF.Round(600 * scale);
        int dx = (viewportW - authoredW) / 2;
        int dy = (viewportH - authoredH) / 2;
        _fontScale = Math.Max(1, (int)MathF.Round(scale));
        for (int i = 0; i < Buttons.Length; i++)
        {
            var b = Buttons[i];
            _rects[i] = (
                dx + (int)MathF.Round(b.X * scale),
                dy + (int)MathF.Round(b.Y * scale),
                (int)MathF.Round(b.W * scale),
                (int)MathF.Round(b.H * scale));
        }
        _backRect = (
            dx + (int)MathF.Round(BackX * scale),
            dy + (int)MathF.Round(BackY * scale),
            (int)MathF.Round(BackW * scale),
            (int)MathF.Round(BackH * scale));
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

    /// <summary>Mirror of <see cref="MainMenuPanel.TryGetButtonStateAndRect"/>
    /// — exposes per-button screen rect + hover/press state so the host
    /// can render hover overlays + the BACK button visual without
    /// reaching into the panel's private layout.</summary>
    public bool TryGetButtonStateAndRect(Action act, int viewportW, int viewportH,
                                         out int x, out int y, out int w, out int h,
                                         out bool hovered, out bool pressed)
    {
        Layout(viewportW, viewportH);
        if (act == Action.Back)
        {
            x = _backRect.X; y = _backRect.Y; w = _backRect.W; h = _backRect.H;
            hovered = _hovered == Action.Back;
            pressed = _pressed == Action.Back;
            return true;
        }
        for (int i = 0; i < Buttons.Length; i++)
        {
            if (Buttons[i].OnClick != act) continue;
            x = _rects[i].X; y = _rects[i].Y; w = _rects[i].W; h = _rects[i].H;
            hovered = _hovered == act;
            pressed = _pressed == act;
            return true;
        }
        x = y = w = h = 0;
        hovered = pressed = false;
        return false;
    }

    Action HitTest(int px, int py)
    {
        for (int i = 0; i < Buttons.Length; i++)
            if (Hits(_rects[i], px, py)) return Buttons[i].OnClick;
        if (Hits(_backRect, px, py)) return Action.Back;
        return Action.None;
    }
}
