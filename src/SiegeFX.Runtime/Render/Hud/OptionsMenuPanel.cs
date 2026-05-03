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

    /// <summary>Phase 23-SC-OPTIONS-B/C/D/F — staged settings buffer.
    /// Edits in the menu mutate this; OK commits to the live engine
    /// (audio volumes apply via <see cref="ApplyAudioToEngine"/>),
    /// Cancel discards by re-syncing from the engine on next Open,
    /// Defaults resets the active tab's fields from
    /// /config/options.gas. SiegeFX's persistence path through
    /// prefs.gas writes is parked as splinter SC-OPTIONS-PERSIST so
    /// values currently round-trip within a session only.</summary>
    public sealed class Settings
    {
        // Video — placeholders that persist to prefs.gas in a future
        // slice. Defaults from /config/options.gas.
        public string Resolution = "1920x1080x32";
        public string Shadows = "complex_party";
        public string TextureFiltering = "trilinear";
        public float Gamma = 1.0f;          // 0..2 (UI 0..100 step 10)
        public float ObjectDetail = 1.0f;   // 0..1 (UI 0..10 step 1)

        // Audio — Master + Music apply live; SFX caps per-Play gain;
        // Sound off mutes everything via master. Ambient + Voice
        // currently persist-only since SiegeFX doesn't separate those
        // channels (Phase 18 wired them into the SFX pool).
        public bool SoundEnabled = true;
        public int MasterVolume = 108;     // 0..127
        public int MusicVolume  = 108;
        public int SfxVolume    = 74;
        public int AmbientVolume = 90;
        public int VoiceVolume   = 85;
        public bool EaxEnabled = false;

        // Input
        public bool CameraInverseX = false;
        public bool CameraInverseY = false;
        public bool ScreenEdgeTracking = true;
        public int CameraSensitivity = 50; // 0..100 (composite slider)
        public int MouseSensitivity = 50;  // 0..100
        public bool LockCameraX = false;
        public bool LockCameraY = false;

        // Game (page 1)
        public bool ShowFramerate = false;
        public bool PriorityBoost = false;
        public int TextScrollRate = 50;     // 0..100 (DS1 default 5.0)
        public int MaxTextDisplayed = 6;    // 0..100
        public int GameSpeed = 100;         // 0..100 step 10 (1.0 default)
        public bool TutorialTips = true;
        public string Difficulty = "Normal"; // Easy/Normal/Hard

        // Game (page 2)
        public bool ShowTooltips = true;
        public string BloodColor = "Red";    // Red/Green/Disabled
        public bool Dismemberment = true;

        public Settings Clone() => (Settings)MemberwiseClone();
    }

    public Settings Live { get; private set; } = new();
    Settings _staged = new();
    int _gamePage; // 0 = page 1, 1 = page 2 (Game tab paging via More/Back)

    /// <summary>Phase 23-SC-OPTIONS-E — read-only snapshot of the
    /// player's current key bindings, surfaced on the Hotkeys
    /// sub-screen. SiegeFX's input is hardcoded today (key→action
    /// mapping lives directly in RenderHost); slice E ships a
    /// READ-ONLY listing matching DS1's bindings panel UI shape so
    /// the menu structure is complete. Full rebinding lands when the
    /// runtime gets a proper key-binding registry — splinter
    /// SC-OPTIONS-REBIND.</summary>
    public sealed record Binding(string Command, string Primary, string Secondary);
    static readonly Binding[] DefaultBindings = new[]
    {
        new Binding("Pause / Open Menu",       "Esc",       "—"),
        new Binding("Open Options",            "F10",        "—"),
        new Binding("Quick Save",              "F5",         "—"),
        new Binding("Quick Load",              "F9",         "—"),
        new Binding("Move Forward",            "W",          "—"),
        new Binding("Move Backward",           "S",          "—"),
        new Binding("Strafe Left",             "A",          "—"),
        new Binding("Strafe Right",            "D",          "—"),
        new Binding("Click-to-Move",           "Left Mouse", "—"),
        new Binding("Click-to-Attack / Talk",  "Right Mouse","—"),
        new Binding("Cast Spell — Primary",    "Q",          "—"),
        new Binding("Cast Spell — Secondary",  "W (RMB-mode)","—"),
        new Binding("Toggle Inventory",        "I",          "—"),
        new Binding("Toggle Spell Book",       "B",          "—"),
        new Binding("Toggle Character Pane",   "C",          "—"),
        new Binding("Camera Yaw",              "RMB-drag",   "—"),
        new Binding("Camera Zoom",             "Wheel",      "—"),
    };
    bool _hotkeysOpen; // Input tab → Hotkeys sub-screen toggle
    // _hotkeysScroll is reserved for the rebind-system slice — DefaultBindings
    // currently fits in one screenful at 1080p so the scroll input handler
    // isn't wired yet. Reads default 0 in DrawHotkeysSubscreen.
    const int _hotkeysScroll = 0;

    public void OpenSubScreenHotkeys() => _hotkeysOpen = true;
    public void CloseSubScreenHotkeys() => _hotkeysOpen = false;

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
        _gamePage = 0;
        _hotkeysOpen = false;
        ConfirmedThisFrame = false;
        CancelledThisFrame = false;
        DefaultsRequestedThisFrame = false;
        _hoveredTab = null;
        _hoveredBtn = Btn.None;
        _pressedBtn = Btn.None;
        _hoveredWidget = -1;
        _activeWidget = -1;
        SyncStagedFromLive();
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
        _hoveredWidget = HitWidget(px, py);
        // Drag-continue for sliders.
        if (_activeWidget >= 0 && _activeWidget < _widgets.Count
            && _widgets[_activeWidget].OnSliderDrag is { } drag)
        {
            drag(px);
        }
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
        _activeWidget = HitWidget(px, py);
        // Slider press jumps the thumb to the click point so the
        // widget feels responsive on a single click anywhere on the
        // track. Drag continues from the same x via OnMouseMove.
        if (_activeWidget >= 0 && _activeWidget < _widgets.Count
            && _widgets[_activeWidget].OnSliderDrag is { } drag)
        {
            drag(px);
        }
    }

    public void OnMouseUp(int px, int py, int viewportW, int viewportH)
    {
        if (!IsOpen) return;
        Layout(viewportW, viewportH, out _);
        // Tabs fire on click-up if the cursor is still over the tab.
        if (Hits(_tabVideo, px, py)) { ActiveTab = Tab.Video; _gamePage = 0; _hotkeysOpen = false; }
        else if (Hits(_tabAudio, px, py)) { ActiveTab = Tab.Audio; _gamePage = 0; _hotkeysOpen = false; }
        else if (Hits(_tabInput, px, py)) { ActiveTab = Tab.Input; _gamePage = 0; _hotkeysOpen = false; }
        else if (Hits(_tabGame,  px, py)) { ActiveTab = Tab.Game;  _gamePage = 0; _hotkeysOpen = false; }

        // Bottom buttons.
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

        // Widget click → fire OnClick for cycle/button widgets when
        // the press AND release land on the same widget.
        int upWidget = HitWidget(px, py);
        if (upWidget >= 0 && upWidget == _activeWidget && upWidget < _widgets.Count
            && _widgets[upWidget].OnClick is { } click)
        {
            click();
        }
        _activeWidget = -1;
    }

    public void OnRightClickWidget(int px, int py, int viewportW, int viewportH)
    {
        if (!IsOpen) return;
        Layout(viewportW, viewportH, out _);
        int idx = HitWidget(px, py);
        if (idx >= 0 && idx < _widgets.Count
            && _widgets[idx].OnRightClick is { } back) back();
    }

    int HitWidget(int px, int py)
    {
        for (int i = 0; i < _widgets.Count; i++)
            if (Hits(_widgets[i].Rect, px, py)) return i;
        return -1;
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

    // ========================================================
    // Phase 23-SC-OPTIONS-B/C/D/F — control widgets + per-tab
    // layout. Single-pass implementation so all four tabs share
    // one Slider + one CycleButton primitive plus a shared row
    // layout helper. Widgets are stateless beyond their config —
    // the staged value lives in `_staged.<Field>` and the widget
    // reads/writes it through a delegate, so a Cancel + re-Open
    // cleanly resyncs from `Live` without per-widget reset.
    // ========================================================

    /// <summary>Layout convention on the inner panel: labels
    /// right-justified at x ∈ [40, 200) of the inner content,
    /// widgets at x ∈ [220, 380) of the inner content. Y rows
    /// cascade from the top of the inner panel at 30px DS1
    /// stride. All coords are AUTHORED in 640×480 space — the
    /// caller-side Layout() runs every Draw to re-scale.</summary>
    const int RowStride = 30;
    const int RowHeight = 16;
    const int LabelLeftX = 40;
    const int LabelW = 160;
    const int WidgetX = 220;
    const int WidgetW = 160;
    const int FirstRowY = 20; // relative to inner panel top
    int _activeWidget = -1; // index of the widget currently being dragged
    int _hoveredWidget = -1;

    /// <summary>Walked once per draw. Each tab calls this for every
    /// row; `i` is a per-tab incrementing index that the host loop
    /// uses to position rows + correlate input. Returns the
    /// absolute on-screen rect (label rect, widget rect) so the
    /// per-tab method can hand them to a slider / cycle button /
    /// button helper without re-doing scaling math.</summary>
    void RowRect(int i, out (int X, int Y, int W, int H) labelR,
                        out (int X, int Y, int W, int H) widgetR,
                        int viewportW, int viewportH)
    {
        Layout(viewportW, viewportH, out var s);
        // Inner panel was scaled at (117,80)-(524,345) authored coords.
        // Add LabelLeftX / WidgetX offsets in authored space, scale, then
        // translate to viewport pixels using the live _inner rect.
        int innerAuthorX = 117;
        int innerAuthorY = 80;
        int yAuthor = innerAuthorY + FirstRowY + i * RowStride;
        labelR = (
            (int)MathF.Round((innerAuthorX + LabelLeftX) * s) + (_inner.X - (int)MathF.Round(innerAuthorX * s)),
            (int)MathF.Round(yAuthor * s),
            (int)MathF.Round(LabelW * s),
            (int)MathF.Round(RowHeight * s));
        widgetR = (
            (int)MathF.Round((innerAuthorX + WidgetX) * s) + (_inner.X - (int)MathF.Round(innerAuthorX * s)),
            (int)MathF.Round(yAuthor * s),
            (int)MathF.Round(WidgetW * s),
            (int)MathF.Round(RowHeight * s));
    }

    /// <summary>Slider widget. Click-anywhere-on-track jumps the
    /// thumb; LMB-drag continues. Value is normalized to [0,1] by
    /// the widget; caller maps to its own range via the get/set
    /// delegate. Widget index `i` is the per-tab row counter so
    /// hit-tests + drag-state lookups stay symmetric.</summary>
    void Slider(BarRenderer bars, TextRenderer text, int vw, int vh,
                int rowIdx, int widgetIdx, string label,
                Func<float> get, Action<float> set, int displayMin, int displayMax)
    {
        RowRect(rowIdx, out var labelR, out var widgetR, vw, vh);
        int labelTextW = text.MeasureWidth(label);
        text.DrawString(vw, vh, label,
            labelR.X + labelR.W - labelTextW, labelR.Y + 1, InkDim);
        bars.DrawRect(vw, vh, widgetR.X, widgetR.Y + widgetR.H / 2 - 1,
            widgetR.W, 2, Border);
        float v01 = Math.Clamp(get(), 0f, 1f);
        int thumbX = widgetR.X + (int)((widgetR.W - 8) * v01);
        bars.DrawRect(vw, vh, thumbX, widgetR.Y, 8, widgetR.H,
            _activeWidget == widgetIdx ? BtnPress
            : _hoveredWidget == widgetIdx ? BtnHover : BtnIdle);
        DrawBorder(bars, vw, vh, (thumbX, widgetR.Y, 8, widgetR.H), Border);
        int valDisp = displayMin + (int)MathF.Round((displayMax - displayMin) * v01);
        var valStr = valDisp.ToString();
        text.DrawString(vw, vh, valStr,
            widgetR.X + widgetR.W + 8, widgetR.Y + 1, Ink);
        // Hit + drag state recorded in _activeWidget / _hoveredWidget
        // by a parent-loop pass below.
    }

    /// <summary>Cycle button. Click steps to the next option in
    /// the array; right-click steps backward. Used for toggles
    /// (On/Off), enums (Easy/Normal/Hard), shadow types, etc.</summary>
    void CycleButton(BarRenderer bars, TextRenderer text, int vw, int vh,
                     int rowIdx, int widgetIdx, string label,
                     Func<int> getIdx, Action<int> setIdx, string[] options)
    {
        RowRect(rowIdx, out var labelR, out var widgetR, vw, vh);
        int labelTextW = text.MeasureWidth(label);
        text.DrawString(vw, vh, label,
            labelR.X + labelR.W - labelTextW, labelR.Y + 1, InkDim);
        var bg = _activeWidget == widgetIdx ? BtnPress
               : _hoveredWidget == widgetIdx ? BtnHover : BtnIdle;
        bars.DrawRect(vw, vh, widgetR.X, widgetR.Y, widgetR.W, widgetR.H, bg);
        DrawBorder(bars, vw, vh, widgetR, Border);
        var optStr = options[Math.Clamp(getIdx(), 0, options.Length - 1)];
        int oW = text.MeasureWidth(optStr);
        text.DrawString(vw, vh, optStr,
            widgetR.X + (widgetR.W - oW) / 2, widgetR.Y + 1, Ink);
    }

    /// <summary>Per-tab widget descriptor — the `_widgets` list
    /// rebuilds every Draw so a tab swap clears stale entries
    /// without manual cleanup. Each entry knows its rect,
    /// optional slider get/set, optional cycle-step delegate.</summary>
    sealed class W
    {
        public (int X, int Y, int W, int H) Rect;
        public Action<float>? OnSliderDrag;
        public Action? OnClick;        // cycle forward / button press
        public Action? OnRightClick;   // cycle backward
    }
    readonly List<W> _widgets = new();

    void DrawTabContent(BarRenderer bars, TextRenderer text, int vw, int vh)
    {
        if (_hotkeysOpen) { DrawHotkeysSubscreen(bars, text, vw, vh); return; }
        _widgets.Clear();
        switch (ActiveTab)
        {
            case Tab.Video: LayoutVideo(bars, text, vw, vh); break;
            case Tab.Audio: LayoutAudio(bars, text, vw, vh); break;
            case Tab.Input: LayoutInput(bars, text, vw, vh); break;
            case Tab.Game:  LayoutGame (bars, text, vw, vh); break;
        }
    }

    int AddSliderWidget(int rowIdx, int displayMax,
                        Func<float> get01, Action<float> set01,
                        int viewportW, int viewportH)
    {
        RowRect(rowIdx, out _, out var widgetR, viewportW, viewportH);
        var idx = _widgets.Count;
        _widgets.Add(new W
        {
            Rect = widgetR,
            OnSliderDrag = px =>
            {
                float t = (px - widgetR.X) / (float)Math.Max(1, widgetR.W);
                set01(Math.Clamp(t, 0f, 1f));
            }
        });
        return idx;
    }

    int AddCycleWidget(int rowIdx, Action stepF, Action stepB,
                       int viewportW, int viewportH)
    {
        RowRect(rowIdx, out _, out var widgetR, viewportW, viewportH);
        var idx = _widgets.Count;
        _widgets.Add(new W { Rect = widgetR, OnClick = stepF, OnRightClick = stepB });
        return idx;
    }

    int AddButtonWidget(int rowIdx, Action onClick, int viewportW, int viewportH)
    {
        RowRect(rowIdx, out _, out var widgetR, viewportW, viewportH);
        var idx = _widgets.Count;
        _widgets.Add(new W { Rect = widgetR, OnClick = onClick });
        return idx;
    }

    void LayoutVideo(BarRenderer bars, TextRenderer text, int vw, int vh)
    {
        var resOptions = new[] { "1280x720x32", "1600x900x32", "1920x1080x32",
                                  "2560x1440x32", "3440x1440x32", "3840x2160x32" };
        var shadows = new[] { "none", "simple_party", "complex_party" };
        var filter  = new[] { "bilinear", "trilinear" };
        int r = 0;
        CycleField(bars, text, vw, vh, r++, "Resolution",
            () => _staged.Resolution, v => _staged.Resolution = v, resOptions);
        CycleField(bars, text, vw, vh, r++, "Shadows",
            () => _staged.Shadows, v => _staged.Shadows = v, shadows);
        CycleField(bars, text, vw, vh, r++, "Texture Filtering",
            () => _staged.TextureFiltering, v => _staged.TextureFiltering = v, filter);
        FloatSlider(bars, text, vw, vh, r++, "Gamma",
            () => _staged.Gamma / 2f, t => _staged.Gamma = t * 2f, 0, 100);
        FloatSlider(bars, text, vw, vh, r++, "Object Detail",
            () => _staged.ObjectDetail, t => _staged.ObjectDetail = t, 0, 10);
    }

    void LayoutAudio(BarRenderer bars, TextRenderer text, int vw, int vh)
    {
        int r = 0;
        BoolCycle(bars, text, vw, vh, r++, "Sound",
            () => _staged.SoundEnabled, v => _staged.SoundEnabled = v);
        IntSlider(bars, text, vw, vh, r++, "Master Volume",
            () => _staged.MasterVolume, v => _staged.MasterVolume = v, 0, 127);
        IntSlider(bars, text, vw, vh, r++, "Music Volume",
            () => _staged.MusicVolume, v => _staged.MusicVolume = v, 0, 127);
        IntSlider(bars, text, vw, vh, r++, "SFX Volume",
            () => _staged.SfxVolume, v => _staged.SfxVolume = v, 0, 127);
        IntSlider(bars, text, vw, vh, r++, "Ambient Volume",
            () => _staged.AmbientVolume, v => _staged.AmbientVolume = v, 0, 127);
        IntSlider(bars, text, vw, vh, r++, "Voice Volume",
            () => _staged.VoiceVolume, v => _staged.VoiceVolume = v, 0, 127);
        BoolCycle(bars, text, vw, vh, r++, "EAX",
            () => _staged.EaxEnabled, v => _staged.EaxEnabled = v);
    }

    void LayoutInput(BarRenderer bars, TextRenderer text, int vw, int vh)
    {
        int r = 0;
        BoolCycle(bars, text, vw, vh, r++, "Invert Camera X",
            () => _staged.CameraInverseX, v => _staged.CameraInverseX = v);
        BoolCycle(bars, text, vw, vh, r++, "Invert Camera Y",
            () => _staged.CameraInverseY, v => _staged.CameraInverseY = v);
        BoolCycle(bars, text, vw, vh, r++, "Screen Edge Tracking",
            () => _staged.ScreenEdgeTracking, v => _staged.ScreenEdgeTracking = v);
        IntSlider(bars, text, vw, vh, r++, "Camera Sensitivity",
            () => _staged.CameraSensitivity, v => _staged.CameraSensitivity = v, 0, 100);
        IntSlider(bars, text, vw, vh, r++, "Mouse Sensitivity",
            () => _staged.MouseSensitivity, v => _staged.MouseSensitivity = v, 0, 100);
        BoolCycle(bars, text, vw, vh, r++, "Lock Camera X",
            () => _staged.LockCameraX, v => _staged.LockCameraX = v);
        BoolCycle(bars, text, vw, vh, r++, "Lock Camera Y",
            () => _staged.LockCameraY, v => _staged.LockCameraY = v);
        DrawPageButton(bars, text, vw, vh, r, "Hotkeys…", () => _hotkeysOpen = true);
    }

    void LayoutGame(BarRenderer bars, TextRenderer text, int vw, int vh)
    {
        var diff  = new[] { "Easy", "Normal", "Hard" };
        var blood = new[] { "Red", "Green", "Disabled" };
        int r = 0;
        if (_gamePage == 0)
        {
            BoolCycle(bars, text, vw, vh, r++, "Show Framerate",
                () => _staged.ShowFramerate, v => _staged.ShowFramerate = v);
            BoolCycle(bars, text, vw, vh, r++, "Raise App Priority",
                () => _staged.PriorityBoost, v => _staged.PriorityBoost = v);
            IntSlider(bars, text, vw, vh, r++, "Text Scroll Rate",
                () => _staged.TextScrollRate, v => _staged.TextScrollRate = v, 0, 100);
            IntSlider(bars, text, vw, vh, r++, "Maximum Text",
                () => _staged.MaxTextDisplayed, v => _staged.MaxTextDisplayed = v, 0, 100);
            IntSlider(bars, text, vw, vh, r++, "Game Speed",
                () => _staged.GameSpeed, v => _staged.GameSpeed = v, 0, 100);
            BoolCycle(bars, text, vw, vh, r++, "Tutorial Tips",
                () => _staged.TutorialTips, v => _staged.TutorialTips = v);
            CycleField(bars, text, vw, vh, r++, "Difficulty",
                () => _staged.Difficulty, v => _staged.Difficulty = v, diff);
            DrawPageButton(bars, text, vw, vh, r, "More →", () => _gamePage = 1);
        }
        else
        {
            BoolCycle(bars, text, vw, vh, r++, "Show Tooltips",
                () => _staged.ShowTooltips, v => _staged.ShowTooltips = v);
            CycleField(bars, text, vw, vh, r++, "Blood Color",
                () => _staged.BloodColor, v => _staged.BloodColor = v, blood);
            BoolCycle(bars, text, vw, vh, r++, "Dismemberment",
                () => _staged.Dismemberment, v => _staged.Dismemberment = v);
            DrawPageButton(bars, text, vw, vh, r, "← Back", () => _gamePage = 0);
        }
    }

    void DrawPageButton(BarRenderer bars, TextRenderer text, int vw, int vh,
                        int rowIdx, string label, Action onClick)
    {
        RowRect(rowIdx, out _, out var widgetR, vw, vh);
        var bg = _hoveredWidget == _widgets.Count ? BtnHover : BtnIdle;
        bars.DrawRect(vw, vh, widgetR.X, widgetR.Y, widgetR.W, widgetR.H, bg);
        DrawBorder(bars, vw, vh, widgetR, Border);
        int lW = text.MeasureWidth(label);
        text.DrawString(vw, vh, label, widgetR.X + (widgetR.W - lW) / 2, widgetR.Y + 1, Ink);
        AddButtonWidget(rowIdx, onClick, vw, vh);
    }

    /// <summary>String-cycle field. Click steps forward, right-click
    /// steps back, both wrap. Options is reference-captured so the
    /// `Difficulty` cycle keeps stepping through Easy→Normal→Hard
    /// without relying on a label-string switch.</summary>
    void CycleField(BarRenderer bars, TextRenderer text, int vw, int vh,
                    int rowIdx, string label,
                    Func<string> get, Action<string> set, string[] options)
    {
        int idx = Array.IndexOf(options, get());
        if (idx < 0) idx = 0;
        CycleButton(bars, text, vw, vh, rowIdx, _widgets.Count, label,
            () => idx, _ => { }, options);
        AddCycleWidget(rowIdx,
            () => { var i = Array.IndexOf(options, get());
                    if (i < 0) i = 0;
                    set(options[(i + 1) % options.Length]); },
            () => { var i = Array.IndexOf(options, get());
                    if (i < 0) i = 0;
                    set(options[(i - 1 + options.Length) % options.Length]); },
            vw, vh);
    }

    /// <summary>Boolean cycle field — same shape as
    /// <see cref="CycleField"/> but always toggles the get/set
    /// delegate's bool. Click + right-click both flip the value.</summary>
    void BoolCycle(BarRenderer bars, TextRenderer text, int vw, int vh,
                   int rowIdx, string label, Func<bool> get, Action<bool> set)
    {
        var onOff = new[] { "Off", "On" };
        int idx = get() ? 1 : 0;
        CycleButton(bars, text, vw, vh, rowIdx, _widgets.Count, label,
            () => idx, _ => { }, onOff);
        AddCycleWidget(rowIdx,
            () => set(!get()),
            () => set(!get()),
            vw, vh);
    }

    /// <summary>Integer slider [min,max] with the displayed value
    /// being the integer itself (e.g. volumes 0..127, sensitivities
    /// 0..100).</summary>
    void IntSlider(BarRenderer bars, TextRenderer text, int vw, int vh,
                   int rowIdx, string label,
                   Func<int> get, Action<int> set, int min, int max)
    {
        Slider(bars, text, vw, vh, rowIdx, _widgets.Count, label,
            () => (get() - min) / (float)Math.Max(1, max - min),
            t => set(min + (int)MathF.Round(t * (max - min))),
            min, max);
        AddSliderWidget(rowIdx, max,
            () => (get() - min) / (float)Math.Max(1, max - min),
            t => set(min + (int)MathF.Round(t * (max - min))),
            vw, vh);
    }

    /// <summary>Generic slider. <paramref name="get01"/> /
    /// <paramref name="set01"/> use a normalized [0,1] value; the
    /// caller maps to its own range. Display values are the integer
    /// min/max for the user-facing label.</summary>
    void FloatSlider(BarRenderer bars, TextRenderer text, int vw, int vh,
                     int rowIdx, string label,
                     Func<float> get01, Action<float> set01,
                     int displayMin, int displayMax)
    {
        Slider(bars, text, vw, vh, rowIdx, _widgets.Count, label,
            get01, set01, displayMin, displayMax);
        AddSliderWidget(rowIdx, displayMax, get01, set01, vw, vh);
    }

    void DrawHotkeysSubscreen(BarRenderer bars, TextRenderer text, int vw, int vh)
    {
        // Read-only key-binding listing. Header row + scrolling list.
        int innerCx = _inner.X + _inner.W / 2;
        var header = "Hotkeys (read-only — rebinding pending splinter SC-OPTIONS-REBIND)";
        int hW = text.MeasureWidth(header);
        text.DrawString(vw, vh, header, innerCx - hW / 2, _inner.Y + 12, InkDim);

        // Column headers
        int colY = _inner.Y + 36;
        int cmdX = _inner.X + 20;
        int priX = _inner.X + 220;
        int secX = _inner.X + 320;
        text.DrawString(vw, vh, "Command",   cmdX, colY, Ink);
        text.DrawString(vw, vh, "Primary",   priX, colY, Ink);
        text.DrawString(vw, vh, "Secondary", secX, colY, Ink);
        bars.DrawRect(vw, vh, _inner.X + 12, colY + 14, _inner.W - 24, 1, Border);

        int rowH = 18;
        int rowY = colY + 22;
        int maxRows = (_inner.Y + _inner.H - rowY - 50) / rowH;
        for (int i = 0; i < Math.Min(maxRows, DefaultBindings.Length); i++)
        {
            var b = DefaultBindings[i + _hotkeysScroll];
            text.DrawString(vw, vh, b.Command,   cmdX, rowY + i * rowH, Ink);
            text.DrawString(vw, vh, b.Primary,   priX, rowY + i * rowH, InkDim);
            text.DrawString(vw, vh, b.Secondary, secX, rowY + i * rowH, InkDim);
        }

        // Back button at the bottom of the inner panel.
        int backW = 120, backH = 22;
        int backX = innerCx - backW / 2;
        int backY = _inner.Y + _inner.H - 36;
        var bg = _hoveredWidget == _widgets.Count ? BtnHover : BtnIdle;
        bars.DrawRect(vw, vh, backX, backY, backW, backH, bg);
        DrawBorder(bars, vw, vh, (backX, backY, backW, backH), Border);
        var lbl = "← Back";
        int lW = text.MeasureWidth(lbl);
        text.DrawString(vw, vh, lbl, backX + (backW - lW) / 2, backY + 4, Ink);
        _widgets.Add(new W { Rect = (backX, backY, backW, backH), OnClick = () => _hotkeysOpen = false });
    }

    /// <summary>Called by the host on Open() to seed `_staged` from
    /// `Live` so a Cancel cleanly reverts. Slice A flow exposes
    /// this as the public entry; later slices that read prefs.gas
    /// will replace `Live` first then call this.</summary>
    public void SyncStagedFromLive() { _staged = Live.Clone(); _gamePage = 0; _hotkeysOpen = false; }

    /// <summary>Commit staged → live + apply runtime hooks. Slice C
    /// wires audio volumes; the rest persist-only until further
    /// runtime knobs land. Caller invokes when ConfirmedThisFrame
    /// fires.</summary>
    public void CommitStaged() { Live = _staged.Clone(); }

    /// <summary>Reset the active tab's fields to the
    /// /config/options.gas defaults. Per-tab reset matches DS1's
    /// `notify(default_options_<tab>)` behavior.</summary>
    public void ApplyDefaultsForActiveTab()
    {
        var d = new Settings();
        switch (ActiveTab)
        {
            case Tab.Video:
                _staged.Resolution = d.Resolution;
                _staged.Shadows = d.Shadows;
                _staged.TextureFiltering = d.TextureFiltering;
                _staged.Gamma = d.Gamma;
                _staged.ObjectDetail = d.ObjectDetail;
                break;
            case Tab.Audio:
                _staged.SoundEnabled = d.SoundEnabled;
                _staged.MasterVolume = d.MasterVolume;
                _staged.MusicVolume = d.MusicVolume;
                _staged.SfxVolume = d.SfxVolume;
                _staged.AmbientVolume = d.AmbientVolume;
                _staged.VoiceVolume = d.VoiceVolume;
                _staged.EaxEnabled = d.EaxEnabled;
                break;
            case Tab.Input:
                _staged.CameraInverseX = d.CameraInverseX;
                _staged.CameraInverseY = d.CameraInverseY;
                _staged.ScreenEdgeTracking = d.ScreenEdgeTracking;
                _staged.CameraSensitivity = d.CameraSensitivity;
                _staged.MouseSensitivity = d.MouseSensitivity;
                _staged.LockCameraX = d.LockCameraX;
                _staged.LockCameraY = d.LockCameraY;
                break;
            case Tab.Game:
                _staged.ShowFramerate = d.ShowFramerate;
                _staged.PriorityBoost = d.PriorityBoost;
                _staged.TextScrollRate = d.TextScrollRate;
                _staged.MaxTextDisplayed = d.MaxTextDisplayed;
                _staged.GameSpeed = d.GameSpeed;
                _staged.TutorialTips = d.TutorialTips;
                _staged.Difficulty = d.Difficulty;
                _staged.ShowTooltips = d.ShowTooltips;
                _staged.BloodColor = d.BloodColor;
                _staged.Dismemberment = d.Dismemberment;
                break;
        }
    }
}
