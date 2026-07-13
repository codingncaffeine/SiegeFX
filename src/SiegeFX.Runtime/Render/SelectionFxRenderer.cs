using System.Numerics;
using Silk.NET.OpenGL;

namespace SiegeFX.Runtime.Render;

/// <summary>SC-MOUSE-FX — DS1's in-world selection feedback: the thick green
/// ring under selected/controlled characters and the small green inverted
/// 3D triangle that marks a click-to-move destination. Rendering follows
/// <see cref="BlobShadowRenderer"/>'s recipe (streamed world-space geometry,
/// alpha blend, depth-test with depth-write off, polygon offset for the
/// ground-hugging ring) with a per-vertex color channel so the marker can
/// fade and the tetra faces can shade without extra draw calls.</summary>
public sealed class SelectionFxRenderer : IDisposable
{
    private const string VertexSrc = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec2 aUv;
layout(location=2) in vec4 aColor;
uniform mat4 uViewProj;
out vec2 vUv;
out vec4 vColor;
void main() { vUv = aUv; vColor = aColor; gl_Position = uViewProj * vec4(aPos, 1.0); }";

    private const string FragmentSrc = @"#version 330 core
in vec2 vUv;
in vec4 vColor;
uniform sampler2D uTex;
out vec4 frag;
void main() {
    float a = texture(uTex, vUv).a * vColor.a;
    if (a <= 0.004) discard;
    frag = vec4(vColor.rgb, a);
}";

    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly uint _vao, _vbo;
    private readonly uint _ringTex, _solidTex;
    private readonly List<float> _ringVerts = new(9 * 12);
    private readonly List<float> _solidVerts = new(9 * 24);
    private bool _disposed;

    private const int Stride = 9; // pos3 + uv2 + rgba4

    public SelectionFxRenderer(GL gl)
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
            _gl.VertexAttribPointer(2, 4, GLEnum.Float, false, Stride * sizeof(float), (void*)(5 * sizeof(float)));
        }
        _gl.BindVertexArray(0);
        _ringTex = BuildRingTexture(gl);
        _solidTex = BuildSolidTexture(gl);
    }

    /// <summary>Ring band alpha: opaque between ~72%..96% of the radius with
    /// smooth in/out edges — the thick DS1 selection circle profile.</summary>
    private static uint BuildRingTexture(GL gl)
    {
        const int size = 64;
        var px = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
            float d = MathF.Sqrt(dx * dx + dy * dy) * 2f; // 0 center → 1 rim
            float inner = Math.Clamp((d - 0.68f) / 0.08f, 0f, 1f);
            float outer = Math.Clamp((0.97f - d) / 0.06f, 0f, 1f);
            float a = MathF.Min(inner, outer);
            int i = (y * size + x) * 4;
            px[i + 0] = px[i + 1] = px[i + 2] = 255;
            px[i + 3] = (byte)(a * 255f);
        }
        return UploadTex(gl, px, size);
    }

    private static uint BuildSolidTexture(GL gl)
    {
        var px = new byte[4 * 4 * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = px[i + 1] = px[i + 2] = px[i + 3] = 255; }
        return UploadTex(gl, px, 2);
    }

    private static uint UploadTex(GL gl, byte[] px, int size)
    {
        uint tex = gl.GenTexture();
        gl.BindTexture(GLEnum.Texture2D, tex);
        unsafe
        {
            fixed (byte* p = px)
                gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba8,
                    (uint)size, (uint)size, 0, GLEnum.Rgba, GLEnum.UnsignedByte, p);
        }
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);
        gl.BindTexture(GLEnum.Texture2D, 0);
        return tex;
    }

    public void Begin()
    {
        _ringVerts.Clear();
        _solidVerts.Clear();
    }

    void Emit(List<float> dst, Vector3 p, float u, float v, Vector4 c)
    {
        dst.Add(p.X); dst.Add(p.Y); dst.Add(p.Z);
        dst.Add(u); dst.Add(v);
        dst.Add(c.X); dst.Add(c.Y); dst.Add(c.Z); dst.Add(c.W);
    }

    /// <summary>The selection circle: a flat ground quad with the ring band.</summary>
    public void AddRing(Vector3 feet, float radius, Vector4 color)
    {
        float y = feet.Y + 0.05f;
        var a = new Vector3(feet.X - radius, y, feet.Z - radius);
        var b = new Vector3(feet.X + radius, y, feet.Z - radius);
        var c = new Vector3(feet.X + radius, y, feet.Z + radius);
        var d = new Vector3(feet.X - radius, y, feet.Z + radius);
        Emit(_ringVerts, a, 0, 0, color); Emit(_ringVerts, b, 1, 0, color); Emit(_ringVerts, c, 1, 1, color);
        Emit(_ringVerts, a, 0, 0, color); Emit(_ringVerts, c, 1, 1, color); Emit(_ringVerts, d, 0, 1, color);
    }

    /// <summary>The move-destination marker: a small solid inverted
    /// tetrahedron, apex touching the ground, base hovering above. Three side
    /// faces shade slightly differently so it reads as 3D; the base caps it.</summary>
    public void AddInvertedTetra(Vector3 ground, float size, Vector4 color)
    {
        var apex = ground + new Vector3(0f, 0.02f, 0f);
        float h = size * 1.35f;
        var b0 = ground + new Vector3(-size * 0.87f, h, size * -0.5f);
        var b1 = ground + new Vector3( size * 0.87f, h, size * -0.5f);
        var b2 = ground + new Vector3(0f,            h, size);

        void Face(Vector3 p0, Vector3 p1, Vector3 p2, float shade)
        {
            var c = new Vector4(color.X * shade, color.Y * shade, color.Z * shade, color.W);
            Emit(_solidVerts, p0, 0.5f, 0.5f, c);
            Emit(_solidVerts, p1, 0.5f, 0.5f, c);
            Emit(_solidVerts, p2, 0.5f, 0.5f, c);
        }
        Face(apex, b0, b1, 0.80f);
        Face(apex, b1, b2, 1.00f);
        Face(apex, b2, b0, 0.62f);
        Face(b0, b2, b1, 1.12f); // base, brightest (lit from above)
    }

    /// <summary>Phase 22 — DS1's blue hover triangle around gold and spells on
    /// the ground. DS1 draws it HOLLOW: a thin triangular OUTLINE with a
    /// transparent middle, not a filled tri. We shrink each corner toward the
    /// centroid to get an inner triangle, then fill only the band between the
    /// two (three edge quads) — the inner triangle is never emitted, so the
    /// middle stays see-through. +0.05 above ground clears z-fighting at DS1's
    /// camera pitch.</summary>
    public void AddFlatTriangle(Vector3 ground, float size, Vector4 color)
    {
        float y = ground.Y + 0.05f;
        var p0 = new Vector3(ground.X, y, ground.Z + size);
        var p1 = new Vector3(ground.X - size * 0.87f, y, ground.Z - size * 0.5f);
        var p2 = new Vector3(ground.X + size * 0.87f, y, ground.Z - size * 0.5f);
        // Inner corners pulled toward the centroid leave a ~22%-wide outline.
        var ctr = (p0 + p1 + p2) / 3f;
        const float borderFrac = 0.22f;
        var q0 = ctr + (p0 - ctr) * (1f - borderFrac);
        var q1 = ctr + (p1 - ctr) * (1f - borderFrac);
        var q2 = ctr + (p2 - ctr) * (1f - borderFrac);
        // One quad per edge (outer edge + matching inner edge) = the frame.
        // CullFace is off in Draw(), so winding doesn't matter.
        void EdgeQuad(Vector3 oa, Vector3 ob, Vector3 ib, Vector3 ia)
        {
            Emit(_solidVerts, oa, 0.5f, 0.5f, color);
            Emit(_solidVerts, ob, 0.5f, 0.5f, color);
            Emit(_solidVerts, ib, 0.5f, 0.5f, color);
            Emit(_solidVerts, oa, 0.5f, 0.5f, color);
            Emit(_solidVerts, ib, 0.5f, 0.5f, color);
            Emit(_solidVerts, ia, 0.5f, 0.5f, color);
        }
        EdgeQuad(p0, p1, q1, q0);
        EdgeQuad(p1, p2, q2, q1);
        EdgeQuad(p2, p0, q0, q2);
    }

    private float[] _uploadBuf = Array.Empty<float>();

    public void Draw(Matrix4x4 viewProj)
    {
        if (_disposed || (_ringVerts.Count == 0 && _solidVerts.Count == 0)) return;

        bool blendWas = _gl.IsEnabled(GLEnum.Blend);
        bool cullWas  = _gl.IsEnabled(GLEnum.CullFace);
        bool offWas   = _gl.IsEnabled(GLEnum.PolygonOffsetFill);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
        _gl.DepthMask(false);

        _shader.Use();
        _shader.SetMatrix4("uViewProj", viewProj);
        _shader.SetInt("uTex", 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);

        void Flush(List<float> verts, uint tex, bool groundOffset)
        {
            if (verts.Count == 0) return;
            if (_uploadBuf.Length < verts.Count)
                _uploadBuf = new float[Math.Max(verts.Count, _uploadBuf.Length * 2)];
            verts.CopyTo(_uploadBuf);
            unsafe
            {
                fixed (float* p = _uploadBuf)
                    _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(verts.Count * sizeof(float)),
                        p, GLEnum.DynamicDraw);
            }
            if (groundOffset)
            {
                _gl.Enable(EnableCap.PolygonOffsetFill);
                _gl.PolygonOffset(-2.0f, -2.0f);
            }
            _gl.BindTexture(GLEnum.Texture2D, tex);
            _gl.DrawArrays(GLEnum.Triangles, 0, (uint)(verts.Count / Stride));
            if (groundOffset)
            {
                _gl.PolygonOffset(0f, 0f);
                _gl.Disable(EnableCap.PolygonOffsetFill);
            }
        }
        Flush(_ringVerts, _ringTex, groundOffset: true);
        Flush(_solidVerts, _solidTex, groundOffset: false);

        _gl.BindVertexArray(0);
        _gl.DepthMask(true);
        if (offWas) _gl.Enable(EnableCap.PolygonOffsetFill);
        if (cullWas) _gl.Enable(EnableCap.CullFace); else _gl.Disable(EnableCap.CullFace);
        if (!blendWas) _gl.Disable(EnableCap.Blend);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gl.DeleteTexture(_ringTex);
        _gl.DeleteTexture(_solidTex);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _shader.Dispose();
    }
}
