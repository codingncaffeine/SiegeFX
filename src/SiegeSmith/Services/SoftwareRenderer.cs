using System;
using System.Numerics;

namespace SiegeSmith.Services;

/// <summary>A tiny CPU triangle rasteriser for the model preview. No GPU/GL dependency, so it
/// runs anywhere and its output is deterministic. Renders a flat-shaded, z-buffered image (or a
/// wireframe) of a mesh from an orbit camera. Geometry is supplied as flattened triangles — three
/// consecutive vertices per triangle — in Z-up model space, matching DS1's axis convention.</summary>
public static class SoftwareRenderer
{
    /// <summary>Renders to a fresh BGRA byte buffer of length <c>width*height*4</c>.</summary>
    public static byte[] Render(
        Vector3[] verts, Vector3[] normals,
        int width, int height,
        Vector3 center, float radius,
        float yaw, float pitch, float dist,
        bool wireframe)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var px = new byte[width * height * 4];
        FillBackground(px);

        if (verts.Length < 3) return px;

        pitch = Math.Clamp(pitch, -1.50f, 1.50f);
        radius = MathF.Max(radius, 0.001f);
        dist = MathF.Max(dist, radius * 0.2f);

        var eye = center + dist * new Vector3(
            MathF.Cos(pitch) * MathF.Cos(yaw),
            MathF.Cos(pitch) * MathF.Sin(yaw),
            MathF.Sin(pitch));
        var view = Matrix4x4.CreateLookAt(eye, center, new Vector3(0, 0, 1));
        float aspect = width / (float)height;
        float near = MathF.Max(0.01f, radius * 0.05f);
        float far = dist + radius * 6f + 1f;
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspect, near, far);
        var vp = view * proj;

        var light = Vector3.Normalize(new Vector3(0.35f, 0.30f, 0.90f));

        int n = verts.Length;
        var sx = new float[n]; var sy = new float[n]; var sz = new float[n]; var ok = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var clip = Vector4.Transform(new Vector4(verts[i], 1f), vp);
            if (clip.W <= 1e-4f) { ok[i] = false; continue; }
            var vv = Vector4.Transform(new Vector4(verts[i], 1f), view);
            float inv = 1f / clip.W;
            sx[i] = (clip.X * inv * 0.5f + 0.5f) * width;
            sy[i] = (1f - (clip.Y * inv * 0.5f + 0.5f)) * height;
            sz[i] = -vv.Z; // right-handed: farther from camera = larger
            ok[i] = true;
        }

        var zbuf = new float[width * height];
        Array.Fill(zbuf, float.MaxValue);

        int triCount = n / 3;
        for (int t = 0; t < triCount; t++)
        {
            int a = 3 * t, b = 3 * t + 1, c = 3 * t + 2;
            if (!ok[a] || !ok[b] || !ok[c]) continue;

            if (wireframe)
            {
                DrawLine(px, width, height, sx[a], sy[a], sx[b], sy[b]);
                DrawLine(px, width, height, sx[b], sy[b], sx[c], sy[c]);
                DrawLine(px, width, height, sx[c], sy[c], sx[a], sy[a]);
                continue;
            }

            var fn = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            if (fn.LengthSquared() < 1e-12f) continue;
            fn = Vector3.Normalize(fn);
            float shade = 0.22f + 0.78f * MathF.Abs(Vector3.Dot(fn, light)); // two-sided
            byte cr = (byte)(210 * shade), cg = (byte)(202 * shade), cb = (byte)(188 * shade);

            RasterTriangle(px, zbuf, width, height,
                sx[a], sy[a], sz[a], sx[b], sy[b], sz[b], sx[c], sy[c], sz[c],
                cr, cg, cb);
        }
        DrawAxisGizmo(px, width, height, yaw, pitch);
        return px;
    }

    /// <summary>A decoded texture ready for sampling: BGRA8888, row 0 at top (DirectX/DS1 origin,
    /// so UV v=0 maps to the top row — no flip needed).</summary>
    public readonly struct Texture
    {
        public readonly byte[] Bgra;
        public readonly int W;
        public readonly int H;
        public Texture(byte[] bgra, int w, int h) { Bgra = bgra; W = w; H = h; }
        public bool Valid => Bgra is { Length: > 0 } && W > 0 && H > 0 && Bgra.Length >= W * H * 4;
    }

    /// <summary>Like <see cref="Render"/> but samples a texture per triangle instead of flat-shading,
    /// so the preview matches how the asset looks in-game. <paramref name="uvs"/> is parallel to
    /// <paramref name="verts"/> (one UV per corner); <paramref name="triTexture"/> is one entry per
    /// triangle indexing <paramref name="textures"/> (-1, or an invalid texture, flat-shades that
    /// triangle so a missing texture degrades gracefully). UV interpolation is perspective-correct.</summary>
    public static byte[] RenderTextured(
        Vector3[] verts, Vector3[] normals, Vector2[] uvs, int[] triTexture, Texture[] textures,
        int width, int height,
        Vector3 center, float radius,
        float yaw, float pitch, float dist)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var px = new byte[width * height * 4];
        FillBackground(px);
        if (verts.Length < 3) return px;

        pitch = Math.Clamp(pitch, -1.50f, 1.50f);
        radius = MathF.Max(radius, 0.001f);
        dist = MathF.Max(dist, radius * 0.2f);

        var eye = center + dist * new Vector3(
            MathF.Cos(pitch) * MathF.Cos(yaw),
            MathF.Cos(pitch) * MathF.Sin(yaw),
            MathF.Sin(pitch));
        var view = Matrix4x4.CreateLookAt(eye, center, new Vector3(0, 0, 1));
        float aspect = width / (float)height;
        float near = MathF.Max(0.01f, radius * 0.05f);
        float far = dist + radius * 6f + 1f;
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspect, near, far);
        var vp = view * proj;

        var light = Vector3.Normalize(new Vector3(0.35f, 0.30f, 0.90f));

        int n = verts.Length;
        var sx = new float[n]; var sy = new float[n]; var sz = new float[n];
        var iw = new float[n]; var uw = new float[n]; var vw = new float[n]; var ok = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var clip = Vector4.Transform(new Vector4(verts[i], 1f), vp);
            if (clip.W <= 1e-4f) { ok[i] = false; continue; }
            var vv = Vector4.Transform(new Vector4(verts[i], 1f), view);
            float inv = 1f / clip.W;
            sx[i] = (clip.X * inv * 0.5f + 0.5f) * width;
            sy[i] = (1f - (clip.Y * inv * 0.5f + 0.5f)) * height;
            sz[i] = -vv.Z;
            iw[i] = inv;
            uw[i] = uvs[i].X * inv;
            vw[i] = uvs[i].Y * inv;
            ok[i] = true;
        }

        var zbuf = new float[width * height];
        Array.Fill(zbuf, float.MaxValue);

        int triCount = n / 3;
        for (int t = 0; t < triCount; t++)
        {
            int a = 3 * t, b = 3 * t + 1, c = 3 * t + 2;
            if (!ok[a] || !ok[b] || !ok[c]) continue;

            var fn = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            if (fn.LengthSquared() < 1e-12f) continue;
            fn = Vector3.Normalize(fn);
            float shade = 0.55f + 0.45f * MathF.Abs(Vector3.Dot(fn, light)); // gentle, so the texture reads

            int ti = t < triTexture.Length ? triTexture[t] : -1;
            var tex = ti >= 0 && ti < textures.Length ? textures[ti] : default;

            if (tex.Valid)
                RasterTriangleTextured(px, zbuf, width, height,
                    sx[a], sy[a], sz[a], iw[a], uw[a], vw[a],
                    sx[b], sy[b], sz[b], iw[b], uw[b], vw[b],
                    sx[c], sy[c], sz[c], iw[c], uw[c], vw[c],
                    tex, shade);
            else
                RasterTriangle(px, zbuf, width, height,
                    sx[a], sy[a], sz[a], sx[b], sy[b], sz[b], sx[c], sy[c], sz[c],
                    (byte)(190 * shade), (byte)(182 * shade), (byte)(170 * shade));
        }
        DrawAxisGizmo(px, width, height, yaw, pitch);
        return px;
    }

    private static void RasterTriangleTextured(byte[] px, float[] z, int w, int h,
        float x0, float y0, float d0, float iw0, float uw0, float vw0,
        float x1, float y1, float d1, float iw1, float uw1, float vw1,
        float x2, float y2, float d2, float iw2, float uw2, float vw2,
        Texture tex, float shade)
    {
        float area = Edge(x0, y0, x1, y1, x2, y2);
        if (MathF.Abs(area) < 1e-6f) return;
        float inv = 1f / area;

        int minX = Math.Max(0, (int)MathF.Floor(Math.Min(x0, Math.Min(x1, x2))));
        int maxX = Math.Min(w - 1, (int)MathF.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
        int minY = Math.Max(0, (int)MathF.Floor(Math.Min(y0, Math.Min(y1, y2))));
        int maxY = Math.Min(h - 1, (int)MathF.Ceiling(Math.Max(y0, Math.Max(y1, y2))));

        for (int py = minY; py <= maxY; py++)
        {
            for (int pxi = minX; pxi <= maxX; pxi++)
            {
                float fx = pxi + 0.5f, fy = py + 0.5f;
                float w0 = Edge(x1, y1, x2, y2, fx, fy) * inv;
                float w1 = Edge(x2, y2, x0, y0, fx, fy) * inv;
                float w2 = Edge(x0, y0, x1, y1, fx, fy) * inv;
                if (w0 < 0f || w1 < 0f || w2 < 0f) continue;

                float depth = w0 * d0 + w1 * d1 + w2 * d2;
                int zi = py * w + pxi;
                if (depth >= z[zi]) continue;

                float iwp = w0 * iw0 + w1 * iw1 + w2 * iw2;
                if (iwp <= 1e-8f) continue;
                float u = (w0 * uw0 + w1 * uw1 + w2 * uw2) / iwp;
                float v = (w0 * vw0 + w1 * vw1 + w2 * vw2) / iwp;

                int tx = (int)MathF.Floor(u * tex.W); tx = ((tx % tex.W) + tex.W) % tex.W;
                int ty = (int)MathF.Floor(v * tex.H); ty = ((ty % tex.H) + tex.H) % tex.H;
                int si = (ty * tex.W + tx) * 4;

                z[zi] = depth;
                int pi = zi * 4;
                px[pi]     = (byte)(tex.Bgra[si]     * shade);
                px[pi + 1] = (byte)(tex.Bgra[si + 1] * shade);
                px[pi + 2] = (byte)(tex.Bgra[si + 2] * shade);
                px[pi + 3] = 0xFF;
            }
        }
    }

    private static void FillBackground(byte[] px)
    {
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = 0x16; px[i + 1] = 0x14; px[i + 2] = 0x14; px[i + 3] = 0xFF; // BGRA ≈ #141416
        }
    }

    private static float Edge(float ax, float ay, float bx, float by, float px, float py) =>
        (bx - ax) * (py - ay) - (by - ay) * (px - ax);

    private static void RasterTriangle(byte[] px, float[] z, int w, int h,
        float x0, float y0, float d0, float x1, float y1, float d1, float x2, float y2, float d2,
        byte r, byte g, byte b)
    {
        float area = Edge(x0, y0, x1, y1, x2, y2);
        if (MathF.Abs(area) < 1e-6f) return;
        float inv = 1f / area;

        int minX = Math.Max(0, (int)MathF.Floor(Math.Min(x0, Math.Min(x1, x2))));
        int maxX = Math.Min(w - 1, (int)MathF.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
        int minY = Math.Max(0, (int)MathF.Floor(Math.Min(y0, Math.Min(y1, y2))));
        int maxY = Math.Min(h - 1, (int)MathF.Ceiling(Math.Max(y0, Math.Max(y1, y2))));

        for (int py = minY; py <= maxY; py++)
        {
            for (int pxi = minX; pxi <= maxX; pxi++)
            {
                float fx = pxi + 0.5f, fy = py + 0.5f;
                // Dividing edge functions by the signed area yields positive weights inside
                // for both windings, so the mesh renders two-sided.
                float w0 = Edge(x1, y1, x2, y2, fx, fy) * inv;
                float w1 = Edge(x2, y2, x0, y0, fx, fy) * inv;
                float w2 = Edge(x0, y0, x1, y1, fx, fy) * inv;
                if (w0 < 0f || w1 < 0f || w2 < 0f) continue;

                float depth = w0 * d0 + w1 * d1 + w2 * d2;
                int zi = py * w + pxi;
                if (depth >= z[zi]) continue;
                z[zi] = depth;
                int pi = zi * 4;
                px[pi] = b; px[pi + 1] = g; px[pi + 2] = r; px[pi + 3] = 0xFF;
            }
        }
    }

    private static void DrawLine(byte[] px, int w, int h, float fx0, float fy0, float fx1, float fy1)
    {
        int x0 = (int)fx0, y0 = (int)fy0, x1 = (int)fx1, y1 = (int)fy1;
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            if ((uint)x0 < (uint)w && (uint)y0 < (uint)h)
            {
                int pi = (y0 * w + x0) * 4;
                px[pi] = 0x35; px[pi + 1] = 0x35; px[pi + 2] = 0xE0; px[pi + 3] = 0xFF; // BGRA ≈ accent #E03535
            }
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>Draws a small XYZ orientation triad in the lower-left corner (X red, Y green,
    /// Z blue) that rotates with the camera, so you can always read which way the view is facing —
    /// the same "what am I looking at" cue asset viewers show. Overlaid after the scene, ignoring
    /// depth, at a fixed screen position; only the camera rotation (yaw/pitch) drives it.</summary>
    private static void DrawAxisGizmo(byte[] px, int w, int h, float yaw, float pitch)
    {
        // Camera direction (center→eye); a pure-rotation view frame from it orients the triad.
        var dir = new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch));
        var view = Matrix4x4.CreateLookAt(dir, Vector3.Zero, new Vector3(0, 0, 1));
        float len = 24f, ox = 34f, oy = h - 34f;

        (Vector3 axis, byte r, byte g, byte b)[] axes =
        {
            (new Vector3(1, 0, 0), 230, 80, 80),   // X — red
            (new Vector3(0, 1, 0), 90, 200, 90),   // Y — green
            (new Vector3(0, 0, 1), 100, 150, 235), // Z — blue (up in DS1)
        };
        // Back-to-front: draw the axis pointing away first so the nearer ones overlay it.
        Array.Sort(axes, (p, q) =>
            Vector3.TransformNormal(p.axis, view).Z.CompareTo(Vector3.TransformNormal(q.axis, view).Z));
        foreach (var (axis, r, g, b) in axes)
        {
            var v = Vector3.TransformNormal(axis, view);
            float ex = ox + v.X * len, ey = oy - v.Y * len;
            DrawLineRGB(px, w, h, ox, oy, ex, ey, r, g, b);
            DrawDot(px, w, h, ex, ey, r, g, b);
        }
    }

    private static void DrawLineRGB(byte[] px, int w, int h, float fx0, float fy0, float fx1, float fy1, byte r, byte g, byte b)
    {
        int x0 = (int)fx0, y0 = (int)fy0, x1 = (int)fx1, y1 = (int)fy1;
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            if ((uint)x0 < (uint)w && (uint)y0 < (uint)h)
            {
                int pi = (y0 * w + x0) * 4;
                px[pi] = b; px[pi + 1] = g; px[pi + 2] = r; px[pi + 3] = 0xFF;
            }
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private static void DrawDot(byte[] px, int w, int h, float cx, float cy, byte r, byte g, byte b)
    {
        int x0 = (int)cx, y0 = (int)cy;
        for (int dyi = -2; dyi <= 2; dyi++)
            for (int dxi = -2; dxi <= 2; dxi++)
            {
                if (dxi * dxi + dyi * dyi > 4) continue;
                int x = x0 + dxi, y = y0 + dyi;
                if ((uint)x < (uint)w && (uint)y < (uint)h)
                {
                    int pi = (y * w + x) * 4;
                    px[pi] = b; px[pi + 1] = g; px[pi + 2] = r; px[pi + 3] = 0xFF;
                }
            }
    }
}
