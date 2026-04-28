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
        /// <summary>Main menu — Single Player / Multiplayer / Options / Exit.</summary>
        MainMenu,
        /// <summary>Single Player sub-menu — New Game / Load Game / Back.</summary>
        SinglePlayer,
        /// <summary>Start New Game — character template selection.</summary>
        SingleNewGame,
        /// <summary>Character design — the spinner-driven hero creator.</summary>
        CharacterSelect,
        /// <summary>Load Map — final screen before world load.</summary>
        LoadMap,
        /// <summary>Multiplayer.</summary>
        Multiplayer,
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
    }

    public void SetState(ScreenState s) => State = s;

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
            case ScreenState.CharacterSelect:
                DrawCharacterSelectState(fullW, fullH);
                break;
            default:
                // Other states are stubs for now — fall back to character_select
                // visual so the composer is never blank during development.
                DrawCharacterSelectState(fullW, fullH);
                break;
        }
    }

    private void DrawCharacterSelectState(int vw, int vh)
    {
        // Layer order: backdrop → leftside / rightside (decorative frame) → logo
        // (top header) → menubars (button bar) → mainmenu (inner panel) →
        // backbutton (Previous/Next nav) → heromenu (axis spinners).
        //
        // For each mesh, a clip + time-fraction defines the cd-state pose.
        // Where the clip is null, the bind pose is used (good for static
        // chrome that doesn't morph between screens, e.g. backdrop).

        DrawMesh("backdrop",   "backdrop",          clip: null,                           hold: 0f, vw, vh);
        DrawMesh("leftside",   "leftside",          clip: "leftside_default",             hold: 0f, vw, vh);
        DrawMesh("rightside",  "rightside",         clip: "rightside_default",            hold: 0f, vw, vh);
        // logo.asp ships only the "Dungeon Siege" title splash that plays
        // BEFORE the main menu (logo-enter / logo-exit transitions). It is
        // not part of the main-menu / character_select chrome — drawing it
        // here puts the DS title floating in the middle of the menu, which
        // is wrong. Leaving it out of the cd-state composition.
        // DrawMesh("logo", ...);
        // Inner panels: hold the END-frame of the transition INTO cd-state.
        // `prs compare` against `_default` confirmed the cd-state pose is NOT
        // the static rest pose — it's the destination of the *2cd transitions:
        //
        //   mainmenu_sng2cd end vs mainmenu_default: 7 bones differ. The Bone01
        //     root drops Y=2.94→2.01 (mesh moves DOWN into screen) and 6
        //     PanelBASE bones reshuffle their Z slots — Z is a row-visibility
        //     axis, where the visible row sits near Z=-0.85 and parked rows
        //     get pushed to Z>0.5. PanelBASE7 returns to visible (-0.85)
        //     while PanelBASE2 parks off-screen (Z=1.84). That row swap IS
        //     how the title bar shows CHOOSE HERO vs DIFFICULTY.
        //
        //   menubars_lm2cd end vs menubars_default: 17 bones differ — Bone01
        //     drops Y=1.70→-0.99 (the whole spinner column slides down into
        //     screen-center) plus MenuBase1..5 take new Z slots.
        //
        // Using `_default` was the bug: it captures the rest pose with no
        // state applied (mesh at top, default row showing). The *2cd end is
        // what frontend_lights.gas calls the `show_character_selection`
        // destination, and matches the visual reference screenshot.
        // Per-state subset mask for mainmenu: skip text-02L/R (subsets 3,4).
        // mainmenu.asp ships TWO independent text atlases — text-01 (5 rows
        // for the SP-state-tree title labels: NEW GAME / SINGLE PLAYER /
        // CHOOSE HERO / LOAD GAME / OPTIONS) and text-02 (5 rows for the
        // MP-state-tree labels: DIFFICULTY / WAR... etc, mostly blank). PRS
        // sng2cd hold=1 places PanelBASE3 (text-01 row 3 = CHOOSE HERO) AND
        // PanelBASE7 (text-02 row 7 = DIFFICULTY) at IDENTICAL slot Y=1.16.
        // Both atlases overlap; text-02 paints over text-01 and the user
        // sees DIFFICULTY instead of CHOOSE HERO. The original DS1 engine
        // disambiguates per state — cd-state uses the SP-tree atlas only.
        // (Confirmed empirically by `siegefx asp trace-pose` + comparing
        // text-01l.png / text-02l.png atlas content.)
        var mainmenuMask = new[] { true, true, true, false, false, true };
        DrawMesh("mainmenu",   "mainmenu",          clip: "mainmenu_sng2cd",              hold: 1f, vw, vh, mainmenuMask);
        DrawMesh("menubars",   "menubars",          clip: "menubars_lm2cd",               hold: 1f, vw, vh);
        // backbutton uses ac/b/e/pn state codes. Character_select shows the
        // Previous/Next button pair (pn). End of ac2pn = pn pose.
        DrawMesh("backbutton", "backbutton",        clip: "backbutton_ac2pn",             hold: 1f, vw, vh);
        // heromenu has no per-screen morph; play its idle default clip looping.
        DrawMesh("heromenu",   "heromenu",          clip: "heromenu_default",             hold: -1f, vw, vh);
    }

    /// <param name="hold">Time-fraction to evaluate the clip at: 0=start of clip,
    /// 1=end of clip, -1=looped real-time idle (for ambient default clips).</param>
    private void DrawMesh(string meshKey, string meshSuffix, string? clip, float hold, int vw, int vh, bool[]? subsetMask = null)
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

        float sx = targetW / refW;
        float sy = targetH / refH;
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
