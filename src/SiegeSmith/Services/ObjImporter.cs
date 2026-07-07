using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace SiegeSmith.Services;

/// <summary>Parses a Wavefront <c>.obj</c> into the position/corner/face arrays <see cref="AspWriter"/>
/// consumes. OBJ is Y-up; DS ASP is Z-up, so positions and normals are rotated <c>(x,y,z) → (x,z,-y)</c>.
/// Corners are de-duplicated by their (vertex, uv, normal) triple; polygons are fan-triangulated;
/// <c>usemtl</c> groups become materials (subtextures). UVs pass through raw. Missing normals are
/// computed per face.</summary>
public static class ObjImporter
{
    public sealed class Result
    {
        public List<Vector3> Positions = new();
        public List<AspCorner> Corners = new();
        public List<AspFace> Faces = new();
        public List<string> TextureNames = new();
        public int SkippedFaces;
    }

    public static Result Parse(string objText)
    {
        var v = new List<Vector3>();
        var vt = new List<Vector2>();
        var vn = new List<Vector3>();
        var matNames = new List<string>();
        var matIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int curMat = -1;

        var r = new Result();
        var cornerCache = new Dictionary<(int, int, int), int>();

        foreach (var raw in objText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var tok = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length == 0) continue;

            switch (tok[0])
            {
                case "v" when tok.Length >= 4:
                    // Y-up → Z-up: (x, y, z) → (x, z, -y)
                    v.Add(new Vector3(F(tok[1]), F(tok[3]), -F(tok[2])));
                    break;
                case "vt" when tok.Length >= 3:
                    vt.Add(new Vector2(F(tok[1]), F(tok[2])));
                    break;
                case "vn" when tok.Length >= 4:
                    vn.Add(Vector3.Normalize(new Vector3(F(tok[1]), F(tok[3]), -F(tok[2]))));
                    break;
                case "usemtl" when tok.Length >= 2:
                    if (!matIndex.TryGetValue(tok[1], out curMat))
                    {
                        curMat = matNames.Count;
                        matIndex[tok[1]] = curMat;
                        matNames.Add(tok[1]);
                    }
                    break;
                case "f" when tok.Length >= 4:
                    AddFace(tok, v, vt, vn, curMat < 0 ? 0 : curMat, r, cornerCache);
                    break;
            }
        }

        if (matNames.Count == 0) matNames.Add("custom");
        r.TextureNames = matNames;
        // BVTX is the full converted vertex list; corners reference it by index.
        r.Positions = v;
        return r;
    }

    private static void AddFace(string[] tok, List<Vector3> v, List<Vector2> vt, List<Vector3> vn,
        int material, Result r, Dictionary<(int, int, int), int> cache)
    {
        // Parse the polygon's corners, then fan-triangulate (0, i, i+1).
        var poly = new List<int>(tok.Length - 1);
        for (int i = 1; i < tok.Length; i++)
        {
            if (!ParseVertexRef(tok[i], v.Count, vt.Count, vn.Count, out int vi, out int ti, out int ni))
            { r.SkippedFaces++; return; }
            poly.Add(CornerIndex(vi, ti, ni, vt, vn, r, cache));
        }
        if (poly.Count < 3) { r.SkippedFaces++; return; }
        for (int i = 1; i + 1 < poly.Count; i++)
            r.Faces.Add(new AspFace(poly[0], poly[i], poly[i + 1], material));
    }

    private static int CornerIndex(int vi, int ti, int ni, List<Vector2> vt, List<Vector3> vn,
        Result r, Dictionary<(int, int, int), int> cache)
    {
        var key = (vi, ti, ni);
        if (cache.TryGetValue(key, out int existing)) return existing;

        var normal = ni >= 0 && ni < vn.Count ? vn[ni] : Vector3.UnitZ; // fixed up per-face below if missing
        var uv = ti >= 0 && ti < vt.Count ? vt[ti] : Vector2.Zero;
        int idx = r.Corners.Count;
        r.Corners.Add(AspCorner.White(vi, normal, uv));
        cache[key] = idx;
        return idx;
    }

    private static bool ParseVertexRef(string s, int nv, int nvt, int nvn, out int vi, out int ti, out int ni)
    {
        vi = ti = ni = -1;
        var parts = s.Split('/');
        if (!TryIndex(parts[0], nv, out vi)) return false;
        if (parts.Length > 1 && parts[1].Length > 0) TryIndex(parts[1], nvt, out ti);
        if (parts.Length > 2 && parts[2].Length > 0) TryIndex(parts[2], nvn, out ni);
        return true;
    }

    /// <summary>OBJ indices are 1-based and may be negative (relative to the end). Returns a 0-based index.</summary>
    private static bool TryIndex(string s, int count, out int zeroBased)
    {
        zeroBased = -1;
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw) || raw == 0) return false;
        zeroBased = raw > 0 ? raw - 1 : count + raw;
        return zeroBased >= 0;
    }

    private static float F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;

    /// <summary>Fills in any corner whose normal was absent with the area-weighted face normal, so a
    /// normal-less OBJ still lights correctly. Call after <see cref="Parse"/>.</summary>
    public static void FillMissingNormals(Result r)
    {
        // A corner keeps UnitZ only if the OBJ gave no vn for it; recompute those from face geometry.
        var accum = new Vector3[r.Corners.Count];
        foreach (var f in r.Faces)
        {
            var pa = r.Positions[r.Corners[f.A].VertexIndex];
            var pb = r.Positions[r.Corners[f.B].VertexIndex];
            var pc = r.Positions[r.Corners[f.C].VertexIndex];
            var fn = Vector3.Cross(pb - pa, pc - pa); // area-weighted (not normalized)
            accum[f.A] += fn; accum[f.B] += fn; accum[f.C] += fn;
        }
        for (int i = 0; i < r.Corners.Count; i++)
        {
            if (r.Corners[i].Normal != Vector3.UnitZ) continue; // had a real normal
            var n = accum[i];
            var normal = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitZ;
            r.Corners[i] = r.Corners[i] with { Normal = normal };
        }
    }
}
