using System.Numerics;
using Silk.NET.OpenGL;
using SiegeFX.Core.Assets;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 21d-2a-viii-e — renders a DS1 frontend ASP mesh (e.g.
/// <c>m_gui_fe_m_mn_3d_heromenu.asp</c>) as a screen-anchored composited element.
///
/// <para>The DS1 character_select / main / menubars panels are not 2D sprite
/// stacks — they are a fully 3D scene of rigged ASPs (heromenu, leftside,
/// rightside, mainmenu, menubars, backbutton, backdrop, loadmap, logo) viewed
/// through an orthographic UI camera, with PRS clips driving per-state
/// animations (begin / default / up / down / flyin / cwin / owin). This
/// renderer is the substrate: it loads the same ASP through
/// <see cref="AspMesh.Load"/>, builds GL buffers via the existing
/// <see cref="SkinnedMesh"/> path, and issues one bind+DrawSubset per BSMM
/// record so each subset binds its own texture (the heromenu has 15 subsets,
/// one per atlas cell + shadows + text-small). PRS animation hooks up at
/// viii-i; viii-e renders the bind/rest pose by uploading identity matrices
/// for each bone.</para>
///
/// <para>Lives inside the HUD pass (depth off, alpha blend on — see
/// <see cref="TextRenderer.BeginPass"/>) so it composites over the gameplay
/// 3D scene without fighting the depth buffer. The vertex shader applies
/// skinning then a screen-down ortho projection (origin top-left, +Y down)
/// matching <see cref="BarRenderer"/> and <see cref="TextRenderer"/>.</para>
/// </summary>
public sealed class UiMeshRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly SkinnedMesh _gpu;
    private readonly AspMesh _asp;
    private readonly Matrix4x4[] _identityBones;
    private readonly Matrix4x4[] _animBones;
    // Phase 21d-2a-viii-FE — per-subset "skip if degenerate UV" mask. menubars
    // ships subsets whose UVs are authored as U∈[-481.7, +481.7] V=0 (zero
    // V-extent, ~963 U-tiles wide); rendering them produces a smear of repeating
    // pixels and visually corrupts the panel. These are author-side scaffolds
    // (probably z-fight pads or FX-only triangles) — skip at draw time.
    private readonly bool[] _subsetSkip;

    /// <summary>Mesh-local bounding box (taken from <see cref="AspMesh.Positions"/>).
    /// Used by <see cref="DrawAt"/> to map mesh space onto a target screen rect.</summary>
    public Vector3 MeshMin { get; }
    public Vector3 MeshMax { get; }

    public AspMesh Asp => _asp;
    public int SubsetCount => _asp.Subsets.Length;

    private const string VertexSource = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUv;
layout(location=3) in vec4 aWeights;
layout(location=4) in uvec4 aBones;
uniform mat4 uBones[64];
uniform mat4 uModel;
uniform vec2 uViewport;
out vec2 vUv;
void main() {
    mat4 skin = uBones[aBones.x] * aWeights.x
              + uBones[aBones.y] * aWeights.y
              + uBones[aBones.z] * aWeights.z
              + uBones[aBones.w] * aWeights.w;
    vec4 skinned = skin * vec4(aPos, 1.0);
    vec4 screen  = uModel * skinned;
    float ndcX = (screen.x / uViewport.x) * 2.0 - 1.0;
    float ndcY = 1.0 - (screen.y / uViewport.y) * 2.0;
    gl_Position = vec4(ndcX, ndcY, 0.0, 1.0);
    vUv = aUv;
}";

    private const string FragmentSource = @"#version 330 core
in vec2 vUv;
uniform sampler2D uAtlas;
uniform vec4 uTint;
uniform int uHasTexture;
out vec4 frag;
void main() {
    if (uHasTexture == 0) {
        // Magenta debug fill for unbound subsets — viii-e wants visible
        // failure, not silent black.
        frag = vec4(1.0, 0.0, 1.0, 1.0);
        return;
    }
    vec4 s = texture(uAtlas, vUv);
    if (s.a < 0.02) discard;
    frag = vec4(s.rgb * uTint.rgb, s.a * uTint.a);
}";

    public UiMeshRenderer(GL gl, AspMesh asp)
    {
        if (!asp.HasSkin)
            throw new ArgumentException("UiMeshRenderer requires a rigged ASP (WCRN); " +
                "the DS1 frontend meshes all ship a 2-bone rig", nameof(asp));

        _gl = gl;
        _asp = asp;
        _shader = new Shader(_gl, VertexSource, FragmentSource);
        _gpu = new SkinnedMesh(_gl, asp);

        _identityBones = new Matrix4x4[asp.BoneCount];
        for (int i = 0; i < _identityBones.Length; i++) _identityBones[i] = Matrix4x4.Identity;
        _animBones = new Matrix4x4[asp.BoneCount];

        // Bounds from the mesh's vertex positions (i.e. bind-pose layout). The
        // model matrix in DrawAt maps this box onto the caller's target rect.
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        foreach (var p in asp.Positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        MeshMin = min;
        MeshMax = max;

        // Compute per-subset UV bbox; mark subsets with abnormal UV ranges
        // (|U| or |V| > 4) as skip — sane atlas-tiling stays in [0,1] with
        // small overflow, but DS1's dummy subsets jump to ±481.
        _subsetSkip = new bool[asp.Subsets.Length];
        for (int s = 0; s < asp.Subsets.Length; s++)
        {
            var sub = asp.Subsets[s];
            if (sub.TriangleCount == 0) { _subsetSkip[s] = true; continue; }
            float umin = float.PositiveInfinity, umax = float.NegativeInfinity;
            float vmin = float.PositiveInfinity, vmax = float.NegativeInfinity;
            int firstIdx = sub.FirstTriangle * 3;
            int endIdx = (sub.FirstTriangle + sub.TriangleCount) * 3;
            for (int i = firstIdx; i < endIdx; i++)
            {
                int corner = asp.TriangleIndices[i];
                if ((uint)corner >= (uint)asp.Corners.Length) continue;
                var uv = asp.Corners[corner].Uv;
                if (uv.X < umin) umin = uv.X; if (uv.X > umax) umax = uv.X;
                if (uv.Y < vmin) vmin = uv.Y; if (uv.Y > vmax) vmax = uv.Y;
            }
            float uRange = umax - umin;
            float vRange = vmax - vmin;
            if (uRange > 4f || vRange > 4f) _subsetSkip[s] = true;
        }
    }

    /// <summary>Draws the mesh into the rect (<paramref name="targetX"/>,
    /// <paramref name="targetY"/>, <paramref name="targetW"/>, <paramref name="targetH"/>)
    /// of the framebuffer (origin top-left, +Y down — same convention as
    /// <see cref="BarRenderer"/> and <see cref="TextRenderer"/>). Mesh space is
    /// stretched non-uniformly to fill the rect; callers that need
    /// aspect-preserving placement should pre-shape the rect.</summary>
    /// <param name="textures">One <see cref="GlTexture"/> per entry in
    /// <see cref="AspMesh.TextureNames"/> (parallel array). Null entries render
    /// as magenta debug fill so unbound subsets are visually loud.</param>
    public void DrawAt(int viewportW, int viewportH,
        int targetX, int targetY, int targetW, int targetH,
        GlTexture?[] textures, PrsAnimation? anim = null, float timeSec = 0f, Vector4? tint = null,
        bool[]? subsetMask = null)
    {
        if (targetW <= 0 || targetH <= 0) return;
        DrawWithModel(viewportW, viewportH,
            BuildScreenRectModel(targetX, targetY, targetW, targetH),
            textures, anim, timeSec, tint, subsetMask);
    }

    /// <summary>Lower-level draw with a caller-supplied model matrix. Useful when
    /// composing UI meshes against a shared world transform (e.g. the full
    /// frontend scene placing heromenu/leftside/rightside through one common
    /// UI camera). The model matrix takes mesh-local positions to
    /// screen-pixel coordinates (origin top-left, +Y down).
    ///
    /// <para>If <paramref name="anim"/> is non-null, the mesh is skinned through that PRS
    /// clip at <paramref name="timeSec"/> (clip times are clip-local, clamped to length).
    /// This is how DS1 frontend scenes drive shared chrome meshes — backbutton with
    /// `pn` (Previous/Next) bones translated into view, menubars in its `cd`
    /// (character_design) pose, leftside `flyin` -> `default` etc. Identity-bone fallback
    /// gives the bind/rest pose used by static UI elements (e.g. heromenu in default view).</para></summary>
    public void DrawWithModel(int viewportW, int viewportH, Matrix4x4 model,
        GlTexture?[] textures, PrsAnimation? anim = null, float timeSec = 0f, Vector4? tint = null,
        bool[]? subsetMask = null)
    {
        _shader.Use();
        _shader.SetMatrix4("uModel", model);
        var loc = _gl.GetUniformLocation(_shader.Handle, "uViewport");
        if (loc >= 0) _gl.Uniform2(loc, (float)viewportW, (float)viewportH);
        Span<Matrix4x4> bones;
        if (anim is not null && _animBones.Length > 0)
        {
            AnimationRuntime.ComputeSkinMatrices(_asp, anim, timeSec, _animBones.AsSpan());
            bones = _animBones.AsSpan();
        }
        else
        {
            bones = _identityBones.AsSpan();
        }
        _shader.SetMatrix4Array("uBones[0]", bones);
        var t = tint ?? new Vector4(1f, 1f, 1f, 1f);
        _shader.SetVec4("uTint", t.X, t.Y, t.Z, t.W);
        _shader.SetInt("uAtlas", 0);

        var subsets = _asp.Subsets;
        if (subsets.Length == 0)
        {
            // Degenerate — bind texture 0 if any and draw the lot.
            BindOrFallback(textures, 0);
            _gpu.Draw();
            return;
        }

        int lastBound = -2; // -1 is a valid "no texture" signal; -2 forces first bind.
        for (int i = 0; i < subsets.Length; i++)
        {
            if (_subsetSkip[i]) continue;
            if (subsetMask is not null && i < subsetMask.Length && !subsetMask[i]) continue;
            var sub = subsets[i];
            if (sub.TextureIndex != lastBound)
            {
                BindOrFallback(textures, sub.TextureIndex);
                lastBound = sub.TextureIndex;
            }
            _gpu.DrawSubset(sub.FirstTriangle, sub.TriangleCount);
        }
    }

    private void BindOrFallback(GlTexture?[] textures, int idx)
    {
        if (idx >= 0 && idx < textures.Length && textures[idx] is not null)
        {
            textures[idx]!.Bind(TextureUnit.Texture0);
            _shader.SetInt("uHasTexture", 1);
        }
        else
        {
            _shader.SetInt("uHasTexture", 0);
        }
    }

    /// <summary>Builds a model matrix that maps the mesh's bind-pose bounding box
    /// onto the given screen rect. Y is negated so mesh-up renders as
    /// screen-up (mesh Y-up, screen Y-down).</summary>
    public Matrix4x4 BuildScreenRectModel(int targetX, int targetY, int targetW, int targetH)
    {
        float meshW = MathF.Max(1e-4f, MeshMax.X - MeshMin.X);
        float meshH = MathF.Max(1e-4f, MeshMax.Y - MeshMin.Y);
        float scaleX = targetW / meshW;
        float scaleY = targetH / meshH;

        float meshCenterX = 0.5f * (MeshMin.X + MeshMax.X);
        float meshCenterY = 0.5f * (MeshMin.Y + MeshMax.Y);
        float targetCenterX = targetX + targetW * 0.5f;
        float targetCenterY = targetY + targetH * 0.5f;

        // Compose right-to-left (System.Numerics row-vector convention applies left-to-right):
        //   v' = ((v - meshCenter) * S(sx, -sy, 1)) + targetCenter
        var t1 = Matrix4x4.CreateTranslation(-meshCenterX, -meshCenterY, 0f);
        var s  = Matrix4x4.CreateScale(scaleX, -scaleY, 1f);
        var t2 = Matrix4x4.CreateTranslation(targetCenterX, targetCenterY, 0f);
        return t1 * s * t2;
    }

    public void Dispose()
    {
        _gpu.Dispose();
        _shader.Dispose();
    }
}
