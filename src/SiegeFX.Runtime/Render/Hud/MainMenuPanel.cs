using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 24-MAINMENU step 5+6 — the seven-button main menu the FrontendScene
/// drops into after the splash → logo-drop sequence completes.
///
/// <para><b>Phase 24 splinters parked at the end of this slice:</b></para>
/// <list type="bullet">
///   <item><b>SC-MAINMENU-NEWGAME</b> — wire SinglePlayer click to a region
///         launch path that doesn't require <c>--play-region</c> CLI args
///         (resolve fh_r1 paths from <c>_ds1ResourcesDir</c>, run LoadRegion +
///         LoadPlayActors + open the existing CharacterCreator).</item>
///   <item><b>SC-MAINMENU-CONTINUE</b> — Continue → load the most recent
///         quicksave from the SaveStore.</item>
///   <item><b>SC-MAINMENU-MULTIPLAYER</b> — Multiplayer sub-screen +
///         provider selection (DS1 ships <c>multiplayer_provider.gas</c>
///         + <c>map_chooser.gas</c> for the LAN/IP/zone picker).</item>
///   <item><b>SC-MAINMENU-CREDITS</b> — Credits sub-screen; possibly
///         playing <c>credits.bik</c> from <c>Objects.dsres /movies/</c>
///         once SC-MAINMENU-BINK lands.</item>
///   <item><b>SC-MAINMENU-BINK</b> — real Bink playback for
///         <c>gpg_intro.bik</c> in the IntroBink slot (1s fade today).</item>
///   <item><b>SC-MAINMENU-LOGO-EXIT</b> — play <c>logo-exit.prs</c>
///         (0.58s sword rise) on the IntroLogoDrop → MainMenu transition
///         instead of cutting to no-logo instantly.</item>
///   <item><b>SC-MAINMENU-NIS</b> — full NIS gizmo runtime
///         (<c>cmd_enter_nis</c> / <c>cmd_camera_command</c> /
///         <c>cmd_camera_waypoint</c> / <c>cmd_leave_nis</c>) so the
///         farmboy-region opening cinematic + the other 18 shipped
///         NISs play. ESC fast-forward + subtitle wiring through
///         <c>conversations.gas nis = true</c>. See
///         <c>project_siegefx_nis_research.md</c>.</item>
///   <item><b>SC-MAINMENU-CHROME-LINEUP</b> — buttons today scale by
///         viewport height in 800×600 authored space, but the
///         FrontendScene chrome behind them is projected via the
///         backdrop's mesh-space rect; non-4:3 monitors will see the
///         buttons floating off-axis vs the chrome's painted button
///         slots. Either project the panel through the same backdrop
///         basis or letterbox the chrome to match 800×600.</item>
///   <item><b>SC-MAINMENU-BUTTONS-RAW</b> — swap the colored-rectangle
///         button visuals for DS1's shipped <c>button_wood_up/down/hov.raw</c>
///         (16,400 bytes each, 128×128) and the matching exit button atlas.</item>
///   <item><b>SC-MAINMENU-ABOUT-RAW</b> — load <c>about_dialog.gas</c>
///         layout for the About sub-screen + use DS1's wood-bordered
///         chrome instead of the placeholder dim-card.</item>
/// </list></summary>
///
/// <para><b>DS1 reference (from <c>main_menu.gas</c> per
/// <c>project_siegefx_frontend_gas_layout.md</c>):</b> Authored at the 800×600
/// frontend reference space. Buttons sit in a vertical column at x=280..517;
/// row pitch ~75 px. Each button has a notify() name DS1's frontend_lights
/// state machine consumes — we map those to direct state transitions /
/// host actions since SiegeFX's frontend state machine is hand-rolled.</para>
///
/// <para>Layout fields are in authored 800×600 coords; <see cref="Layout"/>
/// scales by viewport height (uniform) and centers horizontally.</para>
/// </summary>
internal sealed class MainMenuPanel
{
    /// <summary>Action emitted when a button click commits. Host translates
    /// each into a state transition or a runtime command.</summary>
    public enum Action
    {
        None,
        SinglePlayer,
        Multiplayer,
        Options,
        Continue,
        About,
        Exit,
        Credits,
    }

    /// <summary>True while the menu accepts input. Set false from the host
    /// when a sub-screen / character creator / options dialog is on top.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Set by <see cref="OnMouseUp"/> when the user clicks a button.
    /// Host inspects + clears via <see cref="ConsumeAction"/> on the same
    /// frame so click events fire exactly once.</summary>
    Action _pending = Action.None;
    public Action ConsumeAction()
    {
        var a = _pending;
        _pending = Action.None;
        return a;
    }

    // Authored 800×600 button rects from main_menu.gas.
    // (rect = x0,y0,x1,y1 in DS1; convert to (x,y,w,h) on read.)
    // Phase 25-CHROME-FOLD7/9 — Exit button rect tuned by user feedback.
    // main_menu.gas authors it at 361,567,440,598 (W=79,H=31). Final
    // tuning: centered below the ABOUT button (which spans x=280..517,
    // center x=398.5) and moved up 25px from the prior tuning at y=517.
    // EXIT W=79 with X=359 puts its center at 398.5 (exactly matching
    // ABOUT's center). Resulting rect: 359,492 with W=79, H=46.
    static readonly (int X, int Y, int W, int H, string Label, Action OnClick) [] Buttons =
    {
        (280, 132, 517 - 280, 178 - 132, "SINGLE PLAYER", Action.SinglePlayer),
        (280, 206, 517 - 280, 252 - 206, "MULTIPLAYER",   Action.Multiplayer),
        (280, 280, 517 - 280, 326 - 280, "OPTIONS",       Action.Options),
        (280, 355, 517 - 280, 401 - 355, "CONTINUE",      Action.Continue),
        (280, 430, 517 - 280, 476 - 430, "ABOUT",         Action.About),
        (359, 499, 79, 46,               "EXIT",          Action.Exit),
    };
    // Bottom-right credits glyph anchor; sized at draw time.
    const int CreditsAuthoredW = 16, CreditsAuthoredH = 16;

    /// <summary>Phase 24-MAINMENU step 5+6 — DS1 ships these notify names in
    /// main_menu.gas; surfaced for any future UI that wants to dispatch by
    /// name rather than by Action enum (e.g. a frontend.skrit successor or
    /// a script-driven A11y narrator). Not currently consumed.</summary>
    public static string NotifyNameFor(Action a) => a switch
    {
        Action.SinglePlayer => "show_single_player_menu",
        Action.Multiplayer  => "multi_player_button_press",
        Action.Options      => "show_options",
        Action.Continue     => "continue_button_press",
        Action.About        => "about_button_press",
        Action.Exit         => "exit_button_press",
        Action.Credits      => "button_credits",
        _ => "",
    };

    Action _hovered = Action.None;
    Action _pressed = Action.None;
    int _fontScale = 1;
    (int X, int Y, int W, int H)[] _rects = new (int, int, int, int)[Buttons.Length];
    (int X, int Y, int W, int H) _creditsRect;

    // Phase 24-POLISH-B — DS1 wood-button textures. Host loads via the
    // play resolver (b_gui_fe_m_mn_3d_button_wood_up/hov/down.raw 128×128)
    // and exitback variants 256×256, calls SetButtonTextures once. When
    // non-null Draw renders textured quads instead of the placeholder
    // colored rectangles. Stays null when textures fail to resolve so
    // the menu degrades gracefully on a stripped install.
    // Phase 25-CHROME-FOLD12 — also takes the text-small atlas trio
    // (text-small / text-small-up / text-small-down) which carries the
    // EXIT label baked in alongside other state-tree button labels
    // (NEXT / PREVIOUS / NAME / HERO / BACK / etc.). Per user inspection
    // of the bulk-decoded PNGs: EXIT lives in the bottom-LEFT region of
    // the atlas's visual layout (the user's viewer description was top-
    // right, which corresponds to bottom-left in the V-flipped renderer
    // convention — RAW textures store bottom-up, so visual-bottom of the
    // PNG maps to stored-top in the byte order).
    GlTexture? _texUp, _texHov, _texDown;
    GlTexture? _texExitUp, _texExitHov, _texExitDown;
    GlTexture? _texSmall, _texSmallUp, _texSmallDown;
    public void SetButtonTextures(GlTexture? up, GlTexture? hov, GlTexture? down,
                                  GlTexture? exitUp, GlTexture? exitHov, GlTexture? exitDown,
                                  GlTexture? textSmall = null,
                                  GlTexture? textSmallUp = null,
                                  GlTexture? textSmallDown = null)
    {
        _texUp = up; _texHov = hov; _texDown = down;
        _texExitUp = exitUp; _texExitHov = exitHov; _texExitDown = exitDown;
        _texSmall = textSmall; _texSmallUp = textSmallUp; _texSmallDown = textSmallDown;
    }

    void Layout(int viewportW, int viewportH)
    {
        // Authored at 800×600. Same height-driven scale OptionsMenuPanel uses
        // — viewport height vs 600 reference height — so the menu reads at the
        // same vertical proportion regardless of monitor aspect.
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
        // Credits anchored bottom-right of the authored 800×600 plate, 16,16
        // away from the corner per main_menu.gas. Tiny click target — DS1
        // intent appears to be "discoverable Easter-egg-y" rather than a
        // primary nav.
        _creditsRect = (
            dx + authoredW - (int)MathF.Round((CreditsAuthoredW + 16) * scale),
            dy + authoredH - (int)MathF.Round((CreditsAuthoredH + 16) * scale),
            (int)MathF.Round(CreditsAuthoredW * scale),
            (int)MathF.Round(CreditsAuthoredH * scale));
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

    /// <summary>Phase 24-MAINMENU step 5+6-FOLD — host calls this after a
    /// stub-button click is consumed so the highlight clears immediately
    /// instead of reading as "still selectable" until the cursor moves
    /// off the button. Called per-frame from FlushMainMenu.</summary>
    public void ClearHover() => _hovered = Action.None;

    /// <summary>Phase 26-ARTMAP — expose per-button state + screen rect so
    /// the host can render via the proper asp + art_mapping.gas pipeline
    /// (each widget kind has its own mesh family and texture-swap recipe).
    /// Returns <c>false</c> if the action isn't one of the 7 main menu
    /// buttons or if Layout hasn't run yet for this viewport.</summary>
    public bool TryGetButtonStateAndRect(Action act, int viewportW, int viewportH,
                                         out int x, out int y, out int w, out int h,
                                         out bool hovered, out bool pressed)
    {
        Layout(viewportW, viewportH);
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
        if (Hits(_creditsRect, px, py)) return Action.Credits;
        return Action.None;
    }

    /// <summary>Phase 25-CHROME — visuals for the 5 menubars buttons
    /// (Single Player / Multiplayer / Options / Continue / About) come
    /// from menubars.asp's chrome subsets, NOT from this panel. The user
    /// sees DS1's authored wood-and-engraved-text buttons rendered as
    /// part of the 3D scene; this panel only owns hit-testing for those
    /// rects (the rect Y pitch in main_menu.gas matches MenuBase Z pitch
    /// in menubars_sp2mm by construction). The Exit button (uses
    /// exitback*.raw, separate widget anchored bottom-center) and the
    /// 16×16 Credits hit zone (anchored bottom-right) are NOT in
    /// menubars.asp, so they keep their textured-quad visuals here.</summary>
    public void Draw(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                     int viewportW, int viewportH)
    {
        if (!IsActive) return;
        Layout(viewportW, viewportH);

        var tint = new Vector4(1f, 1f, 1f, 1f);

        // Phase 26-ARTMAP — the EXIT button visual is now rendered by
        // the host through FrontendScene.DrawExitButton() using the
        // proper backbutton.asp mesh + art_mapping.gas texture-swap
        // recipe (per reference_ds1_art_mapping.md). MainMenuPanel
        // owns hit-testing only for that button; the host queries the
        // rect + hover/press state via TryGetButtonStateAndRect.
        // Credits hit zone — invisible by design (DS1's anchored 16×16
        // glyph at the bottom-right is a discoverable corner click, not
        // a primary nav). No fill or label.
    }

    static void DrawBorder(BarRenderer bars, int vw, int vh,
                           (int X, int Y, int W, int H) r, Vector4 color)
    {
        bars.DrawRect(vw, vh, r.X,           r.Y,           r.W, 1,   color);
        bars.DrawRect(vw, vh, r.X,           r.Y + r.H - 1, r.W, 1,   color);
        bars.DrawRect(vw, vh, r.X,           r.Y,           1,   r.H, color);
        bars.DrawRect(vw, vh, r.X + r.W - 1, r.Y,           1,   r.H, color);
    }
}
