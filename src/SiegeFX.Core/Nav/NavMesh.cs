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

    /// <summary>Per-triangle FloorKind. Mixed values: Floor and Water both make it into
    /// the mesh (Ignored is dropped at source). The pathfinder consults
    /// <see cref="NavTraversal"/> to decide which kinds an actor may enter — DS1's stock
    /// land-only actors treat Water as impassable, but the data is here for amphibious
    /// templates and for the funnel/Y-resampler so an actor never falls off the world
    /// when stepping near a beach.</summary>
    public SnoModel.FloorKind[] Kinds { get; }

    /// <summary>Per-triangle centroid in region-space. Cached because A* heuristic +
    /// cost functions both call it per node expansion.</summary>
    public Vector3[] Centroids { get; }

    /// <summary>Per-triangle source node index into <c>graph.Nodes</c> at build time.
    /// Lets the nav-components diagnostic name the SNOs anchoring each connected
    /// component without having to rebuild the mesh. -1 only on triangles authored
    /// outside the original BuildForRegion loop, which the current pipeline never
    /// produces.</summary>
    public int[] SourceNodeIndex { get; }

    /// <summary>Per-triangle <see cref="SnoModel.LogicalGrouping.Id"/>
    /// (the SNO-local "lnode" index, u8) from which the face came.
    /// Phase 24-NAV-LOGICAL-FLAGS feeds the per-triangle gate lookup
    /// in <see cref="LogicalFlagsStore"/>. Always in 0..255 since
    /// BuildForRegion adds <c>group.Id</c> (a byte) — we don't carry
    /// a negative sentinel because no SiegeFX path produces a
    /// triangle outside that loop (audit fold).</summary>
    public int[] SourceLnodeIndex { get; }

    /// <summary>Per-triangle snode guid (the 32-bit RegionGraph node
    /// guid). Pairs with <see cref="SourceLnodeIndex"/> for
    /// <see cref="LogicalFlagsStore.CanEnter"/> queries. 0 when not
    /// available.</summary>
    public uint[] SourceSnodeGuid { get; }

    /// <summary>Number of SNO instances whose nav faces were folded into the mesh.</summary>
    public int SourceSnodeCount { get; }

    /// <summary>Phase 24-NAV-LOGICAL-FLAGS — optional logical-flags
    /// store the pathfinder consults to gate triangles by actor-class.
    /// Set via <see cref="BindLogicalFlags"/> at region-load time after
    /// the gas has been parsed. Null when the region didn't ship the
    /// file (older / fan content) — pathing falls back to flag-less
    /// behavior, matching pre-NAV-LOGICAL-FLAGS.</summary>
    public LogicalFlagsStore? Flags { get; private set; }

    /// <summary>Phase 24-NAV-LOGICAL-FLAGS — bind the parsed gas store
    /// to this mesh. Safe to call once after build; subsequent calls
    /// overwrite (no expected use case, but no need to guard either).</summary>
    public void BindLogicalFlags(LogicalFlagsStore store) { Flags = store; }

    /// <summary>How many SNO faces were dropped because their canonical vertex collapsed
    /// to a degenerate triangle (two vertices welded onto the same bucket). Zero on clean
    /// DS1 data; non-zero means the weld tolerance was too loose for this region.</summary>
    public int DegenerateFaceCount { get; }

    /// <summary>Edges shared by three or more triangles (T-junctions, stacked ramps).
    /// These are treated as boundaries in the adjacency table — better no adjacency than
    /// an arbitrary pair — so the pathfinder can't walk through a non-manifold seam
    /// and pop out on the wrong surface.</summary>
    public int NonManifoldEdgeCount { get; }

    /// <summary>Cross-kind adjacencies wired by the land↔water seam pass. DS1 authors
    /// water surfaces in their own SNOs whose vertices don't weld to the shoreline floor,
    /// so a vertex-equality manifold pass alone leaves Floor and Water on disconnected
    /// components. <see cref="StitchLandWaterSeams"/> finds Floor and Water boundary edges
    /// whose XZ projections overlap within <see cref="SeamXZToleranceUnits"/> and whose
    /// midpoint Y differs by less than <see cref="SeamYToleranceUnits"/>, then wires them
    /// across — letting an amphibious actor wade onto a beach. Each tally is one
    /// floor↔water pair (counted once, not twice).</summary>
    public int SeamEdgeCount { get; }

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
        int[] sourceNodeIndex,
        int[] sourceLnodeIndex,
        uint[] sourceSnodeGuid,
        int sourceSnodeCount,
        int degenerateFaceCount,
        int nonManifoldEdgeCount,
        int seamEdgeCount,
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
        SourceNodeIndex = sourceNodeIndex;
        SourceLnodeIndex = sourceLnodeIndex;
        SourceSnodeGuid = sourceSnodeGuid;
        SourceSnodeCount = sourceSnodeCount;
        DegenerateFaceCount = degenerateFaceCount;
        NonManifoldEdgeCount = nonManifoldEdgeCount;
        SeamEdgeCount = seamEdgeCount;
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

    /// <summary>How far apart (in XZ projection) two boundary edges may sit and still be
    /// stitched together as a Floor↔Water seam. 0.5 units is wider than the weld tolerance
    /// because shoreline floor and water SNOs are authored independently — vertices that
    /// look "the same" in the editor land 0.2-0.4u apart by the time door-chain transforms
    /// have accumulated.</summary>
    public const float SeamXZToleranceUnits = 0.5f;

    /// <summary>Maximum vertical step between a Floor edge midpoint and a Water edge midpoint
    /// for them to be considered the same shoreline. Half a unit roughly matches DS1's
    /// authored wading depth — anything taller is a cliff into water (no walkable transition).</summary>
    public const float SeamYToleranceUnits = 0.5f;

    /// <summary>Builds a region-scope nav mesh from a region's placed-snode layout and an
    /// SNO resolver. Floor and Water groupings both contribute (each face is tagged via
    /// <see cref="Kinds"/>); Ignored groupings are filtered at the source. Whether a
    /// given actor may enter a Water triangle is a pathfinder-time decision driven by
    /// <see cref="NavTraversal"/>.</summary>
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
        var sourceNode = new List<int>(capacity: 2048);
        // Phase 24-NAV-LOGICAL-FLAGS — per-triangle lnode + snode-guid
        // so the pathfinder can consult LogicalFlagsStore at run time.
        var sourceLnode = new List<int>(capacity: 2048);
        var sourceSnodeGuid = new List<uint>(capacity: 2048);
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

        for (int nodeIdx = 0; nodeIdx < graph.Nodes.Count; nodeIdx++)
        {
            var node = graph.Nodes[nodeIdx];
            if (!layout.TryGetTransform(node.Guid, out var snodeXform)) continue;
            var sno = resolveSno(node.MeshGuid);
            if (sno is null) continue;
            sourceSnodes++;
            foreach (var group in sno.LogicalGroupings)
            {
                // Drop Ignored (cosmetic, off-mesh). Floor and Water both flow through —
                // Water becomes a per-triangle Kinds[] tag the pathfinder consults later.
                if (group.Kind != SnoModel.FloorKind.Floor && group.Kind != SnoModel.FloorKind.Water) continue;
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
                    sourceNode.Add(nodeIdx);
                    sourceLnode.Add(group.Id);
                    sourceSnodeGuid.Add(node.Guid);
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

        var vertsArr = verts.ToArray();
        var kindsArr = kinds.ToArray();

        // Land↔water seam stitching: shoreline Floor and Water SNOs are authored in
        // separate meshes whose vertices don't fall inside the WeldToleranceUnits bucket,
        // so the manifold pass leaves them on disconnected components. Wire cross-kind
        // adjacencies for boundary-edge pairs that share an XZ footprint and a wadeable Y.
        int seamEdges = StitchLandWaterSeams(triCount, indices, neighbors, vertsArr, kindsArr);

        var centroids = new Vector3[triCount];
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
                int cx0 = (int)MathF.Floor((minX - originX) / GridCellSize);
                int cx1 = (int)MathF.Floor((maxX - originX) / GridCellSize);
                int cz0 = (int)MathF.Floor((minZ - originZ) / GridCellSize);
                int cz1 = (int)MathF.Floor((maxZ - originZ) / GridCellSize);
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
            kindsArr,
            centroids,
            sourceNode.ToArray(),
            sourceLnode.ToArray(),
            sourceSnodeGuid.ToArray(),
            sourceSnodes,
            degenerate,
            nonManifoldEdges,
            seamEdges,
            originX,
            originZ,
            cellsX,
            cellsZ,
            grid);
    }

    /// <summary>Wires cross-kind adjacencies for Floor↔Water boundary edges that visually
    /// share a shoreline. DS1 authors water surfaces in separate SNOs whose vertices don't
    /// weld to the floor under <see cref="WeldToleranceUnits"/>, so a vertex-equality
    /// manifold pass alone leaves Floor and Water on disconnected components — fh_r1 ships
    /// 0/1435 water tris with a Floor edge before this pass runs. We pair each Floor
    /// boundary edge with the closest unclaimed Water boundary edge whose XZ projection
    /// overlaps within <see cref="SeamXZToleranceUnits"/>, whose direction is roughly
    /// collinear (|cos θ| ≥ 0.85, ~32°), and whose midpoint Y is within
    /// <see cref="SeamYToleranceUnits"/> of the floor's. Each side keeps the single-int
    /// neighbors[] slot — water claimed by floor A can't also be claimed by floor B —
    /// so we sort candidates by combined (XZ + Y) distance and process best-first.
    /// Returns the number of cross-kind pairs wired (one count per Floor↔Water bond).
    /// The pathfinder still consults <see cref="NavTraversal"/> to decide whether a given
    /// actor may cross — land-only stays land-only, amphibious gets a routable shoreline.</summary>
    private static int StitchLandWaterSeams(
        int triCount,
        int[] indices,
        int[] neighbors,
        Vector3[] verts,
        SnoModel.FloorKind[] kinds)
    {
        // Slot s on triangle t spans the edge OPPOSITE vertex s (verts (s+1)%3 and (s+2)%3).
        // Capture (tri, slot, midpoint, unit XZ direction, length) per boundary edge so the
        // pairing pass can score collinearity + overlap without recomputing per candidate.
        var floorEdges = new List<(int tri, int slot, Vector3 mid, float dirX, float dirZ, float len)>();
        var waterEdges = new List<(int tri, int slot, Vector3 mid, float dirX, float dirZ, float len)>();
        for (int t = 0; t < triCount; t++)
        {
            var kind = kinds[t];
            if (kind != SnoModel.FloorKind.Floor && kind != SnoModel.FloorKind.Water) continue;
            for (int s = 0; s < 3; s++)
            {
                if (neighbors[3 * t + s] != -1) continue;
                var a = verts[indices[3 * t + (s + 1) % 3]];
                var b = verts[indices[3 * t + (s + 2) % 3]];
                var mid = 0.5f * (a + b);
                float dx = b.X - a.X, dz = b.Z - a.Z;
                float len = MathF.Sqrt(dx * dx + dz * dz);
                if (len < 1e-6f) continue; // edge-on-Y, degenerate XZ projection
                float ux = dx / len, uz = dz / len;
                if (kind == SnoModel.FloorKind.Floor) floorEdges.Add((t, s, mid, ux, uz, len));
                else waterEdges.Add((t, s, mid, ux, uz, len));
            }
        }
        if (floorEdges.Count == 0 || waterEdges.Count == 0) return 0;

        // Bin water edges by midpoint XZ cell so the per-floor scan stays linear in nearby
        // candidates rather than full water-edge count. Cell size = 2× tolerance: a floor
        // edge in cell C only needs to inspect cells (C ± 1) × (C ± 1) to cover everything
        // within SeamXZToleranceUnits. Empty meshes already early-exited above.
        const float CellSize = SeamXZToleranceUnits * 2f;
        float waterMinX = float.PositiveInfinity, waterMinZ = float.PositiveInfinity;
        float waterMaxX = float.NegativeInfinity, waterMaxZ = float.NegativeInfinity;
        for (int i = 0; i < waterEdges.Count; i++)
        {
            var m = waterEdges[i].mid;
            if (m.X < waterMinX) waterMinX = m.X;
            if (m.Z < waterMinZ) waterMinZ = m.Z;
            if (m.X > waterMaxX) waterMaxX = m.X;
            if (m.Z > waterMaxZ) waterMaxZ = m.Z;
        }
        int wCellsX = Math.Max(1, (int)MathF.Ceiling((waterMaxX - waterMinX) / CellSize) + 1);
        int wCellsZ = Math.Max(1, (int)MathF.Ceiling((waterMaxZ - waterMinZ) / CellSize) + 1);
        var waterGrid = new List<int>?[wCellsX * wCellsZ];
        for (int i = 0; i < waterEdges.Count; i++)
        {
            var m = waterEdges[i].mid;
            int cx = Math.Clamp((int)MathF.Floor((m.X - waterMinX) / CellSize), 0, wCellsX - 1);
            int cz = Math.Clamp((int)MathF.Floor((m.Z - waterMinZ) / CellSize), 0, wCellsZ - 1);
            (waterGrid[cz * wCellsX + cx] ??= new List<int>()).Add(i);
        }

        // Collect candidate (floor, water, score) triples within tolerance, then process
        // best-first so the cleanest shorelines claim their water partner before any
        // marginal alignment can. Each side gets a single neighbors[] slot, so 1:1 wiring
        // is mandatory — sort + greedy-claim is cheaper than a full bipartite match and
        // good enough for DS1 shoreline geometry.
        const float CollinearityCosMin = 0.85f;
        var pairs = new List<(int fIdx, int wIdx, float score)>(floorEdges.Count);
        for (int f = 0; f < floorEdges.Count; f++)
        {
            var fe = floorEdges[f];
            int cx = Math.Clamp((int)MathF.Floor((fe.mid.X - waterMinX) / CellSize), 0, wCellsX - 1);
            int cz = Math.Clamp((int)MathF.Floor((fe.mid.Z - waterMinZ) / CellSize), 0, wCellsZ - 1);
            for (int dz = -1; dz <= 1; dz++)
            {
                int rz = cz + dz;
                if (rz < 0 || rz >= wCellsZ) continue;
                for (int dx = -1; dx <= 1; dx++)
                {
                    int rx = cx + dx;
                    if (rx < 0 || rx >= wCellsX) continue;
                    var bucket = waterGrid[rz * wCellsX + rx];
                    if (bucket is null) continue;
                    foreach (var w in bucket)
                    {
                        var we = waterEdges[w];
                        float ddx = fe.mid.X - we.mid.X;
                        float ddz = fe.mid.Z - we.mid.Z;
                        float dxz = MathF.Sqrt(ddx * ddx + ddz * ddz);
                        if (dxz > SeamXZToleranceUnits) continue;
                        float dy = MathF.Abs(fe.mid.Y - we.mid.Y);
                        if (dy > SeamYToleranceUnits) continue;
                        float cosAlign = MathF.Abs(fe.dirX * we.dirX + fe.dirZ * we.dirZ);
                        if (cosAlign < CollinearityCosMin) continue;
                        pairs.Add((f, w, dxz + dy));
                    }
                }
            }
        }
        if (pairs.Count == 0) return 0;
        pairs.Sort((a, b) => a.score.CompareTo(b.score));

        var floorClaimed = new bool[floorEdges.Count];
        var waterClaimed = new bool[waterEdges.Count];
        int stitched = 0;
        foreach (var p in pairs)
        {
            if (floorClaimed[p.fIdx] || waterClaimed[p.wIdx]) continue;
            var fe = floorEdges[p.fIdx];
            var we = waterEdges[p.wIdx];
            // Sanity: a previously-stitched non-manifold cleanup or duplicate slot could have
            // changed neighbors[] from -1 since we captured the boundary edges. Refuse to
            // overwrite a real wire — this is defensive and never trips on shipped data.
            if (neighbors[3 * fe.tri + fe.slot] != -1) continue;
            if (neighbors[3 * we.tri + we.slot] != -1) continue;
            neighbors[3 * fe.tri + fe.slot] = we.tri;
            neighbors[3 * we.tri + we.slot] = fe.tri;
            floorClaimed[p.fIdx] = true;
            waterClaimed[p.wIdx] = true;
            stitched++;
        }
        return stitched;
    }

    /// <summary>Finds the triangle containing <paramref name="worldPos"/> by XZ
    /// projection (Y is up in DS1; terrain folds are shallow enough that a 2D point-in-
    /// triangle test picks the right tile). Returns the triangle with the smallest
    /// vertical distance when multiple tiles overlap in XZ (overpasses / stairs).</summary>
    public bool TryFindTriangle(Vector3 worldPos, out int triIndex)
    {
        triIndex = -1;
        if (TriangleCount == 0) return false;
        // Floor-divide, not C# int-cast: (int)(-0.5) truncates to 0, which would silently
        // route queries just below the mesh AABB into cell 0 instead of rejecting them.
        int cx = (int)MathF.Floor((worldPos.X - _gridMinX) / GridCellSize);
        int cz = (int)MathF.Floor((worldPos.Z - _gridMinZ) / GridCellSize);
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
    /// triangle is edge-on in XZ — <see cref="InterpolateYXZ"/> is already guarded against
    /// zero-area denominators, but we double-check the result for NaN/Inf so a downstream
    /// follower never inherits a poisoned Y. Used to keep the actor glued to the terrain.</summary>
    public float SampleYOnTriangle(int tri, Vector3 worldPos)
    {
        var a = Vertices[Indices[3 * tri + 0]];
        var b = Vertices[Indices[3 * tri + 1]];
        var c = Vertices[Indices[3 * tri + 2]];
        float y = InterpolateYXZ(worldPos.X, worldPos.Z, a, b, c);
        return float.IsFinite(y) ? y : Centroids[tri].Y;
    }

    // ClampPointToTriangleXZ + ClosestOnSegmentXZ + SqDistXZ were
    // added during the Phase 24-NAV boundary-respect attempt (commit
    // 2dcef74) and then orphaned when the call site was reverted in
    // SC-NAV-FROZEN-MOBS (db6306a). Removed per audit fold to keep
    // the file honest about what's live. The boundary-respect goal
    // is now carried by SC-NAV-BSP-LOOKUP (BSP-accelerated point-in-
    // mesh) and SC-NAV-OBSTACLE-AVOID (prop-based no-go zones).

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
