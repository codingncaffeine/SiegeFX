using System.Numerics;
using Silk.NET.OpenGL;

namespace SiegeFX.Runtime.Render;

/// <summary>ALPHA-2V — soft blob drop-shadows under actors, the runtime behind
/// the Options → Video → Shadows setting (none / simple_party / complex_party;
/// simple = party members only, complex = every visible actor). DS1's own
/// simple shadows were the same idea: a dark radial splat at the feet.
///
/// Rendering mirrors <see cref="DecalRenderer"/>'s ground-decal recipe —
/// alpha-blended, depth-tested with depth-write off, polygon offset toward the
/// camera — but the quad list rebuilds every frame from live actor positions,
/// so the VBO streams with DynamicDraw. The splat texture is procedural (a
/// 64×64 radial falloff), no authored art dependency.</summary>
public sealed class BlobShadowRenderer : IDisposable
{
    private const string VertexSrc = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec2 aUv;
uniform mat4 uViewProj;
out vec2 vUv;
void main() { vUv = aUv; gl_Position = uViewProj * vec4(aPos, 1.0); }";

    private const string FragmentSrc = @"#version 330 core
in vec2 vUv;
uniform sampler2D uTex;
out vec4 frag;
void main() {
    float a = texture(uTex, vUv).a;
    if (a <= 0.004) discard;
    frag = vec4(0.0, 0.0, 0.0, a);
}";

    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly uint _vao, _vbo;
    private readonly uint _splatTex;
    private readonly List<float> _verts = new(64 * 30);
    private int _quadCount;
    private bool _disposed;

    public BlobShadowRenderer(GL gl)
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
            _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, 5 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 2, GLEnum.Float, false, 5 * sizeof(float), (void*)(3 * sizeof(float)));
        }
        _gl.BindVertexArray(0);
        _splatTex = BuildSplatTexture(gl);
    }

    /// <summary>Radial alpha falloff: opaque-ish core easing to zero at the rim
    /// (smoothstep gives the soft penumbra edge a plain linear ramp lacks).</summary>
    private static uint BuildSplatTexture(GL gl)
    {
        const int size = 64;
        var px = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
            float d = MathF.Sqrt(dx * dx + dy * dy) * 2f; // 0 center → 1 rim
            float t = Math.Clamp(1f - d, 0f, 1f);
            float a = t * t * (3f - 2f * t); // smoothstep
            int i = (y * size + x) * 4;
            px[i + 3] = (byte)(a * 165f); // peak alpha ~0.65
        }
        uint tex = gl.GenTexture();
        gl.BindTexture(GLEnum.Texture2D, tex);
        unsafe
        {
            fixed (byte* p = px)
                gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba8,
                    size, size, 0, GLEnum.Rgba, GLEnum.UnsignedByte, p);
        }
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);
        gl.BindTexture(GLEnum.Texture2D, 0);
        return tex;
    }

    /// <summary>Start a new frame's shadow list.</summary>
    public void Begin()
    {
        _verts.Clear();
        _quadCount = 0;
    }

    /// <summary>Queue one shadow splat: a flat XZ quad centered at
    /// <paramref name="feet"/>, nudged up slightly so the polygon-offset
    /// depth trick wins against the ground it sits on. Sloped ground can
    /// clip a rim edge — same limitation DS1's simple shadows had.</summary>
    public void Add(Vector3 feet, float radius)
    {
        float y = feet.Y + 0.04f;
        _verts.Add(feet.X - radius); _verts.Add(y); _verts.Add(feet.Z - radius); _verts.Add(0f); _verts.Add(0f);
        _verts.Add(feet.X + radius); _verts.Add(y); _verts.Add(feet.Z - radius); _verts.Add(1f); _verts.Add(0f);
        _verts.Add(feet.X + radius); _verts.Add(y); _verts.Add(feet.Z + radius); _verts.Add(1f); _verts.Add(1f);
        _verts.Add(feet.X - radius); _verts.Add(y); _verts.Add(feet.Z - radius); _verts.Add(0f); _verts.Add(0f);
        _verts.Add(feet.X + radius); _verts.Add(y); _verts.Add(feet.Z + radius); _verts.Add(1f); _verts.Add(1f);
        _verts.Add(feet.X - radius); _verts.Add(y); _verts.Add(feet.Z + radius); _verts.Add(0f); _verts.Add(1f);
        _quadCount++;
    }

    // ALPHA-PERF — reused upload buffer; ToArray() per frame was a
    // per-frame GC allocation for no benefit.
    private float[] _uploadBuf = Array.Empty<float>();

    public void Draw(Matrix4x4 viewProj)
    {
        if (_disposed || _quadCount == 0) return;

        if (_uploadBuf.Length < _verts.Count)
            _uploadBuf = new float[Math.Max(_verts.Count, _uploadBuf.Length * 2)];
        _verts.CopyTo(_uploadBuf);
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* p = _uploadBuf)
                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(_verts.Count * sizeof(float)),
                    p, GLEnum.DynamicDraw);
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
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(GLEnum.Texture2D, _splatTex);
        _gl.DrawArrays(GLEnum.Triangles, 0, (uint)(_quadCount * 6));
        _gl.BindVertexArray(0);

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
        _gl.DeleteTexture(_splatTex);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _shader.Dispose();
    }
}
