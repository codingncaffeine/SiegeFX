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
    public enum Tab { Video, Audio, Input, Game, Advanced }

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
        // SC-OPTIONS-GAME — schema marker for prefs.json migrations. Files
        // written before this property existed used GameSpeed=100 as a dead
        // knob's default; under the live 0..100 → 0.5x..2.0x mapping that
        // would boot at double speed, so OptionsPrefs.Load resets legacy
        // files' GameSpeed to the 1.0x midpoint.
        public int PrefsVersion = 2;

        // Video — ALPHA-2V all runtime-wired. Defaults from /config/options.gas
        // (modernized: resolution drops DS1's bpp suffix; fullscreen is a new
        // checkbox — DS1 was always fullscreen, SiegeFX defaults windowed).
        // Resolution = the fullscreen mode + a windowed resize preset; free
        // window resizing stays allowed and the last size persists via
        // WindowW/WindowH (not shown in the UI).
        public string Resolution = "1920x1080";
        public bool Fullscreen = false;
        public string Shadows = "complex_party";
        public string TextureFiltering = "trilinear";
        public float Gamma = 1.0f;          // 0..2 (UI 0..100 step 10)
        public float ObjectDetail = 1.0f;   // 0..1 (UI 0..10 step 1)

        // Last windowed size — recorded on every user resize while windowed,
        // restored at boot and when the fullscreen checkbox is cleared.
        public int WindowW = 1280;
        public int WindowH = 720;

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

        // Game (page 1) — SC-OPTIONS-GAME: all runtime-wired.
        public bool ShowFramerate = false;
        public bool PriorityBoost = false;   // process priority AboveNormal
        public int TextScrollRate = 50;      // 0..100 → floating-text hold 1.5x..0.5x
        public int MaxTextDisplayed = 6;     // 1..20 concurrent floating lines
        public int GameSpeed = 50;           // 0..100 → 0.5x..2.0x sim speed (50 = 1.0x)
        public bool TutorialTips = true;     // gates handbook auto-pop cadence
        public string Difficulty = "Normal"; // Easy/Normal/Hard → CombatResolver

        // INFORAIL-F — DS1's vertical paperdoll toggle for "open
        // spellbook when I is pressed". Persists via the standard
        // options round-trip (Live → Save → Reload on next session).
        public bool SpellbookOpensWithI = true;

        // Game (page 2)
        public bool ShowTooltips = true;
        public string BloodColor = "Red";    // Red/Green/Disabled
        public bool Dismemberment = true;

        // SC-OPTIONS-REBIND — persisted key bindings: action id →
        // [primary, secondary] tokens (see KeyBindingRegistry). Empty =
        // authored defaults. Round-trips through prefs.json with the rest.
        public Dictionary<string, string[]> KeyBindings = new();

        // SC-HUD-DRAG — user-positioned HUD pieces (Shift+LMB drag; QoL
        // addition). Normalized viewport fractions of each piece's top-left
        // corner; -1 = the built-in layout. Persist with everything else.
        public float CompassPosX = -1f, CompassPosY = -1f;
        public float InventoryPosX = -1f, InventoryPosY = -1f;
        public float CompanionInvPosX = -1f, CompanionInvPosY = -1f;
        public float AwpPosX = -1f, AwpPosY = -1f;   // player portrait + slots cluster
        public float TeamPosX = -1f, TeamPosY = -1f; // companion portrait strip
        // Info-rail trio (character sheet / inventory / spellbook).
        // RailLocked=true (default) moves them as ONE unit anchored by
        // PaperdollPos; unlocked, each panel takes its own stored spot.
        public bool RailLocked = true;
        public float PaperdollPosX = -1f, PaperdollPosY = -1f;
        public float SpellbookPosX = -1f, SpellbookPosY = -1f;
        public float FieldCmdPosX = -1f, FieldCmdPosY = -1f; // field-commands cluster

        // Advanced — ALPHA-2V modern-GPU tab (new; DS1 had no equivalent).
        // All vendor-neutral GL features. MSAA needs a window rebuild so it
        // applies at next launch; everything else applies on OK.
        public bool VSync = true;
        public string FpsCap = "Unlimited";  // Unlimited/30/60/120/144/240
        public string Anisotropy = "8x";     // 2x/4x/8x/16x (pairs with texture filtering = anisotropic)
        public string Msaa = "Off";          // Off/2x/4x/8x — next launch
        public int PointLightBudget = 16;    // 4..32 per-frame point-light cap
        public int UiScalePercent = 100;     // 50..150 global HUD scale

        public Settings Clone() => (Settings)MemberwiseClone();
    }

    public Settings Live { get; private set; } = new();
    Settings _staged = new();
    /// <summary>Phase 23-SC-OPTIONS-FOLD — exposes the staged settings
    /// so the host's live-preview audio applier reads the in-flight
    /// edit (not yet committed) during a slider drag. After OK
    /// commits, Staged == Live and either reads the same values.</summary>
    public Settings Staged => _staged;

    /// <summary>Phase 23-SC-OPTIONS-FOLD — fired by the panel whenever
    /// an Audio-tab slider changes mid-drag (e.g. Master Volume drag
    /// from 108→0). Caller wires this to <c>ApplyOptionsAudio</c> so
    /// the user hears the volume change live during drag instead of
    /// only after pressing OK. Other tabs don't fire this — their
    /// runtime hooks are persist-only or commit-only today.</summary>
    public event Action? AudioStagedChanged;
    int _gamePage; // 0 = page 1, 1 = page 2 (Game tab paging via More/Back)

    /// <summary>SC-OPTIONS-REBIND — the Hotkeys sub-screen is a full
    /// rebinding editor over <see cref="KeyBindingRegistry"/>'s authored
    /// catalog (the complete non-dev input_bindings.gas list, grouped
    /// Party Controls / View Controls / User Interface / Game Settings).
    /// Layout matches DS1's options_bindings.gas: right-justified command
    /// names, Primary + Secondary cells (LMB = capture a new key, RMB =
    /// clear), a scrollbar, Back + Defaults. Edits stage in
    /// <see cref="_stagedBindings"/> and commit on OK like every other
    /// tab; Cancel discards.</summary>
    public KeyBindingRegistry? Registry;
    Dictionary<string, string[]> _stagedBindings = new();
    bool _hotkeysOpen; // Input tab → Hotkeys sub-screen toggle
    int _bindScroll;
    string _captureId = "";  // action id awaiting a keypress ("" = not capturing)
    int _captureSlot;        // 0 = primary, 1 = secondary
    const int BindRowsVisible = 10;

    // Flattened display list: group headers + catalog rows, authored order.
    sealed record BindRow(string? Header, KeyBindingRegistry.Def? Def);
    static readonly List<BindRow> BindRows = BuildBindRows();
    static List<BindRow> BuildBindRows()
    {
        var rows = new List<BindRow>();
        string group = "";
        foreach (var def in KeyBindingRegistry.Defs)
        {
            if (def.Group != group)
            {
                group = def.Group;
                rows.Add(new BindRow(group, null));
            }
            rows.Add(new BindRow(null, def));
        }
        return rows;
    }

    public void OpenSubScreenHotkeys() => _hotkeysOpen = true;
    public void CloseSubScreenHotkeys() { _hotkeysOpen = false; _captureId = ""; }

    /// <summary>Staged bindings snapshot for the host's OK-commit path.</summary>
    public Dictionary<string, string[]> StagedBindingsSnapshot()
    {
        var copy = new Dictionary<string, string[]>(_stagedBindings.Count);
        foreach (var (id, slots) in _stagedBindings) copy[id] = (string[])slots.Clone();
        return copy;
    }

    /// <summary>Key routing while the menu is open. Returns true when the
    /// keypress was consumed by an in-progress binding capture: Esc
    /// cancels, a lone modifier keeps waiting, anything mappable becomes
    /// the new token (stealing it from any other action that held it —
    /// the conflict resolution DS1 uses).</summary>
    public bool HandleKeyForBinding(Silk.NET.Input.Key key, bool ctrl, bool alt, bool shift)
    {
        if (!IsOpen || !_hotkeysOpen || _captureId.Length == 0) return false;
        if (key == Silk.NET.Input.Key.Escape) { _captureId = ""; return true; }
        if (key is Silk.NET.Input.Key.ControlLeft or Silk.NET.Input.Key.ControlRight
               or Silk.NET.Input.Key.AltLeft or Silk.NET.Input.Key.AltRight
               or Silk.NET.Input.Key.ShiftLeft or Silk.NET.Input.Key.ShiftRight
               or Silk.NET.Input.Key.SuperLeft or Silk.NET.Input.Key.SuperRight)
            return true; // modifier held — keep waiting for the real key
        var token = KeyBindingRegistry.TokenFor(key, ctrl, alt, shift);
        if (token.Length == 0) return true; // unmappable (numpad etc.) — swallow
        // Steal the token from any other slot that holds it.
        foreach (var slots in _stagedBindings.Values)
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == token) slots[i] = "";
        if (_stagedBindings.TryGetValue(_captureId, out var mine))
            mine[Math.Clamp(_captureSlot, 0, mine.Length - 1)] = token;
        _captureId = "";
        return true;
    }

    /// <summary>Mouse-wheel scroll over the bindings list.</summary>
    public void OnScroll(float dy, int viewportW, int viewportH)
    {
        if (!IsOpen || !_hotkeysOpen) return;
        int max = Math.Max(0, BindRows.Count - BindRowsVisible);
        _bindScroll = Math.Clamp(_bindScroll - (int)MathF.Sign(dy) * 2, 0, max);
    }

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
    // Phase 23-SC-OPTIONS-FOLD2 — drop inner Y from 80 to 86 so the tabs
    // (y=67..83) keep their bottom edge clear of the panel fill. Pre-fold
    // the 3-pixel authored overlap (DS1's "tabs visually attach" pattern)
    // ate the bottom ~35% of the tab labels at the integer-scaled font
    // because we draw with solid fills instead of DS1's transparent-bottom
    // tab strip art. The same height-shrink (265→259) keeps the bottom
    // edge at y=345 so OK/Cancel/Defaults rects below stay where DS1
    // authored them.
    static readonly (int X, int Y, int W, int H) RectInner   = (117,  86, 407, 259);
    static readonly (int X, int Y, int W, int H) RectTitle   = (200,  38, 240,  20);
    static readonly (int X, int Y, int W, int H) RectTabRow  = (118,  67, 406,  16);
    // ALPHA-2V — DS1's four ~100px tabs re-split five ways across the same
    // authored 118..524 span to fit the new Advanced tab. The active tab in
    // DS1 is shifted down 3px to overlap the panel edge — not reproduced.
    static readonly (int X, int Y, int W, int H) RectTabVideo    = (118, 67, 81, 16);
    static readonly (int X, int Y, int W, int H) RectTabAudio    = (199, 67, 81, 16);
    static readonly (int X, int Y, int W, int H) RectTabInput    = (280, 67, 81, 16);
    static readonly (int X, int Y, int W, int H) RectTabGame     = (361, 67, 81, 16);
    static readonly (int X, int Y, int W, int H) RectTabAdvanced = (442, 67, 82, 16);
    static readonly (int X, int Y, int W, int H) RectOk       = (200, 358, 110, 16);
    static readonly (int X, int Y, int W, int H) RectCancel   = (330, 358, 110, 16);
    static readonly (int X, int Y, int W, int H) RectDefaults = (361, 318, 140, 16);
    // Bottom-left button (Hotkeys on Input, More/Back on Game) — DS1 docks
    // these opposite Defaults on the same row, not down in the content grid.
    static readonly (int X, int Y, int W, int H) RectMore     = (165, 318, 140, 16);

    // Live (post-Layout) viewport-pixel rects so input handlers and the
    // draw loop share one source of truth.
    (int X, int Y, int W, int H) _outer, _inner, _title, _tabRow;
    (int X, int Y, int W, int H) _tabVideo, _tabAudio, _tabInput, _tabGame, _tabAdvanced;
    (int X, int Y, int W, int H) _ok, _cancel, _defaults, _more;
    Tab? _hoveredTab;
    enum Btn { None, Ok, Cancel, Defaults }
    Btn _hoveredBtn;
    Btn _pressedBtn; // tracks LMB press → release for click validation

    // SC-OPTIONS-CHROME — the authentic b_gui_cmn_* art (button_4 chrome, the
    // slider `track` pieces, the dropdown `down` arrow) is resolved through the
    // FrontendScene each Draw and stashed here so the per-widget helpers can
    // reach it without threading a resolver through every signature. Null in
    // headless/test bootstrap → every draw falls back to its solid-fill shape.
    IconRenderer? _icons;
    Func<string, GlTexture?>? _chrome; // full b_gui_cmn_* basename resolver
    int _vw, _vh;

    /// <summary>button_4 3-slice via <see cref="ButtonChrome"/>; false if the
    /// art isn't available (caller then draws its solid-fill placeholder).</summary>
    bool ChromeButton((int X, int Y, int W, int H) r, ButtonChrome.State st)
        => _icons is not null && _chrome is not null
           && ButtonChrome.Draw(_icons, _chrome, _vw, _vh, r.X, r.Y, r.W, r.H, "button4", st);

    /// <summary>Blit a full common-chrome texture (b_gui_cmn_&lt;name&gt;).</summary>
    void Tex(string cmnName, int x, int y, int w, int h)
    {
        var t = _chrome?.Invoke("b_gui_cmn_" + cmnName);
        if (t is not null && _icons is not null)
            _icons.DrawIcon(_vw, _vh, t, x, y, w, h, Vector4.One);
    }

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
    // DS1 copperplate labels + inactive tab text read as a muted gold, not grey.
    static readonly Vector4 InkDim      = new(0.83f, 0.68f, 0.38f, 1.00f);

    public void Open()
    {
        IsOpen = true;
        ActiveTab = Tab.Video;
        _gamePage = 0;
        _hotkeysOpen = false;
        _captureId = "";
        _bindScroll = 0;
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
    /// <summary>Phase 23-SC-OPTIONS-FOLD — integer pixel scale for the
    /// bitmap font, derived from the panel scale. Clamped to ≥1 so
    /// the legacy code path for sub-480p test windows still draws.
    /// Exposed so each Draw call can pass it to TextRenderer.</summary>
    int _fontScale = 1;

    void Layout(int viewportW, int viewportH, out float scale)
    {
        // Phase 23-SC-OPTIONS-FOLD — clamp to viewportW too so a
        // portrait-orientation window (vh > vw) doesn't push the
        // 640×480-aspect panel past the viewport edges with negative dx.
        // ALPHA-2V — modal scale honors the UI-scale knob (shrink only;
        // growth is fit-capped so the dialog can't overflow the window).
        scale = HudScale.Modal(viewportW, viewportH);
        int scaledBaseW = (int)MathF.Round(640 * scale);
        int scaledBaseH = (int)MathF.Round(480 * scale);
        int dx = (viewportW - scaledBaseW) / 2; // center horizontally
        int dy = (viewportH - scaledBaseH) / 2; // center vertically (kicks in only when viewport is wider than 4:3)
        // Integer font scale: 1080p (scale ≈2.25) → 2; 1440p ultrawide
        // (scale ≈3.0) → 3; 4K (scale ≈4.5) → 4. Bitmap font stays crisp
        // at integer multiples instead of the bilinear-blur a fractional
        // multiplier would produce.
        _fontScale = Math.Max(1, (int)MathF.Round(scale));
        _outer    = Scale(RectOuter,    dx, dy, scale);
        _inner    = Scale(RectInner,    dx, dy, scale);
        _title    = Scale(RectTitle,    dx, dy, scale);
        _tabRow   = Scale(RectTabRow,   dx, dy, scale);
        _tabVideo    = Scale(RectTabVideo,    dx, dy, scale);
        _tabAudio    = Scale(RectTabAudio,    dx, dy, scale);
        _tabInput    = Scale(RectTabInput,    dx, dy, scale);
        _tabGame     = Scale(RectTabGame,     dx, dy, scale);
        _tabAdvanced = Scale(RectTabAdvanced, dx, dy, scale);
        _ok       = Scale(RectOk,       dx, dy, scale);
        _cancel   = Scale(RectCancel,   dx, dy, scale);
        _defaults = Scale(RectDefaults, dx, dy, scale);
        _more     = Scale(RectMore,     dx, dy, scale);
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
            Hits(_tabVideo,    px, py) ? Tab.Video :
            Hits(_tabAudio,    px, py) ? Tab.Audio :
            Hits(_tabInput,    px, py) ? Tab.Input :
            Hits(_tabGame,     px, py) ? Tab.Game  :
            Hits(_tabAdvanced, px, py) ? Tab.Advanced :
            null;
        _hoveredBtn =
            Hits(_ok,       px, py) ? Btn.Ok       :
            Hits(_cancel,   px, py) ? Btn.Cancel   :
            Hits(_defaults, px, py) ? Btn.Defaults :
            Btn.None;
        _hoveredWidget = HitWidget(px, py);
        // Drag-continue for sliders (horizontal tracks + the bindings
        // scrollbar's vertical axis).
        if (_activeWidget >= 0 && _activeWidget < _widgets.Count)
        {
            if (_widgets[_activeWidget].OnSliderDrag is { } drag) drag(px);
            if (_widgets[_activeWidget].OnSliderDragY is { } dragY) dragY(py);
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
        // track. Drag continues from the same x/y via OnMouseMove.
        if (_activeWidget >= 0 && _activeWidget < _widgets.Count)
        {
            if (_widgets[_activeWidget].OnSliderDrag is { } drag) drag(px);
            if (_widgets[_activeWidget].OnSliderDragY is { } dragY) dragY(py);
        }
    }

    public void OnMouseUp(int px, int py, int viewportW, int viewportH)
    {
        if (!IsOpen) return;
        Layout(viewportW, viewportH, out _);
        // Tabs fire on click-up if the cursor is still over the tab.
        // Phase 23-SC-OPTIONS-FOLD — clear _activeWidget on tab change
        // so a slider drag that ended on a tab doesn't leave a stale
        // index pointing at the now-rebuilt widget list.
        bool tabSwapped = false;
        if (Hits(_tabVideo, px, py)) { ActiveTab = Tab.Video; tabSwapped = true; }
        else if (Hits(_tabAudio, px, py)) { ActiveTab = Tab.Audio; tabSwapped = true; }
        else if (Hits(_tabInput, px, py)) { ActiveTab = Tab.Input; tabSwapped = true; }
        else if (Hits(_tabGame,  px, py)) { ActiveTab = Tab.Game;  tabSwapped = true; }
        else if (Hits(_tabAdvanced, px, py)) { ActiveTab = Tab.Advanced; tabSwapped = true; }
        if (tabSwapped)
        {
            _gamePage = 0;
            _hotkeysOpen = false;
            _activeWidget = -1;
            _hoveredWidget = -1;
            _pressedBtn = Btn.None;
            return; // don't also fire OK/Cancel/widget click on the tab swap
        }

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

    public void Draw(BarRenderer bars, TextRenderer text,
        IconRenderer? icons, FrontendScene? scene,
        int viewportW, int viewportH,
        Func<string, GlTexture?>? commonChrome = null)
    {
        if (!IsOpen) return;
        Layout(viewportW, viewportH, out _);

        // Stash the chrome resolver for this frame's widget helpers. In-game the
        // FrontendScene is null (it's disposed on entering play), so the host
        // passes its own GetCommonTexture; only the frontend context supplies a
        // scene. Both load b_gui_cmn_* from Objects.dsres via the bare key; strip
        // the prefix ButtonChrome/Tex re-add so the resolver key form lines up.
        Func<string, GlTexture?>? chrome = commonChrome;
        if (chrome is null && scene is not null) chrome = scene.GetCommonTexture;
        _icons = icons;
        _vw = viewportW; _vh = viewportH;
        _chrome = chrome is null ? null
            : n => chrome(n.StartsWith("b_gui_cmn_") ? n["b_gui_cmn_".Length..] : n);

        // Modal dim: a screen-wide darkening so the underlying scene
        // stops competing for attention. 60% black is the same shade
        // PauseMenu uses.
        bars.DrawRect(viewportW, viewportH, 0, 0, viewportW, viewportH,
            new Vector4(0f, 0f, 0f, 0.60f));

        // SC-OPTIONS-CHROME — outer panel uses cpbox_wide 9-patch
        // (b_gui_cmn_cpbox2_*) per options_video.gas common_template.
        // Falls back to the prior solid-fill placeholder if the icon
        // renderer or chrome scene isn't available.
        bool drewChrome = false;
        if (icons is not null && chrome is not null)
        {
            NinePatch.DrawCpboxWide(icons, chrome, viewportW, viewportH,
                _outer.X, _outer.Y, _outer.W, _outer.H,
                new Vector4(1f, 1f, 1f, 1f));
            drewChrome = true;
        }
        if (!drewChrome)
        {
            bars.DrawRect(viewportW, viewportH, _outer.X, _outer.Y, _outer.W, _outer.H, PanelBg);
            DrawBorder(bars, viewportW, viewportH, _outer, Border);
        }

        // Title.
        var titleStr = "Options Menu";
        int titleW = text.MeasureWidth(titleStr, _fontScale);
        int titleX = _outer.X + (_outer.W - titleW) / 2;
        int titleY = _title.Y;
        text.DrawString(viewportW, viewportH, titleStr, titleX, titleY, Ink, _fontScale);

        // Tab bar — four equal-ish strips. Active tab uses the inner-panel
        // background colour so it visually attaches to the content area.
        DrawTab(bars, text, viewportW, viewportH, _tabVideo, "VIDEO", Tab.Video);
        DrawTab(bars, text, viewportW, viewportH, _tabAudio, "AUDIO", Tab.Audio);
        DrawTab(bars, text, viewportW, viewportH, _tabInput, "INPUT", Tab.Input);
        DrawTab(bars, text, viewportW, viewportH, _tabGame,  "GAME",  Tab.Game);
        // "ADV" — the full word overflows the 5-way-split 82px tab at the
        // copperplate label width.
        DrawTab(bars, text, viewportW, viewportH, _tabAdvanced, "ADV", Tab.Advanced);

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
        // DS1 tabs are button_4 (common_control_art): the selected tab shows the
        // brightest lit face, the others sit recessed (dark, merged into the
        // content panel), lightening to the mid face on hover. Fall back to the
        // flat placeholder fills when the art isn't loaded.
        var st = active ? ButtonChrome.State.Hover
               : hover  ? ButtonChrome.State.Up
               :          ButtonChrome.State.Down;
        if (!ChromeButton(r, st))
        {
            var bg = active ? TabActiveBg : (hover ? TabHoverBg : TabIdleBg);
            bars.DrawRect(vw, vh, r.X, r.Y, r.W, r.H, bg);
            DrawBorder(bars, vw, vh, r, Border);
        }
        int labelW = text.MeasureWidth(label, _fontScale);
        int lx = r.X + (r.W - labelW) / 2;
        // Phase 23-SC-OPTIONS-FOLD2 — match DrawButton's font-scale-aware
        // centering (was using bare `12` which dropped labels ~3-7px low
        // at 1080p / 1440p / 4K integer scales, eating into the inner-
        // panel border once the panel was visible-adjacent to tabs).
        int ly = r.Y + (r.H - 12 * _fontScale) / 2;
        text.DrawString(vw, vh, label, lx, ly, active ? Ink : InkDim, _fontScale);
    }

    void DrawButton(BarRenderer bars, TextRenderer text, int vw, int vh,
                    (int X, int Y, int W, int H) r, string label, Btn id)
    {
        var st = (_pressedBtn == id && _hoveredBtn == id) ? ButtonChrome.State.Down
               : _hoveredBtn == id ? ButtonChrome.State.Hover
               : ButtonChrome.State.Up;
        if (!ChromeButton(r, st))
        {
            var bg = st == ButtonChrome.State.Down ? BtnPress
                   : st == ButtonChrome.State.Hover ? BtnHover : BtnIdle;
            bars.DrawRect(vw, vh, r.X, r.Y, r.W, r.H, bg);
            DrawBorder(bars, vw, vh, r, Border);
        }
        int labelW = text.MeasureWidth(label, _fontScale);
        int lx = r.X + (r.W - labelW) / 2;
        int ly = r.Y + (r.H - 12 * _fontScale) / 2;
        text.DrawString(vw, vh, label, lx, ly, Ink, _fontScale);
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
    // Phase 23-SC-OPTIONS-FOLD2 — RowStride dropped 30→24. Pre-fold,
    // tabs that ship 8 rows (Input ends with Hotkeys at row 7; Game
    // page 1 ends with More at row 7) put their last row's widget at
    // authored y=310..326, which sat directly underneath Defaults at
    // y=318..334 and produced two stacked buttons at the same x band.
    // Shrinking the stride packs 8 rows into y=100..284 with 34px of
    // clearance above the Defaults band; 7-row Audio and 5-row Video
    // had plenty of slack already and stay readable.
    const int RowStride = 24;
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
        // Phase 23-SC-OPTIONS-FOLD2 — keep this in sync with RectInner.Y
        // above; the Y origin moved from 80 → 86 to clear the tab labels.
        int innerAuthorY = 86;
        int yAuthor = innerAuthorY + FirstRowY + i * RowStride;
        // ALPHA-2V FIX — anchor rows to the LIVE inner-panel rect on BOTH
        // axes. X always did this (the `_inner.X - round(innerAuthorX*s)`
        // translation re-adds Layout's dx); Y was raw authored×scale with
        // no dy, which floated every row above the panel whenever the
        // panel is vertically centered (dy>0 — i.e. any UI scale < 100%,
        // where the fit-capped modal shrinks below full height).
        int xOff = _inner.X - (int)MathF.Round(innerAuthorX * s);
        int yOff = _inner.Y - (int)MathF.Round(innerAuthorY * s);
        labelR = (
            (int)MathF.Round((innerAuthorX + LabelLeftX) * s) + xOff,
            (int)MathF.Round(yAuthor * s) + yOff,
            (int)MathF.Round(LabelW * s),
            (int)MathF.Round(RowHeight * s));
        widgetR = (
            (int)MathF.Round((innerAuthorX + WidgetX) * s) + xOff,
            (int)MathF.Round(yAuthor * s) + yOff,
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
        int labelTextW = text.MeasureWidth(label, _fontScale);
        text.DrawString(vw, vh, label,
            labelR.X + labelR.W - labelTextW, labelR.Y + 1, InkDim, _fontScale);

        float v01 = Math.Clamp(get(), 0f, 1f);
        // DS1 `track` (common_control_art): a left cap + a tiled notched centre
        // (b_gui_cmn_track_center carries the tick marks) + a right cap, with the
        // diamond b_gui_cmn_track_button_up thumb sliding along it. The 16×16 art
        // scales with the row height.
        int cap = widgetR.H, thumbW = widgetR.H;
        bool drewTrack = false;
        if (_chrome is not null && _icons is not null)
        {
            Tex("track_lt_side", widgetR.X, widgetR.Y, cap, widgetR.H);
            int cx = widgetR.X + cap, cEnd = widgetR.X + widgetR.W - cap;
            for (int x = cx; x < cEnd; x += cap)
                Tex("track_center", x, widgetR.Y, Math.Min(cap, cEnd - x), widgetR.H);
            Tex("track_rt_side", cEnd, widgetR.Y, cap, widgetR.H);
            int thumbX = widgetR.X + (int)MathF.Round((widgetR.W - thumbW) * v01);
            Tex("track_button_up", thumbX, widgetR.Y, thumbW, widgetR.H);
            drewTrack = true;
        }
        if (!drewTrack)
        {
            bars.DrawRect(vw, vh, widgetR.X, widgetR.Y + widgetR.H / 2 - 1, widgetR.W, 2, Border);
            int thumbX = widgetR.X + (int)((widgetR.W - 8) * v01);
            bars.DrawRect(vw, vh, thumbX, widgetR.Y, 8, widgetR.H,
                _activeWidget == widgetIdx ? BtnPress
                : _hoveredWidget == widgetIdx ? BtnHover : BtnIdle);
            DrawBorder(bars, vw, vh, (thumbX, widgetR.Y, 8, widgetR.H), Border);
        }
        // - / + glyphs flanking the track (DS1 text_*_minus / _plus elements).
        int minusW = text.MeasureWidth("-", _fontScale);
        text.DrawString(vw, vh, "-",
            widgetR.X - minusW - 4 * _fontScale, widgetR.Y + 1, Ink, _fontScale);
        text.DrawString(vw, vh, "+",
            widgetR.X + widgetR.W + 4 * _fontScale, widgetR.Y + 1, Ink, _fontScale);
        // Hit + drag state recorded in _activeWidget / _hoveredWidget
        // by a parent-loop pass below.
    }

    /// <summary>Cycle button. Click steps to the next option in
    /// the array; right-click steps backward. Used for toggles
    /// (On/Off), enums (Easy/Normal/Hard), shadow types, etc.</summary>
    void CycleButton(BarRenderer bars, TextRenderer text, int vw, int vh,
                     int rowIdx, int widgetIdx, string label,
                     Func<int> getIdx, Action<int> setIdx, string[] options,
                     bool dropdown = false)
    {
        RowRect(rowIdx, out var labelR, out var widgetR, vw, vh);
        int labelTextW = text.MeasureWidth(label, _fontScale);
        text.DrawString(vw, vh, label,
            labelR.X + labelR.W - labelTextW, labelR.Y + 1, InkDim, _fontScale);
        var st = _activeWidget == widgetIdx ? ButtonChrome.State.Down
               : _hoveredWidget == widgetIdx ? ButtonChrome.State.Hover
               : ButtonChrome.State.Up;
        // Dropdowns (Resolution, Shadows) reserve a square at the right edge for
        // the DS1 combo `down` arrow (b_gui_cmn_button_down_up); plain cycle /
        // toggle buttons fill the whole box.
        int arrowW = dropdown ? widgetR.H : 0;
        var boxR = (X: widgetR.X, Y: widgetR.Y, W: widgetR.W - arrowW, H: widgetR.H);
        if (!ChromeButton(boxR, st))
        {
            var bg = st == ButtonChrome.State.Down ? BtnPress
                   : st == ButtonChrome.State.Hover ? BtnHover : BtnIdle;
            bars.DrawRect(vw, vh, boxR.X, boxR.Y, boxR.W, boxR.H, bg);
            DrawBorder(bars, vw, vh, boxR, Border);
        }
        if (dropdown)
            Tex("button_down_up", widgetR.X + widgetR.W - arrowW, widgetR.Y, arrowW, widgetR.H);
        var optStr = options[Math.Clamp(getIdx(), 0, options.Length - 1)];
        int oW = text.MeasureWidth(optStr, _fontScale);
        text.DrawString(vw, vh, optStr,
            boxR.X + (boxR.W - oW) / 2, widgetR.Y + 1, Ink, _fontScale);
    }

    /// <summary>Per-tab widget descriptor — the `_widgets` list
    /// rebuilds every Draw so a tab swap clears stale entries
    /// without manual cleanup. Each entry knows its rect,
    /// optional slider get/set, optional cycle-step delegate.</summary>
    sealed class W
    {
        public (int X, int Y, int W, int H) Rect;
        public Action<float>? OnSliderDrag;
        public Action<float>? OnSliderDragY; // SC-OPTIONS-REBIND — vertical scrollbar drag
        public Action? OnClick;        // cycle forward / button press
        public Action? OnRightClick;   // cycle backward
    }
    readonly List<W> _widgets = new();

    void DrawTabContent(BarRenderer bars, TextRenderer text, int vw, int vh)
    {
        // Clear BEFORE the hotkeys branch — the sub-screen registers its
        // cell/scroll/back widgets fresh each frame too.
        _widgets.Clear();
        if (_hotkeysOpen) { DrawHotkeysSubscreen(bars, text, vw, vh); return; }
        switch (ActiveTab)
        {
            case Tab.Video: LayoutVideo(bars, text, vw, vh); break;
            case Tab.Audio: LayoutAudio(bars, text, vw, vh); break;
            case Tab.Input: LayoutInput(bars, text, vw, vh); break;
            case Tab.Game:  LayoutGame (bars, text, vw, vh); break;
            case Tab.Advanced: LayoutAdvanced(bars, text, vw, vh); break;
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

    /// <summary>ALPHA-2V — the host seeds this with the monitor's actual
    /// video modes at load (distinct WxH, ascending). Null falls back to
    /// a common-list so headless/test bootstrap still lays out.</summary>
    public string[]? ResolutionOptions;
    static readonly string[] FallbackResolutions =
        { "1280x720", "1600x900", "1920x1080", "2048x1080", "2560x1080",
          "2560x1440", "3440x1440", "3840x2160" };

    void LayoutVideo(BarRenderer bars, TextRenderer text, int vw, int vh)
    {
        var resOptions = ResolutionOptions is { Length: > 0 } ro ? ro : FallbackResolutions;
        var shadows = new[] { "none", "simple_party", "complex_party" };
        var filter  = new[] { "bilinear", "trilinear", "anisotropic" };
        int r = 0;
        CycleField(bars, text, vw, vh, r++, "Resolution",
            () => _staged.Resolution, v => _staged.Resolution = v, resOptions, dropdown: true);
        CheckboxField(bars, text, vw, vh, r++, "Fullscreen",
            () => _staged.Fullscreen, v => _staged.Fullscreen = v);
        CycleField(bars, text, vw, vh, r++, "Shadows",
            () => _staged.Shadows, v => _staged.Shadows = v, shadows, dropdown: true);
        CycleField(bars, text, vw, vh, r++, "Texture Filtering",
            () => _staged.TextureFiltering, v => _staged.TextureFiltering = v, filter);
        FloatSlider(bars, text, vw, vh, r++, "Gamma",
            () => _staged.Gamma / 2f, t => _staged.Gamma = t * 2f, 0, 100);
        FloatSlider(bars, text, vw, vh, r++, "Object Detail",
            () => _staged.ObjectDetail, t => _staged.ObjectDetail = t, 0, 10);
    }

    /// <summary>ALPHA-2V — themed checkbox row (the Fullscreen toggle). DS1
    /// ships b_gui_cmn_checkbox / b_gui_cmn_checkbox_x 16×16 raws that its
    /// own Options screens never used; they're exactly the house style, so
    /// the modernized row stays in-theme. Falls back to a bordered square
    /// with an X glyph when the art isn't resolvable.</summary>
    void CheckboxField(BarRenderer bars, TextRenderer text, int vw, int vh,
                       int rowIdx, string label, Func<bool> get, Action<bool> set)
    {
        RowRect(rowIdx, out var labelR, out var widgetR, vw, vh);
        int labelTextW = text.MeasureWidth(label, _fontScale);
        text.DrawString(vw, vh, label,
            labelR.X + labelR.W - labelTextW, labelR.Y + 1, InkDim, _fontScale);

        // Square box docked at the widget column's left edge, sized to the row.
        var boxR = (X: widgetR.X, Y: widgetR.Y, W: widgetR.H, H: widgetR.H);
        int widgetIdx = _widgets.Count;
        bool hover = _hoveredWidget == widgetIdx;
        bool drew = false;
        if (_chrome is not null && _icons is not null)
        {
            var t = _chrome("b_gui_cmn_" + (get() ? "checkbox_x" : "checkbox"));
            if (t is not null)
            {
                _icons.DrawIcon(_vw, _vh, t, boxR.X, boxR.Y, boxR.W, boxR.H,
                    hover ? new Vector4(1.15f, 1.12f, 1.05f, 1f) : Vector4.One);
                drew = true;
            }
        }
        if (!drew)
        {
            bars.DrawRect(vw, vh, boxR.X, boxR.Y, boxR.W, boxR.H,
                hover ? BtnHover : BtnIdle);
            DrawBorder(bars, vw, vh, boxR, Border);
            if (get())
            {
                int xw = text.MeasureWidth("X", _fontScale);
                text.DrawString(vw, vh, "X",
                    boxR.X + (boxR.W - xw) / 2, boxR.Y + (boxR.H - 12 * _fontScale) / 2,
                    Ink, _fontScale);
            }
        }
        // State readout beside the box, matching the cycle buttons' value ink.
        text.DrawString(vw, vh, get() ? "On" : "Off",
            boxR.X + boxR.W + 6 * _fontScale, widgetR.Y + 1, Ink, _fontScale);

        _widgets.Add(new W
        {
            Rect = boxR,
            OnClick = () => set(!get()),
            OnRightClick = () => set(!get()),
        });
    }

    void LayoutAudio(BarRenderer bars, TextRenderer text, int vw, int vh)
    {
        // Phase 23-SC-OPTIONS-FOLD — Audio sliders fire AudioStagedChanged
        // on every set so the host can re-apply volumes live during
        // drag. Without this the master/music/sfx sliders feel dead
        // until OK. Ambient + Voice + EAX are persist-only (SiegeFX
        // doesn't separate those channels) — labels render in InkDim
        // to hint that they don't apply yet.
        int r = 0;
        Action notify = () => AudioStagedChanged?.Invoke();
        BoolCycle(bars, text, vw, vh, r++, "Sound",
            () => _staged.SoundEnabled,
            v => { _staged.SoundEnabled = v; notify(); });
        IntSlider(bars, text, vw, vh, r++, "Master Volume",
            () => _staged.MasterVolume,
            v => { _staged.MasterVolume = v; notify(); }, 0, 127);
        IntSlider(bars, text, vw, vh, r++, "Music Volume",
            () => _staged.MusicVolume,
            v => { _staged.MusicVolume = v; notify(); }, 0, 127);
        IntSlider(bars, text, vw, vh, r++, "SFX Volume",
            () => _staged.SfxVolume,
            v => { _staged.SfxVolume = v; notify(); }, 0, 127);
        IntSlider(bars, text, vw, vh, r++, "Ambient Volume (inactive)",
            () => _staged.AmbientVolume, v => _staged.AmbientVolume = v, 0, 127);
        IntSlider(bars, text, vw, vh, r++, "Voice Volume (inactive)",
            () => _staged.VoiceVolume, v => _staged.VoiceVolume = v, 0, 127);
        BoolCycle(bars, text, vw, vh, r++, "EAX (inactive)",
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
        DrawPageButton(bars, text, vw, vh, "Hotkeys…", () => _hotkeysOpen = true);
    }

    void LayoutGame(BarRenderer bars, TextRenderer text, int vw, int vh)
    {
        var diff  = new[] { "Easy", "Normal", "Hard" };
        var blood = new[] { "Red", "Green", "Disabled" };
        int r = 0;
        if (_gamePage == 0)
        {
            // SC-OPTIONS-GAME — authored labels + value words from
            // options_game.gas (framerate reads Yes/No in DS1, the rest
            // On/Off). Every knob on this page is runtime-wired.
            var yesNo = new[] { "No", "Yes" };
            CycleButton(bars, text, vw, vh, r, _widgets.Count, "Display Onscreen Framerate",
                () => _staged.ShowFramerate ? 1 : 0, _ => { }, yesNo);
            AddCycleWidget(r++, () => _staged.ShowFramerate = !_staged.ShowFramerate,
                () => _staged.ShowFramerate = !_staged.ShowFramerate, vw, vh);
            BoolCycle(bars, text, vw, vh, r++, "Raise App Priority",
                () => _staged.PriorityBoost, v => _staged.PriorityBoost = v);
            IntSlider(bars, text, vw, vh, r++, "Text Scroll Rate",
                () => _staged.TextScrollRate, v => _staged.TextScrollRate = v, 0, 100);
            IntSlider(bars, text, vw, vh, r++, "Maximum Text Displayed",
                () => _staged.MaxTextDisplayed, v => _staged.MaxTextDisplayed = v, 1, 20);
            IntSlider(bars, text, vw, vh, r++, "Game Speed",
                () => _staged.GameSpeed, v => _staged.GameSpeed = v, 0, 100);
            BoolCycle(bars, text, vw, vh, r++, "Tutorial Tips",
                () => _staged.TutorialTips, v => _staged.TutorialTips = v);
            CycleField(bars, text, vw, vh, r++, "Game Difficulty",
                () => _staged.Difficulty, v => _staged.Difficulty = v, diff);
            DrawPageButton(bars, text, vw, vh, "More →", () => _gamePage = 1);
        }
        else
        {
            // Page 2 (authored group options_game_2). Blood color and
            // dismemberment have no runtime systems yet — labeled inactive
            // rather than pretending (same convention as Ambient Volume).
            BoolCycle(bars, text, vw, vh, r++, "Show Rollover Help",
                () => _staged.ShowTooltips, v => _staged.ShowTooltips = v);
            CycleField(bars, text, vw, vh, r++, "Blood Color",
                () => _staged.BloodColor, v => _staged.BloodColor = v, blood);
            BoolCycle(bars, text, vw, vh, r++, "Dismemberment (inactive)",
                () => _staged.Dismemberment, v => _staged.Dismemberment = v);
            DrawPageButton(bars, text, vw, vh, "← Back", () => _gamePage = 0);
        }
    }

    /// <summary>ALPHA-2V — the Advanced tab: modern vendor-neutral GPU
    /// features DS1 never exposed. VSync / FPS cap / UI scale / point-light
    /// budget apply live on OK; anisotropy degree pairs with the Video tab's
    /// texture filtering set to "anisotropic"; MSAA rebuilds the GL surface
    /// so it takes effect at the next launch (persisted immediately).</summary>
    void LayoutAdvanced(BarRenderer bars, TextRenderer text, int vw, int vh)
    {
        var fps   = new[] { "Unlimited", "30", "60", "120", "144", "240" };
        var aniso = new[] { "2x", "4x", "8x", "16x" };
        var msaa  = new[] { "Off", "2x", "4x", "8x" };
        int r = 0;
        BoolCycle(bars, text, vw, vh, r++, "VSync",
            () => _staged.VSync, v => _staged.VSync = v);
        CycleField(bars, text, vw, vh, r++, "Frame Rate Cap",
            () => _staged.FpsCap, v => _staged.FpsCap = v, fps, dropdown: true);
        CycleField(bars, text, vw, vh, r++, "Anisotropy",
            () => _staged.Anisotropy, v => _staged.Anisotropy = v, aniso);
        CycleField(bars, text, vw, vh, r++, "MSAA (next launch)",
            () => _staged.Msaa, v => _staged.Msaa = v, msaa);
        IntSlider(bars, text, vw, vh, r++, "Point Light Budget",
            () => _staged.PointLightBudget, v => _staged.PointLightBudget = v, 4, 32);
        IntSlider(bars, text, vw, vh, r++, "UI Scale %",
            () => _staged.UiScalePercent, v => _staged.UiScalePercent = v, 50, 150);
        BoolCycle(bars, text, vw, vh, r++, "Move Panels As One",
            () => _staged.RailLocked, v => _staged.RailLocked = v);
    }

    void DrawPageButton(BarRenderer bars, TextRenderer text, int vw, int vh,
                        string label, Action onClick)
    {
        // Docked bottom-left (mirror of Defaults) — not in the content grid.
        var r = _more;
        var st = _activeWidget == _widgets.Count ? ButtonChrome.State.Down
               : _hoveredWidget == _widgets.Count ? ButtonChrome.State.Hover
               : ButtonChrome.State.Up;
        if (!ChromeButton(r, st))
        {
            var bg = st == ButtonChrome.State.Down ? BtnPress
                   : st == ButtonChrome.State.Hover ? BtnHover : BtnIdle;
            bars.DrawRect(vw, vh, r.X, r.Y, r.W, r.H, bg);
            DrawBorder(bars, vw, vh, r, Border);
        }
        int lW = text.MeasureWidth(label, _fontScale);
        text.DrawString(vw, vh, label,
            r.X + (r.W - lW) / 2, r.Y + (r.H - 12 * _fontScale) / 2, Ink, _fontScale);
        _widgets.Add(new W { Rect = r, OnClick = onClick });
    }

    /// <summary>String-cycle field. Click steps forward, right-click
    /// steps back, both wrap. Options is reference-captured so the
    /// `Difficulty` cycle keeps stepping through Easy→Normal→Hard
    /// without relying on a label-string switch.</summary>
    void CycleField(BarRenderer bars, TextRenderer text, int vw, int vh,
                    int rowIdx, string label,
                    Func<string> get, Action<string> set, string[] options,
                    bool dropdown = false)
    {
        // ALPHA-2V — a value outside the option list (e.g. a freely-resized
        // window's WxH) displays as-is instead of masquerading as the first
        // preset; the first click steps onto the preset list.
        int idx = Array.IndexOf(options, get());
        var displayOpts = idx >= 0 ? options : new[] { get() };
        CycleButton(bars, text, vw, vh, rowIdx, _widgets.Count, label,
            () => idx >= 0 ? idx : 0, _ => { }, displayOpts, dropdown);
        AddCycleWidget(rowIdx,
            () => { var i = Array.IndexOf(options, get());
                    set(i < 0 ? options[0] : options[(i + 1) % options.Length]); },
            () => { var i = Array.IndexOf(options, get());
                    set(i < 0 ? options[^1] : options[(i - 1 + options.Length) % options.Length]); },
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

    /// <summary>SC-OPTIONS-REBIND — the rebindable hotkeys editor, laid out
    /// 1:1 from DS1's options_bindings.gas: "Primary"/"Secondary" column
    /// headers at authored y=90; ten 20px-pitch rows at y=110..306 (command
    /// name right-justified in 124..290, primary cell 300..395, secondary
    /// cell 400..495); scrollbar at 500,110..306; Back bottom-left. Group
    /// headers (Party Controls / View Controls / User Interface / Game
    /// Settings) render as left-aligned gold rows inside the list, matching
    /// the original screen. LMB a cell to capture a new key (Esc cancels),
    /// RMB clears it; assigning a key steals it from whichever action held
    /// it. Defaults (main button) resets every binding while this screen
    /// is up.</summary>
    void DrawHotkeysSubscreen(BarRenderer bars, TextRenderer text, int vw, int vh)
    {
        Layout(vw, vh, out var s);
        int xOff = _inner.X - (int)MathF.Round(117 * s);
        int yOff = _inner.Y - (int)MathF.Round(86 * s);
        (int X, int Y, int W, int H) A(int x0, int y0, int x1, int y1) => (
            (int)MathF.Round(x0 * s) + xOff,
            (int)MathF.Round(y0 * s) + yOff,
            (int)MathF.Round((x1 - x0) * s),
            (int)MathF.Round((y1 - y0) * s));

        // Column headers (authored text_key0 / text_key1).
        var priHdr = A(300, 90, 395, 106);
        var secHdr = A(400, 90, 495, 106);
        void Centered(string t, (int X, int Y, int W, int H) r, Vector4 ink)
        {
            int w = text.MeasureWidth(t, _fontScale);
            text.DrawString(vw, vh, t, r.X + (r.W - w) / 2,
                r.Y + (r.H - 12 * _fontScale) / 2, ink, _fontScale);
        }
        Centered("Primary", priHdr, Ink);
        Centered("Secondary", secHdr, Ink);

        int maxScroll = Math.Max(0, BindRows.Count - BindRowsVisible);
        _bindScroll = Math.Clamp(_bindScroll, 0, maxScroll);

        var cellBg    = new Vector4(0.06f, 0.06f, 0.07f, 0.75f);
        var cellHover = new Vector4(0.30f, 0.28f, 0.20f, 0.95f);
        var cellSel   = new Vector4(0.45f, 0.38f, 0.16f, 1.00f);

        for (int i = 0; i < BindRowsVisible; i++)
        {
            int rowIdx = _bindScroll + i;
            if (rowIdx >= BindRows.Count) break;
            var row = BindRows[rowIdx];
            int y0 = 110 + i * 20, y1 = y0 + 16;
            var labelR = A(124, y0, 290, y1);

            if (row.Header is { } hdr)
            {
                // Group header — left-aligned gold, like the original.
                text.DrawString(vw, vh, hdr, labelR.X,
                    labelR.Y + (labelR.H - 12 * _fontScale) / 2, InkDim, _fontScale);
                continue;
            }

            var def = row.Def!;
            int nameW = text.MeasureWidth(def.Name, _fontScale);
            text.DrawString(vw, vh, def.Name,
                labelR.X + labelR.W - nameW,
                labelR.Y + (labelR.H - 12 * _fontScale) / 2, Ink, _fontScale);

            var slots = _stagedBindings.TryGetValue(def.Id, out var sl)
                ? sl : new[] { def.DefPrimary, def.DefSecondary };
            for (int slot = 0; slot < 2; slot++)
            {
                var cellR = slot == 0 ? A(300, y0, 395, y1) : A(400, y0, 495, y1);
                bool capturing = _captureId == def.Id && _captureSlot == slot;
                bool hover = _hoveredWidget == _widgets.Count;
                var bg = capturing ? cellSel : hover ? cellHover : cellBg;
                bars.DrawRect(vw, vh, cellR.X, cellR.Y, cellR.W, cellR.H, bg);
                DrawBorder(bars, vw, vh, cellR, capturing ? Ink : Border);
                Centered(capturing ? "press a key…" : KeyBindingRegistry.Display(slots[slot]),
                    cellR, capturing ? Ink : InkDim);

                var (id, si) = (def.Id, slot);
                _widgets.Add(new W
                {
                    Rect = cellR,
                    OnClick = () => { _captureId = id; _captureSlot = si; },
                    OnRightClick = () =>
                    {
                        if (_stagedBindings.TryGetValue(id, out var mine)) mine[si] = "";
                        if (_captureId == id && _captureSlot == si) _captureId = "";
                    },
                });
            }
        }

        // Scrollbar (authored scroll_bindings at 500,110..306) — simple
        // proportional thumb; click/drag anywhere on the track jumps.
        var trackR = A(500, 110, 516, 306);
        bars.DrawRect(vw, vh, trackR.X, trackR.Y, trackR.W, trackR.H, cellBg);
        DrawBorder(bars, vw, vh, trackR, Border);
        if (maxScroll > 0)
        {
            int thumbH = Math.Max(12, trackR.H * BindRowsVisible / BindRows.Count);
            int thumbY = trackR.Y + (int)((trackR.H - thumbH) * (_bindScroll / (float)maxScroll));
            bars.DrawRect(vw, vh, trackR.X + 1, thumbY, trackR.W - 2, thumbH,
                _hoveredWidget == _widgets.Count ? BtnHover : BtnIdle);
            _widgets.Add(new W
            {
                Rect = trackR,
                OnSliderDragY = py =>
                {
                    float t = (py - trackR.Y - thumbH / 2f) / Math.Max(1f, trackR.H - thumbH);
                    _bindScroll = Math.Clamp((int)MathF.Round(t * maxScroll), 0, maxScroll);
                },
            });
        }

        // Back — authored button_back (140,318)-(280,334); reuses the
        // bottom-left dock rect the Hotkeys… button occupies on the way in.
        DrawPageButton(bars, text, vw, vh, "← Back", () => { _hotkeysOpen = false; _captureId = ""; });
    }

    /// <summary>Called by the host on Open() to seed `_staged` from
    /// `Live` so a Cancel cleanly reverts. Slice A flow exposes
    /// this as the public entry; later slices that read prefs.gas
    /// will replace `Live` first then call this.</summary>
    public void SyncStagedFromLive()
    {
        _staged = Live.Clone();
        _gamePage = 0;
        _hotkeysOpen = false;
        _captureId = "";
        // SC-OPTIONS-REBIND — stage the bindings from the live registry so
        // Cancel discards edits the same way every other tab does.
        _stagedBindings = Registry?.Snapshot() ?? new Dictionary<string, string[]>();
    }

    /// <summary>ALPHA-2V — replace the live settings wholesale (prefs.json
    /// load at boot). Staged re-syncs so a menu opened right after boot
    /// shows the persisted values.</summary>
    public void SetLive(Settings s) { Live = s; _staged = s.Clone(); }

    /// <summary>Commit staged → live + apply runtime hooks. Slice C
    /// wires audio volumes; the rest persist-only until further
    /// runtime knobs land. Caller invokes when ConfirmedThisFrame
    /// fires. SC-OPTIONS-REBIND — the staged key bindings ride the
    /// same commit: Live.KeyBindings gets the full staged map (fresh
    /// dictionary, never shared with the editor buffer) so the host
    /// can Apply() it to the registry and persist it.</summary>
    public void CommitStaged()
    {
        Live = _staged.Clone();
        Live.KeyBindings = StagedBindingsSnapshot();
        _staged.KeyBindings = Live.KeyBindings;
    }

    /// <summary>Reset the active tab's fields to the
    /// /config/options.gas defaults. Per-tab reset matches DS1's
    /// `notify(default_options_<tab>)` behavior.</summary>
    public void ApplyDefaultsForActiveTab()
    {
        // SC-OPTIONS-REBIND — Defaults while the Hotkeys sub-screen is up
        // resets every staged binding to the authored input_bindings.gas
        // defaults (DS1's notify(default_options_bindings)), leaving the
        // Input tab's own settings alone.
        if (_hotkeysOpen)
        {
            _captureId = "";
            _stagedBindings.Clear();
            foreach (var def in KeyBindingRegistry.Defs)
                _stagedBindings[def.Id] = new[] { def.DefPrimary, def.DefSecondary };
            return;
        }
        var d = new Settings();
        switch (ActiveTab)
        {
            case Tab.Video:
                _staged.Resolution = d.Resolution;
                _staged.Fullscreen = d.Fullscreen;
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
            case Tab.Advanced:
                _staged.VSync = d.VSync;
                _staged.FpsCap = d.FpsCap;
                _staged.Anisotropy = d.Anisotropy;
                _staged.Msaa = d.Msaa;
                _staged.PointLightBudget = d.PointLightBudget;
                _staged.UiScalePercent = d.UiScalePercent;
                _staged.RailLocked = d.RailLocked;
                break;
        }
    }
}
