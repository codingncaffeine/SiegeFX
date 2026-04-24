using System.Numerics;
using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Nav;

/// <summary>
/// Region-scope navigation mesh: all <see cref="SnoModel.FloorKind.Floor"/> triangles
/// from every placed SNO in a region, lifted into region-space, welded across SNO
/// boundaries, and wired with per-triangle edge adjacency.
///
/// DS1 stores nav triangles as three unshared <c>Vector3</c>s per face — even neighboring
/// triangles inside a single SNO don't reuse vertices, and different SNOs have entirely
/// separate pools. We reconcile both at build time by quantizing each world-space vertex
/// to a small grid (<see cref="WeldToleranceUnits"/>) and interning it; two triangles
/// then count as edge-adjacent when they share a canonical undirected edge (pair of
/// interned vertex ids).
///
/// The mesh is region-scope on purpose. Cross-region pathing stitches adjacent meshes
/// through door portals the same way <see cref="Assets.WorldLayout"/> already handles
/// snode offsets.
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

    /// <summary>Edges shared by three or more triangles (T-junctions, stacked ramps).
    /// These are treated as boundaries in the adjacency table — better no adjacency than
    /// an arbitrary pair — so the pathfinder can't walk through a non-manifold seam
    /// and pop out on the wrong surface.</summary>
    public int NonManifoldEdgeCount { get; }

    public int TriangleCount => Indices.Length / 3;

    // XZ uniform-grid spatial index. Built once at construction and queried by
    // TryFindTriangle. A cell size of 4 units is a compromise for DS1 scale: SNO nav
    // triangles are typically 1-3 units per side, so most cells hold 4-20 triangles,
    // and a query visits one cell on average.
    private const float GridCellSize = 4f;
    private readonly float _gridMinX;
    private readonly float _gridMinZ;
    private readonly int _gridCellsX;
    private readonly int _gridCellsZ;
    // Row-major flat grid of int[] buckets. Null means "no triangles overlap this cell".
    private readonly int[]?[] _grid;

    private NavMesh(
        Vector3[] vertices,
        int[] indices,
        int[] neighbors,
        SnoModel.FloorKind[] kinds,
        Vector3[] centroids,
        int sourceSnodeCount,
        int degenerateFaceCount,
        int nonManifoldEdgeCount,
        float gridMinX,
        float gridMinZ,
        int gridCellsX,
        int gridCellsZ,
        int[]?[] grid)
    {
        Vertices = vertices;
        Indices = indices;
        Neighbors = neighbors;
        Kinds = kinds;
        Centroids = centroids;
        SourceSnodeCount = sourceSnodeCount;
        DegenerateFaceCount = degenerateFaceCount;
        NonManifoldEdgeCount = nonManifoldEdgeCount;
        _gridMinX = gridMinX;
        _gridMinZ = gridMinZ;
        _gridCellsX = gridCellsX;
        _gridCellsZ = gridCellsZ;
        _grid = grid;
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

        // Edge → occurrence state. The first triangle touching an edge stores (tri, slot)
        // with occurrence count 1. The second triangle promotes the entry to count 2 and
        // wires both sides' neighbors. A third or later triangle (T-junction, stacked
        // ramp) flips the edge to "non-manifold" — both previously-wired sides get
        // reset to -1 so we never hand the pathfinder an arbitrary adjacency. Keeping
        // the entry in the dictionary (rather than removing after pair-up) is what lets
        // us detect the third hit.
        var edgeMap = new Dictionary<(int, int), (int tri, int slot, int count)>(capacity: indices.Length);
        int nonManifoldEdges = 0;
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
                if (!edgeMap.TryGetValue(key, out var other))
                {
                    edgeMap[key] = (t, s, 1);
                }
                else if (other.count == 1)
                {
                    neighbors[3 * t + s] = other.tri;
                    neighbors[3 * other.tri + other.slot] = t;
                    edgeMap[key] = (other.tri, other.slot, 2);
                }
                else
                {
                    // Third (or later) incidence: mark and defer the actual teardown
                    // to the post-pass. Counting once on the 2→3 transition gives one
                    // tally per non-manifold edge regardless of how many faces touch it.
                    if (other.count == 2) nonManifoldEdges++;
                    edgeMap[key] = (other.tri, other.slot, other.count + 1);
                }
            }
        }
        // Second pass: edges whose occurrence count exceeded 2 need their pair-up undone.
        // The dictionary entry still points at the first triangle (triA, slotA); its
        // mate was wired during the count==1→2 transition and lives at
        // neighbors[3*triA + slotA]. Clear both sides and leave the edge as a boundary.
        foreach (var kv in edgeMap)
        {
            if (kv.Value.count <= 2) continue;
            int triA = kv.Value.tri;
            int slotA = kv.Value.slot;
            int triB = neighbors[3 * triA + slotA];
            neighbors[3 * triA + slotA] = -1;
            if (triB < 0) continue;
            // Find triA's back-reference in triB's slots — the slot whose neighbor is
            // triA is the one we wired up.
            for (int ss = 0; ss < 3; ss++)
                if (neighbors[3 * triB + ss] == triA) { neighbors[3 * triB + ss] = -1; break; }
        }

        var centroids = new Vector3[triCount];
        var vertsArr = verts.ToArray();
        float gMinX = float.PositiveInfinity, gMinZ = float.PositiveInfinity;
        float gMaxX = float.NegativeInfinity, gMaxZ = float.NegativeInfinity;
        for (int t = 0; t < triCount; t++)
        {
            var p0 = vertsArr[indices[3 * t + 0]];
            var p1 = vertsArr[indices[3 * t + 1]];
            var p2 = vertsArr[indices[3 * t + 2]];
            centroids[t] = (p0 + p1 + p2) / 3f;
            float minX = MathF.Min(p0.X, MathF.Min(p1.X, p2.X));
            float maxX = MathF.Max(p0.X, MathF.Max(p1.X, p2.X));
            float minZ = MathF.Min(p0.Z, MathF.Min(p1.Z, p2.Z));
            float maxZ = MathF.Max(p0.Z, MathF.Max(p1.Z, p2.Z));
            if (minX < gMinX) gMinX = minX;
            if (minZ < gMinZ) gMinZ = minZ;
            if (maxX > gMaxX) gMaxX = maxX;
            if (maxZ > gMaxZ) gMaxZ = maxZ;
        }

        // Build the XZ uniform grid. Empty mesh → 1x1 grid with one null cell.
        int cellsX, cellsZ;
        float originX, originZ;
        int[]?[] grid;
        if (triCount == 0)
        {
            cellsX = cellsZ = 1;
            originX = originZ = 0f;
            grid = new int[]?[1];
        }
        else
        {
            originX = gMinX;
            originZ = gMinZ;
            cellsX = Math.Max(1, (int)MathF.Ceiling((gMaxX - gMinX) / GridCellSize) + 1);
            cellsZ = Math.Max(1, (int)MathF.Ceiling((gMaxZ - gMinZ) / GridCellSize) + 1);
            // First pass counts per-cell; second pass fills. Temporary List<int> arena
            // keeps the final int[] buckets tight (no List<int> overhead per cell).
            var buckets = new List<int>?[cellsX * cellsZ];
            for (int t = 0; t < triCount; t++)
            {
                var p0 = vertsArr[indices[3 * t + 0]];
                var p1 = vertsArr[indices[3 * t + 1]];
                var p2 = vertsArr[indices[3 * t + 2]];
                float minX = MathF.Min(p0.X, MathF.Min(p1.X, p2.X));
                float maxX = MathF.Max(p0.X, MathF.Max(p1.X, p2.X));
                float minZ = MathF.Min(p0.Z, MathF.Min(p1.Z, p2.Z));
                float maxZ = MathF.Max(p0.Z, MathF.Max(p1.Z, p2.Z));
                int cx0 = (int)((minX - originX) / GridCellSize);
                int cx1 = (int)((maxX - originX) / GridCellSize);
                int cz0 = (int)((minZ - originZ) / GridCellSize);
                int cz1 = (int)((maxZ - originZ) / GridCellSize);
                cx0 = Math.Clamp(cx0, 0, cellsX - 1);
                cx1 = Math.Clamp(cx1, 0, cellsX - 1);
                cz0 = Math.Clamp(cz0, 0, cellsZ - 1);
                cz1 = Math.Clamp(cz1, 0, cellsZ - 1);
                for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    int cell = cz * cellsX + cx;
                    (buckets[cell] ??= new List<int>()).Add(t);
                }
            }
            grid = new int[]?[buckets.Length];
            for (int i = 0; i < buckets.Length; i++)
                grid[i] = buckets[i]?.ToArray();
        }

        return new NavMesh(
            vertsArr,
            indices,
            neighbors,
            kinds.ToArray(),
            centroids,
            sourceSnodes,
            degenerate,
            nonManifoldEdges,
            originX,
            originZ,
            cellsX,
            cellsZ,
            grid);
    }

    /// <summary>Finds the triangle containing <paramref name="worldPos"/> by XZ
    /// projection (Y is up in DS1; terrain folds are shallow enough that a 2D point-in-
    /// triangle test picks the right tile). Returns the triangle with the smallest
    /// vertical distance when multiple tiles overlap in XZ (overpasses / stairs).</summary>
    public bool TryFindTriangle(Vector3 worldPos, out int triIndex)
    {
        triIndex = -1;
        if (TriangleCount == 0) return false;
        int cx = (int)((worldPos.X - _gridMinX) / GridCellSize);
        int cz = (int)((worldPos.Z - _gridMinZ) / GridCellSize);
        if (cx < 0 || cx >= _gridCellsX || cz < 0 || cz >= _gridCellsZ) return false;
        var bucket = _grid[cz * _gridCellsX + cx];
        if (bucket is null) return false;
        int bestTri = -1;
        float bestDy = float.PositiveInfinity;
        for (int i = 0; i < bucket.Length; i++)
        {
            int t = bucket[i];
            var a = Vertices[Indices[3 * t + 0]];
            var b = Vertices[Indices[3 * t + 1]];
            var c = Vertices[Indices[3 * t + 2]];
            if (!PointInTriangleXZ(worldPos, a, b, c)) continue;
            float triY = InterpolateYXZ(worldPos.X, worldPos.Z, a, b, c);
            float dy = MathF.Abs(triY - worldPos.Y);
            if (dy < bestDy) { bestDy = dy; bestTri = t; }
        }
        triIndex = bestTri;
        return bestTri >= 0;
    }

    /// <summary>Projects <paramref name="worldPos"/> onto triangle <paramref name="tri"/>'s
    /// plane in XZ and returns its world Y. Falls back to the triangle centroid Y when the
    /// triangle is edge-on. Used by the follower to keep the actor glued to the terrain.</summary>
    public float SampleYOnTriangle(int tri, Vector3 worldPos)
    {
        var a = Vertices[Indices[3 * tri + 0]];
        var b = Vertices[Indices[3 * tri + 1]];
        var c = Vertices[Indices[3 * tri + 2]];
        return InterpolateYXZ(worldPos.X, worldPos.Z, a, b, c);
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
