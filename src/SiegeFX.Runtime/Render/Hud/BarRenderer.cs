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

    /// <summary>Phase 21-SC-INV-B (round 3) — chamfered-corner filled rect.
    /// Top/bottom corner radii can be set independently so the title bar
    /// can round its top corners while leaving its bottom flush with the
    /// panel separator. <paramref name="topRadius"/>/<paramref name="bottomRadius"/>
    /// are clamped if the rect is too small for the requested chamfer.</summary>
    public void DrawRoundedRect(int viewportW, int viewportH, int x, int y, int w, int h,
                                Vector4 color, int topRadius = 2, int bottomRadius = 2)
    {
        if (w <= 0 || h <= 0 || color.W <= 0f) return;
        if (topRadius < 0) topRadius = 0;
        if (bottomRadius < 0) bottomRadius = 0;
        // Clamp so chamfers can't collide.
        int maxR = Math.Max(0, Math.Min(w / 2, h / 2));
        if (topRadius > maxR) topRadius = maxR;
        if (bottomRadius > maxR) bottomRadius = maxR;

        // Top chamfer rows (inset shrinks by 1 each step inward).
        for (int i = 0; i < topRadius; i++)
        {
            int inset = topRadius - i;
            int rw = w - 2 * inset;
            if (rw > 0) DrawRect(viewportW, viewportH, x + inset, y + i, rw, 1, color);
        }
        // Body — full width between the two chamfers.
        int bodyY = y + topRadius;
        int bodyH = h - topRadius - bottomRadius;
        if (bodyH > 0) DrawRect(viewportW, viewportH, x, bodyY, w, bodyH, color);
        // Bottom chamfer rows.
        for (int i = 0; i < bottomRadius; i++)
        {
            int inset = bottomRadius - i;
            int rw = w - 2 * inset;
            if (rw > 0) DrawRect(viewportW, viewportH, x + inset, y + h - 1 - i, rw, 1, color);
        }
    }

    /// <summary>Phase 21-SC-INV-B (round 4) — DS1-style horizontal gradient
    /// fill for HP/MP vials: full-strength tint at the center column, falling
    /// off to <paramref name="edgeMult"/>× at the left/right edges. Drawn as
    /// 1-pixel-wide vertical strips so the gradient is visible without a real
    /// shader. <paramref name="gamma"/> controls falloff curvature; lower
    /// values widen the visible falloff zone instead of keeping the bright
    /// center wide. (Round 6) defaults bumped to a stronger gradient — the
    /// 0.55 edge / 2.0 gamma pair was visually too subtle on the small vials.</summary>
    public void DrawHGradientFill(int viewportW, int viewportH, int x, int y, int w, int h,
                                  Vector4 baseColor, float edgeMult = 0.30f, float gamma = 1.3f)
    {
        if (w <= 0 || h <= 0 || baseColor.W <= 0f) return;
        if (w == 1) { DrawRect(viewportW, viewportH, x, y, 1, h, baseColor); return; }
        for (int i = 0; i < w; i++)
        {
            float center = (i + 0.5f) / w;
            float t = MathF.Abs(center * 2f - 1f); // 0 at center, 1 at edges
            float darken = edgeMult + (1f - edgeMult) * (1f - MathF.Pow(t, gamma));
            var c = new Vector4(baseColor.X * darken, baseColor.Y * darken, baseColor.Z * darken, baseColor.W);
            DrawRect(viewportW, viewportH, x + i, y, 1, h, c);
        }
    }

    /// <summary>Phase 21-SC-INV-B (round 3) — 1-pixel chamfered border that
    /// pairs with <see cref="DrawRoundedRect"/>. Stair-steps the corner
    /// pixels so the outline traces the same chamfered silhouette.</summary>
    public void DrawRoundedBorder(int viewportW, int viewportH, int x, int y, int w, int h,
                                  Vector4 color, int radius = 2)
    {
        if (radius <= 0 || w < 2 * radius + 1 || h < 2 * radius + 1)
        {
            DrawBorder(viewportW, viewportH, x, y, w, h, color);
            return;
        }
        // Straight edges (inset by radius at the corners).
        DrawRect(viewportW, viewportH, x + radius, y,             w - 2 * radius, 1, color); // top
        DrawRect(viewportW, viewportH, x + radius, y + h - 1,     w - 2 * radius, 1, color); // bottom
        DrawRect(viewportW, viewportH, x,             y + radius, 1, h - 2 * radius, color); // left
        DrawRect(viewportW, viewportH, x + w - 1,     y + radius, 1, h - 2 * radius, color); // right
        // Stair-step corner pixels.
        for (int i = 0; i < radius; i++)
        {
            int inset = radius - i;
            DrawRect(viewportW, viewportH, x + inset - 1,    y + i,         1, 1, color); // TL
            DrawRect(viewportW, viewportH, x + w - inset,    y + i,         1, 1, color); // TR
            DrawRect(viewportW, viewportH, x + inset - 1,    y + h - 1 - i, 1, 1, color); // BL
            DrawRect(viewportW, viewportH, x + w - inset,    y + h - 1 - i, 1, 1, color); // BR
        }
    }

    public void Dispose()
    {
        _shader.Dispose();
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
