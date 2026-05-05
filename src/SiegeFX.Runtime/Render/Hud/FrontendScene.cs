using System.Numerics;
using Silk.NET.OpenGL;
using SiegeFX.Core.Assets;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 21d-2a-viii-FE — composes the full DS1 frontend scene from the shipped
/// 8 frontend ASP meshes. The original engine layers these into a single 3D scene
/// viewed through an orthographic UI camera; this composer reproduces that layering.
///
/// <para><b>Why a composer:</b> The DS1 main menu and character_select are not
/// independent screens. They share the OUTSIDE chrome (backdrop / leftside /
/// rightside / logo) and morph the INSIDE panels (mainmenu / menubars / backbutton)
/// through PRS clips like <c>mainmenu_sng2cd</c> ("single new game → character
/// design"). Rendering character_select correctly therefore requires loading all
/// the same meshes the main menu uses and putting them in the cd-state PRS pose
/// — which is what the START frame of the <c>cd2*</c> transitions or the END frame
/// of the <c>*2cd</c> transitions captures.</para>
///
/// <para><b>State machine:</b> One <see cref="ScreenState"/> per logical screen.
/// Each state declares per-mesh (clip, timeFraction) tuples — typically the END
/// frame (timeFraction=1) of the transition INTO that state. Transitions between
/// states animate by playing the named transition clip from time 0 to its full
/// length, then settle on the destination state's hold pose.</para>
///
/// <para><b>Scope of this slice (viii-FE):</b> The character_select (cd) state is
/// the primary target. Other states (mm, sp, sng, lm, mp) are stubbed but not
/// yet visually verified. The viii-d solid-color scaffolding under
/// CharacterCreatorPanel goes away in step D; this composer is the new chrome.</para>
/// </summary>
public sealed class FrontendScene : IDisposable
{
    public enum ScreenState
    {
        /// <summary>Phase 24-MAINMENU step 1 — Microsoft splash (3-panel
        /// alpha-anim from <c>intro_microsoft.gas</c>). Auto-advances to
        /// <see cref="IntroGaspowered"/> when the alpha curve completes,
        /// firing the <c>end_microsoft_fade</c> notification DS1's gas
        /// listens for.</summary>
        IntroMicrosoft,
        /// <summary>Phase 24-MAINMENU step 2 — GPG splash (3-panel
        /// alpha-anim from <c>intro_gaspowered.gas</c>). Auto-advances
        /// to <see cref="IntroBink"/>.</summary>
        IntroGaspowered,
        /// <summary>Phase 24-MAINMENU step 3 (stub) — placeholder for the
        /// <c>gpg_intro.bik</c> playback. Currently a 1-second fade-to-black;
        /// real Bink decode lives behind splinter SC-MAINMENU-BINK. Auto-
        /// advances to <see cref="IntroLogoDrop"/>.</summary>
        IntroBink,
        /// <summary>Phase 24-MAINMENU step 4 — "Dungeon Siege" sword drop.
        /// Plays <c>a_gui_fe_m_mn_3d_logo-enter.prs</c> (2.17s) on
        /// <c>m_gui_fe_m_mn_3d_logo.asp</c> inside the
        /// <c>frontend_lights.gas to_main_menu ramp_duration = 6.0</c>
        /// window with <c>s_e_frontend_logo_flyin.wav</c> on entry.</summary>
        IntroLogoDrop,
        /// <summary>Phase 25-CHROME — logo holds posed at the end of
        /// logo-enter for a short beat before flyout starts. Gives the
        /// user a moment to read the title before the sword flies up
        /// and the menu drops in.</summary>
        IntroLogoHold,
        /// <summary>Phase 25-CHROME — sword flies up out of the log via
        /// <c>logo-exit.prs</c> (0.58s) with <c>s_e_frontend_logo_flyout.wav</c>
        /// on entry. Auto-advances to <see cref="IntroMenuFlyIn"/>.</summary>
        IntroLogoExit,
        /// <summary>Phase 25-CHROME — main menu chrome drops down from
        /// above. mainmenu_flyin / menubars_flyin / leftside_flyin /
        /// rightside_flyin / backbutton_flyin all run simultaneously
        /// (~1.5s longest). Auto-advances to <see cref="MainMenu"/>.</summary>
        IntroMenuFlyIn,
        /// <summary>Main menu — Single Player / Multiplayer / Options / Exit.</summary>
        MainMenu,
        /// <summary>Phase 27-SP-FLYOUT — Main → Single Player transition.
        /// mainmenu_mm2sp / menubars_mm2sp / backbutton_e2b run together
        /// over <see cref="MmToSpDur"/>; auto-advances to
        /// <see cref="SinglePlayer"/> at completion. Mirrors the
        /// Intro→MainMenu fly-in pattern.</summary>
        MainMenuToSp,
        /// <summary>Single Player sub-menu — New Game / Load Game / Back.</summary>
        SinglePlayer,
        /// <summary>Phase 27-SP-FLYOUT — Single Player → Main reverse
        /// transition. mainmenu_sp2mm / menubars_sp2mm / backbutton_b2e
        /// over <see cref="SpToMmDur"/>; auto-advances to
        /// <see cref="MainMenu"/> at completion.</summary>
        SinglePlayerToMm,
        /// <summary>Phase 28-CD-FLYOUT — Single Player → Character
        /// Creator forward transition. mainmenu_sng2cd / menubars_lm2cd
        /// / backbutton_b2pn / heromenu_begin run together over
        /// <see cref="SpToCdDur"/>; auto-advances to
        /// <see cref="CharacterSelect"/> at completion.</summary>
        SinglePlayerToCd,
        /// <summary>Start New Game — character template selection.</summary>
        SingleNewGame,
        /// <summary>Character design — the spinner-driven hero creator.</summary>
        CharacterSelect,
        /// <summary>Phase 28-CD-FLYOUT — Character Creator → Single
        /// Player reverse transition. mainmenu_cd2sng / menubars_cd2lm
        /// / backbutton_pn2b over <see cref="CdToSpDur"/>;
        /// auto-advances to <see cref="SinglePlayer"/> at completion.</summary>
        CharacterSelectToSp,
        /// <summary>SC-DIFF — cd → Difficulty forward transition. Same
        /// chrome family as the cd state but with the title row swapping
        /// from text-01 (CHOOSE HERO) to text-02 (DIFFICULTY) and the
        /// button column swapping from heromenu spinners to the three
        /// menubars buttons (EASY/MEDIUM/HARD). Auto-advances to
        /// <see cref="Difficulty"/> at <see cref="CdToDiffDur"/>.</summary>
        CharacterSelectToDifficulty,
        /// <summary>SC-DIFF — settled Difficulty screen. Easy/Medium/Hard
        /// buttons + BACK. Hard click kicks off the region-launch flow
        /// using the saved hero picker.</summary>
        Difficulty,
        /// <summary>SC-DIFF — Difficulty → cd reverse transition (BACK
        /// click). Auto-advances to <see cref="CharacterSelect"/> at
        /// <see cref="DiffToCdDur"/>.</summary>
        DifficultyToCharacterSelect,
        /// <summary>Load Map — final screen before world load.</summary>
        LoadMap,
        /// <summary>Multiplayer.</summary>
        Multiplayer,
    }

    // Phase 24-MAINMENU splash timing. DS1's gas authors `alpha_animation`
    // without a literal duration in the bodies we've inspected; the
    // 0.8/1.5/0.8 = 3.1s per-splash curve below is a reasonable starting
    // point matching the visual cadence of every retail playthrough we
    // checked. Tunable per slice once the user eyeballs it.
    const float IntroFadeIn  = 0.8f;
    const float IntroHold    = 1.5f;
    const float IntroFadeOutDur = 0.8f;
    const float IntroPerSplash = IntroFadeIn + IntroHold + IntroFadeOutDur;
    // Phase 24-MAINMENU step 3 — Bink stub. 1s fade-to-black sits in the
    // GPG-intro slot. Splinter SC-MAINMENU-BINK replaces this with real
    // .bik playback (gpg_intro.bik = 4.98 MB Bink in Objects.dsres).
    const float IntroBinkDur = 1.0f;
    // Phase 24-MAINMENU step 4 / Phase 25-CHROME — logo-drop window.
    // logo-enter.prs is 2.1667s; logo-exit.prs is 0.5833s. The
    // 6.0s frontend_lights.gas to_main_menu ramp_duration covers
    // Bink (1s) + LogoDrop (2.17s) + LogoHold (~1.0s) + LogoExit
    // (0.58s) + MenuFlyIn (~1.25s) ≈ 6s total. Tunable per slice
    // once the user eyeballs the cadence end-to-end.
    const float LogoEnterDur    = 2.1667f;
    const float LogoExitDur     = 0.5833f;
    const float LogoHoldDur     = 1.0f;
    const float MenuFlyInDur    = 1.6667f; // mainmenu_flyin / menubars_flyin both 1.6667s
    /// <summary>Phase 24-MAINMENU — exposed for the host's logo-drop
    /// renderer so it knows the time fraction to evaluate
    /// <c>logo-enter.prs</c> at. Clamped to [0, 1] for hold semantics.</summary>
    public float LogoDropTimeFraction =>
        State == ScreenState.IntroLogoDrop ? Math.Clamp(_stateTime / LogoEnterDur, 0f, 1f) :
        State == ScreenState.IntroLogoHold ? 1f :
        State == ScreenState.IntroLogoExit ? 0f /* logo holds end pose; exit clip drives sword bone */ :
        0f;
    /// <summary>Phase 25-CHROME — exposed for the host so logo-exit.prs
    /// runs over the 0.58s exit window. Clamped to [0, 1].</summary>
    public float LogoExitTimeFraction =>
        State == ScreenState.IntroLogoExit ? Math.Clamp(_stateTime / LogoExitDur, 0f, 1f) : 0f;
    /// <summary>Phase 25-CHROME — exposed for the host so the
    /// mainmenu/menubars/sides/backbutton _flyin clips run together
    /// at the same time-fraction. Clamped to [0, 1]; held at 1 once
    /// the menu has settled into MainMenu state so the post-flyin
    /// pose persists.</summary>
    public float MenuFlyInTimeFraction =>
        State == ScreenState.IntroMenuFlyIn ? Math.Clamp(_stateTime / MenuFlyInDur, 0f, 1f) :
        State == ScreenState.MainMenu       ? 1f :
        0f;

    // Phase 27-SP-FLYOUT — Main → SP and SP → Main transitions.
    // mainmenu_mm2sp / menubars_mm2sp run authored at ~1.0s each
    // (rough heuristic; PRS lengths in DS1's main-menu set cluster
    // around 1.0–1.5s for the cross-state morphs). Tunable.
    const float MmToSpDur = 1.0f;
    const float SpToMmDur = 1.0f;
    /// <summary>Phase 27-SP-FLYOUT — fraction along mm2sp clips
    /// (clamped 0..1). Held at 1 while in <see cref="ScreenState.SinglePlayer"/>
    /// so the SP pose persists between transitions.</summary>
    public float MmToSpTimeFraction =>
        State == ScreenState.MainMenuToSp ? Math.Clamp(_stateTime / MmToSpDur, 0f, 1f) :
        State == ScreenState.SinglePlayer ? 1f :
        0f;
    /// <summary>Phase 27-SP-FLYOUT — fraction along sp2mm clips
    /// (clamped 0..1). Used only during the reverse transition.</summary>
    public float SpToMmTimeFraction =>
        State == ScreenState.SinglePlayerToMm ? Math.Clamp(_stateTime / SpToMmDur, 0f, 1f) : 0f;

    // Phase 28-CD-FLYOUT — SP → Character Creator and reverse.
    // mainmenu_sng2cd / menubars_lm2cd / backbutton_b2pn / heromenu_begin
    // run together. Authored clip lengths cluster around 1.0–1.7s in
    // the main-menu PRS catalog; tunable.
    const float SpToCdDur = 1.5f;
    const float CdToSpDur = 1.5f;
    const float CdToDiffDur = 1.0f;
    const float DiffToCdDur = 1.0f;
    /// <summary>Phase 28-CD-FLYOUT — fraction along sng2cd / lm2cd /
    /// b2pn / heromenu_begin clips (clamped 0..1). Held at 1 while in
    /// <see cref="ScreenState.CharacterSelect"/> so the cd pose persists
    /// between transitions.</summary>
    public float SpToCdTimeFraction =>
        State == ScreenState.SinglePlayerToCd ? Math.Clamp(_stateTime / SpToCdDur, 0f, 1f) :
        State == ScreenState.CharacterSelect  ? 1f :
        0f;
    /// <summary>Phase 28-CD-FLYOUT — fraction along cd2sng / cd2lm /
    /// pn2b clips (clamped 0..1). Used only during the reverse
    /// transition.</summary>
    public float CdToSpTimeFraction =>
        State == ScreenState.CharacterSelectToSp ? Math.Clamp(_stateTime / CdToSpDur, 0f, 1f) : 0f;

    /// <summary>Time spent in the current state. Re-zeroed on every
    /// <see cref="SetState"/>.</summary>
    float _stateTime;

    /// <summary>Phase 24-MAINMENU step 1+2 — the splash texture-name
    /// prefix DS1 ships per state (<c>b_gui_nis_ms_</c> / <c>b_gui_nis_gpg_</c>).
    /// Returns null for non-splash states; host renders nothing for those.
    /// Three textures stitch into the single 640×256 splash strip with
    /// authored rects 0,0,256,256 / 256,0,512,256 / 512,0,640,256 (the
    /// third panel is the narrow 128-wide right-cap).</summary>
    public string? IntroTexturePrefix => State switch
    {
        ScreenState.IntroMicrosoft  => "b_gui_nis_ms_",
        ScreenState.IntroGaspowered => "b_gui_nis_gpg_",
        _ => null,
    };

    /// <summary>Phase 24-MAINMENU — current alpha for the splash overlay
    /// in [0, 1]. Fade-in over <see cref="IntroFadeIn"/>, hold at 1, fade
    /// out over <see cref="IntroFadeOutDur"/>. The <see cref="IntroFadeOut"/>
    /// state holds at 0 (black) for <see cref="IntroFadeOutHold"/> seconds
    /// before transitioning to <see cref="ScreenState.MainMenu"/>.</summary>
    public float IntroAlpha
    {
        get
        {
            switch (State)
            {
                case ScreenState.IntroMicrosoft:
                case ScreenState.IntroGaspowered:
                    if (_stateTime < IntroFadeIn)
                        return Math.Clamp(_stateTime / IntroFadeIn, 0f, 1f);
                    if (_stateTime < IntroFadeIn + IntroHold)
                        return 1f;
                    if (_stateTime < IntroPerSplash)
                        return Math.Clamp(1f - (_stateTime - IntroFadeIn - IntroHold) / IntroFadeOutDur, 0f, 1f);
                    return 0f;
                default:
                    return 0f;
            }
        }
    }

    private readonly GL _gl;
    private readonly AssetResolver _resolver;

    // Per-mesh renderers. Lazy-loaded — only those referenced by an active state
    // pay the GPU upload cost. (Backdrop / logo / sides are eager since they
    // appear in every state.)
    private readonly Dictionary<string, UiMeshRenderer> _renderers = new();
    private readonly Dictionary<string, GlTexture> _textures = new();
    private readonly Dictionary<string, PrsAnimation?> _clips = new();

    // Per-frame clock used to advance default/idle clip times (the heromenu
    // default clip is a 3.3s loop). Transitions update this differently when
    // they're added.
    private float _timeSec;

    // Shared frontend-space reference rect (backdrop's bounds). DS1 authors
    // every frontend mesh in a single common coordinate system: backdrop
    // spans roughly (-1.64, -1.64)..(1.64, 1.64) and represents the visible
    // screen; logo / leftside / rightside / etc. occupy specific subregions
    // within that box; heromenu/menubars/mainmenu sit ABOVE the screen in
    // bind pose and PRS clips pull them down into place. So every mesh must
    // render through the SAME backdrop-derived projection — independent
    // per-mesh viewport scaling (UiMeshRenderer.DrawAt's default) destroys
    // those spatial relationships and zooms small meshes up to fill the
    // screen.
    //
    // Filled in by EnsureReference once backdrop is loaded; the constants
    // below are a safe fallback if the backdrop ASP somehow comes back null.
    private float _refMinX = -1.7f, _refMinY = -1.7f, _refMaxX = 1.7f, _refMaxY = 1.7f;
    private bool _refResolved;

    public ScreenState State { get; private set; } = ScreenState.CharacterSelect;

    public FrontendScene(GL gl, AssetResolver resolver)
    {
        _gl = gl;
        _resolver = resolver;
    }

    public void Tick(float dt)
    {
        _timeSec += dt;
        // Phase 24-MAINMENU step 1+2 — drive splash → main menu auto-advance.
        // Each splash auto-fires its `end_*_fade` equivalent at IntroPerSplash;
        // the FadeOut beat (IntroFadeOutHold) is the placeholder for the
        // Bink-intro/logo-drop window batch 2 fills in.
        _stateTime += dt;
        switch (State)
        {
            case ScreenState.IntroMicrosoft:
                if (_stateTime >= IntroPerSplash) SetState(ScreenState.IntroGaspowered);
                break;
            case ScreenState.IntroGaspowered:
                if (_stateTime >= IntroPerSplash) SetState(ScreenState.IntroBink);
                break;
            case ScreenState.IntroBink:
                if (_stateTime >= IntroBinkDur) SetState(ScreenState.IntroLogoDrop);
                break;
            case ScreenState.IntroLogoDrop:
                if (_stateTime >= LogoEnterDur) SetState(ScreenState.IntroLogoHold);
                break;
            case ScreenState.IntroLogoHold:
                if (_stateTime >= LogoHoldDur) SetState(ScreenState.IntroLogoExit);
                break;
            case ScreenState.IntroLogoExit:
                if (_stateTime >= LogoExitDur) SetState(ScreenState.IntroMenuFlyIn);
                break;
            case ScreenState.IntroMenuFlyIn:
                if (_stateTime >= MenuFlyInDur) SetState(ScreenState.MainMenu);
                break;
            case ScreenState.MainMenuToSp:
                if (_stateTime >= MmToSpDur) SetState(ScreenState.SinglePlayer);
                break;
            case ScreenState.SinglePlayerToMm:
                if (_stateTime >= SpToMmDur) SetState(ScreenState.MainMenu);
                break;
            case ScreenState.SinglePlayerToCd:
                if (_stateTime >= SpToCdDur) SetState(ScreenState.CharacterSelect);
                break;
            case ScreenState.CharacterSelectToSp:
                if (_stateTime >= CdToSpDur) SetState(ScreenState.SinglePlayer);
                break;
            case ScreenState.CharacterSelectToDifficulty:
                if (_stateTime >= CdToDiffDur) SetState(ScreenState.Difficulty);
                break;
            case ScreenState.DifficultyToCharacterSelect:
                if (_stateTime >= DiffToCdDur) SetState(ScreenState.CharacterSelect);
                break;
        }
    }

    public void SetState(ScreenState s) { State = s; _stateTime = 0f; }

    /// <summary>Draws the full frontend scene. Caller must already be inside a HUD
    /// pass (depth off, alpha blend on). Drawn in back-to-front order so the
    /// per-mesh transparency composites correctly.</summary>
    public void Draw(int viewportW, int viewportH)
    {
        // Reference 800×600 layout; everything scales linearly.
        // Each mesh occupies the full screen (DS1's frontend is composed of
        // overlapping full-screen elements) and the per-mesh bind-pose XY
        // already places content within that space.
        var fullW = viewportW;
        var fullH = viewportH;

        switch (State)
        {
            case ScreenState.IntroMicrosoft:
            case ScreenState.IntroGaspowered:
            case ScreenState.IntroBink:
                // Phase 24-MAINMENU step 1-3 — splash + Bink-stub states
                // render through the host's IconRenderer (3 RAW panels +
                // alpha) or just black. FrontendScene only owns the state-
                // machine timing here.
                return;
            case ScreenState.IntroLogoDrop:
            case ScreenState.IntroLogoHold:
                // Phase 24-MAINMENU step 4 / Phase 25-CHROME — sword drop +
                // hold. logo.asp plays logo-enter.prs over LogoDropDur,
                // then holds the end pose for LogoHoldDur (sword sits in
                // the log) before flyout begins.
                DrawLogoDropState(fullW, fullH);
                return;
            case ScreenState.IntroLogoExit:
                // Phase 25-CHROME — sword rises out of the log via
                // logo-exit.prs (0.58s). Backdrop + sides stay; logo
                // mesh animates upward off-screen.
                DrawLogoExitState(fullW, fullH);
                return;
            case ScreenState.IntroMenuFlyIn:
                // Phase 25-CHROME — main menu chrome drops down from
                // above. mainmenu_flyin / menubars_flyin / leftside_flyin
                // / rightside_flyin / backbutton_flyin all share the
                // same MenuFlyInTimeFraction so the parts arrive in
                // sync. End-pose matches MainMenu's resting state so
                // the transition is seamless.
                DrawMenuFlyInState(fullW, fullH);
                return;
            case ScreenState.MainMenu:
                // Phase 25-CHROME — proper main-menu chrome (was falling
                // back to DrawCharacterSelectState which used the wrong
                // pose + showed the spinner column with character-creator
                // labels). DS1's main menu = backdrop / leftside /
                // rightside in default poses + mainmenu.asp in
                // `mainmenu_sp2mm` end pose (Bone01 Y=2.01, panel slid
                // down) + menubars.asp in `menubars_sp2mm` end pose
                // (Bone01 Y=-0.99, the 5 menu buttons visible). heromenu
                // / backbutton / logo are intentionally skipped — they
                // belong to other states (logo is splash-only per the
                // existing memory).
                DrawMainMenuState(fullW, fullH);
                return;
            case ScreenState.MainMenuToSp:
                // Phase 27-SP-FLYOUT — Main → SP transition. mainmenu_mm2sp
                // + menubars_mm2sp run together; backbutton plays e2b
                // so the wood button on the bottom morphs from EXIT to
                // BACK. Backdrop + sides hold their default poses.
                DrawMmToSpState(fullW, fullH);
                return;
            case ScreenState.SinglePlayer:
                // Phase 27-SP-FLYOUT — SP submenu settled pose. Same chrome
                // as MainMenu but mainmenu_mm2sp / menubars_mm2sp /
                // backbutton_e2b held at end-frame so the title bar shows
                // SINGLE PLAYER, the menubars column shows the 2 SP rows
                // (NEW GAME / LOAD GAME) instead of the 5 main-menu rows,
                // and the bottom button reads BACK.
                DrawSinglePlayerState(fullW, fullH);
                return;
            case ScreenState.SinglePlayerToMm:
                // Phase 27-SP-FLYOUT — reverse transition (Back click).
                // mainmenu_sp2mm + menubars_sp2mm + backbutton_b2e
                // unwind to the MainMenu pose.
                DrawSpToMmState(fullW, fullH);
                return;
            case ScreenState.SinglePlayerToCd:
                // Phase 28-CD-FLYOUT — SP → Character Creator. mainmenu_sng2cd
                // + menubars_lm2cd + backbutton_b2pn + heromenu_begin all run
                // synchronized over SpToCdDur.
                DrawSpToCdState(fullW, fullH);
                return;
            case ScreenState.CharacterSelect:
                DrawCharacterSelectState(fullW, fullH);
                break;
            case ScreenState.CharacterSelectToSp:
                // Phase 28-CD-FLYOUT — Character Creator → SP reverse
                // transition (Previous click). Reverse clips unwind
                // back to the SP submenu pose.
                DrawCdToSpState(fullW, fullH);
                return;
            case ScreenState.CharacterSelectToDifficulty:
                DrawDifficultyChrome(fullW, fullH,
                    hold: Math.Clamp(_stateTime / CdToDiffDur, 0f, 1f), fromCd: true);
                return;
            case ScreenState.Difficulty:
                DrawDifficultyChrome(fullW, fullH, hold: 1f, fromCd: true);
                return;
            case ScreenState.DifficultyToCharacterSelect:
                DrawDifficultyChrome(fullW, fullH,
                    hold: Math.Clamp(_stateTime / DiffToCdDur, 0f, 1f), fromCd: false);
                return;
            default:
                // Other states are stubs for now — fall back to character_select
                // visual so the composer is never blank during development.
                DrawCharacterSelectState(fullW, fullH);
                break;
        }
    }

    /// <summary>Phase 25-CHROME — main menu chrome assembly. Reads each
    /// shipped frontend ASP at the pose + subset mask appropriate for
    /// MainMenu state (NOT cd-state, which the pre-Phase-25 fallback was
    /// rendering). Pose research receipts:
    /// <list type="bullet">
    ///   <item><c>mainmenu_sp2mm</c> end-frame Bone01 Y=2.01 (down into
    ///         screen), PanelBASE1+2 at Z=-0.85 (visible title slot).
    ///         End-pose matches <c>mainmenu_flyin</c> end (the boot-from-
    ///         logo entry); both converge to the same MainMenu-state pose.</item>
    ///   <item><c>menubars_sp2mm</c> end-frame Bone01 Y=-0.99 (slid down
    ///         into screen) with MenuBase1..5 at Z=0.37/0.67/0.98/1.29/1.59
    ///         (5 button rows). Z pitch ≈0.30u matches the 74-px row
    ///         pitch authored in <c>main_menu.gas</c>'s 5 button rects, so
    ///         <c>MainMenuPanel</c>'s hit-tests line up with the rendered
    ///         button rows by construction.</item>
    ///   <item><c>menubars_default</c> end-frame Bone01 Y=1.70 — parked
    ///         above the visible frame (the "before menu" pose). Used by
    ///         the splash sequence's IntroBink/IntroLogoDrop states; not
    ///         here.</item>
    /// </list>
    /// <para>All subsets render today. Text atlases (subsets 6-15) ship
    /// state-keyed labels — DS1's main menu labels live in some subset
    /// of them. Once the user eyeballs and reports which atlas rows hold
    /// "SINGLE PLAYER / MULTIPLAYER / OPTIONS / CONTINUE / ABOUT," we'll
    /// add a state-specific subset mask the way <c>DrawCharacterSelectState</c>
    /// masks out text-02 to keep DIFFICULTY from painting over CHOOSE
    /// HERO. Splinter SC-MAINMENU-MENUBARS-LABELS tracks the mask.</para></summary>
    private void DrawMainMenuState(int vw, int vh)
    {
        // Backdrop + side pillars: same poses as cd-state (these are the
        // always-on chrome that doesn't morph between menu screens).
        // Phase 25-CHROME-FOLD — rightside replaced by an X-mirrored
        // leftside.asp draw because rightside.asp ships as ASP v2.2 (the
        // others are v2.3) and the parser produces a stretched render
        // with the gear bones not animating. Mirroring leftside gives a
        // symmetric pair with the gear spinning the same on both sides.
        // Phase 25-CHROME-FOLD4 — leftside.asp ships shadows (subset 5)
        // that draws AFTER columns (subset 2) in asp subset order, so
        // alpha-over reads as a dark stripe over the pillars; mask off.
        // Phase 25-CHROME-FOLD6 — leftside.asp also ships doors-01
        // (subset 0) and doors-03 (subset 1) BEFORE columns (subset 2)
        // in the authored subset order, so the pillar columns paint
        // OVER the door panels — exactly the inverse of DS1's intended
        // depth (doors are static wood panels that sit IN FRONT of the
        // pillar, immediately to the left and right of each menu
        // button). DS1's render order is implicit-3D; ours is 2D draw-
        // order. Two-pass fix: leftsideBodyMask draws columns +
        // backgrounds (no doors, no shadows); leftsideDoorsMask draws
        // doors only on a second pass so they end up on top.
        var leftsideBodyMask  = new[] { false, false, true,  true,  true,  false };
        var leftsideDoorsMask = new[] { true,  true,  false, false, false, false };
        DrawMesh("backdrop",   "backdrop", clip: null,                hold: 0f, vw, vh);
        DrawMesh("leftside-body",  "leftside", clip: "leftside_default", hold: 0f, vw, vh, leftsideBodyMask);
        DrawMesh("rightside-body", "leftside", clip: "leftside_default", hold: 0f, vw, vh, leftsideBodyMask,  xMirror: true);
        // Doors second pass — draws AFTER mainmenu+menubars below would
        // also be valid (then doors would sit above the menu chrome)
        // but DS1's intent reads as "doors sit on the PILLAR, in front
        // of columns but behind the menu panel," so we draw doors
        // before the menu chrome and let the menu paint over them
        // where they overlap.
        DrawMesh("leftside-doors",  "leftside", clip: "leftside_default", hold: 0f, vw, vh, leftsideDoorsMask);
        DrawMesh("rightside-doors", "leftside", clip: "leftside_default", hold: 0f, vw, vh, leftsideDoorsMask, xMirror: true);

        // Phase 25-CHROME-FOLD2 — subset masks. mainmenu.asp ships 6
        // subsets: 0=chrome, 1+2=text-01L/R (SP-tree labels per atlas
        // dump: NEW GAME / SINGLE PLAYER / CHOOSE HERO / LOAD GAME /
        // OPTIONS), 3+4=text-02L/R (LOAD MAP top / blank middle /
        // DIFFICULTY + WARRIOR bottom), 5=shadows. The user reports
        // a title slot at the top of the inner panel — none of the
        // four text atlases obviously says "MAIN MENU", so we render
        // ALL of them and let the bone-Z visibility determine which
        // label lands in the slot at sp2mm's end pose. PanelBASE1+2
        // sit at Z=-0.85 (visible), PanelBASE3..10 at Z>=-0.49 (parked).
        // Shadows stays off (alpha-over of the drop-shadow layer reads
        // as a dark-tinted-glass overlay across the chrome).
        // Phase 25-CHROME-FOLD5 — masking out subsets 1-4 (the previous
        // "chrome only" approach) cleared the wrong-state corruption
        // but also erased whatever DS1 was painting in the title slot.
        // User flagged the regression: "we lost the Main Menu words at
        // the top." Re-enable both atlases; if either produces
        // wrong-state peek-through we can narrow down per-atlas later
        // (splinter SC-MAINMENU-TITLE-ATLAS).
        var mainmenuMask = new[] { true, true, true, true, true, false };
        // menubars.asp has 17 subsets: 0=chrome, 1-5=per-row button
        // chrome (the 5 wood buttons themselves), 6-15=per-row text
        // atlases (the actual button labels SINGLE PLAYER etc.),
        // 16=shadows. Keep everything except shadows (same dark-tint
        // bug) so the buttons + their labels render correctly. Per-state
        // text atlas masking (some subsets hold non-MainMenu labels)
        // is a future polish slice once we figure out which atlas row
        // is which state — for now all 10 atlases render and the
        // wrong-state ones happen to land off-screen via bone Z.
        var menubarsMask = new bool[17];
        for (int i = 0; i < 16; i++) menubarsMask[i] = true; // 16 stays false (shadow)
        // Inner panel: chrome + frame + text atlases (mainmenuMask kills
        // shadows only). Mirror-the-whole-chrome-subset approach didn't
        // work because the chrome subset's right side is opaque wood,
        // which either covers the mirror's right-hand or gets covered
        // by it depending on draw order. SC-MAINMENU-RIGHT-HAND splinter
        // tracks the proper fix: render a UV-cropped 2D quad sourced
        // from b_gui_fe_m_mn_3d_mainmenu.raw at the upper-right of the
        // menu, sampling just the left-hand region X-mirrored.
        DrawMesh("mainmenu",  "mainmenu",  clip: "mainmenu_sp2mm",    hold: 1f, vw, vh, mainmenuMask);
        // Menubars: 5 button rows + their text labels; shadow subset
        // killed by menubarsMask to drop the dark-tinted-glass overlay.
        DrawMesh("menubars",  "menubars",  clip: "menubars_sp2mm",    hold: 1f, vw, vh, menubarsMask);
        // logo / heromenu / backbutton intentionally NOT drawn at MainMenu
        // state. logo is splash-only (fades out via logo-exit.prs on the
        // splash → MainMenu transition; future SC-MAINMENU-LOGO-EXIT
        // splinter wires that 0.58s tail). heromenu + backbutton belong
        // to character-creator + sub-menu states.
    }

    /// <summary>Phase 27-SP-FLYOUT — Main → Single Player forward
    /// transition. Renders the always-on chrome (backdrop + sides) at
    /// rest while mainmenu_mm2sp / menubars_mm2sp animate the inner
    /// panel + button column from MainMenu to SP poses. End-frame of
    /// each clip matches <see cref="DrawSinglePlayerState"/>'s hold pose
    /// for a seamless settle.</summary>
    private void DrawMmToSpState(int vw, int vh)
    {
        DrawSpChrome(vw, vh, MmToSpTimeFraction, mmToSp: true);
    }

    /// <summary>Phase 27-SP-FLYOUT — SP submenu resting pose. Same as
    /// the end-frame of the mm2sp clips so the user reads it as the
    /// settled SP screen.</summary>
    private void DrawSinglePlayerState(int vw, int vh)
    {
        DrawSpChrome(vw, vh, hold: 1f, mmToSp: true);
    }

    /// <summary>Phase 27-SP-FLYOUT — Single Player → Main reverse
    /// transition (Back click). Plays sp2mm clips so the panel +
    /// button column unwind back to the MainMenu pose.</summary>
    private void DrawSpToMmState(int vw, int vh)
    {
        DrawSpChrome(vw, vh, SpToMmTimeFraction, mmToSp: false);
    }

    /// <summary>Phase 27-SP-FLYOUT — shared chrome assembly for the
    /// MainMenuToSp / SinglePlayer / SinglePlayerToMm states. <paramref name="mmToSp"/>
    /// picks the forward (mm2sp / e2b) vs. reverse (sp2mm / b2e) clip
    /// set; <paramref name="hold"/> is the 0..1 time-fraction along
    /// those clips.</summary>
    private void DrawSpChrome(int vw, int vh, float hold, bool mmToSp)
    {
        // Backdrop + sides: same masks as MainMenu state.
        var leftsideBodyMask  = new[] { false, false, true,  true,  true,  false };
        var leftsideDoorsMask = new[] { true,  true,  false, false, false, false };
        DrawMesh("backdrop",   "backdrop", clip: null,                hold: 0f, vw, vh);
        DrawMesh("leftside-body",  "leftside", clip: "leftside_default", hold: 0f, vw, vh, leftsideBodyMask);
        DrawMesh("rightside-body", "leftside", clip: "leftside_default", hold: 0f, vw, vh, leftsideBodyMask,  xMirror: true);
        DrawMesh("leftside-doors",  "leftside", clip: "leftside_default", hold: 0f, vw, vh, leftsideDoorsMask);
        DrawMesh("rightside-doors", "leftside", clip: "leftside_default", hold: 0f, vw, vh, leftsideDoorsMask, xMirror: true);

        // mainmenu text-01 (subsets 1+2) renders the "SINGLE PLAYER"
        // title at the panel header at SP state — leave enabled.
        // text-02 (subsets 3+4, MP-tree labels) and shadows masked off.
        var mainmenuMask = new[] { true, true, true, false, false, false };
        // Phase 27-SP-FLYOUT-FIX3 — SP state menubars: render ONLY the
        // panel chrome + per-row wood buttons (subsets 0-5). Mask off
        // text subsets (6-15) so the multi-bone text quads can't bleed
        // adjacent atlas rows onto slots 1+2. The actual button labels
        // (NEW GAME / LOAD GAME) come from per-button DrawMenubarsButton
        // calls in RenderHost using the art_mapping.gas[button_*]
        // recipe — exactly how DS1 renders these widgets.
        var menubarsMask = new bool[17];
        for (int i = 0; i < 6; i++) menubarsMask[i] = true; // chrome only

        var mainClip = mmToSp ? "mainmenu_mm2sp" : "mainmenu_sp2mm";
        var barsClip = mmToSp ? "menubars_mm2sp" : "menubars_sp2mm";
        DrawMesh("mainmenu",  "mainmenu",  clip: mainClip,  hold: hold, vw, vh, mainmenuMask);
        DrawMesh("menubars",  "menubars",  clip: barsClip,  hold: hold, vw, vh, menubarsMask);
        // Per-button text rendering is layered on top by the host (see
        // DrawMenubarsButton + DrawSpBackButton), one asp draw per
        // widget, exactly the way DS1's engine does it.
    }

    /// <summary>Phase 25-CHROME — sword rises out of the log via
    /// logo-exit.prs over the 0.58s exit window. Backdrop + sides hold;
    /// logo mesh animates upward via the exit clip. Once the clip
    /// completes, IntroMenuFlyIn takes over and the chrome drops in
    /// from above so the user never sees a frame without something
    /// posed.</summary>
    private void DrawLogoExitState(int vw, int vh)
    {
        var bodyMask  = new[] { false, false, true,  true,  true,  false };
        var doorsMask = new[] { true,  true,  false, false, false, false };
        DrawMesh("backdrop",  "backdrop",  clip: null,                hold: 0f, vw, vh);
        DrawMesh("leftside-body",  "leftside", clip: "leftside_default", hold: 0f, vw, vh, bodyMask);
        DrawMesh("rightside-body", "leftside", clip: "leftside_default", hold: 0f, vw, vh, bodyMask,  xMirror: true);
        DrawMesh("leftside-doors", "leftside", clip: "leftside_default", hold: 0f, vw, vh, doorsMask);
        DrawMesh("rightside-doors","leftside", clip: "leftside_default", hold: 0f, vw, vh, doorsMask, xMirror: true);
        DrawMesh("logo",      "logo",      clip: "logo-exit",         hold: LogoExitTimeFraction, vw, vh);
    }

    /// <summary>Phase 25-CHROME — main menu chrome flies in. mainmenu_flyin
    /// (1.6667s, drops Bone01 from Y=2.94 to Y=2.01) + menubars_flyin
    /// (drops menubars from above into screen) + leftside_flyin /
    /// rightside_flyin (gear flourishes) + backbutton_flyin run together.
    /// All driven by the same MenuFlyInTimeFraction so they stay synced.
    /// End pose matches MainMenu's rest pose, making the transition
    /// to MainMenu state visually seamless.</summary>
    private void DrawMenuFlyInState(int vw, int vh)
    {
        // Phase 25-CHROME-FOLD2 — same subset masks as DrawMainMenuState
        // so the tinted-glass shadow + wrong-state title atlases don't
        // pop in during the fly-in beat. End pose matches MainMenu's
        // resting state, so the masks have to match too for a seamless
        // transition.
        var mainmenuMask = new[] { true, true, true, true, true, false };
        var menubarsMask = new bool[17];
        for (int i = 0; i < 16; i++) menubarsMask[i] = true;
        var bodyMask  = new[] { false, false, true,  true,  true,  false };
        var doorsMask = new[] { true,  true,  false, false, false, false };
        DrawMesh("backdrop",  "backdrop",  clip: null,                 hold: 0f,                   vw, vh);
        DrawMesh("leftside-body",  "leftside", clip: "leftside_flyin", hold: MenuFlyInTimeFraction, vw, vh, bodyMask);
        DrawMesh("rightside-body", "leftside", clip: "leftside_flyin", hold: MenuFlyInTimeFraction, vw, vh, bodyMask,  xMirror: true);
        DrawMesh("leftside-doors", "leftside", clip: "leftside_flyin", hold: MenuFlyInTimeFraction, vw, vh, doorsMask);
        DrawMesh("rightside-doors","leftside", clip: "leftside_flyin", hold: MenuFlyInTimeFraction, vw, vh, doorsMask, xMirror: true);
        DrawMesh("mainmenu",  "mainmenu",  clip: "mainmenu_flyin",     hold: MenuFlyInTimeFraction, vw, vh, mainmenuMask);
        DrawMesh("menubars",  "menubars",  clip: "menubars_flyin",     hold: MenuFlyInTimeFraction, vw, vh, menubarsMask);
        // backbutton has its own flyin too — keep it in the assembly
        // even though MainMenu state itself doesn't draw the back button.
        // Wait — actually MainMenu DOES NOT show backbutton (no Previous/Next
        // nav at the menu top level). Skipping it on flyin keeps the
        // visual aligned with MainMenu's steady state.
    }

    /// <summary>Phase 24-MAINMENU step 4 — sword drop sequence. Backdrop +
    /// the two side pillars come up alongside the logo so the visual
    /// reads as "we're in the menu chrome, the title is dropping in"
    /// rather than the logo floating in space. logo.asp's logo-enter.prs
    /// drives the sword drop; LogoDropTimeFraction holds at 1.0 once
    /// the 2.17s clip completes so the rest of the 5s state window the
    /// title sits in place while the backdrop finishes ramping in.</summary>
    private void DrawLogoDropState(int vw, int vh)
    {
        var bodyMask  = new[] { false, false, true,  true,  true,  false };
        var doorsMask = new[] { true,  true,  false, false, false, false };
        DrawMesh("backdrop",   "backdrop",  clip: null,                hold: 0f, vw, vh);
        DrawMesh("leftside-body",  "leftside", clip: "leftside_default", hold: 0f, vw, vh, bodyMask);
        DrawMesh("rightside-body", "leftside", clip: "leftside_default", hold: 0f, vw, vh, bodyMask,  xMirror: true);
        DrawMesh("leftside-doors", "leftside", clip: "leftside_default", hold: 0f, vw, vh, doorsMask);
        DrawMesh("rightside-doors","leftside", clip: "leftside_default", hold: 0f, vw, vh, doorsMask, xMirror: true);
        // logo-enter is the only logo clip we render here; logo-exit
        // fires when leaving the splash state on entry to MainMenu —
        // for now we cut from logo-end-pose to no-logo-at-all when
        // MainMenu takes over (logo.asp isn't part of the main menu
        // chrome per DrawCharacterSelectState's commentary).
        DrawMesh("logo",       "logo",      clip: "logo-enter",        hold: LogoDropTimeFraction, vw, vh);
    }

    /// <summary>Phase 28-CD-FLYOUT — SP → Character Creator forward
    /// transition. mainmenu_sng2cd / menubars_lm2cd / backbutton_b2pn /
    /// heromenu_begin run together.</summary>
    private void DrawSpToCdState(int vw, int vh)
        => DrawCdChrome(vw, vh, SpToCdTimeFraction, fromSp: true);

    /// <summary>Phase 28-CD-FLYOUT — settled cd pose. Same as the
    /// end-frame of the sng2cd / lm2cd / b2pn clips so the user reads
    /// it as the settled CharacterSelect screen.</summary>
    private void DrawCharacterSelectState(int vw, int vh)
        => DrawCdChrome(vw, vh, hold: 1f, fromSp: true);

    /// <summary>Phase 28-CD-FLYOUT — Character Creator → SP reverse
    /// transition (Previous click). Plays cd2sng / cd2lm / pn2b clips
    /// so the panel + button column unwind back to the SP submenu pose.</summary>
    private void DrawCdToSpState(int vw, int vh)
        => DrawCdChrome(vw, vh, CdToSpTimeFraction, fromSp: false);

    /// <summary>Phase 28-CD-FLYOUT — shared chrome assembly for the
    /// SinglePlayerToCd / CharacterSelect / CharacterSelectToSp states.
    /// Mirrors <see cref="DrawSpChrome"/>: <paramref name="fromSp"/>
    /// picks the forward (sng2cd / lm2cd / b2pn) vs. reverse (cd2sng /
    /// cd2lm / pn2b) clip set; <paramref name="hold"/> is the 0..1
    /// time-fraction along those clips.</summary>
    private void DrawCdChrome(int vw, int vh, float hold, bool fromSp)
    {
        // Phase 29-CD-CREATOR-FIX5 — split leftside into a shadows-first
        // pass and a body+doors pass so the translucent shadow strip
        // (subset 5, b_gui_fe_m_mn_3d_shadows) doesn't paint on top of
        // the prev/next buttons + heromenu chrome. leftside.asp authors
        // shadows AS THE LAST SUBSET, so a single all-in draw renders
        // them last (on top). Doing them in their own pass before the
        // pillars lets later UI layers (backbutton, heromenu) sit clean.
        var leftsideShadowMask = new[] { false, false, false, false, false, true  };
        var leftsidePillarMask = new[] { true,  true,  true,  true,  true,  false };
        DrawMesh("backdrop",        "backdrop", clip: null,                hold: 0f, vw, vh);
        DrawMesh("leftside-shadow", "leftside", clip: "leftside_default",  hold: 0f, vw, vh, leftsideShadowMask);
        // SC-CD-RIGHTSIDE Phase B — state-driven rightside clip. During the
        // SinglePlayerToCd transition, rightside_open plays as a peer to
        // mainmenu_sng2cd / menubars_lm2cd / backbutton_b2pn / heromenu_begin
        // (all run at the same `hold` fraction over SpToCdDur). The door
        // bone slides X 0.009 → 1.613 in mesh units alongside gear rotations
        // and piston extends, revealing the stone backdrop + 3D char rect.
        // CharacterSelect holds the open end-pose at hold=1f. The reverse
        // CharacterSelectToSp uses the dedicated rightside_close clip
        // (its own authored easing), NOT 1-hold of open.
        string rightsideClip;
        float  rightsideHold;
        if (State == ScreenState.CharacterSelect)
        {
            rightsideClip = "rightside_open";
            rightsideHold = 1f;
        }
        else if (fromSp)
        {
            rightsideClip = "rightside_open";
            rightsideHold = hold;
        }
        else
        {
            rightsideClip = "rightside_close";
            rightsideHold = hold;
        }
        DrawMesh("rightside-shadow","rightside", clip: rightsideClip, hold: rightsideHold, vw, vh, leftsideShadowMask);
        DrawMesh("leftside",        "leftside",  clip: "leftside_default",  hold: 0f,           vw, vh, leftsidePillarMask);
        DrawMesh("rightside",       "rightside", clip: rightsideClip, hold: rightsideHold, vw, vh, leftsidePillarMask);

        // SC-CD-CHOOSEHERO — mainmenu.asp draw at sng2cd@hold for the
        // title scroll plaque + CHOOSE HERO engraved labels. Subsets
        // 0+1+2 = chrome plate + text-01L + text-01R; mask drops
        // text-02L/R (DIFFICULTY) and shadows.
        var mainmenuMask = new[] { true, true, true, false, false, false };
        var mainClip = fromSp ? "mainmenu_sng2cd" : "mainmenu_cd2sng";
        DrawMesh("mainmenu", "mainmenu", clip: mainClip, hold: hold, vw, vh, mainmenuMask);
        // backbutton uses ac/b/e/pn state codes. Character_select shows
        // the Previous/Next button pair (pn). End of b2pn = pn pose
        // (same destination as ac2pn — which the pre-Phase-28 code used
        // — but b2pn starts from BACK pose so it picks up smoothly from
        // the SP submenu's e2b@1 end-pose without a visual pop).
        var backClip = fromSp ? "backbutton_b2pn" : "backbutton_pn2b";
        // Phase 29-CD-CREATOR-FIX6 — drop backbutton subset 10 (shadows
        // atlas). At pn pose its quad lands across the hero-name plate
        // and the previous/next pair, painting a translucent grey haze
        // over both. backbutton.asp authored that shadow for an earlier
        // pose where it sat beneath the chrome; in pn pose it's just in
        // the way. The pillar-shadow tier already covers the bottom
        // chrome's drop shadow, so we lose nothing visually here.
        var backbuttonMask = new bool[11];
        for (int i = 0; i < 10; i++) backbuttonMask[i] = true;
        DrawMesh("backbutton", "backbutton",        clip: backClip, hold: hold, vw, vh, backbuttonMask);

        // heromenu — fly in during forward transition, settled at
        // begin@1.0 (cd-state pose) when held, fly out during reverse.
        // Phase 29-CD-CREATOR-FIX: settled state uses heromenu_begin@1.0
        // not heromenu_default — heromenu_default is bind-pose where
        // the chrome plate (subset 0) and label strip (subset 14)
        // project off the top of the viewport. begin@1.0 drops the
        // column into the visible spinner area where it belongs.
        if (State == ScreenState.CharacterSelect)
        {
            DrawMesh("heromenu", "heromenu", clip: "heromenu_begin", hold: 1f, vw, vh);
        }
        else if (fromSp)
        {
            DrawMesh("heromenu", "heromenu", clip: "heromenu_begin", hold: hold, vw, vh);
        }
        else
        {
            DrawMesh("heromenu", "heromenu", clip: "heromenu_begin", hold: 1f - hold, vw, vh);
        }
    }

    /// <summary>SC-DIFF Phase B — Difficulty screen chrome. Mirrors
    /// DrawCdChrome but with text-02 (DIFFICULTY) title instead of
    /// text-01 (CHOOSE HERO), 3-button menubars (EASY/MEDIUM/HARD) at
    /// lm2cd@1.0 pose instead of heromenu spinners, and a single BACK
    /// button instead of the prev/next pair. Backdrop + pillars +
    /// rightside slide stay open (3D char preview still showing through
    /// at this state). <paramref name="fromCd"/> picks forward (cd→diff)
    /// vs reverse (diff→cd) clip set.</summary>
    private void DrawDifficultyChrome(int vw, int vh, float hold, bool fromCd)
    {
        var leftsideShadowMask = new[] { false, false, false, false, false, true  };
        var leftsidePillarMask = new[] { true,  true,  true,  true,  true,  false };
        DrawMesh("backdrop",        "backdrop", clip: null,                hold: 0f, vw, vh);
        DrawMesh("leftside-shadow", "leftside", clip: "leftside_default",  hold: 0f, vw, vh, leftsideShadowMask);
        // Right pillar held open at the cd-state end pose so the 3D
        // char + stone backdrop stay visible behind the difficulty
        // buttons (DS1's actual difficulty screen does the same).
        DrawMesh("rightside-shadow","rightside", clip: "rightside_open",   hold: 1f, vw, vh, leftsideShadowMask);
        DrawMesh("leftside",        "leftside",  clip: "leftside_default", hold: 0f, vw, vh, leftsidePillarMask);
        DrawMesh("rightside",       "rightside", clip: "rightside_open",   hold: 1f, vw, vh, leftsidePillarMask);

        // Title chrome: same mainmenu.asp at sng2cd@1.0 (the chrome
        // settles at the cd pose and STAYS there for difficulty —
        // only the visible Z slot changes the title row from text-01
        // to text-02 implicitly via the bone-Z swap that's already
        // in the clip end-pose). Mask {0, 3, 4} = chrome plate +
        // text-02L "DIFFI" + text-02R "CULTY" instead of text-01
        // CHOOSE / HERO. text-01 (subsets 1, 2) and shadows masked off.
        var mainmenuMask = new[] { true, false, false, true, true, false };
        DrawMesh("mainmenu", "mainmenu", clip: "mainmenu_sng2cd", hold: 1f, vw, vh, mainmenuMask);

        // Menubars chrome at lm2cd@1.0 pose. art_mapping.gas:
        //   button_easy   = subsets 3 + 10 + 11 (chrome + text labels)
        //   button_medium = subsets 4 + 12 + 13
        //   button_hard   = subsets 5 + 14 + 15
        // Mask all three buttons' chrome + text subsets.
        var menubarsMask = new bool[17];
        // Chrome subsets (button bodies)
        menubarsMask[3] = true; menubarsMask[4] = true; menubarsMask[5] = true;
        // Text subsets (DS1 labels for EASY/MEDIUM/HARD via text-menubars1+3)
        menubarsMask[10] = true; menubarsMask[11] = true;
        menubarsMask[12] = true; menubarsMask[13] = true;
        menubarsMask[14] = true; menubarsMask[15] = true;
        DrawMesh("menubars", "menubars", clip: "menubars_lm2cd", hold: 1f, vw, vh, menubarsMask);

        // BACK button only (no Next pair). Use backbutton_pn2b on the
        // forward path so the prev/next from cd state unwinds to a
        // single BACK at the difficulty pose. b2pn on reverse goes
        // back to the prev/next pair.
        var backClip = fromCd ? "backbutton_pn2b" : "backbutton_b2pn";
        var backbuttonMask = new bool[11];
        for (int i = 0; i < 10; i++) backbuttonMask[i] = true;
        DrawMesh("backbutton", "backbutton", clip: backClip, hold: hold, vw, vh, backbuttonMask);
    }

    /// <param name="hold">Time-fraction to evaluate the clip at: 0=start of clip,
    /// 1=end of clip, -1=looped real-time idle (for ambient default clips).</param>
    private void DrawMesh(string meshKey, string meshSuffix, string? clip, float hold, int vw, int vh, bool[]? subsetMask = null, bool xMirror = false)
    {
        var renderer = GetOrLoadMesh(meshSuffix);
        if (renderer is null) return;

        var anim = clip is null ? null : GetOrLoadClip(clip);
        float timeSec;
        if (anim is null)
        {
            timeSec = 0f;
        }
        else if (hold < 0f)
        {
            // Loop the clip with real time.
            var len = anim.AnimLength > 0f ? anim.AnimLength : 1f;
            timeSec = _timeSec - MathF.Floor(_timeSec / len) * len;
        }
        else
        {
            timeSec = anim.AnimLength * hold;
        }

        var textures = ResolveTexturesFor(renderer);
        // Use the SHARED frontend-space projection so all 8 meshes layer
        // coherently inside backdrop's box. Per-mesh DrawAt would re-center
        // and re-scale each mesh to fill the viewport independently —
        // exactly the wrong thing for a multi-mesh authored scene.
        var model = BuildSharedSceneModel(vw, vh);
        // Phase 25-CHROME-FOLD — xMirror flips the mesh across the world
        // Y-axis (X=0). Used to render leftside.asp again on the right
        // half so the gear-pillar animation is symmetric: rightside.asp
        // ships as ASP v2.2 (everything else is v2.3) and the parser drift
        // produces a stretched render with no gear animation. Mesh-space
        // mirror prepended so the flip happens before the centering /
        // scaling math, producing a clean right-side reflection.
        if (xMirror)
        {
            model = Matrix4x4.CreateScale(-1f, 1f, 1f) * model;
            // Phase 25-CHROME-FOLD3 — when X is negated, triangle winding
            // flips CCW → CW. If GL_CULL_FACE is enabled upstream with
            // FrontFace=CCW (the default), every mirrored triangle gets
            // culled as a back-face — which is why the user saw only the
            // gear (rendered double-sided by authored geometry) on the
            // right side; the pillar body got eaten by culling. Force
            // cull off for this draw + restore after.
            _gl.GetInteger(GLEnum.CullFace, out int wasCull);
            _gl.Disable(EnableCap.CullFace);
            renderer.DrawWithModel(vw, vh, model, textures, anim, timeSec, tint: null, subsetMask: subsetMask);
            if (wasCull != 0) _gl.Enable(EnableCap.CullFace);
            return;
        }
        renderer.DrawWithModel(vw, vh, model, textures, anim, timeSec, tint: null, subsetMask: subsetMask);
    }

    /// <summary>Builds the shared mesh-space → screen-pixel matrix used for
    /// every frontend mesh. Maps the backdrop-derived reference rect onto
    /// a 4:3 letterboxed area inside the viewport (DS1's frontend is
    /// authored 4:3 so widescreen displays bar the sides rather than
    /// stretch the chrome). Y is negated since mesh space is +Y up but
    /// screen space is +Y down.</summary>
    private Matrix4x4 BuildSharedSceneModel(int vw, int vh)
    {
        EnsureReference();
        float refW = MathF.Max(1e-4f, _refMaxX - _refMinX);
        float refH = MathF.Max(1e-4f, _refMaxY - _refMinY);
        float refCx = 0.5f * (_refMinX + _refMaxX);
        float refCy = 0.5f * (_refMinY + _refMaxY);

        // Letterbox the reference-rect's own aspect (typically ≈ 4.34/3.28
        // ≈ 1.32, very close to 4:3) so the chrome keeps its authored
        // proportions on widescreen displays. Using the rect's own ratio
        // rather than a hardcoded 4/3 keeps the gear pillars uncropped.
        float targetAspect = refW / refH;
        float targetW, targetH;
        float vpAspect = vw / (float)vh;
        if (vpAspect > targetAspect)
        {
            targetH = vh;
            targetW = vh * targetAspect;
        }
        else
        {
            targetW = vw;
            targetH = vw / targetAspect;
        }

        // Phase 26-VIEWPORT — overscan zoom (split X / Y). DS1 was authored
        // expecting the viewport to crop the outer fringe of the chrome,
        // and the right ratio differs per axis: horizontal pillars only
        // need the outer ~25% trimmed, but vertical chrome (mainmenu /
        // menubars at bind-pose Y up to 5.18) extends much further past
        // the backdrop frame and needs more aggressive vertical cropping.
        // User-tuned: 1.30 X (pillar outer ~25% gone, backdrop edges
        // visible) + 1.45 Y (top/bottom of mesh garbage that DS1 expected
        // the framebuffer to crop is now actually clipped).
        const float overscanX = 1.30f;
        const float overscanY = 1.30f;
        float sx = (targetW / refW) * overscanX;
        float sy = (targetH / refH) * overscanY;
        float tx = vw * 0.5f;
        float ty = vh * 0.5f;

        var t1 = Matrix4x4.CreateTranslation(-refCx, -refCy, 0f);
        var s  = Matrix4x4.CreateScale(sx, -sy, 1f);
        var t2 = Matrix4x4.CreateTranslation(tx, ty, 0f);
        return t1 * s * t2;
    }

    /// <summary>One-shot probe of the frontend reference rect. Use backdrop
    /// alone — its bounds (≈ ±1.64 X, ±1.64 Y) define the visible-screen
    /// frame. Other meshes (mainmenu Y[1.76, 5.18], menubars Y[1.79, 4.36],
    /// leftside X to ±2.17) sit OUTSIDE backdrop in bind pose; they animate
    /// INTO the backdrop frame at their cd-state PRS pose (e.g. mainmenu
    /// Bone01 drops Y=2.94→2.01 at sng2cd hold=1, so PanelBASE3's row lands
    /// at Y=1.16 — inside backdrop's Y range). Letting the union dictate the
    /// reference rect kept all meshes in-frame at every PRS pose, but flipped
    /// the projection aspect (refH 3.28→6.82) which letterboxed the whole
    /// scene into a tall thin strip. Use backdrop's frame; trust PRS to put
    /// the inner panels where they belong. Out-of-frame leftside X-extents
    /// (the gear pillars) are intentional in DS1 too — they're authored just
    /// outside the visible 4:3 frame and only the inner edge protrudes.</summary>
    private void EnsureReference()
    {
        if (_refResolved) return;
        var backdrop = GetOrLoadMesh("backdrop");
        if (backdrop is null) return;
        _refMinX = backdrop.MeshMin.X;
        _refMaxX = backdrop.MeshMax.X;
        _refMinY = backdrop.MeshMin.Y;
        _refMaxY = backdrop.MeshMax.Y;
        _refResolved = true;
    }

    private UiMeshRenderer? GetOrLoadMesh(string meshSuffix)
    {
        if (_renderers.TryGetValue(meshSuffix, out var r)) return r;
        var basename = $"m_gui_fe_m_mn_3d_{meshSuffix}.asp";
        if (!_resolver.TryLoadByBasename(basename, out var bytes))
            return null;
        var asp = AspMesh.Load(bytes);
        // UiMeshRenderer demands HasSkin (DS1 frontend meshes are all rigged).
        // logo.asp is version 2.5 with 4 bones — should still pass.
        if (!asp.HasSkin)
            return null;
        var rr = new UiMeshRenderer(_gl, asp);
        _renderers[meshSuffix] = rr;
        return rr;
    }

    private PrsAnimation? GetOrLoadClip(string clipSuffix)
    {
        if (_clips.TryGetValue(clipSuffix, out var cached)) return cached;
        var basename = $"a_gui_fe_m_mn_3d_{clipSuffix}.prs";
        if (!_resolver.TryLoadByBasename(basename, out var bytes))
        {
            _clips[clipSuffix] = null;
            return null;
        }
        try
        {
            var anim = PrsAnimation.Load(bytes);
            _clips[clipSuffix] = anim;
            return anim;
        }
        catch
        {
            _clips[clipSuffix] = null;
            return null;
        }
    }

    private GlTexture?[] ResolveTexturesFor(UiMeshRenderer renderer)
    {
        var names = renderer.Asp.TextureNames;
        var arr = new GlTexture?[names.Count];
        for (int i = 0; i < names.Count; i++)
            arr[i] = GetOrLoadTexture(names[i]);
        return arr;
    }

    // GetMenubarsButtonScreenRect / GetExitButtonScreenRect /
    // GetTitleSlotScreenRect were attempted projection-aware rect
    // helpers; reverted because the trace-pose-derived mesh values
    // didn't match my axis assumptions and broke hit-testing. Panels
    // use their gas-authored rects directly.
    private bool GetMenubarsButtonScreenRect_unused(int slotIdx, int viewportW, int viewportH,
                                                    out int x, out int y, out int w, out int h)
    {
        x = y = w = h = 0;
        // Slot mesh-space rects per state, sourced from
        // `siegefx asp trace-pose menubars.asp <clip> 1.0`
        // for subsets 1-5 (per-row chrome bboxes). Mesh +Y projects to
        // smaller screen Y (top of screen) since BuildSharedSceneModel
        // negates Y. So at MM state the TOP visible button (slot 0) is
        // the one with the LARGEST mesh Y (subset 1 = MenuLogBASE5).
        // First-attempt code had slot 0 → subset 5 (MenuLogBASE1, mesh
        // Y ≈ -0.6) which projected to the BOTTOM of the screen and
        // broke hit-testing on Single Player at MM state.
        bool inSp = State == ScreenState.SinglePlayer
                 || State == ScreenState.MainMenuToSp
                 || State == ScreenState.SinglePlayerToMm;
        (float mxMin, float myMin, float mxMax, float myMax)? rect = null;
        if (inSp)
        {
            // SP state: top 2 visible buttons are MenuLogBASE2 (chrome
            // subset 4, mesh Y[0.016, 0.272]) and MenuLogBASE1 (subset 5,
            // mesh Y[-0.300, -0.044]). MenuLogBASE3..5 are parked
            // off-top of viewport at this state pose.
            rect = slotIdx switch
            {
                0 => (-0.568f,  0.016f, 0.558f,  0.272f),  // top: NEW GAME
                1 => (-0.568f, -0.300f, 0.558f, -0.044f),  // bottom: LOAD GAME
                _ => null,
            };
        }
        else
        {
            // MM state: 5 visible buttons. MenuLogBASE5 (subset 1) is
            // the TOP slot, MenuLogBASE1 (subset 5) is the BOTTOM.
            rect = slotIdx switch
            {
                0 => (-0.568f,  0.477f, 0.558f,  0.733f),  // top:    SINGLE PLAYER
                1 => (-0.568f,  0.169f, 0.558f,  0.425f),  //         MULTIPLAYER
                2 => (-0.568f, -0.137f, 0.558f,  0.119f),  //         OPTIONS
                3 => (-0.568f, -0.445f, 0.558f, -0.190f),  //         CONTINUE
                4 => (-0.568f, -0.754f, 0.558f, -0.498f),  // bottom: ABOUT
                _ => null,
            };
        }
        if (rect is null) return false;
        var r = rect.Value;
        EnsureReference();
        var model = BuildSharedSceneModel(viewportW, viewportH);
        var p0 = Vector4.Transform(new Vector4(r.mxMin, r.myMin, 0f, 1f), model);
        var p1 = Vector4.Transform(new Vector4(r.mxMax, r.myMax, 0f, 1f), model);
        int sx0 = (int)MathF.Round(MathF.Min(p0.X, p1.X));
        int sx1 = (int)MathF.Round(MathF.Max(p0.X, p1.X));
        int sy0 = (int)MathF.Round(MathF.Min(p0.Y, p1.Y));
        int sy1 = (int)MathF.Round(MathF.Max(p0.Y, p1.Y));
        x = sx0;
        y = sy0;
        w = Math.Max(1, sx1 - sx0);
        h = Math.Max(1, sy1 - sy0);
        return true;
    }

    /// <summary>Phase 27-SP-FLYOUT-FIX2 — companion to
    /// <see cref="GetMenubarsButtonScreenRect"/> for the EXIT/BACK button.
    /// Same projection-aware computation, returning the actual screen
    /// rect of backbutton.asp's NameBase wood-button chrome (subset 0+5).
    /// Trace-pose-derived: at bind pose (the only pose DrawExitButton
    /// uses today; e2b clip in BACK swaps internal Z but not the outer
    /// rect), NameBase chrome spans roughly mesh-space (-0.7..0.7) X
    /// and (-0.4..0.0) Y above the menubars panel. Empirically tuned to
    /// match the existing user-tuned EXIT rect (359,492 W=79 H=46 in
    /// 800x600 ref space) so the visual position carries over.</summary>
    private bool GetTitleSlotScreenRect_unused(int viewportW, int viewportH,
                                               out int x, out int y, out int w, out int h)
    {
        EnsureReference();
        var model = BuildSharedSceneModel(viewportW, viewportH);
        var p0 = Vector4.Transform(new Vector4(-0.60f, 1.05f, 0f, 1f), model);
        var p1 = Vector4.Transform(new Vector4( 0.60f, 1.30f, 0f, 1f), model);
        int sx0 = (int)MathF.Round(MathF.Min(p0.X, p1.X));
        int sx1 = (int)MathF.Round(MathF.Max(p0.X, p1.X));
        int sy0 = (int)MathF.Round(MathF.Min(p0.Y, p1.Y));
        int sy1 = (int)MathF.Round(MathF.Max(p0.Y, p1.Y));
        x = sx0;
        y = sy0;
        w = Math.Max(1, sx1 - sx0);
        h = Math.Max(1, sy1 - sy0);
        return true;
    }

    private bool GetExitButtonScreenRect_unused(int viewportW, int viewportH,
                                                out int x, out int y, out int w, out int h)
    {
        EnsureReference();
        var model = BuildSharedSceneModel(viewportW, viewportH);
        var p0 = Vector4.Transform(new Vector4(-0.30f, 0.82f, 0f, 1f), model);
        var p1 = Vector4.Transform(new Vector4( 0.30f, 1.05f, 0f, 1f), model);
        int sx0 = (int)MathF.Round(MathF.Min(p0.X, p1.X));
        int sx1 = (int)MathF.Round(MathF.Max(p0.X, p1.X));
        int sy0 = (int)MathF.Round(MathF.Min(p0.Y, p1.Y));
        int sy1 = (int)MathF.Round(MathF.Max(p0.Y, p1.Y));
        x = sx0;
        y = sy0;
        w = Math.Max(1, sx1 - sx0);
        h = Math.Max(1, sy1 - sy0);
        return true;
    }

    /// <summary>Phase 26-ARTMAP — render the EXIT button using DS1's actual
    /// asset chain: render <c>m_gui_fe_m_mn_3d_backbutton.asp</c> (the
    /// mesh family that owns the EXIT button visual) into the supplied
    /// 2D screen rect with per-subset textures bound per art_mapping.gas's
    /// [button_exit] swap recipe for the current mouse state. The mesh's
    /// vertex UVs already point at the right atlas regions; we just
    /// supply the right .raw to each subset and the engine renders the
    /// authored result. See <c>reference_ds1_art_mapping.md</c> for the
    /// full schema.</summary>
    public void DrawExitButton(int viewportW, int viewportH,
                               int x, int y, int w, int h,
                               bool hovered, bool pressed,
                               bool drawLabel = true)
    {
        var renderer = GetOrLoadMesh("backbutton");
        if (renderer is null) return;
        var names = renderer.Asp.TextureNames;
        var arr = new GlTexture?[names.Count];
        var mask = new bool[names.Count];

        // art_mapping.gas[button_exit] override recipe (hardcoded for
        // now; a generic art_mapping.gas parser is the broader Phase 26
        // scope). Keys are 1-based asp subset indices matching the
        // -mapN suffix; values are the texture base name (engine
        // prepends b_gui_fe_m_mn_3d_ and appends .raw).
        //
        //   [mouseover]  { 3 = exitback-up;   8 = text-small-up;   }
        //   [mousedown]  { 3 = exitback-down; 8 = text-small-down; }
        //   [mouseout]   { 3 = exitback;      8 = text-small;      }
        string exitTex = pressed ? "exitback-down" : hovered ? "exitback-up" : "exitback";
        string textTex = pressed ? "text-small-down" : hovered ? "text-small-up" : "text-small";

        // Phase 26-ARTMAP-FIX — keys in art_mapping.gas are DIRECT
        // 0-based subset indices, NOT -mapN suffix matches. Cross-
        // verified across multiple widget entries:
        //   button_next:     1 + 7   → subset[1] (arrow) + subset[7] (NEXT text)
        //   button_previous: 2 + 6   → subset[2] + subset[6] (PREV text)
        //   button_exit:     3 + 8   → subset[3] (EXIT deco) + subset[8] (EXIT text)
        //   button_continue: 4+12+13 → subset[4] (4th menubar row) + subset[12]+[13] (text)
        // Subset[3]'s UV in backbutton.asp samples the EXIT-decoration
        // region of exitback*.raw; subset[8]'s UV samples the EXIT/BACK
        // row of text-small*.raw (visual bottom — V[0.795, 1.002]).
        // Default fill the texture array so the rest of the asp's slots
        // have something bound (won't render due to mask=false anyway).
        for (int i = 0; i < names.Count; i++)
            arr[i] = GetOrLoadTexture(names[i]);
        // Activate subsets per art_mapping.gas + decorative chrome:
        // [0] = "exitback" base (EXIT button chrome — the wood-button shape)
        // [1] = NEXT arrow geometry (UV sample = horizontal arrow shape;
        //       visually behind the button as a decorative right-pointer)
        // [2] = PREVIOUS arrow geometry (mirror of [1] — left-pointer;
        //       same UV sample, different vertex positions in the asp)
        // [3] = EXIT-specific decoration (per art_mapping key 3)
        // [8] = EXIT text from text-small atlas (per art_mapping key 8)
        // SKIP [5] — that's the HERO NAME plate / name_edit_box backdrop,
        //         confirmed by the user's retest after we enabled it.
        if (0 < arr.Length) { arr[0] = GetOrLoadTextureBase(exitTex); mask[0] = true; }
        if (1 < arr.Length) { arr[1] = GetOrLoadTextureBase(exitTex); mask[1] = true; }
        if (2 < arr.Length) { arr[2] = GetOrLoadTextureBase(exitTex); mask[2] = true; }
        if (3 < arr.Length) { arr[3] = GetOrLoadTextureBase(exitTex); mask[3] = true; }
        // Subset 8 = EXIT text from text-small atlas. Skipped when the
        // caller wants to overlay a different label (e.g. BACK in SP
        // state) — see DrawBackButton.
        if (drawLabel && 8 < arr.Length) { arr[8] = GetOrLoadTextureBase(textTex); mask[8] = true; }

        // Phase 26-ARTMAP — compute tight bounds from active subsets and
        // map them to a VISUAL rect that's wider than the hit-test rect.
        // The decorative arrow subsets [1] + [2] make the geometry's
        // natural aspect ratio wide; squeezing them into the user-tuned
        // 79×46 hit-test rect scrunches everything horizontally.
        // 3.0× width / 1.6× height, centered on the rect midpoint, gives
        // the arrows room to breathe while keeping the EXIT button
        // visually anchored at the same spot the user tuned.
        const float visualWMul = 5.0f;
        const float visualHMul = 2.0f;
        int vw = (int)(w * visualWMul);
        int vh = (int)(h * visualHMul);
        int vx = x + (w - vw) / 2;
        int vy = y + (h - vh) / 2;
        var (subMin, subMax) = ComputeSubsetBounds2D(renderer.Asp, mask);
        var model = BuildSubsetRectModel(vx, vy, vw, vh, subMin, subMax);
        renderer.DrawWithModel(viewportW, viewportH, model, arr,
            anim: null, timeSec: 0f, tint: null, subsetMask: mask);
    }

    /// <summary>Phase 27-SP-FLYOUT-FIX3 — render one menubars-button
    /// widget (button_start_new_game / button_load_game) using the
    /// art_mapping.gas recipe, exactly the way DS1's engine does and
    /// the way <see cref="DrawExitButton"/> already handles button_exit.
    /// Each widget renders as ONE asp draw with ONLY its own subsets
    /// active — adjacent atlas regions can't bleed because their
    /// subsets are masked off. The asp's vertex UVs already point at
    /// the right atlas regions; we just bind the right .raw to each
    /// subset per the gas-specified mouseover/mouseout/mousedown
    /// recipe and the engine renders the authored result.
    ///
    /// <para>Recipes from /ui/config/art_mapping/art_mapping.gas:</para>
    /// <list type="bullet">
    ///   <item><c>button_start_new_game</c>: subsets 4 / 12 / 13 →
    ///         menubars / text-menubars1 / text-menubars2</item>
    ///   <item><c>button_load_game</c>: subsets 5 / 14 / 15 → same
    ///         atlas triplet pattern</item>
    /// </list>
    ///
    /// <para>Each recipe swaps to <c>-up</c> / <c>-down</c> variants
    /// per mouse state.</para></summary>
    public void DrawMenubarsButton(int viewportW, int viewportH,
                                   int x, int y, int w, int h,
                                   bool hovered, bool pressed,
                                   string widgetName)
    {
        int chromeSubset, text1Subset;
        switch (widgetName)
        {
            case "button_start_new_game":
                chromeSubset = 4; text1Subset = 12;
                break;
            case "button_load_game":
                chromeSubset = 5; text1Subset = 14;
                break;
            default:
                return;
        }

        var renderer = GetOrLoadMesh("menubars");
        if (renderer is null) return;
        var names = renderer.Asp.TextureNames;
        var arr = new GlTexture?[names.Count];
        var mask = new bool[names.Count];

        string menubarsTex = pressed ? "menubars-down" : hovered ? "menubars-up" : "menubars";
        string text1Tex    = pressed ? "text-menubars1-down" : hovered ? "text-menubars1-up" : "text-menubars1";

        // Default-fill: every asp slot gets its authored default so any
        // un-masked subset has SOMETHING bound (mask=false → won't render
        // anyway, but renderer reads textures unconditionally).
        for (int i = 0; i < names.Count; i++)
            arr[i] = GetOrLoadTexture(names[i]);

        // art_mapping.gas[button_start_new_game] / [button_load_game]
        // both list THREE subsets per state — chrome (4 or 5),
        // text-menubars1 (12 or 14), text-menubars2 (13 or 15). But
        // text-menubars2.raw on disk visibly contains the MP-screen
        // labels (NETWORK / INTERNET / etc.), and binding it on top of
        // text-menubars1 paints "NETWORK" over the LOAD GAME label.
        // text-menubars1 alone produces the correct full SP label
        // (verified visually — START NEW GAME reads cleanly underneath
        // when text-menubars2 isn't drawn over it). Skipping the
        // text-menubars2 subset until we understand why DS1's engine
        // doesn't surface its content here (open splinter — likely a
        // state-aware texture-name resolver in the engine that we
        // don't replicate yet).
        if (chromeSubset < arr.Length) { arr[chromeSubset] = GetOrLoadTextureBase(menubarsTex); mask[chromeSubset] = true; }
        if (text1Subset  < arr.Length) { arr[text1Subset]  = GetOrLoadTextureBase(text1Tex);    mask[text1Subset]  = true; }

        // Use the chrome's shared scene projection (same as the
        // full-menubars DrawMesh path), NOT the bind-pose bbox-to-rect
        // mapping DrawExitButton uses. The menubars_mm2sp PRS clip
        // moves bones FAR from bind pose to position the SP atlas
        // rows at slots 1+2 — that motion is large enough that the
        // bind-pose-bbox approach pushes skinned vertices outside the
        // screen rect (no text visible). Chrome projection lets the
        // bones land naturally at their SP-pose mesh positions, which
        // project through backdrop reference into slots 1+2 the same
        // way the full menubars draw already does. The (x,y,w,h)
        // params stay for hit-testing only — visual placement is
        // entirely bone-driven now.
        var model = BuildSharedSceneModel(viewportW, viewportH);
        var anim = GetOrLoadClip("menubars_mm2sp");
        float timeSec = anim is not null ? anim.AnimLength * 1f : 0f;
        renderer.DrawWithModel(viewportW, viewportH, model, arr,
            anim: anim, timeSec: timeSec, tint: null, subsetMask: mask);
    }

    /// <summary>Phase 27-SP-FLYOUT-FIX3 — render the SP-state Back button
    /// (widget <c>button_sp_back</c>). Different from
    /// <see cref="DrawExitButton"/> in two ways per art_mapping.gas:
    /// (a) uses subset 4 of backbutton.asp instead of subset 3 — that
    /// subset's UV samples a different decoration region of exitback*.raw,
    /// (b) subset 8 needs the <c>backbutton_e2b</c> PRS clip applied at
    /// hold=1 so the BackBase bone wins over ExitBase at the visible Z
    /// slot of the text-small atlas, flipping the label from EXIT to
    /// BACK.</summary>
    public void DrawSpBackButton(int viewportW, int viewportH,
                                 int x, int y, int w, int h,
                                 bool hovered, bool pressed)
    {
        var renderer = GetOrLoadMesh("backbutton");
        if (renderer is null) return;
        var names = renderer.Asp.TextureNames;
        var arr = new GlTexture?[names.Count];
        var mask = new bool[names.Count];

        //   art_mapping.gas[button_sp_back]:
        //     [mouseover]  { 4 = exitback-up;   8 = text-small-up;   }
        //     [mousedown]  { 4 = exitback-down; 8 = text-small-down; }
        //     [mouseout]   { 4 = exitback;      8 = text-small;      }
        string backTex = pressed ? "exitback-down" : hovered ? "exitback-up" : "exitback";
        string textTex = pressed ? "text-small-down" : hovered ? "text-small-up" : "text-small";

        for (int i = 0; i < names.Count; i++)
            arr[i] = GetOrLoadTexture(names[i]);

        // Same chrome strategy as DrawExitButton (subsets 0/1/2 carry
        // the wood button shape + arrows), but use [4] for the BACK
        // decoration per art_mapping (button_exit uses [3], button_sp_back
        // uses [4] — both subsets sample different regions of exitback).
        if (0 < arr.Length) { arr[0] = GetOrLoadTextureBase(backTex); mask[0] = true; }
        if (1 < arr.Length) { arr[1] = GetOrLoadTextureBase(backTex); mask[1] = true; }
        if (2 < arr.Length) { arr[2] = GetOrLoadTextureBase(backTex); mask[2] = true; }
        if (4 < arr.Length) { arr[4] = GetOrLoadTextureBase(backTex); mask[4] = true; }
        if (8 < arr.Length) { arr[8] = GetOrLoadTextureBase(textTex); mask[8] = true; }

        // Same visual-rect padding as DrawExitButton so the wood button
        // has room for the decorative arrow subsets.
        const float visualWMul = 5.0f;
        const float visualHMul = 2.0f;
        int vw = (int)(w * visualWMul);
        int vh = (int)(h * visualHMul);
        int vx = x + (w - vw) / 2;
        int vy = y + (h - vh) / 2;
        var (subMin, subMax) = ComputeSubsetBounds2D(renderer.Asp, mask);
        var model = BuildSubsetRectModel(vx, vy, vw, vh, subMin, subMax);

        // Apply backbutton_e2b at hold=1 — subset 8 is owned by both
        // ExitBase and BackBase; at bind pose ExitBase is at the
        // visible Z (label = EXIT), the e2b clip swaps so BackBase
        // wins (label = BACK). Verified via `siegefx asp subsets
        // backbutton.asp` (subset 8 bones: BackBase, ExitBase).
        var anim = GetOrLoadClip("backbutton_e2b");
        float timeSec = anim is not null ? anim.AnimLength * 1f : 0f;
        renderer.DrawWithModel(viewportW, viewportH, model, arr,
            anim: anim, timeSec: timeSec, tint: null, subsetMask: mask);
    }

    /// <summary>Legacy text-overlay BACK button (kept to avoid breaking
    /// callers during the migration). Prefer <see cref="DrawSpBackButton"/>
    /// — it renders the authentic atlas BACK label via the
    /// art_mapping.gas[button_sp_back] recipe.</summary>
    public void DrawBackButton(int viewportW, int viewportH,
                               int x, int y, int w, int h,
                               bool hovered, bool pressed,
                               TextRenderer? text, int fontScale)
    {
        DrawSpBackButton(viewportW, viewportH, x, y, w, h, hovered, pressed);
    }

    /// <summary>Phase 28-CD-FLYOUT — Previous nav button on the
    /// CharacterSelect screen. Per art_mapping.gas[button_previous]:
    /// subsets 2 + 6 of backbutton.asp, textures exitback / text-small.
    /// Subset 2 is the PREV-arrow chrome region of exitback*.raw;
    /// subset 6 is the PREV row of text-small atlas. No PRS clip
    /// needed — the bind pose has the Previous-button bones at the
    /// visible Z slot when backbutton is in pn (Previous/Next) state,
    /// which is the cd settled pose end-frame of b2pn.</summary>
    public void DrawPreviousButton(int viewportW, int viewportH,
                                   int x, int y, int w, int h,
                                   bool hovered, bool pressed)
    {
        // SC-CD-PREVNEXT-HOVER (2026-05-04) — overlay-on-chrome pattern
        // matching DrawHeromenuButton: project through BuildSharedSceneModel
        // (chrome's own projection) so the masked subsets land at exactly
        // their cd-state pose position from backbutton_b2pn@1.0; no rect-
        // fitting math, no W/H multipliers, no overlap. (x,y,w,h) are
        // panel hit-rect coords — kept for API parity with DrawNextButton
        // but ignored inside this draw, since the chrome's mesh
        // projection drives where pixels land.
        var renderer = GetOrLoadMesh("backbutton");
        if (renderer is null) return;
        var names = renderer.Asp.TextureNames;
        var arr = new GlTexture?[names.Count];
        var mask = new bool[names.Count];

        string backTex = pressed ? "exitback-down" : hovered ? "exitback-up" : "exitback";
        string textTex = pressed ? "text-small-down" : hovered ? "text-small-up" : "text-small";

        for (int i = 0; i < names.Count; i++)
            arr[i] = GetOrLoadTexture(names[i]);

        // art_mapping.gas[button_previous] = subsets 2 + 6 only. Subset 0
        // is the shared chrome plate behind BOTH buttons — touching it on
        // hover is incorrect per the gas (and makes the highlight pollute
        // the Next side). 2 = exitback-up arrow chrome; 6 = text-small-up
        // PREVIOUS label.
        if (2 < arr.Length) { arr[2] = GetOrLoadTextureBase(backTex); mask[2] = true; }
        if (6 < arr.Length) { arr[6] = GetOrLoadTextureBase(textTex); mask[6] = true; }

        var model = BuildSharedSceneModel(viewportW, viewportH);

        // backbutton_b2pn at hold=1 so subset 6's PrevBase bone is at the
        // visible Z slot — same bone-Z-swap the chrome uses.
        var anim = GetOrLoadClip("backbutton_b2pn");
        float timeSec = anim is not null ? anim.AnimLength * 1f : 0f;
        renderer.DrawWithModel(viewportW, viewportH, model, arr,
            anim: anim, timeSec: timeSec, tint: null, subsetMask: mask);
    }

    /// <summary>Phase 28-CD-FLYOUT — Next nav button on the
    /// CharacterSelect screen. Per art_mapping.gas[button_next]:
    /// subsets 1 + 7 of backbutton.asp, textures exitback / text-small.
    /// Subset 1 is the NEXT-arrow chrome; subset 7 is the NEXT row of
    /// text-small atlas. Same b2pn clip as Previous so both arrow
    /// bones (PrevBase + NextBase) are at the visible Z slot.</summary>
    public void DrawNextButton(int viewportW, int viewportH,
                               int x, int y, int w, int h,
                               bool hovered, bool pressed)
    {
        // SC-CD-PREVNEXT-HOVER — same pattern as DrawPreviousButton.
        // BuildSharedSceneModel projects subsets to their cd-state-pose
        // chrome position from backbutton_b2pn@1.0. (x,y,w,h) ignored.
        var renderer = GetOrLoadMesh("backbutton");
        if (renderer is null) return;
        var names = renderer.Asp.TextureNames;
        var arr = new GlTexture?[names.Count];
        var mask = new bool[names.Count];

        string backTex = pressed ? "exitback-down" : hovered ? "exitback-up" : "exitback";
        string textTex = pressed ? "text-small-down" : hovered ? "text-small-up" : "text-small";

        for (int i = 0; i < names.Count; i++)
            arr[i] = GetOrLoadTexture(names[i]);

        // art_mapping.gas[button_next] = subsets 1 + 7 only. 1 = exitback-up
        // arrow chrome (NEXT side); 7 = text-small-up NEXT label.
        if (1 < arr.Length) { arr[1] = GetOrLoadTextureBase(backTex); mask[1] = true; }
        if (7 < arr.Length) { arr[7] = GetOrLoadTextureBase(textTex); mask[7] = true; }

        var model = BuildSharedSceneModel(viewportW, viewportH);

        var anim = GetOrLoadClip("backbutton_b2pn");
        float timeSec = anim is not null ? anim.AnimLength * 1f : 0f;
        renderer.DrawWithModel(viewportW, viewportH, model, arr,
            anim: anim, timeSec: timeSec, tint: null, subsetMask: mask);
    }

    /// <summary>Phase 29-CD-CREATOR — render one heromenu spinner-arrow
    /// widget using the art_mapping.gas recipe. Receipts (from
    /// <c>siegefx asp subsets heromenu.asp</c> + art_mapping):
    /// all 12 spinners share atlas <c>b_gui_fe_m_mn_3d_heromenu.raw</c>
    /// with <c>-up</c>/<c>-down</c> hover/press variants. The asp is
    /// single-bone (Bone01 only) so no PRS clip is needed — bind pose
    /// is the settled cd-state pose per <c>asp trace-pose ... 1.0</c>
    /// (bind == skin everywhere). Each spinner is its own subset; the
    /// <c>-mapN</c> material-slot suffix lines up 1:1 with the
    /// art_mapping slot index.</summary>
    public void DrawHeromenuButton(int viewportW, int viewportH,
                                   int x, int y, int w, int h,
                                   bool hovered, bool pressed,
                                   string widgetName)
    {
        int subset = widgetName switch
        {
            "button_shirt_left"   => 1,
            "button_pants_left"   => 2,
            "button_face_left"    => 3,
            "button_hair_left"    => 4,
            "button_gender_left"  => 5,
            "button_shirt_right"  => 6,
            "button_pants_right"  => 7,
            "button_face_right"   => 8,
            "button_hair_right"   => 9,
            "button_gender_right" => 10,
            "button_head_right"   => 11,
            "button_head_left"    => 13,
            _ => -1,
        };
        if (subset < 0) return;

        var renderer = GetOrLoadMesh("heromenu");
        if (renderer is null) return;
        var names = renderer.Asp.TextureNames;
        var arr = new GlTexture?[names.Count];
        var mask = new bool[names.Count];

        string heromenuTex = pressed ? "heromenu-down" : hovered ? "heromenu-up" : "heromenu";

        for (int i = 0; i < names.Count; i++)
            arr[i] = GetOrLoadTexture(names[i]);

        if (subset < arr.Length) { arr[subset] = GetOrLoadTextureBase(heromenuTex); mask[subset] = true; }

        var (subMin, subMax) = ComputeSubsetBounds2D(renderer.Asp, mask);
        var model = BuildSubsetRectModel(x, y, w, h, subMin, subMax);
        renderer.DrawWithModel(viewportW, viewportH, model, arr,
            anim: null, timeSec: 0f, tint: null, subsetMask: mask);
    }

    /// <summary>Phase 26-ARTMAP — walk the active subsets' triangles to find
    /// the 2D (XY) bounding box of just those subsets' vertices. Used to
    /// place a single widget's geometry inside a shared mesh family
    /// without the other widgets' bounds dragging the placement.</summary>
    private static (System.Numerics.Vector2 min, System.Numerics.Vector2 max)
        ComputeSubsetBounds2D(SiegeFX.Core.Assets.AspMesh asp, bool[] mask)
    {
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        var corners = asp.Corners;
        var triIdx = asp.TriangleIndices;
        var positions = asp.Positions;
        for (int s = 0; s < asp.Subsets.Length; s++)
        {
            if (s >= mask.Length || !mask[s]) continue;
            var sub = asp.Subsets[s];
            int triEnd = sub.FirstTriangle + sub.TriangleCount;
            for (int t = sub.FirstTriangle; t < triEnd; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    int cornerIdx = triIdx[t * 3 + k];
                    int vIdx = corners[cornerIdx].VertexIndex;
                    var p = positions[vIdx];
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                }
            }
        }
        if (float.IsInfinity(minX))
            return (System.Numerics.Vector2.Zero, System.Numerics.Vector2.One);
        return (new System.Numerics.Vector2(minX, minY),
                new System.Numerics.Vector2(maxX, maxY));
    }

    /// <summary>Phase 26-ARTMAP — variant of UiMeshRenderer.BuildScreenRectModel
    /// that takes explicit subset-bounds instead of the asp's full extents.
    /// Y is negated so mesh-up renders as screen-up.</summary>
    private static Matrix4x4 BuildSubsetRectModel(int targetX, int targetY,
        int targetW, int targetH, System.Numerics.Vector2 boundsMin, System.Numerics.Vector2 boundsMax)
    {
        float meshW = MathF.Max(1e-4f, boundsMax.X - boundsMin.X);
        float meshH = MathF.Max(1e-4f, boundsMax.Y - boundsMin.Y);
        float scaleX = targetW / meshW;
        float scaleY = targetH / meshH;
        float meshCenterX = 0.5f * (boundsMin.X + boundsMax.X);
        float meshCenterY = 0.5f * (boundsMin.Y + boundsMax.Y);
        float targetCenterX = targetX + targetW * 0.5f;
        float targetCenterY = targetY + targetH * 0.5f;
        var t1 = Matrix4x4.CreateTranslation(-meshCenterX, -meshCenterY, 0f);
        var s  = Matrix4x4.CreateScale(scaleX, -scaleY, 1f);
        var t2 = Matrix4x4.CreateTranslation(targetCenterX, targetCenterY, 0f);
        return t1 * s * t2;
    }

    /// <summary>Phase 26-ARTMAP helper — like <see cref="GetOrLoadTexture"/>
    /// but takes a bare base name (no <c>b_gui_fe_m_mn_3d_</c> prefix,
    /// no <c>-mapN</c> suffix) and resolves to the actual .raw file. The
    /// engine's hardcoded prefix lives at EXE offset 0x378e1c per the
    /// RE notes; we replicate it here.</summary>
    private GlTexture? GetOrLoadTextureBase(string baseName)
        => GetOrLoadTexture("b_gui_fe_m_mn_3d_" + baseName);

    /// <summary>Public accessor for the cached chrome texture by bare
    /// base name (e.g. "text-small" → b_gui_fe_m_mn_3d_text-small.raw).
    /// Used by RenderHost to crop label sprites out of text-small.raw
    /// for the character creator row labels.</summary>
    public GlTexture? GetChromeTexture(string baseName)
        => GetOrLoadTextureBase(baseName);

    /// <summary>SC-OPTIONS-CHROME — load a common-chrome texture by
    /// bare base name (e.g. "cpbox2_ul" → b_gui_cmn_cpbox2_ul.raw).
    /// Different prefix from the frontend chrome accessor since the
    /// 9-patch / button / slider chrome lives under
    /// `/art/bitmaps/gui/common/b_gui_cmn_*.raw` in Objects.dsres.</summary>
    public GlTexture? GetCommonTexture(string baseName)
        => GetOrLoadTexture("b_gui_cmn_" + baseName);

    private GlTexture? GetOrLoadTexture(string textureName)
    {
        // Strip the -mapN atlas-cell aliases (heromenu-map7 → heromenu) so all
        // sibling subsets share one underlying GPU texture.
        var key = StripMapSuffix(textureName);
        if (_textures.TryGetValue(key, out var cached)) return cached;
        var basename = $"{key}.raw";
        if (!_resolver.TryLoadByBasename(basename, out var bytes))
        {
            // Some MAXFILE-stamped names may not have a backing .raw (the
            // mesh authored a slot for a placeholder that ships only on disk).
            // Tag it null so we don't keep trying.
            _textures[key] = null!;
            return null;
        }
        var tex = new GlTexture(_gl, RawImage.Load(bytes));
        _textures[key] = tex;
        return tex;
    }

    private static string StripMapSuffix(string name)
    {
        // "b_gui_fe_m_mn_3d_heromenu-map7" -> "b_gui_fe_m_mn_3d_heromenu".
        // Only strip when the suffix is exactly "-map" + digits — preserves
        // "heromenu-up" / "heromenu-down" state variants and the multi-cell
        // logo names like "logo-upper-left" which are NOT atlas aliases.
        var dash = name.LastIndexOf("-map", StringComparison.Ordinal);
        if (dash <= 0) return name;
        for (int i = dash + 4; i < name.Length; i++)
            if (!char.IsDigit(name[i])) return name;
        if (dash + 4 == name.Length) return name;
        return name[..dash];
    }

    public void Dispose()
    {
        foreach (var r in _renderers.Values) r.Dispose();
        foreach (var t in _textures.Values) t?.Dispose();
        _renderers.Clear();
        _textures.Clear();
        _clips.Clear();
    }
}
