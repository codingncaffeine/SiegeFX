using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// DS1's in-game (Escape) menu — <c>/ui/interfaces/backend/in_game_menu/
/// in_game_menu.gas</c>. A modal overlay with five copperplate buttons stacked
/// and screen-centered (the gas sets <c>centered = save_button</c>, the middle
/// one): RESUME GAME, OPTIONS, SAVE GAME, LOAD GAME, EXIT GAME. The gas authors
/// no background frame — the buttons sit over a dimmed, paused world.
///
/// Layout is the authored 640×480 reference, uniformly scaled by viewportH/480
/// and horizontally centered, recomputed each Draw so a window resize re-centers
/// without extra plumbing. The host acts on the <see cref="Action"/> returned
/// from <see cref="OnMouseUp"/>.
/// </summary>
public sealed class PauseMenu
{
    public bool IsOpen { get; set; }

    public enum Action { None, Resume, Options, SaveGame, LoadGame, ExitGame }

    public const int RefW = 640, RefH = 480;

    // Authored 640×480 button rects (x0,y0,x1,y1), top-to-bottom, each 160×32
    // and contiguous from y159. Labels are all-caps exactly as the gas text
    // children (justify = center, font b_gui_fnt_12p_copperplate-light).
    private static readonly (Action A, (int x0, int y0, int x1, int y1) R, string Label)[] Buttons =
    {
        (Action.Resume,   (240, 159, 400, 191), "RESUME GAME"),
        (Action.Options,  (240, 191, 400, 223), "OPTIONS"),
        (Action.SaveGame, (240, 223, 400, 255), "SAVE GAME"),
        (Action.LoadGame, (240, 255, 400, 287), "LOAD GAME"),
        (Action.ExitGame, (240, 287, 400, 319), "EXIT GAME"),
    };

    private readonly MenuButton[] _buttons;

    public PauseMenu()
    {
        _buttons = new MenuButton[Buttons.Length];
        for (int i = 0; i < Buttons.Length; i++)
            _buttons[i] = new MenuButton(Buttons[i].Label, 0, 0, 0, 0);
    }

    public void Toggle() { IsOpen = !IsOpen; if (!IsOpen) CancelPresses(); }
    public void Close()  { IsOpen = false;  CancelPresses(); }

    private void CancelPresses() { foreach (var b in _buttons) b.CancelPress(); }

    private void Layout(int viewportW, int viewportH)
    {
        float s = viewportH / (float)RefH;
        int originX = (int)MathF.Round((viewportW - RefW * s) / 2f);
        for (int i = 0; i < _buttons.Length; i++)
        {
            var r = Buttons[i].R;
            _buttons[i].X      = originX + (int)MathF.Round(r.x0 * s);
            _buttons[i].Y      = (int)MathF.Round(r.y0 * s);
            _buttons[i].Width  = (int)MathF.Round((r.x1 - r.x0) * s);
            _buttons[i].Height = (int)MathF.Round((r.y1 - r.y0) * s);
        }
    }

    public void OnMouseMove(int px, int py)
    {
        if (!IsOpen) return;
        foreach (var b in _buttons) b.UpdateHover(px, py);
    }

    /// <summary>LMB-down. Returns true if the menu consumed the click (it owns
    /// the screen while open, so it always does).</summary>
    public bool OnMouseDown(int px, int py)
    {
        if (!IsOpen) return false;
        foreach (var b in _buttons) b.TryPress(px, py);
        return true;
    }

    /// <summary>LMB-up. Returns the <see cref="Action"/> of the button that
    /// fired this frame (<see cref="Action.None"/> if the release missed every
    /// button — the up-event is still consumed upstream while the menu is open).</summary>
    public Action OnMouseUp(int px, int py)
    {
        if (!IsOpen) return Action.None;
        for (int i = 0; i < _buttons.Length; i++)
            if (_buttons[i].Release(px, py)) return Buttons[i].A;
        return Action.None;
    }

    public void Draw(BarRenderer bars, TextRenderer text, int viewportW, int viewportH)
    {
        if (!IsOpen) return;
        Layout(viewportW, viewportH);

        // Modal dim so the paused world reads as suspended (DS1 sets b modal =
        // true; the button_5 chrome carries each button's background).
        bars.DrawRect(viewportW, viewportH, 0, 0, viewportW, viewportH, new Vector4(0f, 0f, 0f, 0.55f));
        foreach (var b in _buttons) b.Draw(bars, text, viewportW, viewportH);
    }
}
