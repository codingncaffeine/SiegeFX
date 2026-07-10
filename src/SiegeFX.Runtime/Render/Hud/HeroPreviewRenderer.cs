using System.Numerics;
using Silk.NET.OpenGL;
using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Skrit;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>21d-2a-viii-FE-2 — live 3D character preview rendered into the
/// character_select listener rect. Owns its own <see cref="ActorSpawner"/>
/// + <see cref="SkritRuntime"/> + <see cref="WorldMessageBus"/> so the
/// preview actor never registers with the play-region runtime; the runtime
/// is constructed but never ticked, so the skrit's OnStartChore$ dispatch
/// (which fires synchronously inside <see cref="ActorSpawner.Spawn"/>)
/// runs once to set CurrentAnimIndex=0 (idle) and then sits frozen.
///
/// <para>Variant rebuild is hash-driven: each frame we hash
/// (Gender, BodyTypeIdx, SkinSuffix, PantsSuffix); when it changes,
/// the previous Actor + GL mesh are dropped and a fresh one is spawned
/// against the new <see cref="HeroVariantPicker"/>. Mesh and clip caches
/// live on the spawner, so cycling through pos_a1..a7 only pays the .asp
/// load cost once per body type.</para>
///
/// <para>Drawing scopes <c>glViewport</c> + <c>glScissor</c> to the
/// listener rect (gas 408,73,649,494 in 800×600 reference space, scaled
/// to the current viewport). Depth is cleared inside the scissored region
/// so the chrome's already-drawn pixels (drawn earlier in the cd-state
/// pass) supply the stage backdrop while our 3D pass renders the hero on
/// top with a fresh depth buffer.</para>
///
/// <para>Click-drag on the listener rect spins the model around its
/// vertical axis. Yaw is unbounded so the user can rotate freely past
/// 360°; the rotation matrix wraps naturally.</para></summary>
internal sealed class HeroPreviewRenderer : IDisposable
{
    // 800×600 reference rect — same source as CharacterCreatorPanel.RectListener.
    // Kept in sync manually; if the panel's listener rect ever moves, update both.
    // Gas-authored character_select.gas listener rect is 408,73,649,494.
    // Shifted left 30 and down 15 to center the character in the panel
    // area revealed by the right-pillar door slide; the gas-authored
    // position assumed a 4:3 framebuffer where the pillars cropped out
    // of view, but with our scissor letterbox both pillars are visible
    // and the listener rect needs to move inward toward the visible
    // panel center.
    private const int RefRectX = 378;
    private const int RefRectY = 100;
    private const int RefRectW = 649 - 408;
    private const int RefRectH = 494 - 73;

    private readonly GL _gl;
    private readonly Shader _skinShader;
    private readonly TemplateStore _store;
    private readonly AssetResolver _resolver;
    private readonly Func<Actor, int, GlTexture?> _resolveTexture;

    // Private spawner + runtime — never Tick()ed. Its WorldMessageBus is local
    // so combat hooks fired from the preview's skrit don't leak into the play
    // region's bus.
    private readonly ActorSpawner _spawner;

    // Built actor + GL mesh. Null until EnsurePreview succeeds for the first
    // picker; null again after a variant change until the next EnsurePreview.
    private Actor? _actor;
    private SkinnedMesh? _glMesh;
    private long _builtHash = -1;
    private string? _skinOverride;
    private string? _pantsOverride;
    // chore_fidget variants — heroes.gas authors `00=dff` and `01=dff-02`.
    // The original DS1 select_fidget skrit cycles between them; we load both
    // and alternate every loop so the head-looking-around motion (which lives
    // mostly in dff-02) actually plays. ActorSpawner only loads the first
    // variant via chore_default, which is the subtle "stand" pose and reads
    // as combat-ready on its own.
    private PrsAnimation? _fidgetA;  // dff
    private PrsAnimation? _fidgetB;  // dff-02

    private float _animTime;
    private float _yawRad;

    // Drag state. Coordinates are window pixel space (matches mouse events).
    private bool _dragging;
    private int _dragLastX;

    public Actor? Actor => _actor;
    public string? SkinOverrideName => _skinOverride;
    public string? PantsOverrideName => _pantsOverride;

    public HeroPreviewRenderer(
        GL gl,
        Shader skinShader,
        TemplateStore store,
        AssetResolver resolver,
        Func<Actor, int, GlTexture?> resolveTexture)
    {
        _gl = gl;
        _skinShader = skinShader;
        _store = store;
        _resolver = resolver;
        _resolveTexture = resolveTexture;
        _spawner = new ActorSpawner(store, resolver);
    }

    /// <summary>Rebuild the preview actor when the picker hash changes.
    /// Cheap when the hash is the same — early-returns without touching the
    /// spawner caches.</summary>
    public void EnsurePreview(HeroVariantPicker picker)
    {
        var hash = HashPicker(picker);
        if (hash == _builtHash && _actor is not null) return;

        // Drop the previous GL mesh — the spawner's mesh cache may still hold
        // the AspMesh, but the SkinnedMesh wrapper wraps a specific .asp's
        // VBO/EBO, and a body-type swap means a different .asp.
        _glMesh?.Dispose();
        _glMesh = null;
        _actor = null;
        _skinOverride = null;
        _pantsOverride = null;

        string templateName = picker.Gender == HeroGender.Girl ? "farmgirl" : "farmboy";
        if (!_store.TryGet(templateName, out var template)) return;

        // Synthetic instance at world origin. The character preview camera
        // looks at origin, so no per-actor world offset is needed; the model
        // matrix in Draw() carries the yaw rotation directly.
        // Scid 0xfffffe00 is well clear of region scids (0x01xxxxxx) and the
        // play-region player scid (0xffffff00) so save/load can't collide.
        var inst = ActorInstance.CreateSynthetic(
            templateName, scid: 0xfffffe00u, worldPosition: Vector3.Zero,
            orientation: Quaternion.Identity);

        var ov = picker.BuildOverride(_store, template);
        var overrides = ov is null
            ? null
            : new Dictionary<string, TemplateOverride>(StringComparer.OrdinalIgnoreCase)
              { [templateName] = ov };

        // preferredStance=0 — preview hero is unarmed, the unarmed idle is correct.
        var spawned = _spawner.Spawn(new[] { inst }, preferredStance: 0, overrides);
        if (spawned.Count == 0) return;

        var actor = spawned[0];
        try
        {
            _glMesh = new SkinnedMesh(_gl, actor.Mesh);
        }
        catch
        {
            // Mesh has no skin (static prop accidentally selected) or exceeds
            // MaxBones. Either is a bug in the picker, not a fatal error here.
            _glMesh = null;
            return;
        }

        _actor = actor;
        _skinOverride = ov?.SkinTextureName;
        _pantsOverride = ov?.ClothingTextureName;
        _builtHash = hash;

        // Load the two fidget variants directly. The chore_fidget anim_files
        // block names them; we read both attribute values rather than just
        // FirstOrDefault so dff-02 (the head-look-around) is available.
        _fidgetA = null;
        _fidgetB = null;
        var prefix = _store.GetAttribute(template, "body", "chore_dictionary", "chore_prefix");
        var fidget = _store.GetSection(template, "body", "chore_dictionary", "chore_fidget");
        if (prefix is not null && fidget is not null)
        {
            var animFiles = TemplateStore.FindChild(fidget, "anim_files");
            if (animFiles is not null)
            {
                int slot = 0;
                foreach (var attr in animFiles.Attributes)
                {
                    var val = attr.Value;
                    if (string.IsNullOrEmpty(val)) continue;
                    var fname = $"{prefix}0_{val}.prs";
                    if (!_resolver.TryLoadByBasename(fname, out var bytes)) continue;
                    PrsAnimation? clip = null;
                    try { clip = PrsAnimation.Load(bytes); } catch { }
                    if (clip is null) continue;
                    if (slot == 0) _fidgetA = clip;
                    else if (slot == 1) { _fidgetB = clip; break; }
                    slot++;
                }
            }
        }

        Console.WriteLine($"[hero-preview] {templateName}: clips={actor.Clips.Length} fidgetA={(_fidgetA?.AnimLength ?? -1):F2}s fidgetB={(_fidgetB?.AnimLength ?? -1):F2}s mesh.BoneCount={actor.Mesh.BoneCount}");
    }

    public void Tick(float dt)
    {
        if (_actor is null) return;
        _animTime += dt;
    }

    /// <summary>Render the preview into the listener rect of the current
    /// viewport. Saves and restores GL viewport + scissor state so the
    /// caller's full-frame draw isn't disturbed.</summary>
    public void Draw(int viewportW, int viewportH)
    {
        if (_actor is null || _glMesh is null) return;

        var (rx, ry, rw, rh) = ScaleRect(viewportW, viewportH);
        if (rw <= 0 || rh <= 0) return;
        // gas/HUD is +Y down; GL viewport is +Y up. Flip the Y origin.
        int glY = viewportH - (ry + rh);

        // Save state — we restore everything we touch so the caller's pass
        // continues unaffected. SC-CD-SCISSOR-FIX (2026-05-04): also save
        // the scissor BOX coordinates, not just the enable bit. Earlier
        // code only saved/restored EnableCap.ScissorTest, so when called
        // with a caller-active scissor (e.g. boot path's chromeAspect
        // letterbox), the scissor was left pinned to the listener rect
        // after our draw — clipping subsequent overlay rendering
        // (spinners, name input, row labels) to the 3D char rect only.
        bool wasScissor = _gl.IsEnabled(EnableCap.ScissorTest);
        bool wasDepth = _gl.IsEnabled(EnableCap.DepthTest);
        bool wasCull = _gl.IsEnabled(EnableCap.CullFace);
        bool wasBlend = _gl.IsEnabled(EnableCap.Blend);
        Span<int> prevViewport = stackalloc int[4];
        Span<int> prevScissor  = stackalloc int[4];
        unsafe
        {
            fixed (int* p = prevViewport) _gl.GetInteger(GLEnum.Viewport, p);
            fixed (int* p = prevScissor)  _gl.GetInteger(GLEnum.ScissorBox, p);
        }

        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor(rx, glY, (uint)rw, (uint)rh);
        _gl.Viewport(rx, glY, (uint)rw, (uint)rh);

        // Clear depth only — the chrome's stone backdrop in this rect is what
        // we want behind the hero. Color stays untouched so the chrome shows
        // through wherever the actor doesn't cover.
        _gl.Clear((uint)ClearBufferMask.DepthBufferBit);

        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(GLEnum.Back);
        _gl.FrontFace(GLEnum.Ccw);
        _gl.Disable(EnableCap.Blend);

        // Camera framing. Mesh.Min/Max are bind-pose extents in mesh space;
        // good enough to centre the actor and pick a distance that fits its
        // height. Bump the distance multiplier if the head clips the top of
        // the rect on certain pos_aN body types.
        Vector3 min = _glMesh.Min, max = _glMesh.Max;
        Vector3 center = (min + max) * 0.5f;
        float height = MathF.Max(0.5f, max.Y - min.Y);

        float aspect = (float)rw / rh;
        float fov = 35f * (MathF.PI / 180f);
        // Distance derived from height + fov so a tall pos_aN still fits.
        // Slight slack (1.4×) keeps head + feet inside the rect with room
        // to spare for the idle's vertical breathing.
        float dist = (height * 0.5f) / MathF.Tan(fov * 0.5f) * 1.4f;

        var camPos = new Vector3(center.X, center.Y, center.Z + dist);
        var view = Matrix4x4.CreateLookAt(camPos, center, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, 0.5f, dist * 4f);
        var viewProj = view * proj;

        // Model matrix carries the user's drag yaw (rotation around Y axis
        // through the model centre). Translate centre to origin → rotate →
        // translate back so the pivot stays in frame.
        var model =
            Matrix4x4.CreateTranslation(-center) *
            Matrix4x4.CreateRotationY(_yawRad) *
            Matrix4x4.CreateTranslation(center);

        _skinShader.Use();
        _skinShader.SetMatrix4("uViewProj", viewProj);
        _skinShader.SetMatrix4("uModel", model);
        _skinShader.SetInt("uFlipV", 0);
        _skinShader.SetInt("uSubsetTintActive", 0);
        // Single soft directional light from camera-front-upper so the model
        // reads cleanly against the stone backdrop. Ambient floor keeps the
        // shadowed side from going pure black.
        _skinShader.SetInt("uDirCount", 1);
        var lightDir = Vector3.Normalize(new Vector3(0.4f, 1.0f, 0.6f));
        _skinShader.SetVec3Array("uDirDir[0]", new[] { lightDir });
        _skinShader.SetVec3Array("uDirColor[0]", new[] { new Vector3(0.85f, 0.85f, 0.80f) });
        _skinShader.SetFloat("uAmbient", 0.45f);
        // The preview hand-rolls its lighting and never routes through
        // ApplyLightingUniforms, so it must set every world-lighting uniform
        // the shared skin fragment shader reads — otherwise it inherits the
        // GL default (0) or stale play-region state. uGamma is the load-bearing
        // one: the shader's final `pow(lit, 1/max(uGamma,0.1))` becomes
        // pow(lit, 10) at the default uGamma=0, crushing the hero to near-black
        // with only red-dominant highlights surviving. 1.0 = neutral (matches
        // the look before options-gamma existed). The rest force a clean,
        // fog-free, single-directional lighting state regardless of whether a
        // play region drew earlier.
        _skinShader.SetFloat("uGamma", 1.0f);
        _skinShader.SetInt("uUseBakedLight", 0);
        _skinShader.SetInt("uPointCount", 0);
        _skinShader.SetInt("uFogOn", 0);

        // Skin matrices — replicate DS1's select_fidget skrit by alternating
        // between dff (subtle stand) and dff-02 (head-look-around) every
        // loop. ActorSpawner only loads the first via chore_default, so the
        // looking-around motion never plays from clips[] alone — that's
        // why earlier passes read as "frozen combat stance." Falls back to
        // clips[0] then identity if neither fidget variant resolved.
        int boneCount = _actor.Mesh.BoneCount;
        const int MaxStack = 64;
        Span<Matrix4x4> skinBuf = boneCount <= MaxStack
            ? stackalloc Matrix4x4[MaxStack]
            : new Matrix4x4[boneCount];
        var skin = skinBuf.Slice(0, boneCount);

        PrsAnimation? activeClip = null;
        if (_fidgetA is not null && _fidgetB is not null)
        {
            float totalA = _fidgetA.AnimLength;
            float totalB = _fidgetB.AnimLength;
            float cycle = totalA + totalB;
            if (cycle > 0f)
            {
                float phase = _animTime % cycle;
                activeClip = phase < totalA ? _fidgetA : _fidgetB;
            }
            else activeClip = _fidgetA;
        }
        else activeClip = _fidgetA ?? _fidgetB ?? (_actor.Clips.Length > 0 ? _actor.Clips[0] : null);

        if (activeClip is not null)
        {
            // Phase within the active clip itself (re-derive from cycle so
            // the second clip starts at t=0 rather than t=totalA).
            float t;
            if (_fidgetA is not null && _fidgetB is not null)
            {
                float totalA = _fidgetA.AnimLength;
                float cycle = totalA + _fidgetB.AnimLength;
                float phase = cycle > 0f ? _animTime % cycle : 0f;
                t = phase < totalA ? phase : (phase - totalA);
            }
            else t = activeClip.AnimLength > 0f ? _animTime % activeClip.AnimLength : 0f;
            AnimationRuntime.ComputeSkinMatrices(_actor.Mesh, activeClip, t, skin);
        }
        else
            for (int i = 0; i < boneCount; i++) skin[i] = Matrix4x4.Identity;
        _skinShader.SetMatrix4Array("uBones[0]", skin);

        var subsets = _actor.Mesh.Subsets;
        if (subsets.Length == 0)
        {
            var tex = _resolveTexture(_actor, 0);
            if (tex is not null) { tex.Bind(TextureUnit.Texture0); _skinShader.SetInt("uHasTexture", 1); }
            else _skinShader.SetInt("uHasTexture", 0);
            _glMesh.Draw();
        }
        else
        {
            int lastSlot = -1;
            foreach (var sub in subsets)
            {
                if (sub.TextureIndex != lastSlot)
                {
                    var tex = _resolveTexture(_actor, sub.TextureIndex);
                    if (tex is not null) { tex.Bind(TextureUnit.Texture0); _skinShader.SetInt("uHasTexture", 1); }
                    else _skinShader.SetInt("uHasTexture", 0);
                    lastSlot = sub.TextureIndex;
                }
                _glMesh.DrawSubset(sub.FirstTriangle, sub.TriangleCount);
            }
        }

        // Restore viewport, scissor box + enable, GL state.
        _gl.Viewport(prevViewport[0], prevViewport[1], (uint)prevViewport[2], (uint)prevViewport[3]);
        _gl.Scissor(prevScissor[0], prevScissor[1], (uint)prevScissor[2], (uint)prevScissor[3]);
        if (!wasScissor) _gl.Disable(EnableCap.ScissorTest);
        if (!wasDepth) _gl.Disable(EnableCap.DepthTest);
        if (!wasCull) _gl.Disable(EnableCap.CullFace);
        if (wasBlend) _gl.Enable(EnableCap.Blend);
    }

    /// <summary>Hit-test the listener rect; on hit, latch drag state.
    /// Returns true if the click landed inside the preview rect (caller
    /// should suppress button-press handling for the click).</summary>
    public bool TryStartDrag(int px, int py, int viewportW, int viewportH)
    {
        var (rx, ry, rw, rh) = ScaleRect(viewportW, viewportH);
        if (px < rx || px >= rx + rw || py < ry || py >= ry + rh) return false;
        _dragging = true;
        _dragLastX = px;
        return true;
    }

    public void OnDragMove(int px)
    {
        if (!_dragging) return;
        int dx = px - _dragLastX;
        _dragLastX = px;
        // 0.012 rad/px ≈ a full revolution per ~520 horizontal pixels, which
        // matches "click and drag from one side of the preview rect to the
        // other to spin the hero halfway around" at 800-wide ref.
        _yawRad += dx * 0.012f;
    }

    public void EndDrag() => _dragging = false;
    public bool IsDragging => _dragging;

    private static (int X, int Y, int W, int H) ScaleRect(int vw, int vh)
    {
        float sx = vw / 800f, sy = vh / 600f;
        return (
            (int)MathF.Round(RefRectX * sx),
            (int)MathF.Round(RefRectY * sy),
            (int)MathF.Round(RefRectW * sx),
            (int)MathF.Round(RefRectH * sy));
    }

    private static long HashPicker(HeroVariantPicker p)
    {
        // 6-axis pack: gender(1) | bodyIdx(8) | skin(10) | hair(10) | shirt(8) |
        // pants(10). Each subfield is sized for its real range; long has 64 bits
        // total so this fits comfortably.
        long h = (long)(p.Gender == HeroGender.Girl ? 1 : 0);
        h = (h << 8) | (long)(byte)(p.BodyTypeIdx & 0xFF);
        h = (h << 10) | (ParseSuffixOrZero(p.SkinSuffix) & 0x3FF);
        h = (h << 10) | (ParseSuffixOrZero(p.HairSuffix) & 0x3FF);
        h = (h << 8)  | (long)(byte)(p.ShirtIdx & 0xFF);
        h = (h << 10) | (ParseSuffixOrZero(p.PantsSuffix) & 0x3FF);
        return h;
    }

    private static long ParseSuffixOrZero(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        return long.TryParse(s, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    public void Dispose()
    {
        _glMesh?.Dispose();
        _glMesh = null;
        _actor = null;
    }
}
