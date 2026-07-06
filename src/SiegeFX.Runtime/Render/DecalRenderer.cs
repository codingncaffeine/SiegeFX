using System.Numerics;
using Silk.NET.OpenGL;

namespace SiegeFX.Runtime.Render;

/// <summary>SC-DECALS — renders a region's projected decals (blood, ground scorch,
/// burnt-wood char, drop shadows, dirt, straw, rugs) as oriented, textured quads.
/// Each decal is a flat world-space quad (built by RenderHost from DecalStore +
/// node transforms), alpha-blended over the already-drawn terrain and props,
/// depth-tested so it's occluded correctly but with depth-write off and a polygon
/// offset toward the camera so it sits ON its surface without z-fighting. Quads are
/// grouped by texture into one static VBO, so a whole region draws in a handful of
/// binds + DrawArrays. This is the layer that puts the char on the burnt farmhouse
/// doors — the door mesh/texture are clean; DS1 projects b_d_burnt-wood-a over them.</summary>
public sealed class DecalRenderer : IDisposable
{
    public readonly record struct DecalQuad(Vector3 P0, Vector3 P1, Vector3 P2, Vector3 P3, string Texture);

    private const string VertexSrc = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec2 aUv;
uniform mat4 uViewProj;
out vec2 vUv;
void main() { vUv = aUv; gl_Position = uViewProj * vec4(aPos, 1.0); }";

    private const string FragmentSrc = @"#version 330 core
in vec2 vUv;
uniform sampler2D uTex;
uniform float uAlpha;
out vec4 frag;
void main() {
    vec4 t = texture(uTex, vUv);
    if (t.a <= 0.004) discard;
    frag = vec4(t.rgb, t.a * uAlpha);
}";

    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly uint _vao, _vbo;
    private readonly List<Batch> _batches = new();
    private readonly List<GlTexture> _ownedTextures = new();
    private bool _disposed;

    private readonly record struct Batch(GlTexture Texture, int First, uint Count);

    public int DecalCount { get; private set; }

    public DecalRenderer(GL gl)
    {
        _gl = gl;
        _shader = new Shader(gl, VertexSrc, FragmentSrc);
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
    }

    /// <summary>Rebuild the region's decal geometry. Groups quads by texture, loads
    /// each texture once via <paramref name="textureLoader"/> (null-returns are
    /// skipped), and uploads one interleaved pos(3)+uv(2) VBO. Previously-owned
    /// textures + batches are freed first, so this doubles as the per-region reset.</summary>
    public void SetDecals(IReadOnlyList<DecalQuad> quads, Func<string, GlTexture?> textureLoader)
    {
        if (_disposed) return;
        foreach (var t in _ownedTextures) t.Dispose();
        _ownedTextures.Clear();
        _batches.Clear();
        DecalCount = 0;

        // Group quad indices by texture, preserving first-seen order for stable batches.
        var byTex = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        for (int i = 0; i < quads.Count; i++)
        {
            var name = quads[i].Texture;
            if (!byTex.TryGetValue(name, out var idxs)) { idxs = new List<int>(); byTex[name] = idxs; order.Add(name); }
            idxs.Add(i);
        }

        // 6 verts/quad, 5 floats/vert (pos.xyz, uv.xy). Two tris (P0,P1,P2)+(P0,P2,P3);
        // culling is disabled at draw so winding doesn't matter (decals are two-sided).
        var verts = new List<float>(quads.Count * 30);
        int vertCursor = 0;
        foreach (var name in order)
        {
            var tex = textureLoader(name);
            if (tex is null) continue;
            _ownedTextures.Add(tex);
            int first = vertCursor;
            foreach (var qi in byTex[name])
            {
                var q = quads[qi];
                Append(verts, q.P0, 0f, 0f);
                Append(verts, q.P1, 1f, 0f);
                Append(verts, q.P2, 1f, 1f);
                Append(verts, q.P0, 0f, 0f);
                Append(verts, q.P2, 1f, 1f);
                Append(verts, q.P3, 0f, 1f);
                vertCursor += 6;
                DecalCount++;
            }
            if (vertCursor > first) _batches.Add(new Batch(tex, first, (uint)(vertCursor - first)));
        }

        var data = verts.ToArray();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* p = data)
                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(data.Length * sizeof(float)),
                    data.Length == 0 ? null : p, GLEnum.StaticDraw);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, 5 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 2, GLEnum.Float, false, 5 * sizeof(float), (void*)(3 * sizeof(float)));
        }
        _gl.BindVertexArray(0);
        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
    }

    private static void Append(List<float> v, Vector3 p, float u, float w)
    {
        v.Add(p.X); v.Add(p.Y); v.Add(p.Z); v.Add(u); v.Add(w);
    }

    /// <summary>Draw all decals over the world scene. Alpha-blended, depth-tested
    /// (occluded by nearer geometry) but depth-write off, backface-cull off (decals
    /// read from either side), polygon-offset toward the camera so a decal co-planar
    /// with its surface wins the depth test. GL state is saved and restored.</summary>
    private bool _loggedDraw;

    public void Draw(Matrix4x4 viewProj)
    {
        if (_disposed || _batches.Count == 0) return;
        if (!_loggedDraw)
        {
            _loggedDraw = true;
            Console.WriteLine($"[decals] draw path live: {_batches.Count} texture batch(es), {DecalCount} decals");
        }

        bool blendWas = _gl.IsEnabled(GLEnum.Blend);
        bool cullWas  = _gl.IsEnabled(GLEnum.CullFace);
        bool offWas   = _gl.IsEnabled(GLEnum.PolygonOffsetFill);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
        _gl.DepthMask(false);
        _gl.Enable(EnableCap.PolygonOffsetFill);
        _gl.PolygonOffset(-2.0f, -2.0f);

        _shader.Use();
        _shader.SetMatrix4("uViewProj", viewProj);
        _shader.SetInt("uTex", 0);
        _shader.SetFloat("uAlpha", 1.0f);

        _gl.BindVertexArray(_vao);
        foreach (var b in _batches)
        {
            b.Texture.Bind(TextureUnit.Texture0);
            _gl.DrawArrays(GLEnum.Triangles, b.First, b.Count);
        }
        _gl.BindVertexArray(0);

        // Restore. The world pass runs with depth-write on, so re-enable it.
        _gl.PolygonOffset(0f, 0f);
        if (!offWas) _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.DepthMask(true);
        if (cullWas) _gl.Enable(EnableCap.CullFace); else _gl.Disable(EnableCap.CullFace);
        if (!blendWas) _gl.Disable(EnableCap.Blend);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var t in _ownedTextures) t.Dispose();
        _ownedTextures.Clear();
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _shader.Dispose();
    }
}
