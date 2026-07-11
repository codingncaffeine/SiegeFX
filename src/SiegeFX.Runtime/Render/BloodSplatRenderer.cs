using System.Numerics;
using Silk.NET.OpenGL;

namespace SiegeFX.Runtime.Render;

/// <summary>Phase 21 — dynamic combat blood splats on the ground. DS1's
/// melee_hit_2 effect spawns a 16-particle blood burst whose particles
/// <c>splat()</c> into ground decals as they land (authored textures
/// b_sfx_blood_001-003 red / 004-006 green, splatscaleup 1.95). The baked
/// region DecalRenderer can't take runtime adds, so combat splats stream
/// through this small renderer instead: capped FIFO of textured ground
/// quads with a long hold and a fade-out. Rendering recipe follows
/// <see cref="SelectionFxRenderer"/> (world-space quads, alpha blend,
/// depth-test no-write, polygon offset).</summary>
public sealed class BloodSplatRenderer : IDisposable
{
    private const string VertexSrc = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec2 aUv;
layout(location=2) in float aAlpha;
uniform mat4 uViewProj;
out vec2 vUv;
out float vAlpha;
void main() { vUv = aUv; vAlpha = aAlpha; gl_Position = uViewProj * vec4(aPos, 1.0); }";

    private const string FragmentSrc = @"#version 330 core
in vec2 vUv;
in float vAlpha;
uniform sampler2D uTex;
out vec4 frag;
void main() {
    vec4 t = texture(uTex, vUv);
    float a = t.a * vAlpha;
    if (a <= 0.004) discard;
    // The authored splat RAWs are dark (avg 84,2,2); the original renders
    // them brighter (blood.bmp reference). 1.7x lift — user-tuned down
    // from 2.5 for a darker, wetter read — still lets the baked highlight
    // pixels pop first.
    frag = vec4(min(t.rgb * 1.7, vec3(1.0)), a);
}";

    public readonly record struct Splat(
        Vector3 Pos, float Size, float RotRad, int TexIdx, float Age);

    private const int MaxSplats = 64;
    // User-tuned 2026-07-11: shorter linger + quicker fade.
    private const float HoldSec = 8f;
    private const float FadeSec = 4f;

    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly uint _vao, _vbo;
    private readonly List<Splat> _splats = new(MaxSplats);
    private readonly List<float> _verts = new(64 * 6 * 6);
    private float[] _upload = Array.Empty<float>();
    private bool _disposed;

    private const int Stride = 6; // pos3 + uv2 + alpha1

    public BloodSplatRenderer(GL gl)
    {
        _gl = gl;
        _shader = new Shader(gl, VertexSrc, FragmentSrc);
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
        unsafe
        {
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, Stride * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 2, GLEnum.Float, false, Stride * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(2, 1, GLEnum.Float, false, Stride * sizeof(float), (void*)(5 * sizeof(float)));
        }
        _gl.BindVertexArray(0);
    }

    public void Add(Vector3 groundPos, float size, float rotRad, int texIdx)
    {
        if (_splats.Count >= MaxSplats) _splats.RemoveAt(0);
        _splats.Add(new Splat(groundPos, size, rotRad, texIdx, 0f));
    }

    public void Clear() => _splats.Clear();

    public void Tick(float dt)
    {
        for (int i = _splats.Count - 1; i >= 0; i--)
        {
            var s = _splats[i] with { Age = _splats[i].Age + dt };
            if (s.Age >= HoldSec + FadeSec) _splats.RemoveAt(i);
            else _splats[i] = s;
        }
    }

    /// <summary>Draw all splats using one texture per index bucket.
    /// <paramref name="textures"/> is indexed by each splat's TexIdx;
    /// null entries skip their splats.</summary>
    public void Draw(Matrix4x4 viewProj, IReadOnlyList<GlTexture?> textures)
    {
        if (_disposed || _splats.Count == 0) return;

        bool blendWas = _gl.IsEnabled(GLEnum.Blend);
        bool cullWas = _gl.IsEnabled(GLEnum.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        // Ground quads must not be winding-culled (same rule as
        // SelectionFxRenderer) — with culling left on, every splat was
        // invisible from the normal camera side.
        _gl.Disable(EnableCap.CullFace);
        _gl.DepthMask(false);
        _gl.Enable(EnableCap.PolygonOffsetFill);
        _gl.PolygonOffset(-2.0f, -2.0f);

        _shader.Use();
        _shader.SetMatrix4("uViewProj", viewProj);
        _shader.SetInt("uTex", 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);

        for (int t = 0; t < textures.Count; t++)
        {
            var tex = textures[t];
            if (tex is null) continue;
            _verts.Clear();
            foreach (var s in _splats)
            {
                if (s.TexIdx != t) continue;
                float alpha = s.Age <= HoldSec ? 0.85f
                    : 0.85f * MathF.Max(0f, 1f - (s.Age - HoldSec) / FadeSec);
                float c = MathF.Cos(s.RotRad) * s.Size;
                float n = MathF.Sin(s.RotRad) * s.Size;
                float y = s.Pos.Y + 0.03f;
                var a = new Vector3(s.Pos.X - c + n, y, s.Pos.Z - n - c);
                var b = new Vector3(s.Pos.X + c + n, y, s.Pos.Z + n - c);
                var d = new Vector3(s.Pos.X + c - n, y, s.Pos.Z + n + c);
                var e = new Vector3(s.Pos.X - c - n, y, s.Pos.Z - n + c);
                Emit(a, 0, 0, alpha); Emit(b, 1, 0, alpha); Emit(d, 1, 1, alpha);
                Emit(a, 0, 0, alpha); Emit(d, 1, 1, alpha); Emit(e, 0, 1, alpha);
            }
            if (_verts.Count == 0) continue;
            if (_upload.Length < _verts.Count)
                _upload = new float[Math.Max(_verts.Count, _upload.Length * 2)];
            _verts.CopyTo(_upload);
            unsafe
            {
                fixed (float* p = _upload)
                    _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(_verts.Count * sizeof(float)),
                        p, GLEnum.DynamicDraw);
            }
            tex.Bind(TextureUnit.Texture0);
            _gl.DrawArrays(GLEnum.Triangles, 0, (uint)(_verts.Count / Stride));
        }

        _gl.BindVertexArray(0);
        _gl.PolygonOffset(0f, 0f);
        _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.DepthMask(true);
        if (cullWas) _gl.Enable(EnableCap.CullFace);
        if (!blendWas) _gl.Disable(EnableCap.Blend);
    }

    void Emit(Vector3 p, float u, float v, float alpha)
    {
        _verts.Add(p.X); _verts.Add(p.Y); _verts.Add(p.Z);
        _verts.Add(u); _verts.Add(v); _verts.Add(alpha);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _shader.Dispose();
    }
}
