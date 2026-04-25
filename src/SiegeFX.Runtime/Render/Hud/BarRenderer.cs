using System.Numerics;
using Silk.NET.OpenGL;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Solid-color quad emitter for HUD bars and panels. Pairs with
/// <see cref="TextRenderer"/> — same screen-down ortho space, same blend pass —
/// but without a texture sample so colored fills don't have to round-trip
/// through a 1x1 atlas.
/// </summary>
public sealed class BarRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly uint _vao;
    private readonly uint _vbo;

    // 2 floats per vertex: x, y. Color is a uniform per-draw.
    private const int FloatsPerVertex = 2;
    private const int VerticesPerQuad = 6;
    private readonly float[] _verts = new float[FloatsPerVertex * VerticesPerQuad];

    private const string VertexSource = @"#version 330 core
layout(location=0) in vec2 aPos;
uniform vec2 uViewport;
void main() {
    float x =  (aPos.x / uViewport.x) * 2.0 - 1.0;
    float y =  1.0 - (aPos.y / uViewport.y) * 2.0;
    gl_Position = vec4(x, y, 0.0, 1.0);
}";

    private const string FragmentSource = @"#version 330 core
uniform vec4 uColor;
out vec4 frag;
void main() { frag = uColor; }";

    public BarRenderer(GL gl)
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
        }
        _gl.BindVertexArray(0);
    }

    /// <summary>Draw a filled rectangle at pixel (<paramref name="x"/>, <paramref name="y"/>)
    /// (top-left origin). Color is RGBA 0..1. Caller must have an active blend pass —
    /// see <see cref="TextRenderer.BeginPass"/>/<see cref="TextRenderer.EndPass"/>.</summary>
    public void DrawRect(int viewportW, int viewportH, int x, int y, int w, int h, Vector4 color)
    {
        if (w <= 0 || h <= 0 || color.W <= 0f) return;

        float px0 = x;
        float py0 = y;
        float px1 = x + w;
        float py1 = y + h;
        int wi = 0;
        void V(float px, float py) { _verts[wi++] = px; _verts[wi++] = py; }
        V(px0, py0); V(px0, py1); V(px1, py1);
        V(px0, py0); V(px1, py1); V(px1, py0);

        _shader.Use();
        _shader.SetVec4("uColor", color.X, color.Y, color.Z, color.W);
        var loc = _gl.GetUniformLocation(_shader.Handle, "uViewport");
        if (loc >= 0) _gl.Uniform2(loc, (float)viewportW, (float)viewportH);

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

    /// <summary>Draw a hollow 1-pixel border at (<paramref name="x"/>, <paramref name="y"/>) with
    /// the given outer dimensions. Cheap convenience built on four <see cref="DrawRect"/> calls.</summary>
    public void DrawBorder(int viewportW, int viewportH, int x, int y, int w, int h, Vector4 color)
    {
        DrawRect(viewportW, viewportH, x,         y,         w, 1, color); // top
        DrawRect(viewportW, viewportH, x,         y + h - 1, w, 1, color); // bottom
        DrawRect(viewportW, viewportH, x,         y,         1, h, color); // left
        DrawRect(viewportW, viewportH, x + w - 1, y,         1, h, color); // right
    }

    public void Dispose()
    {
        _shader.Dispose();
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
