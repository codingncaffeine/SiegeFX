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

    /// <summary>SC-EXIT-CONFIRM — DS1's show_exit_game_dialog: EXIT GAME first
    /// raises "Are you sure you want to exit to the Main Menu without saving?"
    /// over the dimmed menu (user ref withoutsaving.bmp). While true, the five
    /// menu buttons are inert/dimmed and only Yes / No respond.</summary>
    public bool ConfirmingExit { get; private set; }

    public enum Action { None, Resume, Options, SaveGame, LoadGame, ExitGame, ExitConfirmed }

    public const int RefW = 640, RefH = 480;

    private const string ConfirmText =
        "ARE YOU SURE YOU WANT TO EXIT TO THE MAIN MENU WITHOUT SAVING?";
    // Confirm panel + Yes/No rects in the 640×480 reference frame (measured
    // off the retail screenshot: panel spans the menu column's width, text
    // upper third, buttons lower third).
    private static readonly (int x0, int y0, int x1, int y1) ConfirmPanel = (202, 198, 438, 286);
    private static readonly (int x0, int y0, int x1, int y1) YesRect = (256, 252, 316, 272);
    private static readonly (int x0, int y0, int x1, int y1) NoRect  = (326, 252, 386, 272);
    private readonly MenuButton _yesBtn = new("Yes", 0, 0, 0, 0);
    private readonly MenuButton _noBtn  = new("No", 0, 0, 0, 0);

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

    public void Toggle()
    {
        // Esc while the exit confirm is up backs out of the confirm first —
        // the menu itself stays open (one Esc per layer).
        if (IsOpen && ConfirmingExit) { ConfirmingExit = false; CancelPresses(); return; }
        IsOpen = !IsOpen;
        if (!IsOpen) { ConfirmingExit = false; CancelPresses(); }
    }
    public void Close()  { IsOpen = false; ConfirmingExit = false; CancelPresses(); }

    /// <summary>SC-EXIT-CONFIRM — enter the exit confirmation state (EXIT GAME
    /// was clicked). The menu stays open behind the dialog.</summary>
    public void BeginExitConfirm() { ConfirmingExit = true; CancelPresses(); }

    private void CancelPresses()
    {
        foreach (var b in _buttons) b.CancelPress();
        _yesBtn.CancelPress();
        _noBtn.CancelPress();
    }

    private void Layout(int viewportW, int viewportH)
    {
        // SC-HUD-MATCH — the button column sizes by the SAME shared HUD
        // scale as every panel (baseline × UI-scale knob) so the Escape
        // menu matches the rest of the interface, and the whole authored
        // composition centers on BOTH axes — that's what keeps the column
        // mid-screen at any scale (the earlier clamp attempt only scaled,
        // leaving the top-anchored Y coords stranded high).
        float s = HudScale.Hud(viewportH);
        int originX = (int)MathF.Round((viewportW - RefW * s) / 2f);
        int originY = (int)MathF.Round((viewportH - RefH * s) / 2f);
        for (int i = 0; i < _buttons.Length; i++)
        {
            var r = Buttons[i].R;
            _buttons[i].X      = originX + (int)MathF.Round(r.x0 * s);
            _buttons[i].Y      = originY + (int)MathF.Round(r.y0 * s);
            _buttons[i].Width  = (int)MathF.Round((r.x1 - r.x0) * s);
            _buttons[i].Height = (int)MathF.Round((r.y1 - r.y0) * s);
        }
        void Place(MenuButton b, (int x0, int y0, int x1, int y1) r)
        {
            b.X      = originX + (int)MathF.Round(r.x0 * s);
            b.Y      = originY + (int)MathF.Round(r.y0 * s);
            b.Width  = (int)MathF.Round((r.x1 - r.x0) * s);
            b.Height = (int)MathF.Round((r.y1 - r.y0) * s);
        }
        Place(_yesBtn, YesRect);
        Place(_noBtn, NoRect);
    }

    public void OnMouseMove(int px, int py)
    {
        if (!IsOpen) return;
        if (ConfirmingExit)
        {
            _yesBtn.UpdateHover(px, py);
            _noBtn.UpdateHover(px, py);
            return;
        }
        foreach (var b in _buttons) b.UpdateHover(px, py);
    }

    /// <summary>LMB-down. Returns true if the menu consumed the click (it owns
    /// the screen while open, so it always does).</summary>
    public bool OnMouseDown(int px, int py)
    {
        if (!IsOpen) return false;
        if (ConfirmingExit)
        {
            _yesBtn.TryPress(px, py);
            _noBtn.TryPress(px, py);
            return true;
        }
        foreach (var b in _buttons) b.TryPress(px, py);
        return true;
    }

    /// <summary>LMB-up. Returns the <see cref="Action"/> of the button that
    /// fired this frame (<see cref="Action.None"/> if the release missed every
    /// button — the up-event is still consumed upstream while the menu is open).</summary>
    public Action OnMouseUp(int px, int py)
    {
        if (!IsOpen) return Action.None;
        if (ConfirmingExit)
        {
            if (_yesBtn.Release(px, py)) return Action.ExitConfirmed;
            if (_noBtn.Release(px, py)) { ConfirmingExit = false; CancelPresses(); }
            return Action.None;
        }
        for (int i = 0; i < _buttons.Length; i++)
            if (_buttons[i].Release(px, py)) return Buttons[i].A;
        return Action.None;
    }

    public void Draw(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                     Func<string, GlTexture?>? guiTex, int viewportW, int viewportH)
    {
        if (!IsOpen) return;
        Layout(viewportW, viewportH);

        // Modal dim so the paused world reads as suspended (DS1 sets b modal =
        // true; the button_5 chrome carries each button's background).
        bars.DrawRect(viewportW, viewportH, 0, 0, viewportW, viewportH, new Vector4(0f, 0f, 0f, 0.55f));

        // SC-HUD-MATCH — label size rides the same shared HUD scale as the
        // buttons (was raw viewportH/480 = oversized vs the rest of the UI).
        int fontScale = System.Math.Max(1, (int)MathF.Round(HudScale.Hud(viewportH)));
        foreach (var b in _buttons)
        {
            var state = b.Pressed ? ButtonChrome.State.Down
                      : b.Hovered && !ConfirmingExit ? ButtonChrome.State.Hover
                                  : ButtonChrome.State.Up;
            // Authentic button_5 push-button chrome; fall back to the flat fill
            // if the raws don't resolve (headless / missing art).
            bool chrome = ButtonChrome.Draw(icons, guiTex, viewportW, viewportH,
                                            b.X, b.Y, b.Width, b.Height, "button5", state);
            if (!chrome) { b.Draw(bars, text, viewportW, viewportH); continue; }

            // SC-EXIT-CONFIRM — the menu dims to ~half ink behind the confirm
            // dialog (retail grays the column out; only Yes/No are live).
            var ink = ConfirmingExit ? new Vector4(0.45f, 0.42f, 0.36f, 1f)
                    : b.Hovered      ? new Vector4(1f, 0.96f, 0.85f, 1f)
                                     : new Vector4(0.88f, 0.82f, 0.70f, 1f);
            int lw = text.MeasureWidth(b.Label, fontScale);
            int fh = 12 * fontScale;
            text.DrawString(viewportW, viewportH, b.Label,
                            b.X + (b.Width - lw) / 2,
                            b.Y + (b.Height - fh) / 2 + (b.Pressed ? fontScale : 0),
                            ink, fontScale);
        }

        // SC-EXIT-CONFIRM — the "exit without saving?" dialog over the dimmed
        // menu (user ref withoutsaving.bmp): bordered dark panel, centered
        // question, Yes / No push buttons.
        if (ConfirmingExit)
        {
            float s = HudScale.Hud(viewportH);
            int originX = (int)MathF.Round((viewportW - RefW * s) / 2f);
            int originY = (int)MathF.Round((viewportH - RefH * s) / 2f);
            int px0 = originX + (int)MathF.Round(ConfirmPanel.x0 * s);
            int py0 = originY + (int)MathF.Round(ConfirmPanel.y0 * s);
            int pw  = (int)MathF.Round((ConfirmPanel.x1 - ConfirmPanel.x0) * s);
            int ph  = (int)MathF.Round((ConfirmPanel.y1 - ConfirmPanel.y0) * s);
            bars.DrawRect(viewportW, viewportH, px0, py0, pw, ph,
                new Vector4(0.02f, 0.02f, 0.02f, 0.88f));
            bars.DrawBorder(viewportW, viewportH, px0, py0, pw, ph,
                new Vector4(0.42f, 0.40f, 0.34f, 1f));

            // Question, centered; wraps to two lines when the panel is narrow.
            var qInk = new Vector4(0.93f, 0.90f, 0.74f, 1f);
            int qw = text.MeasureWidth(ConfirmText, fontScale);
            int lineH = 12 * fontScale;
            int qy = py0 + (int)MathF.Round(24 * s);
            if (qw <= pw - 12 * fontScale)
            {
                text.DrawString(viewportW, viewportH, ConfirmText,
                    px0 + (pw - qw) / 2, qy, qInk, fontScale);
            }
            else
            {
                const string l1 = "ARE YOU SURE YOU WANT TO EXIT TO";
                const string l2 = "THE MAIN MENU WITHOUT SAVING?";
                int w1 = text.MeasureWidth(l1, fontScale);
                int w2 = text.MeasureWidth(l2, fontScale);
                text.DrawString(viewportW, viewportH, l1, px0 + (pw - w1) / 2, qy, qInk, fontScale);
                text.DrawString(viewportW, viewportH, l2, px0 + (pw - w2) / 2, qy + lineH + 2, qInk, fontScale);
            }

            foreach (var (btn, label) in new[] { (_yesBtn, "Yes"), (_noBtn, "No") })
            {
                var bstate = btn.Pressed ? ButtonChrome.State.Down
                           : btn.Hovered ? ButtonChrome.State.Hover
                                         : ButtonChrome.State.Up;
                bool bchrome = ButtonChrome.Draw(icons, guiTex, viewportW, viewportH,
                    btn.X, btn.Y, btn.Width, btn.Height, "button5", bstate);
                if (!bchrome) { btn.Draw(bars, text, viewportW, viewportH); continue; }
                var bink = btn.Hovered ? new Vector4(1f, 0.96f, 0.85f, 1f)
                                       : new Vector4(0.88f, 0.82f, 0.70f, 1f);
                int blw = text.MeasureWidth(label, fontScale);
                int bfh = 12 * fontScale;
                text.DrawString(viewportW, viewportH, label,
                    btn.X + (btn.Width - blw) / 2,
                    btn.Y + (btn.Height - bfh) / 2 + (btn.Pressed ? fontScale : 0),
                    bink, fontScale);
            }
        }
    }
}
