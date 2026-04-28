using System.Numerics;
using Silk.NET.OpenGL;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Textured-rect emitter for HUD icons (inventory cells, equipment slots,
/// minimap markers). Pairs with <see cref="BarRenderer"/> /
/// <see cref="TextRenderer"/> — same screen-down ortho space, same blend
/// pass — but samples an arbitrary <see cref="GlTexture"/> per draw.
///
/// V coordinates are flipped so DS1's bottom-up .raw textures (the
/// <c>b_gui_ig_*</c> icon set, all of which ship that way) render
/// right-side-up under the screen-down origin shader convention.
/// </summary>
public sealed class IconRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly uint _vao;
    private readonly uint _vbo;

    private const int FloatsPerVertex = 4; // x, y, u, v
    private const int VerticesPerQuad = 6;
    private readonly float[] _verts = new float[FloatsPerVertex * VerticesPerQuad];

    private const string VertexSource = @"#version 330 core
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aUv;
uniform vec2 uViewport;
out vec2 vUv;
void main() {
    float x =  (aPos.x / uViewport.x) * 2.0 - 1.0;
    float y =  1.0 - (aPos.y / uViewport.y) * 2.0;
    gl_Position = vec4(x, y, 0.0, 1.0);
    vUv = aUv;
}";

    private const string FragmentSource = @"#version 330 core
in vec2 vUv;
uniform sampler2D uTex;
uniform vec4 uTint;
out vec4 frag;
void main() {
    vec4 s = texture(uTex, vUv);
    if (s.a < 0.02) discard;
    frag = vec4(s.rgb * uTint.rgb, s.a * uTint.a);
}";

    public IconRenderer(GL gl)
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

    /// <summary>Draw <paramref name="tex"/> stretched to fill the rect at
    /// (<paramref name="x"/>, <paramref name="y"/>). Tint multiplies the
    /// sample (RGBA 0..1); pass <c>(1,1,1,1)</c> for the unmodified icon.
    /// Caller must have an active blend pass — see
    /// <see cref="TextRenderer.BeginPass"/>.</summary>
    public void DrawIcon(int viewportW, int viewportH, GlTexture tex,
                         int x, int y, int w, int h, Vector4 tint)
    {
        if (w <= 0 || h <= 0 || tint.W <= 0f) return;

        float px0 = x;
        float py0 = y;
        float px1 = x + w;
        float py1 = y + h;
        // V flipped: top of quad samples v=1 (visual top of bottom-up image).
        const float u0 = 0f, u1 = 1f, v0 = 1f, v1 = 0f;

        int wi = 0;
        void V(float px, float py, float uu, float vv)
        {
            _verts[wi++] = px;
            _verts[wi++] = py;
            _verts[wi++] = uu;
            _verts[wi++] = vv;
        }
        V(px0, py0, u0, v0);
        V(px0, py1, u0, v1);
        V(px1, py1, u1, v1);
        V(px0, py0, u0, v0);
        V(px1, py1, u1, v1);
        V(px1, py0, u1, v0);

        _shader.Use();
        _shader.SetVec4("uTint", tint.X, tint.Y, tint.Z, tint.W);
        var loc = _gl.GetUniformLocation(_shader.Handle, "uViewport");
        if (loc >= 0) _gl.Uniform2(loc, (float)viewportW, (float)viewportH);
        tex.Bind(TextureUnit.Texture0);
        _shader.SetInt("uTex", 0);

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* p = _verts)
                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(_verts.Length * sizeof(float)),
                    p, GLEnum.DynamicDraw);
        }
        _gl.DrawArrays(GLEnum.Triangles, 0, (uint)VerticesPerQuad);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _shader.Dispose();
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
