using System.Numerics;
using Silk.NET.OpenGL;
using SiegeFX.Core.Assets;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// 2D text overlay backed by a <see cref="BitmapFont"/>. Builds a single dynamic
/// vertex buffer of textured quads per <see cref="DrawString"/> call and submits
/// them in screen space (origin top-left, +Y down) under an ortho projection
/// sized to the framebuffer.
///
/// Designed to be cheap to call per-frame: the VBO is reused, glyphs share one
/// atlas texture, and a single program does all draws. Phase 15a only needs ~30
/// glyphs on screen at once (HP/MP readout, "SiegeFX" tag) so we don't bother
/// with persistent-mapped buffers or batched-multi-string optimizations.
/// </summary>
public sealed class TextRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly uint _vao;
    private readonly uint _vbo;
    private GlTexture? _atlasTex;
    private BitmapFont? _font;

    // 4 floats per vertex: x, y, u, v
    private const int FloatsPerVertex = 4;
    private const int VerticesPerQuad = 6;
    private float[] _verts = new float[FloatsPerVertex * VerticesPerQuad * 64];

    private const string VertexSource = @"#version 330 core
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aUv;
uniform vec2 uViewport;
out vec2 vUv;
void main() {
    // Pixel coords (origin top-left) -> NDC (-1..1, +Y up).
    float x =  (aPos.x / uViewport.x) * 2.0 - 1.0;
    float y =  1.0 - (aPos.y / uViewport.y) * 2.0;
    gl_Position = vec4(x, y, 0.0, 1.0);
    vUv = aUv;
}";

    private const string FragmentSource = @"#version 330 core
in vec2 vUv;
uniform sampler2D uAtlas;
uniform vec4 uColor;
out vec4 frag;
void main() {
    // DS1 fonts: glyph mask is in alpha, RGB carries the gradient. Keep the RGB
    // shading and tint on top, but reject zero-alpha samples to avoid bleeding.
    vec4 s = texture(uAtlas, vUv);
    if (s.a < 0.02) discard;
    frag = vec4(uColor.rgb * s.rgb, s.a * uColor.a);
}";

    public TextRenderer(GL gl)
    {
        _gl = gl;
        _shader = new Shader(_gl, VertexSource, FragmentSource);

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
        unsafe
        {
            _gl.VertexAttribPointer(0, 2, GLEnum.Float, false, FloatsPerVertex * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 2, GLEnum.Float, false, FloatsPerVertex * sizeof(float), (void*)(2 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);
        }
        _gl.BindVertexArray(0);
    }

    public BitmapFont? Font => _font;
    public bool HasFont => _font is not null;

    public void SetFont(BitmapFont font)
    {
        _font = font;
        _atlasTex?.Dispose();
        _atlasTex = new GlTexture(_gl, font.Atlas);
        // Override the default REPEAT wrap. The bottommost glyph cell can sample
        // v=1.0 exactly (its visual top is the atlas's last row), and REPEAT would
        // wrap that to the opposite edge and bleed in foreign pixels.
        _atlasTex.Bind(TextureUnit.Texture0);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);
    }

    /// <summary>Width in pixels that <paramref name="text"/> will occupy at the font's
    /// authored height. Useful for right/center alignment.</summary>
    public int MeasureWidth(string text)
    {
        if (_font is null || string.IsNullOrEmpty(text)) return 0;
        int w = 0;
        foreach (var c in text)
        {
            var g = _font.Find(c);
            if (g is null) continue;
            w += g.Value.Advance;
        }
        return w;
    }

    /// <summary>Draw <paramref name="text"/> at pixel (<paramref name="x"/>, <paramref name="y"/>)
    /// (origin top-left of the framebuffer). Color is RGBA 0..1. Caller is responsible
    /// for setting up GL state to allow blending — see
    /// <see cref="BeginPass"/>/<see cref="EndPass"/>.</summary>
    public void DrawString(int viewportW, int viewportH, string text, int x, int y, Vector4 color)
    {
        if (_font is null || _atlasTex is null || string.IsNullOrEmpty(text)) return;

        int neededFloats = text.Length * FloatsPerVertex * VerticesPerQuad;
        if (_verts.Length < neededFloats) Array.Resize(ref _verts, neededFloats);
        int written = 0;

        float aw = _font.Atlas.Width;
        float ah = _font.Atlas.Height;
        int cursorX = x;
        foreach (var c in text)
        {
            var g = _font.Find(c);
            if (g is null) { cursorX += _font.Height / 3; continue; }
            var gv = g.Value;
            if (gv.Width <= 0) { cursorX += gv.Advance; continue; }

            // DS1 .raw atlases are stored bottom-up — file-row 0 is the image's
            // visual BOTTOM. Our scan reports gv.Y as the cell's "top" in file
            // coords (the low-file-y end), which is visually the BOTTOM. To
            // render right-side-up in screen-down coords, the top vertex of the
            // quad must sample the high-file-y end of the cell.
            float u0 = gv.X / aw;
            float u1 = (gv.X + gv.Width) / aw;
            float v0 = (gv.Y + gv.Height) / ah; // top of quad → visual top of glyph
            float v1 = gv.Y / ah;                // bottom of quad → visual bottom

            float px0 = cursorX;
            float py0 = y;
            float px1 = cursorX + gv.Width;
            float py1 = y + gv.Height;

            // Two triangles, CCW in screen space (origin top-left, +Y down).
            void V(float px, float py, float uu, float vv)
            {
                _verts[written++] = px;
                _verts[written++] = py;
                _verts[written++] = uu;
                _verts[written++] = vv;
            }
            V(px0, py0, u0, v0);
            V(px0, py1, u0, v1);
            V(px1, py1, u1, v1);
            V(px0, py0, u0, v0);
            V(px1, py1, u1, v1);
            V(px1, py0, u1, v0);

            cursorX += gv.Advance;
        }
        if (written == 0) return;

        _shader.Use();
        _shader.SetVec4("uColor", color.X, color.Y, color.Z, color.W);
        var loc = _gl.GetUniformLocation(_shader.Handle, "uViewport");
        if (loc >= 0) _gl.Uniform2(loc, (float)viewportW, (float)viewportH);
        _atlasTex.Bind(TextureUnit.Texture0);
        _shader.SetInt("uAtlas", 0);

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* p = _verts)
                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(written * sizeof(float)),
                    p, GLEnum.DynamicDraw);
        }
        _gl.DrawArrays(GLEnum.Triangles, 0, (uint)(written / FloatsPerVertex));
        _gl.BindVertexArray(0);
    }

    /// <summary>Caller pattern: call before any DrawString in the frame, EndPass after.
    /// Disables depth, enables alpha blend, restores both at EndPass.</summary>
    public void BeginPass()
    {
        _gl.Disable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    public void EndPass()
    {
        _gl.Disable(EnableCap.Blend);
        _gl.Enable(EnableCap.DepthTest);
    }

    public void Dispose()
    {
        _atlasTex?.Dispose();
        _shader.Dispose();
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
