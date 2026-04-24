using System.Numerics;
using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Nav;

/// <summary>
/// Region-scope navigation mesh: all <see cref="SnoModel.FloorKind.Floor"/> triangles
/// from every placed SNO in a region, lifted into region-space, welded across SNO
/// boundaries, and wired with per-triangle edge adjacency for A* in Phase 11b.
///
/// DS1 stores nav triangles as three unshared <c>Vector3</c>s per face — even neighboring
/// triangles inside a single SNO don't reuse vertices, and different SNOs have entirely
/// separate pools. We reconcile both at build time by quantizing each world-space vertex
/// to a <c>0.01</c>-unit grid and interning it; two triangles then count as edge-adjacent
/// when they share a canonical undirected edge (pair of interned vertex ids).
///
/// The mesh is region-scope on purpose: Phase 11c will chain region nav-meshes together
/// through door portals the same way <see cref="WorldLayout"/> already handles snode
/// offsets. Cross-region pathing lands in that phase, not this one.
/// </summary>
public sealed class NavMesh
{
    /// <summary>Canonical welded vertex pool in region-space.</summary>
    public Vector3[] Vertices { get; }

    /// <summary>Flat triangle index list (length == 3 × <see cref="TriangleCount"/>).
    /// Triangle <c>t</c>'s vertices live at indices <c>3t, 3t+1, 3t+2</c>.</summary>
    public int[] Indices { get; }

    /// <summary>Per-triangle edge neighbors, -1 if an edge is a mesh boundary. Slot i
    /// corresponds to the edge opposite vertex i (edge <c>(v[(i+1)%3], v[(i+2)%3])</c>).
    /// That lets a triangle-walker "cross edge i" without reindexing.</summary>
    public int[] Neighbors { get; }

    /// <summary>Per-triangle FloorKind — always <see cref="SnoModel.FloorKind.Floor"/>
    /// in the current build since we filter to walkable on construction. Kept as a field
    /// so Phase 11b can start stitching water tiles in without reshaping the mesh.</summary>
    public SnoModel.FloorKind[] Kinds { get; }

    /// <summary>Per-triangle centroid in region-space. Cached because A* heuristic +
    /// cost functions both call it per node expansion.</summary>
    public Vector3[] Centroids { get; }

    /// <summary>Number of SNO instances whose nav faces were folded into the mesh.</summary>
    public int SourceSnodeCount { get; }

    /// <summary>How many SNO faces were dropped because their canonical vertex collapsed
    /// to a degenerate triangle (two vertices welded onto the same bucket). Zero on clean
    /// DS1 data; non-zero means the weld tolerance was too loose for this region.</summary>
    public int DegenerateFaceCount { get; }

    public int TriangleCount => Indices.Length / 3;

    private NavMesh(
        Vector3[] vertices,
        int[] indices,
        int[] neighbors,
        SnoModel.FloorKind[] kinds,
        Vector3[] centroids,
        int sourceSnodeCount,
        int degenerateFaceCount)
    {
        Vertices = vertices;
        Indices = indices;
        Neighbors = neighbors;
        Kinds = kinds;
        Centroids = centroids;
        SourceSnodeCount = sourceSnodeCount;
        DegenerateFaceCount = degenerateFaceCount;
    }

    /// <summary>Weld tolerance in game units. DS1 authoring snaps to ~integer grids, so
    /// 10cm (0.1) is more than tight enough to keep distinct junction vertices apart —
    /// the smallest authored gaps in the shipped data are ~0.5 units wide. Anything
    /// tighter loses inter-SNO edge adjacency: BFS-composed snode transforms accumulate
    /// fractional-unit error over long door chains, so mating-edge vertices from two
    /// different SNOs can be that far apart even though they were authored identical.
    /// Empirical fuzz across 81 regions: 0.01 → ~29 components/mesh; 0.1 → 1 component
    /// in the typical region.</summary>
    public const float WeldToleranceUnits = 0.1f;

    /// <summary>Builds a region-scope nav mesh from a region's placed-snode layout and an
    /// SNO resolver. Only <see cref="SnoModel.FloorKind.Floor"/> groupings contribute —
    /// water and ignored groupings are filtered at the source so downstream consumers
    /// can trust every triangle is walkable.</summary>
    public static NavMesh BuildForRegion(
        RegionGraph graph,
        RegionLayout layout,
        Func<uint, SnoModel?> resolveSno)
    {
        var verts = new List<Vector3>();
        // Canonical vertex lookup: quantized world position -> index into verts.
        var vertIndex = new Dictionary<(int, int, int), int>(capacity: 4096);
        var tris = new List<int>(capacity: 2048);
        var kinds = new List<SnoModel.FloorKind>(capacity: 2048);
        int sourceSnodes = 0;
        int degenerate = 0;
        float inv = 1f / WeldToleranceUnits;

        int Intern(Vector3 p)
        {
            // Round-to-nearest quantization. Can't use truncation (floor) because positive
            // and negative coordinates would snap inconsistently at the origin.
            var key = (
                (int)MathF.Round(p.X * inv),
                (int)MathF.Round(p.Y * inv),
                (int)MathF.Round(p.Z * inv));
            if (vertIndex.TryGetValue(key, out var idx)) return idx;
            idx = verts.Count;
            // Store the un-quantized position for the *first* vertex that lands in the
            // bucket — closer to the authored intent than the quantized grid point.
            verts.Add(p);
            vertIndex[key] = idx;
            return idx;
        }

        foreach (var node in graph.Nodes)
        {
            if (!layout.TryGetTransform(node.Guid, out var snodeXform)) continue;
            var sno = resolveSno(node.MeshGuid);
            if (sno is null) continue;
            sourceSnodes++;
            foreach (var group in sno.LogicalGroupings)
            {
                if (group.Kind != SnoModel.FloorKind.Floor) continue;
                foreach (var face in group.Faces)
                {
                    // Row-vector convention: p * snodeXform lifts SNO-local into region-frame.
                    var a = Vector3.Transform(face.A, snodeXform);
                    var b = Vector3.Transform(face.B, snodeXform);
                    var c = Vector3.Transform(face.C, snodeXform);
                    var ia = Intern(a);
                    var ib = Intern(b);
                    var ic = Intern(c);
                    if (ia == ib || ib == ic || ia == ic) { degenerate++; continue; }
                    tris.Add(ia);
                    tris.Add(ib);
                    tris.Add(ic);
                    kinds.Add(group.Kind);
                }
            }
        }

        var indices = tris.ToArray();
        var triCount = indices.Length / 3;
        var neighbors = new int[indices.Length];
        for (int i = 0; i < neighbors.Length; i++) neighbors[i] = -1;

        // Edge → (triIndex, slot). Two triangles sharing an edge meet when the second one
        // probes the dictionary and finds the first. Canonicalize the edge by sorting
        // (min,max) so (a,b) and (b,a) match.
        var edgeMap = new Dictionary<(int, int), (int tri, int slot)>(capacity: indices.Length);
        for (int t = 0; t < triCount; t++)
        {
            int v0 = indices[3 * t + 0], v1 = indices[3 * t + 1], v2 = indices[3 * t + 2];
            // Slot convention: slot i = edge opposite vertex i.
            int[] slotA = { v1, v2, v0 };
            int[] slotB = { v2, v0, v1 };
            for (int s = 0; s < 3; s++)
            {
                int a = slotA[s], b = slotB[s];
                var key = a < b ? (a, b) : (b, a);
                if (edgeMap.TryGetValue(key, out var other))
                {
                    neighbors[3 * t + s] = other.tri;
                    neighbors[3 * other.tri + other.slot] = t;
                    edgeMap.Remove(key);
                }
                else
                {
                    edgeMap[key] = (t, s);
                }
            }
        }

        var centroids = new Vector3[triCount];
        var vertsArr = verts.ToArray();
        for (int t = 0; t < triCount; t++)
        {
            var p0 = vertsArr[indices[3 * t + 0]];
            var p1 = vertsArr[indices[3 * t + 1]];
            var p2 = vertsArr[indices[3 * t + 2]];
            centroids[t] = (p0 + p1 + p2) / 3f;
        }

        return new NavMesh(
            vertsArr,
            indices,
            neighbors,
            kinds.ToArray(),
            centroids,
            sourceSnodes,
            degenerate);
    }

    /// <summary>Finds the triangle containing <paramref name="worldPos"/> by XZ
    /// projection (Y is up in DS1; terrain folds are shallow enough that a 2D point-in-
    /// triangle test picks the right tile). Returns the triangle with the smallest
    /// vertical distance when multiple tiles overlap in XZ (overpasses / stairs).</summary>
    public bool TryFindTriangle(Vector3 worldPos, out int triIndex)
    {
        int bestTri = -1;
        float bestDy = float.PositiveInfinity;
        for (int t = 0; t < TriangleCount; t++)
        {
            var a = Vertices[Indices[3 * t + 0]];
            var b = Vertices[Indices[3 * t + 1]];
            var c = Vertices[Indices[3 * t + 2]];
            if (!PointInTriangleXZ(worldPos, a, b, c)) continue;
            // Interpolate Y on the triangle plane via barycentrics, pick closest.
            float triY = InterpolateYXZ(worldPos.X, worldPos.Z, a, b, c);
            float dy = MathF.Abs(triY - worldPos.Y);
            if (dy < bestDy) { bestDy = dy; bestTri = t; }
        }
        triIndex = bestTri;
        return bestTri >= 0;
    }

    private static bool PointInTriangleXZ(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        // Sign-of-cross-product test in the XZ plane. Accepts either winding.
        float d1 = Cross2(p.X - b.X, p.Z - b.Z, a.X - b.X, a.Z - b.Z);
        float d2 = Cross2(p.X - c.X, p.Z - c.Z, b.X - c.X, b.Z - c.Z);
        float d3 = Cross2(p.X - a.X, p.Z - a.Z, c.X - a.X, c.Z - a.Z);
        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    private static float Cross2(float ax, float ay, float bx, float by) => ax * by - ay * bx;

    private static float InterpolateYXZ(float x, float z, Vector3 a, Vector3 b, Vector3 c)
    {
        // Barycentric in XZ, then lerp Y. If the triangle is edge-on (denom ~0), fall
        // back to the centroid Y — the caller only uses this to disambiguate near-ties.
        float denom = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
        if (MathF.Abs(denom) < 1e-6f) return (a.Y + b.Y + c.Y) / 3f;
        float wa = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / denom;
        float wb = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / denom;
        float wc = 1f - wa - wb;
        return wa * a.Y + wb * b.Y + wc * c.Y;
    }
}
