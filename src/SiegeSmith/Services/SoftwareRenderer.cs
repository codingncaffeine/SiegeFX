using System;
using System.Numerics;

namespace SiegeSmith.Services;

/// <summary>A tiny CPU triangle rasteriser for the model preview. No GPU/GL dependency, so it
/// runs anywhere and its output is deterministic. Renders a flat-shaded, z-buffered image (or a
/// wireframe) of a mesh from an orbit camera. Geometry is supplied as flattened triangles — three
/// consecutive vertices per triangle — in Z-up model space, matching DS1's axis convention.</summary>
public static class SoftwareRenderer
{
    /// <summary>A directional light for the preview: a (normalized) world-space direction plus a
    /// linear RGB colour (0..1) scaled by intensity. Mirrors the engine's directional lights so the
    /// World Builder can preview the level's mood before launch.</summary>
    public readonly record struct DirLight(Vector3 Dir, float R, float G, float B, float Intensity);

    // The built-in preview sun, used when no lights are supplied (keeps every other viewer unchanged).
    private static readonly Vector3 DefaultLightDir = Vector3.Normalize(new Vector3(0.35f, 0.30f, 0.90f));

    /// <summary>Per-channel light factor for a face normal. With no lights, reproduces the built-in
    /// single-sun shade (<paramref name="ambient"/> + <paramref name="direct"/>·|N·L|) in white so the
    /// default look is unchanged; with lights, sums each light's |N·L|·intensity·colour.</summary>
    private static (float r, float g, float b) Lighting(Vector3 fn, DirLight[]? lights, float ambient, float direct)
    {
        if (lights is null || lights.Length == 0)
        {
            float s = ambient + direct * MathF.Abs(Vector3.Dot(fn, DefaultLightDir));
            return (s, s, s);
        }
        float r = ambient, g = ambient, b = ambient;
        foreach (var l in lights)
        {
            float nl = MathF.Abs(Vector3.Dot(fn, l.Dir)) * l.Intensity * direct;
            r += nl * l.R; g += nl * l.G; b += nl * l.B;
        }
        return (r, g, b);
    }

    private static byte ClampByte(float v) => (byte)Math.Clamp(v, 0f, 255f);

    /// <summary>Renders to a fresh BGRA byte buffer of length <c>width*height*4</c>.</summary>
    /// <summary><paramref name="triColor"/>, when supplied, is one packed 0xRRGGBB per triangle (-1 = the
    /// default terrain tint). Used to colour-code editor markers (fire/smoke/trigger/command) so they're
    /// distinguishable at a glance instead of an anonymous beige box.</summary>
    /// <summary>ED-8 — linear camera-distance fog for the mood audition,
    /// applied as a post-pass over the view-space z-buffer (matches the
    /// engine's shader: lerp toward the fog colour from Near to Far meters;
    /// the background fills with the fog colour, which IS the horizon).</summary>
    public readonly record struct Fog(float Near, float Far, byte R, byte G, byte B);

    private static void ApplyFog(byte[] px, float[] zbuf, Fog f)
    {
        float inv = 1f / MathF.Max(f.Far - f.Near, 0.001f);
        for (int i = 0; i < zbuf.Length; i++)
        {
            float z = zbuf[i];
            float t = z == float.MaxValue ? 1f : Math.Clamp((z - f.Near) * inv, 0f, 1f);
            if (t <= 0f) continue;
            int o = i * 4;
            px[o]     = (byte)(px[o]     + (int)((f.B - px[o])     * t));
            px[o + 1] = (byte)(px[o + 1] + (int)((f.G - px[o + 1]) * t));
            px[o + 2] = (byte)(px[o + 2] + (int)((f.R - px[o + 2]) * t));
        }
    }

    /// <summary>A soft round particle: world position + world radius, drawn as
    /// a screen-space splat with quadratic radial falloff — z-tested against
    /// the scene, no z-write. <paramref name="Additive"/> brightens (fire glow);
    /// otherwise the splat alpha-blends (smoke). This is what makes emitter
    /// particles read as puffs of fire and smoke instead of polygons: geometry
    /// can only ever have hard edges in a triangle rasterizer.</summary>
    public readonly record struct Splat(Vector3 Center, float Radius, byte R, byte G, byte B, float Alpha, bool Additive);

    private static void DrawSplats(byte[] px, float[] zbuf, int width, int height,
        in Matrix4x4 view, in Matrix4x4 proj, IReadOnlyList<Splat> splats, Fog? fog)
    {
        if (splats.Count == 0) return;

        // Project every splat first, then paint far→near so overlapping
        // alpha layers stack correctly.
        var order = new List<(float Depth, float Cx, float Cy, float Rpx, Splat S)>(splats.Count);
        foreach (var s in splats)
        {
            var vv = Vector4.Transform(new Vector4(s.Center, 1f), view);
            float depth = -vv.Z;
            if (depth <= 0.01f) continue;
            var clip = Vector4.Transform(vv, proj);
            if (clip.W <= 1e-4f) continue;
            float inv = 1f / clip.W;
            float cx = (clip.X * inv * 0.5f + 0.5f) * width;
            float cy = (1f - (clip.Y * inv * 0.5f + 0.5f)) * height;
            // World radius → pixels via the projection's x scale (works for
            // both perspective, where clip.W = depth, and ortho, where W = 1).
            float rpx = s.Radius * proj.M11 * inv * 0.5f * width;
            if (rpx < 0.75f) rpx = 0.75f;
            if (rpx > 600f) continue; // degenerate close-up — skip rather than fill the frame
            if (cx + rpx < 0 || cx - rpx >= width || cy + rpx < 0 || cy - rpx >= height) continue;
            order.Add((depth, cx, cy, rpx, s));
        }
        order.Sort((a, b) => b.Depth.CompareTo(a.Depth));

        foreach (var (depth, cx, cy, rpx, s) in order)
        {
            float a0 = s.Alpha;
            byte cr = s.R, cg = s.G, cb = s.B;
            if (fog is { } f)
            {
                // Fog the splat by ITS OWN depth (the post-pass fog only knows
                // the geometry behind it): tint toward the fog colour and thin out.
                float t = Math.Clamp((depth - f.Near) / MathF.Max(f.Far - f.Near, 0.001f), 0f, 1f);
                cr = (byte)(cr + (int)((f.R - cr) * t));
                cg = (byte)(cg + (int)((f.G - cg) * t));
                cb = (byte)(cb + (int)((f.B - cb) * t));
                a0 *= 1f - t * 0.85f;
            }
            if (a0 <= 0.004f) continue;

            int x0 = Math.Max(0, (int)(cx - rpx)), x1 = Math.Min(width - 1, (int)(cx + rpx) + 1);
            int y0 = Math.Max(0, (int)(cy - rpx)), y1 = Math.Min(height - 1, (int)(cy + rpx) + 1);
            float invR2 = 1f / (rpx * rpx);
            for (int y = y0; y <= y1; y++)
            {
                float dy = y - cy;
                int row = y * width;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - cx;
                    float d2 = (dx * dx + dy * dy) * invR2;
                    if (d2 >= 1f) continue;               // outside the DISC — corners stay untouched
                    int idx = row + x;
                    if (zbuf[idx] < depth) continue;      // scene geometry in front
                    float fall = 1f - d2;
                    float a = a0 * fall * fall;           // quadratic falloff = soft round core
                    if (a <= 0.004f) continue;
                    int o = idx * 4;
                    if (s.Additive)
                    {
                        px[o]     = ClampByte(px[o]     + cb * a);
                        px[o + 1] = ClampByte(px[o + 1] + cg * a);
                        px[o + 2] = ClampByte(px[o + 2] + cr * a);
                    }
                    else
                    {
                        px[o]     = (byte)(px[o]     + (int)((cb - px[o])     * a));
                        px[o + 1] = (byte)(px[o + 1] + (int)((cg - px[o + 1]) * a));
                        px[o + 2] = (byte)(px[o + 2] + (int)((cr - px[o + 2]) * a));
                    }
                }
            }
        }
    }

    public static byte[] Render(
        Vector3[] verts, Vector3[] normals,
        int width, int height,
        Vector3 center, float radius,
        float yaw, float pitch, float dist,
        bool wireframe, DirLight[]? lights = null, int[]? triColor = null,
        bool ortho = false, Fog? fog = null, IReadOnlyList<Splat>? splats = null)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var px = new byte[width * height * 4];
        FillBackground(px);

        if (verts.Length < 3) return px;

        pitch = Math.Clamp(pitch, -1.50f, 1.50f);
        radius = MathF.Max(radius, 0.001f);
        dist = MathF.Max(dist, 0.05f); // deep zoom: the floor is absolute, not scene-relative

        var eye = center + dist * CamDir(yaw, pitch);
        var view = Matrix4x4.CreateLookAt(eye, center, RolledUp(CamDir(yaw, pitch), 0f));
        float aspect = width / (float)height;
        // Near plane tracks the CAMERA DISTANCE when zoomed in close (a
        // scene-relative near would clip everything at deep zoom). The
        // z-buffer stores view-space distance, so a tiny near costs nothing.
        float near = MathF.Max(0.005f, MathF.Min(radius * 0.05f, dist * 0.2f));
        float far = dist + radius * 6f + 1f;
        // ED-2 — orthographic option: the ortho frustum height matches what
        // the perspective view would span at the orbit distance, so toggling
        // projections keeps roughly the same framing.
        var proj = ortho
            ? Matrix4x4.CreateOrthographic(dist * 0.828f * aspect, dist * 0.828f, near, far)
            : Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspect, near, far);
        var vp = view * proj;

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

            int ov = triColor is not null && t < triColor.Length ? triColor[t] : -1;
            // GAME-1 — triColor's high byte is particle alpha (0 = opaque
            // marker, back-compat; -1 = untinted). Effect particles render
            // self-lit and alpha-blended with no z-write, so fire/smoke
            // reads as wisps. High-alpha values make the int negative, so
            // the sentinel test is explicitly against -1.
            int pa = ov != -1 ? (int)((uint)ov >> 24) : 0;
            if (pa > 0)
            {
                RasterTriangleBlend(px, zbuf, width, height,
                    sx[a], sy[a], sz[a], sx[b], sy[b], sz[b], sx[c], sy[c], sz[c],
                    (byte)((ov >> 16) & 0xFF), (byte)((ov >> 8) & 0xFF), (byte)(ov & 0xFF), pa / 255f);
                continue;
            }
            byte cr, cg, cb;
            if (ov >= 0)
            {
                // Marker: brighter ambient so the colour reads, but keep some shading so it's a 3D cube.
                var (mlr, mlg, mlb) = Lighting(fn, lights, 0.62f, 0.38f);
                cr = ClampByte(((ov >> 16) & 0xFF) * mlr);
                cg = ClampByte(((ov >> 8) & 0xFF) * mlg);
                cb = ClampByte((ov & 0xFF) * mlb);
            }
            else
            {
                var (lr, lg, lb) = Lighting(fn, lights, 0.22f, 0.78f); // two-sided
                cr = ClampByte(210 * lr); cg = ClampByte(202 * lg); cb = ClampByte(188 * lb);
            }

            RasterTriangle(px, zbuf, width, height,
                sx[a], sy[a], sz[a], sx[b], sy[b], sz[b], sx[c], sy[c], sz[c],
                cr, cg, cb);
        }
        if (fog is { } fp && !wireframe) ApplyFog(px, zbuf, fp); // gizmo stays crisp above the fog
        if (splats is { Count: > 0 } && !wireframe)
            DrawSplats(px, zbuf, width, height, view, proj, splats, fog); // soft particles over the fogged scene
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
        float yaw, float pitch, float dist, DirLight[]? lights = null, int[]? triColor = null,
        bool ortho = false, Fog? fog = null, IReadOnlyList<Splat>? splats = null)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var px = new byte[width * height * 4];
        FillBackground(px);
        if (verts.Length < 3) return px;

        pitch = Math.Clamp(pitch, -1.50f, 1.50f);
        radius = MathF.Max(radius, 0.001f);
        dist = MathF.Max(dist, 0.05f); // deep zoom: the floor is absolute, not scene-relative

        var eye = center + dist * CamDir(yaw, pitch);
        var view = Matrix4x4.CreateLookAt(eye, center, RolledUp(CamDir(yaw, pitch), 0f));
        float aspect = width / (float)height;
        // Near plane tracks the CAMERA DISTANCE when zoomed in close (a
        // scene-relative near would clip everything at deep zoom). The
        // z-buffer stores view-space distance, so a tiny near costs nothing.
        float near = MathF.Max(0.005f, MathF.Min(radius * 0.05f, dist * 0.2f));
        float far = dist + radius * 6f + 1f;
        var proj = ortho
            ? Matrix4x4.CreateOrthographic(dist * 0.828f * aspect, dist * 0.828f, near, far)
            : Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspect, near, far);
        var vp = view * proj;

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
            var (sr, sg, sb) = Lighting(fn, lights, 0.55f, 0.45f); // gentle, so the texture reads

            int ti = t < triTexture.Length ? triTexture[t] : -1;
            var tex = ti >= 0 && ti < textures.Length ? textures[ti] : default;

            if (tex.Valid)
                RasterTriangleTextured(px, zbuf, width, height,
                    sx[a], sy[a], sz[a], iw[a], uw[a], vw[a],
                    sx[b], sy[b], sz[b], iw[b], uw[b], vw[b],
                    sx[c], sy[c], sz[c], iw[c], uw[c], vw[c],
                    tex, sr, sg, sb);
            else
            {
                int ov = triColor is not null && t < triColor.Length ? triColor[t] : -1;
                int pa = ov != -1 ? (int)((uint)ov >> 24) : 0;
                if (pa > 0)
                {
                    // GAME-1 — self-lit alpha-blended effect particle.
                    RasterTriangleBlend(px, zbuf, width, height,
                        sx[a], sy[a], sz[a], sx[b], sy[b], sz[b], sx[c], sy[c], sz[c],
                        (byte)((ov >> 16) & 0xFF), (byte)((ov >> 8) & 0xFF), (byte)(ov & 0xFF), pa / 255f);
                    continue;
                }
                if (ov >= 0)
                    RasterTriangle(px, zbuf, width, height,
                        sx[a], sy[a], sz[a], sx[b], sy[b], sz[b], sx[c], sy[c], sz[c],
                        ClampByte(((ov >> 16) & 0xFF) * sr), ClampByte(((ov >> 8) & 0xFF) * sg), ClampByte((ov & 0xFF) * sb));
                else
                    RasterTriangle(px, zbuf, width, height,
                        sx[a], sy[a], sz[a], sx[b], sy[b], sz[b], sx[c], sy[c], sz[c],
                        ClampByte(190 * sr), ClampByte(182 * sg), ClampByte(170 * sb));
            }
        }
        if (fog is { } fp) ApplyFog(px, zbuf, fp); // ED-8 — mood fog audition
        if (splats is { Count: > 0 })
            DrawSplats(px, zbuf, width, height, view, proj, splats, fog); // soft particles over the fogged scene
        DrawAxisGizmo(px, width, height, yaw, pitch);
        return px;
    }

    private static void RasterTriangleTextured(byte[] px, float[] z, int w, int h,
        float x0, float y0, float d0, float iw0, float uw0, float vw0,
        float x1, float y1, float d1, float iw1, float uw1, float vw1,
        float x2, float y2, float d2, float iw2, float uw2, float vw2,
        Texture tex, float sr, float sg, float sb)
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
                px[pi]     = ClampByte(tex.Bgra[si]     * sb); // B
                px[pi + 1] = ClampByte(tex.Bgra[si + 1] * sg); // G
                px[pi + 2] = ClampByte(tex.Bgra[si + 2] * sr); // R
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

    /// <summary>GAME-1 — alpha-blended triangle for effect particles: z-TESTED
    /// against the scene but never z-written, so wisps overlap each other and
    /// still hide behind terrain. dst = src·a + dst·(1−a).</summary>
    private static void RasterTriangleBlend(byte[] px, float[] z, int w, int h,
        float x0, float y0, float d0, float x1, float y1, float d1, float x2, float y2, float d2,
        byte r, byte g, byte b, float alpha)
    {
        float area = Edge(x0, y0, x1, y1, x2, y2);
        if (MathF.Abs(area) < 1e-6f) return;
        float inv = 1f / area;

        int minX = Math.Max(0, (int)MathF.Floor(Math.Min(x0, Math.Min(x1, x2))));
        int maxX = Math.Min(w - 1, (int)MathF.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
        int minY = Math.Max(0, (int)MathF.Floor(Math.Min(y0, Math.Min(y1, y2))));
        int maxY = Math.Min(h - 1, (int)MathF.Ceiling(Math.Max(y0, Math.Max(y1, y2))));

        float a = Math.Clamp(alpha, 0f, 1f);
        float ia = 1f - a;
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
                if (depth >= z[zi]) continue; // test only — no write
                int pi = zi * 4;
                px[pi]     = (byte)(b * a + px[pi] * ia);
                px[pi + 1] = (byte)(g * a + px[pi + 1] * ia);
                px[pi + 2] = (byte)(r * a + px[pi + 2] * ia);
                px[pi + 3] = 0xFF;
            }
        }
    }

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

    // ── Camera basis (shared by the scene and the gizmo) ────────
    /// <summary>Direction from the look-at centre toward the eye, for the orbit angles.</summary>
    private static Vector3 CamDir(float yaw, float pitch) =>
        new(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch));

    /// <summary>The camera up vector after a roll of <paramref name="roll"/> radians about the
    /// view axis (middle-drag "twist"). Degenerates gracefully at the poles (top/bottom views).</summary>
    private static Vector3 RolledUp(Vector3 dir, float roll)
    {
        var fwd = -dir;                       // eye looks from center+dist*dir toward center
        var right = Vector3.Cross(fwd, new Vector3(0, 0, 1));
        if (right.LengthSquared() < 1e-6f)    // looking straight up/down — pick a stable seam
            right = Vector3.Cross(fwd, new Vector3(0, 1, 0));
        right = Vector3.Normalize(right);
        var up0 = Vector3.Normalize(Vector3.Cross(right, fwd));
        return up0 * MathF.Cos(roll) + right * MathF.Sin(roll);
    }

    // ── Orientation gizmo (interactive) ─────────────────────────
    // A small XYZ triad in the lower-left corner. It rotates with the camera to
    // show which way the view faces, and its axis tips + centre hub are clickable
    // hit targets (see HitGizmo) so the view can snap to an axis or reset. Drawing
    // and hit-testing share GizmoOrigin/GizmoAxisTip so they can never drift apart.
    public const float GizmoLen = 46f;
    public const float GizmoMargin = 58f;

    /// <summary>Screen position of the gizmo's centre hub (fixed to the lower-left corner).</summary>
    public static (float x, float y) GizmoOrigin(int w, int h) => (GizmoMargin, h - GizmoMargin);

    /// <summary>Screen position of a unit axis's tip for the given camera rotation — the same
    /// projection <see cref="DrawAxisGizmo"/> draws, so hit-testing lands exactly on the dot.</summary>
    public static (float x, float y) GizmoAxisTip(Vector3 axis, float yaw, float pitch, int w, int h)
    {
        var dir = CamDir(yaw, pitch);
        var view = Matrix4x4.CreateLookAt(dir, Vector3.Zero, RolledUp(dir, 0f));
        var v = Vector3.TransformNormal(axis, view);
        var (ox, oy) = GizmoOrigin(w, h);
        return (ox + v.X * GizmoLen, oy - v.Y * GizmoLen);
    }

    /// <summary>Hit-tests a viewport click against the gizmo. Returns 0 for the centre hub (reset),
    /// 1/2/3 for the X/Y/Z axis tips, or -1 for a miss (the caller should orbit instead).</summary>
    public static int HitGizmo(double sx, double sy, float yaw, float pitch, int w, int h)
    {
        var (ox, oy) = GizmoOrigin(w, h);
        if (Sq(sx - ox) + Sq(sy - oy) <= 18 * 18) return 0;
        Vector3[] axes = { new(1, 0, 0), new(0, 1, 0), new(0, 0, 1) };
        for (int i = 0; i < 3; i++)
        {
            var (tx, ty) = GizmoAxisTip(axes[i], yaw, pitch, w, h);
            if (Sq(sx - tx) + Sq(sy - ty) <= 24 * 24) return i + 1;
        }
        return -1;
    }

    private static double Sq(double v) => v * v;

    /// <summary>Picks the id of the nearest triangle under a screen point, or 0 for a miss. Uses the
    /// same projection as <see cref="Render"/> so a click lands exactly where the triangle is drawn.
    /// <paramref name="verts"/> is flattened (3 per triangle, world space); <paramref name="triId"/>
    /// is one id per triangle (e.g. a node GUID). Depth-sorted so the front-most surface wins.</summary>
    public static uint PickTriangle(Vector3[] verts, uint[] triId, int width, int height,
        Vector3 center, float radius, float yaw, float pitch, float dist, double sx, double sy,
        bool ortho = false)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (verts.Length < 3) return 0;

        pitch = Math.Clamp(pitch, -1.50f, 1.50f);
        radius = MathF.Max(radius, 0.001f);
        dist = MathF.Max(dist, 0.05f); // deep zoom: the floor is absolute, not scene-relative

        var eye = center + dist * CamDir(yaw, pitch);
        var view = Matrix4x4.CreateLookAt(eye, center, RolledUp(CamDir(yaw, pitch), 0f));
        float aspect = width / (float)height;
        // Near plane tracks the CAMERA DISTANCE when zoomed in close (a
        // scene-relative near would clip everything at deep zoom). The
        // z-buffer stores view-space distance, so a tiny near costs nothing.
        float near = MathF.Max(0.005f, MathF.Min(radius * 0.05f, dist * 0.2f));
        float far = dist + radius * 6f + 1f;
        var proj = ortho
            ? Matrix4x4.CreateOrthographic(dist * 0.828f * aspect, dist * 0.828f, near, far)
            : Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspect, near, far);
        var vp = view * proj;

        int triCount = verts.Length / 3;
        uint best = 0;
        float bestDepth = float.MaxValue;
        Span<float> px = stackalloc float[3], py = stackalloc float[3], pz = stackalloc float[3];
        for (int t = 0; t < triCount; t++)
        {
            bool ok = true;
            for (int j = 0; j < 3; j++)
            {
                var v = verts[3 * t + j];
                var clip = Vector4.Transform(new Vector4(v, 1f), vp);
                if (clip.W <= 1e-4f) { ok = false; break; }
                var vv = Vector4.Transform(new Vector4(v, 1f), view);
                float inv = 1f / clip.W;
                px[j] = (clip.X * inv * 0.5f + 0.5f) * width;
                py[j] = (1f - (clip.Y * inv * 0.5f + 0.5f)) * height;
                pz[j] = -vv.Z;
            }
            if (!ok) continue;

            float area = Edge(px[0], py[0], px[1], py[1], px[2], py[2]);
            if (MathF.Abs(area) < 1e-6f) continue;
            float invA = 1f / area;
            float w0 = Edge(px[1], py[1], px[2], py[2], (float)sx, (float)sy) * invA;
            float w1 = Edge(px[2], py[2], px[0], py[0], (float)sx, (float)sy) * invA;
            float w2 = Edge(px[0], py[0], px[1], py[1], (float)sx, (float)sy) * invA;
            if (w0 < 0f || w1 < 0f || w2 < 0f) continue; // two-sided, matching the rasteriser

            float depth = w0 * pz[0] + w1 * pz[1] + w2 * pz[2];
            if (depth < bestDepth && t < triId.Length) { bestDepth = depth; best = triId[t]; }
        }
        return best;
    }

    /// <summary>Like <see cref="PickTriangle"/> but returns the WORLD-space hit point on the nearest
    /// triangle whose id equals <paramref name="wantId"/> (0 = any). Drops a placed object onto its
    /// own node's surface: pass the node guid as wantId so the object slides along that node.</summary>
    public static bool PickPoint(Vector3[] verts, uint[] triId, uint wantId, int width, int height,
        Vector3 center, float radius, float yaw, float pitch, float dist, double sx, double sy, out Vector3 hit,
        bool ortho = false)
    {
        hit = default;
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (verts.Length < 3) return false;

        pitch = Math.Clamp(pitch, -1.50f, 1.50f);
        radius = MathF.Max(radius, 0.001f);
        dist = MathF.Max(dist, 0.05f); // deep zoom: the floor is absolute, not scene-relative

        var eye = center + dist * CamDir(yaw, pitch);
        var view = Matrix4x4.CreateLookAt(eye, center, RolledUp(CamDir(yaw, pitch), 0f));
        float aspect = width / (float)height;
        // Near plane tracks the CAMERA DISTANCE when zoomed in close (a
        // scene-relative near would clip everything at deep zoom). The
        // z-buffer stores view-space distance, so a tiny near costs nothing.
        float near = MathF.Max(0.005f, MathF.Min(radius * 0.05f, dist * 0.2f));
        float far = dist + radius * 6f + 1f;
        var proj = ortho
            ? Matrix4x4.CreateOrthographic(dist * 0.828f * aspect, dist * 0.828f, near, far)
            : Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspect, near, far);
        var vp = view * proj;

        int triCount = verts.Length / 3;
        float bestDepth = float.MaxValue;
        bool found = false;
        Span<float> px = stackalloc float[3], py = stackalloc float[3], pz = stackalloc float[3];
        for (int t = 0; t < triCount; t++)
        {
            if (wantId != 0 && (t >= triId.Length || triId[t] != wantId)) continue;
            bool ok = true;
            for (int j = 0; j < 3; j++)
            {
                var v = verts[3 * t + j];
                var clip = Vector4.Transform(new Vector4(v, 1f), vp);
                if (clip.W <= 1e-4f) { ok = false; break; }
                var vv = Vector4.Transform(new Vector4(v, 1f), view);
                float inv = 1f / clip.W;
                px[j] = (clip.X * inv * 0.5f + 0.5f) * width;
                py[j] = (1f - (clip.Y * inv * 0.5f + 0.5f)) * height;
                pz[j] = -vv.Z;
            }
            if (!ok) continue;

            float area = Edge(px[0], py[0], px[1], py[1], px[2], py[2]);
            if (MathF.Abs(area) < 1e-6f) continue;
            float invA = 1f / area;
            float w0 = Edge(px[1], py[1], px[2], py[2], (float)sx, (float)sy) * invA;
            float w1 = Edge(px[2], py[2], px[0], py[0], (float)sx, (float)sy) * invA;
            float w2 = Edge(px[0], py[0], px[1], py[1], (float)sx, (float)sy) * invA;
            if (w0 < 0f || w1 < 0f || w2 < 0f) continue;

            float depth = w0 * pz[0] + w1 * pz[1] + w2 * pz[2];
            if (depth < bestDepth)
            {
                bestDepth = depth;
                hit = w0 * verts[3 * t] + w1 * verts[3 * t + 1] + w2 * verts[3 * t + 2];
                found = true;
            }
        }
        return found;
    }

    private static void DrawAxisGizmo(byte[] px, int w, int h, float yaw, float pitch)
    {
        var dir = CamDir(yaw, pitch);
        var view = Matrix4x4.CreateLookAt(dir, Vector3.Zero, RolledUp(dir, 0f));
        var (ox, oy) = GizmoOrigin(w, h);

        (Vector3 axis, byte r, byte g, byte b)[] axes =
        {
            (new Vector3(1, 0, 0), 230, 80, 80),   // X — red
            (new Vector3(0, 1, 0), 90, 200, 90),   // Y — green
            (new Vector3(0, 0, 1), 100, 150, 235), // Z — blue (up in DS1)
        };
        // Back-to-front: draw the axis pointing away first so the nearer ones overlay it.
        Array.Sort(axes, (p, q) =>
            Vector3.TransformNormal(p.axis, view).Z.CompareTo(Vector3.TransformNormal(q.axis, view).Z));
        DrawDot(px, w, h, ox, oy, 170, 170, 176, 4); // centre hub (reset target)
        foreach (var (axis, r, g, b) in axes)
        {
            var v = Vector3.TransformNormal(axis, view);
            float ex = ox + v.X * GizmoLen, ey = oy - v.Y * GizmoLen;
            DrawLineRGB(px, w, h, ox, oy, ex, ey, r, g, b, 2);
            DrawDot(px, w, h, ex, ey, r, g, b, 6); // clickable tip
        }
    }

    private static void DrawLineRGB(byte[] px, int w, int h, float fx0, float fy0, float fx1, float fy1, byte r, byte g, byte b, int thick = 1)
    {
        int x0 = (int)fx0, y0 = (int)fy0, x1 = (int)fx1, y1 = (int)fy1;
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy, half = thick / 2;
        while (true)
        {
            for (int oy = -half; oy <= half; oy++)
                for (int ox = -half; ox <= half; ox++)
                {
                    int x = x0 + ox, y = y0 + oy;
                    if ((uint)x < (uint)w && (uint)y < (uint)h)
                    {
                        int pi = (y * w + x) * 4;
                        px[pi] = b; px[pi + 1] = g; px[pi + 2] = r; px[pi + 3] = 0xFF;
                    }
                }
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private static void DrawDot(byte[] px, int w, int h, float cx, float cy, byte r, byte g, byte b, int rad = 2)
    {
        int x0 = (int)cx, y0 = (int)cy, rr = rad * rad + 1;
        for (int dyi = -rad; dyi <= rad; dyi++)
            for (int dxi = -rad; dxi <= rad; dxi++)
            {
                if (dxi * dxi + dyi * dyi > rr) continue;
                int x = x0 + dxi, y = y0 + dyi;
                if ((uint)x < (uint)w && (uint)y < (uint)h)
                {
                    int pi = (y * w + x) * 4;
                    px[pi] = b; px[pi + 1] = g; px[pi + 2] = r; px[pi + 3] = 0xFF;
                }
            }
    }
}
