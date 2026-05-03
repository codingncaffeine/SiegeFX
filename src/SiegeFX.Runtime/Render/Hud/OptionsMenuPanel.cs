using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>Phase 23-SC-OPTIONS-A — in-game Options Menu skeleton.
///
/// <para><b>DS1 reference (read project_siegefx_options_menu_research.md
/// for the full layout dump).</b> Authoring resolution is 640×480 with a
/// modal centered dialog. Outer frame at (105,22)–(535,390) on a
/// <c>cpbox_wide</c> 9-slice; inner content panel at (117,80)–(524,345);
/// title "Options Menu" centered at the top in a 22pt copperplate-light;
/// four tab buttons across (118,67)–(524,83) — Video / Audio / Input /
/// Game in that order; bottom button bar with OK at (200,358)–(310,374),
/// Cancel at (330,358)–(440,374), and Defaults at (361,318)–(501,334).
/// (Defaults is per-tab — resets only the active tab's controls.)</para>
///
/// <para><b>Modernization:</b> DS1 ships these coords for a 4:3 640×480
/// frame buffer; SiegeFX targets 1080p as the floor and supports 1440p
/// ultrawide + 4K. The panel scales by viewport HEIGHT (preserves the
/// 4:3 aspect of the panel itself), and the menu is centered horizontally
/// on the window. At 1080p that lands a 968×828 panel (90% of vertical
/// height); at 1440p ultrawide a 1290×1104 panel; at 4K a 1935×1656
/// panel — all the same proportion of vertical space, so the menu feels
/// consistent regardless of monitor.</para>
///
/// <para>This slice (A) ships the state machine, tab switching, F10
/// hotkey, pause-menu Options-button hookup, OK/Cancel/Defaults wiring,
/// and the chrome rendered as solid-fill placeholders so layout +
/// interaction can be verified before slice A2 swaps in the
/// <c>b_gui_cmn_*</c> 9-slice borders / subtab strips / button4 atlases.
/// Per-tab content (slices B/C/D/E/F) drops into the
/// <see cref="DrawTabContent"/> branch points.</para></summary>
internal sealed class OptionsMenuPanel
{
    public enum Tab { Video, Audio, Input, Game }

    public bool IsOpen { get; private set; }
    public bool QuitRequested { get; private set; }
    public Tab ActiveTab { get; private set; } = Tab.Video;

    /// <summary>True for one frame after OK is clicked. Caller polls and
    /// applies the staged changes (slice A has nothing to apply; later
    /// slices read this to commit volume / resolution / keybind edits).</summary>
    public bool ConfirmedThisFrame { get; private set; }

    /// <summary>True for one frame after Cancel is clicked or Esc. Caller
    /// polls and discards staged edits.</summary>
    public bool CancelledThisFrame { get; private set; }

    /// <summary>True for one frame after Defaults is clicked. Caller
    /// polls + restores the active tab's controls to their authored
    /// defaults from /config/options.gas.</summary>
    public bool DefaultsRequestedThisFrame { get; private set; }

    // DS1 authoring rects — see header comment for the source. Stored as
    // (l, t, w, h) so Layout's Scale helper composes cleanly.
    static readonly (int X, int Y, int W, int H) RectOuter   = (105,  22, 430, 368);
    static readonly (int X, int Y, int W, int H) RectInner   = (117,  80, 407, 265);
    static readonly (int X, int Y, int W, int H) RectTitle   = (200,  38, 240,  20);
    static readonly (int X, int Y, int W, int H) RectTabRow  = (118,  67, 406,  16);
    // Each tab is ~100×16. The active tab in DS1 is shifted down 3px to
    // overlap the panel edge — slice A doesn't reproduce that yet (no
    // border to overlap until A2 lands the 9-slice chrome).
    static readonly (int X, int Y, int W, int H) RectTabVideo = (118, 67, 100, 16);
    static readonly (int X, int Y, int W, int H) RectTabAudio = (219, 67, 102, 16);
    static readonly (int X, int Y, int W, int H) RectTabInput = (322, 67, 101, 16);
    static readonly (int X, int Y, int W, int H) RectTabGame  = (424, 67, 100, 16);
    static readonly (int X, int Y, int W, int H) RectOk       = (200, 358, 110, 16);
    static readonly (int X, int Y, int W, int H) RectCancel   = (330, 358, 110, 16);
    static readonly (int X, int Y, int W, int H) RectDefaults = (361, 318, 140, 16);

    // Live (post-Layout) viewport-pixel rects so input handlers and the
    // draw loop share one source of truth.
    (int X, int Y, int W, int H) _outer, _inner, _title, _tabRow;
    (int X, int Y, int W, int H) _tabVideo, _tabAudio, _tabInput, _tabGame;
    (int X, int Y, int W, int H) _ok, _cancel, _defaults;
    Tab? _hoveredTab;
    enum Btn { None, Ok, Cancel, Defaults }
    Btn _hoveredBtn;
    Btn _pressedBtn; // tracks LMB press → release for click validation

    static readonly Vector4 PanelBg     = new(0.10f, 0.10f, 0.12f, 0.92f);
    static readonly Vector4 InnerBg     = new(0.16f, 0.16f, 0.18f, 1.00f);
    static readonly Vector4 Border      = new(0.55f, 0.50f, 0.40f, 1.00f);
    static readonly Vector4 TabIdleBg   = new(0.20f, 0.20f, 0.22f, 1.00f);
    static readonly Vector4 TabHoverBg  = new(0.32f, 0.30f, 0.26f, 1.00f);
    static readonly Vector4 TabActiveBg = new(0.16f, 0.16f, 0.18f, 1.00f); // matches inner panel for "tab attached to content" cue
    static readonly Vector4 BtnIdle     = new(0.28f, 0.26f, 0.22f, 1.00f);
    static readonly Vector4 BtnHover    = new(0.40f, 0.38f, 0.30f, 1.00f);
    static readonly Vector4 BtnPress    = new(0.20f, 0.18f, 0.16f, 1.00f);
    static readonly Vector4 Ink         = new(0.95f, 0.92f, 0.80f, 1.00f);
    static readonly Vector4 InkDim      = new(0.65f, 0.62f, 0.55f, 1.00f);

    public void Open()
    {
        IsOpen = true;
        ActiveTab = Tab.Video;
        ConfirmedThisFrame = false;
        CancelledThisFrame = false;
        DefaultsRequestedThisFrame = false;
        _hoveredTab = null;
        _hoveredBtn = Btn.None;
        _pressedBtn = Btn.None;
    }

    public void Close()
    {
        IsOpen = false;
        ConfirmedThisFrame = false;
        CancelledThisFrame = false;
        DefaultsRequestedThisFrame = false;
    }

    /// <summary>Phase 23-SC-OPTIONS-A — caller invokes once per frame
    /// AFTER consuming <see cref="ConfirmedThisFrame"/> /
    /// <see cref="CancelledThisFrame"/> / <see cref="DefaultsRequestedThisFrame"/>
    /// so the edge-trigger flags only fire for one frame.</summary>
    public void ClearEdgeFlags()
    {
        ConfirmedThisFrame = false;
        CancelledThisFrame = false;
        DefaultsRequestedThisFrame = false;
    }

    static (int X, int Y, int W, int H) Scale((int X, int Y, int W, int H) r,
                                                int dx, int dy, float s)
        => ((int)MathF.Round(r.X * s) + dx,
            (int)MathF.Round(r.Y * s) + dy,
            (int)MathF.Round(r.W * s),
            (int)MathF.Round(r.H * s));

    /// <summary>Rebuild every viewport-pixel rect from the authored
    /// 640×480 source rects. Height-driven scale preserves the panel's
    /// 4:3 aspect; horizontal centering keeps it floating in the middle
    /// of widescreen / ultrawide windows. Called from every Draw and
    /// every input handler so a mid-frame resize doesn't desync rects.</summary>
    void Layout(int viewportW, int viewportH, out float scale)
    {
        scale = viewportH / 480f;
        int scaledBaseW = (int)MathF.Round(640 * scale);
        int dx = (viewportW - scaledBaseW) / 2; // center horizontally on the window
        int dy = 0;
        _outer    = Scale(RectOuter,    dx, dy, scale);
        _inner    = Scale(RectInner,    dx, dy, scale);
        _title    = Scale(RectTitle,    dx, dy, scale);
        _tabRow   = Scale(RectTabRow,   dx, dy, scale);
        _tabVideo = Scale(RectTabVideo, dx, dy, scale);
        _tabAudio = Scale(RectTabAudio, dx, dy, scale);
        _tabInput = Scale(RectTabInput, dx, dy, scale);
        _tabGame  = Scale(RectTabGame,  dx, dy, scale);
        _ok       = Scale(RectOk,       dx, dy, scale);
        _cancel   = Scale(RectCancel,   dx, dy, scale);
        _defaults = Scale(RectDefaults, dx, dy, scale);
    }

    static bool Hits((int X, int Y, int W, int H) r, int px, int py) =>
        px >= r.X && px < r.X + r.W && py >= r.Y && py < r.Y + r.H;

    /// <summary>Caller forwards Esc — closes the menu (Cancel semantics).</summary>
    public void OnEscape()
    {
        if (!IsOpen) return;
        CancelledThisFrame = true;
        IsOpen = false;
    }

    /// <summary>Caller forwards Enter — closes the menu (OK semantics).</summary>
    public void OnEnter()
    {
        if (!IsOpen) return;
        ConfirmedThisFrame = true;
        IsOpen = false;
    }

    public void OnMouseMove(int px, int py, int viewportW, int viewportH)
    {
        if (!IsOpen) return;
        Layout(viewportW, viewportH, out _);
        _hoveredTab =
            Hits(_tabVideo, px, py) ? Tab.Video :
            Hits(_tabAudio, px, py) ? Tab.Audio :
            Hits(_tabInput, px, py) ? Tab.Input :
            Hits(_tabGame,  px, py) ? Tab.Game  :
            null;
        _hoveredBtn =
            Hits(_ok,       px, py) ? Btn.Ok       :
            Hits(_cancel,   px, py) ? Btn.Cancel   :
            Hits(_defaults, px, py) ? Btn.Defaults :
            Btn.None;
    }

    public void OnMouseDown(int px, int py, int viewportW, int viewportH)
    {
        if (!IsOpen) return;
        Layout(viewportW, viewportH, out _);
        _pressedBtn =
            Hits(_ok,       px, py) ? Btn.Ok       :
            Hits(_cancel,   px, py) ? Btn.Cancel   :
            Hits(_defaults, px, py) ? Btn.Defaults :
            Btn.None;
    }

    public void OnMouseUp(int px, int py, int viewportW, int viewportH)
    {
        if (!IsOpen) return;
        Layout(viewportW, viewportH, out _);
        // Tabs fire on click-up if the cursor is still over the tab.
        // (Pre-fold the original DS1 fired on `oncheck` of a radio group;
        // we treat each tab as a click-up button to keep the input
        // handler symmetric with the bottom buttons.)
        if (Hits(_tabVideo, px, py)) ActiveTab = Tab.Video;
        else if (Hits(_tabAudio, px, py)) ActiveTab = Tab.Audio;
        else if (Hits(_tabInput, px, py)) ActiveTab = Tab.Input;
        else if (Hits(_tabGame,  px, py)) ActiveTab = Tab.Game;

        // Bottom buttons require press AND release on the same button —
        // matches Windows-standard click semantics.
        var btn =
            Hits(_ok,       px, py) ? Btn.Ok       :
            Hits(_cancel,   px, py) ? Btn.Cancel   :
            Hits(_defaults, px, py) ? Btn.Defaults :
            Btn.None;
        if (btn == _pressedBtn && btn != Btn.None)
        {
            switch (btn)
            {
                case Btn.Ok:
                    ConfirmedThisFrame = true;
                    IsOpen = false;
                    break;
                case Btn.Cancel:
                    CancelledThisFrame = true;
                    IsOpen = false;
                    break;
                case Btn.Defaults:
                    DefaultsRequestedThisFrame = true;
                    break;
            }
        }
        _pressedBtn = Btn.None;
    }

    public bool IsPointInPanel(int px, int py, int viewportW, int viewportH)
    {
        if (!IsOpen) return false;
        Layout(viewportW, viewportH, out _);
        return Hits(_outer, px, py);
    }

    public void Draw(BarRenderer bars, TextRenderer text, int viewportW, int viewportH)
    {
        if (!IsOpen) return;
        Layout(viewportW, viewportH, out _);

        // Modal dim: a screen-wide darkening so the underlying scene
        // stops competing for attention. 60% black is the same shade
        // PauseMenu uses.
        bars.DrawRect(viewportW, viewportH, 0, 0, viewportW, viewportH,
            new Vector4(0f, 0f, 0f, 0.60f));

        // Outer panel (placeholder solid fill — slice A2 will swap in
        // the b_gui_cmn_cpbox2 9-slice border).
        bars.DrawRect(viewportW, viewportH, _outer.X, _outer.Y, _outer.W, _outer.H, PanelBg);
        DrawBorder(bars, viewportW, viewportH, _outer, Border);

        // Title.
        var titleStr = "Options Menu";
        int titleW = text.MeasureWidth(titleStr);
        int titleX = _outer.X + (_outer.W - titleW) / 2;
        int titleY = _title.Y;
        text.DrawString(viewportW, viewportH, titleStr, titleX, titleY, Ink);

        // Tab bar — four equal-ish strips. Active tab uses the inner-panel
        // background colour so it visually attaches to the content area.
        DrawTab(bars, text, viewportW, viewportH, _tabVideo, "VIDEO", Tab.Video);
        DrawTab(bars, text, viewportW, viewportH, _tabAudio, "AUDIO", Tab.Audio);
        DrawTab(bars, text, viewportW, viewportH, _tabInput, "INPUT", Tab.Input);
        DrawTab(bars, text, viewportW, viewportH, _tabGame,  "GAME",  Tab.Game);

        // Inner content panel.
        bars.DrawRect(viewportW, viewportH, _inner.X, _inner.Y, _inner.W, _inner.H, InnerBg);
        DrawBorder(bars, viewportW, viewportH, _inner, Border);

        // Tab content (slice A — placeholder labels; slices B–F replace).
        DrawTabContent(bars, text, viewportW, viewportH);

        // Bottom buttons.
        DrawButton(bars, text, viewportW, viewportH, _ok,       "OK",       Btn.Ok);
        DrawButton(bars, text, viewportW, viewportH, _cancel,   "Cancel",   Btn.Cancel);
        DrawButton(bars, text, viewportW, viewportH, _defaults, "Defaults", Btn.Defaults);
    }

    void DrawTab(BarRenderer bars, TextRenderer text, int vw, int vh,
                 (int X, int Y, int W, int H) r, string label, Tab id)
    {
        bool active = ActiveTab == id;
        bool hover  = _hoveredTab == id && !active;
        var bg = active ? TabActiveBg : (hover ? TabHoverBg : TabIdleBg);
        bars.DrawRect(vw, vh, r.X, r.Y, r.W, r.H, bg);
        DrawBorder(bars, vw, vh, r, Border);
        int labelW = text.MeasureWidth(label);
        int lx = r.X + (r.W - labelW) / 2;
        int ly = r.Y + (r.H - 12) / 2;
        text.DrawString(vw, vh, label, lx, ly, active ? Ink : InkDim);
    }

    void DrawButton(BarRenderer bars, TextRenderer text, int vw, int vh,
                    (int X, int Y, int W, int H) r, string label, Btn id)
    {
        Vector4 bg;
        if (_pressedBtn == id && _hoveredBtn == id) bg = BtnPress;
        else if (_hoveredBtn == id) bg = BtnHover;
        else bg = BtnIdle;
        bars.DrawRect(vw, vh, r.X, r.Y, r.W, r.H, bg);
        DrawBorder(bars, vw, vh, r, Border);
        int labelW = text.MeasureWidth(label);
        int lx = r.X + (r.W - labelW) / 2;
        int ly = r.Y + (r.H - 12) / 2;
        text.DrawString(vw, vh, label, lx, ly, Ink);
    }

    static void DrawBorder(BarRenderer bars, int vw, int vh,
                           (int X, int Y, int W, int H) r, Vector4 color)
    {
        // 1-pixel outline (width-aware would be nicer but the bitmap
        // chrome in slice A2 will replace this). Rendered as four edges
        // so the corner pixels overlap cleanly.
        bars.DrawRect(vw, vh, r.X,           r.Y,           r.W, 1,   color);
        bars.DrawRect(vw, vh, r.X,           r.Y + r.H - 1, r.W, 1,   color);
        bars.DrawRect(vw, vh, r.X,           r.Y,           1,   r.H, color);
        bars.DrawRect(vw, vh, r.X + r.W - 1, r.Y,           1,   r.H, color);
    }

    void DrawTabContent(BarRenderer bars, TextRenderer text, int vw, int vh)
    {
        // Slice A placeholders. Each tab's real content lands in B/C/D/E/F.
        var (msg, sub) = ActiveTab switch
        {
            Tab.Video => ("Video Tab",
                          "Resolution / Shadows / Texture Filtering / Gamma / Object Detail"),
            Tab.Audio => ("Audio Tab",
                          "Sound on/off + 5 volume sliders + EAX"),
            Tab.Input => ("Input Tab",
                          "Camera + mouse sensitivity + invert/lock + Hotkeys sub-screen"),
            Tab.Game  => ("Game Tab",
                          "Framerate / Priority / Text Scroll / Difficulty / Tooltips / Blood / ..."),
            _         => ("?", "")
        };
        int innerCx = _inner.X + _inner.W / 2;
        int titleW = text.MeasureWidth(msg);
        text.DrawString(vw, vh, msg, innerCx - titleW / 2, _inner.Y + 24, Ink);
        int subW = text.MeasureWidth(sub);
        text.DrawString(vw, vh, sub, innerCx - subW / 2, _inner.Y + 48, InkDim);
        int hintW = text.MeasureWidth("(slice A skeleton — content lands in slices B / C / D / E / F)");
        text.DrawString(vw, vh, "(slice A skeleton — content lands in slices B / C / D / E / F)",
                        innerCx - hintW / 2, _inner.Y + _inner.H - 24, InkDim);
    }
}
