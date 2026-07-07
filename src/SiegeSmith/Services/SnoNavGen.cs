using System;
using System.Collections.Generic;
using System.Numerics;
using SiegeFX.Core.Assets;

namespace SiegeSmith.Services;

/// <summary>Generates the nav groupings the engine's <see cref="SiegeFX.Core.Nav.NavMesh"/> consumes from a
/// raw tile mesh: classifies each triangle by slope (walkable → Floor, too steep → Ignored, optional Water),
/// flood-fills same-kind triangles into connected components (one grouping each, with an AABB), and builds a
/// median-split BVH BSP over each component's faces. This is the P5 nav core — the general path that replaces
/// the earlier single-Floor-single-leaf stub, so a ramped/mixed tile classifies correctly.</summary>
public static class SnoNavGen
{
    private const int BspLeafMax = 8;
    private const int BspMaxDepth = 32;
    private const float WeldTolerance = 0.01f;

    /// <summary>Classifies + groups the mesh. <paramref name="walkableSlopeDeg"/> is the max slope (from
    /// horizontal) still treated as floor (Recast's default band is ~45–50°). <paramref name="isWater"/>, if
    /// given, marks a face as a Water grouping instead of Floor. Ignored (too-steep) faces are emitted as
    /// their own grouping so the tile data is complete; the nav builder skips them.</summary>
    public static List<SnoWriter.GroupingDef> Build(
        IReadOnlyList<SnoWriter.Vertex> verts,
        IReadOnlyList<SnoWriter.Tri> tris,
        float walkableSlopeDeg = 48f,
        Func<SnoWriter.NavFace, bool>? isWater = null)
    {
        var result = new List<SnoWriter.GroupingDef>();
        if (tris.Count == 0) return result;

        float cosThresh = MathF.Cos(Math.Clamp(walkableSlopeDeg, 0f, 89f) * MathF.PI / 180f);
        var up = Vector3.UnitY;

        var faces = new SnoWriter.NavFace[tris.Count];
        var kinds = new SnoModel.FloorKind[tris.Count];
        for (int i = 0; i < tris.Count; i++)
        {
            var t = tris[i];
            var a = verts[t.A].Position; var b = verts[t.B].Position; var c = verts[t.C].Position;
            var n = SnoWriter.TriNormal(a, b, c);
            faces[i] = new SnoWriter.NavFace(a, b, c, n);
            if (Vector3.Dot(n, up) < cosThresh) kinds[i] = SnoModel.FloorKind.Ignored;
            else if (isWater is not null && isWater(faces[i])) kinds[i] = SnoModel.FloorKind.Water;
            else kinds[i] = SnoModel.FloorKind.Floor;
        }

        // Weld vertices to a grid so shared-edge adjacency survives independent corner records.
        var vidByKey = new Dictionary<(int, int, int), int>(verts.Count);
        var triVid = new int[tris.Count][];
        int Weld(Vector3 p)
        {
            var key = ((int)MathF.Round(p.X / WeldTolerance), (int)MathF.Round(p.Y / WeldTolerance), (int)MathF.Round(p.Z / WeldTolerance));
            if (!vidByKey.TryGetValue(key, out var id)) { id = vidByKey.Count; vidByKey[key] = id; }
            return id;
        }
        for (int i = 0; i < tris.Count; i++)
            triVid[i] = new[] { Weld(faces[i].A), Weld(faces[i].B), Weld(faces[i].C) };

        // Edge → incident triangles (an edge is an unordered welded-vertex pair).
        var edgeTris = new Dictionary<(int, int), List<int>>();
        void AddEdge(int a, int b, int tri)
        {
            var e = a < b ? (a, b) : (b, a);
            if (!edgeTris.TryGetValue(e, out var list)) { list = new List<int>(2); edgeTris[e] = list; }
            list.Add(tri);
        }
        for (int i = 0; i < tris.Count; i++)
        {
            var v = triVid[i];
            AddEdge(v[0], v[1], i); AddEdge(v[1], v[2], i); AddEdge(v[2], v[0], i);
        }

        // Flood-fill connected components among same-kind triangles.
        var comp = new int[tris.Count];
        Array.Fill(comp, -1);
        int compCount = 0;
        var stack = new Stack<int>();
        for (int seed = 0; seed < tris.Count; seed++)
        {
            if (comp[seed] != -1) continue;
            int id = compCount++;
            comp[seed] = id;
            stack.Push(seed);
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                var v = triVid[cur];
                foreach (var e in new[] { (v[0], v[1]), (v[1], v[2]), (v[2], v[0]) })
                {
                    var key = e.Item1 < e.Item2 ? e : (e.Item2, e.Item1);
                    foreach (int nb in edgeTris[key])
                        if (comp[nb] == -1 && kinds[nb] == kinds[cur]) { comp[nb] = id; stack.Push(nb); }
                }
            }
        }

        // Materialize a grouping per component.
        byte nextId = 1;
        for (int id = 0; id < compCount; id++)
        {
            var localTris = new List<int>();
            for (int i = 0; i < tris.Count; i++) if (comp[i] == id) localTris.Add(i);
            if (localTris.Count == 0) continue;

            var compFaces = new SnoWriter.NavFace[localTris.Count];
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            for (int k = 0; k < localTris.Count; k++)
            {
                var f = faces[localTris[k]];
                compFaces[k] = f;
                foreach (var p in new[] { f.A, f.B, f.C }) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            }

            var localIdx = new List<int>(localTris.Count);
            for (int k = 0; k < localTris.Count; k++) localIdx.Add(k);
            var bsp = BuildBsp(localIdx, compFaces, 0);

            result.Add(new SnoWriter.GroupingDef
            {
                Id = nextId++,
                Kind = kinds[localTris[0]],
                Min = min,
                Max = max,
                Faces = compFaces,
                Bsp = bsp,
            });
        }
        return result;
    }

    /// <summary>Convenience: treat the whole mesh as floor (no slope rejection, no water) — the common case
    /// for a flat authored tile, but still flood-filled and BSP-partitioned like the general path.</summary>
    public static List<SnoWriter.GroupingDef> Walkable(
        IReadOnlyList<SnoWriter.Vertex> verts, IReadOnlyList<SnoWriter.Tri> tris)
        => Build(verts, tris, walkableSlopeDeg: 89f);

    /// <summary>Median-split BVH over triangle centroids: split the longest AABB axis at the centroid median,
    /// recurse until ≤<see cref="BspLeafMax"/> triangles or <see cref="BspMaxDepth"/>. Leaves carry u16 indices
    /// into <paramref name="compFaces"/>. Whole-triangle AABB buckets (no plane clipping), per the plan.</summary>
    private static SnoWriter.BspNodeDef BuildBsp(List<int> idx, SnoWriter.NavFace[] compFaces, int depth)
    {
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        foreach (int i in idx)
        {
            var f = compFaces[i];
            foreach (var p in new[] { f.A, f.B, f.C }) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        }

        if (idx.Count <= BspLeafMax || depth >= BspMaxDepth)
            return Leaf(idx, min, max);

        // Longest axis of the centroid spread.
        var ext = max - min;
        int axis = ext.X >= ext.Y && ext.X >= ext.Z ? 0 : (ext.Y >= ext.Z ? 1 : 2);
        float Centroid(int i) { var f = compFaces[i]; var c = (f.A + f.B + f.C) / 3f; return axis == 0 ? c.X : axis == 1 ? c.Y : c.Z; }

        var sorted = new List<int>(idx);
        sorted.Sort((a, b) => Centroid(a).CompareTo(Centroid(b)));
        int mid = sorted.Count / 2;
        var left = sorted.GetRange(0, mid);
        var right = sorted.GetRange(mid, sorted.Count - mid);

        // Degenerate split (all centroids coincident on the chosen axis) → leaf, else infinite recursion.
        if (left.Count == 0 || right.Count == 0)
            return Leaf(idx, min, max);

        return new SnoWriter.BspNodeDef
        {
            Min = min,
            Max = max,
            IsLeaf = false,
            Children = new[] { BuildBsp(left, compFaces, depth + 1), BuildBsp(right, compFaces, depth + 1) },
        };
    }

    private static SnoWriter.BspNodeDef Leaf(List<int> idx, Vector3 min, Vector3 max)
    {
        var tris = new ushort[idx.Count];
        for (int i = 0; i < idx.Count; i++) tris[i] = (ushort)idx[i];
        return new SnoWriter.BspNodeDef { Min = min, Max = max, IsLeaf = true, TriIndices = tris };
    }
}
