using System.Numerics;

namespace SiegeFX.Core.Assets;

/// <summary>
/// Composes many per-region <see cref="RegionLayout"/>s into a single world-wide
/// <c>guid → Matrix4x4</c> map. DS1 cross-region connectivity doesn't live in
/// <c>nodes.gas</c> (those door edges are all intra-region) — it's in each region's
/// <c>editor/stitch_helper.gas</c>. Each file lists, per destination region, a bag of
/// <c>(pairId → localSnode, localDoor)</c> entries. Matching pairIds across both
/// sides of a boundary reconstruct the cross-region door edge, and the same
/// door-pair composition used inside a region (<see cref="RegionLayout.ComposeNeighborTransform"/>)
/// then pins the neighbor region's offset relative to this one.
///
/// One region is picked as the root and placed at world-origin. Each other region
/// is placed the first time a stitch from an already-placed region lands on one of
/// its snodes. Regions never reached show up in <see cref="UnreachableRegionCount"/>
/// — non-zero generally means an isolated sub-world (demo/menu region) rather than
/// a parser miss.
/// </summary>
public sealed class WorldLayout
{
    public string RootRegion { get; }

    /// <summary>Per-region world offset: <c>global(snode) = RegionOffsets[region] * localLayout.Transforms[snode]</c>.
    /// The root region's offset is <see cref="Matrix4x4.Identity"/> by construction.</summary>
    public IReadOnlyDictionary<string, Matrix4x4> RegionOffsets { get; }

    /// <summary>Global world transform for every placed snode across every placed region.
    /// Keyed by the snode's instance guid (unique across the whole map per DS1 convention).</summary>
    public IReadOnlyDictionary<uint, Matrix4x4> Transforms { get; }

    /// <summary>Which region each guid belongs to — useful when the caller needs the source
    /// region for rendering (texset), debugging, or cross-reference.</summary>
    public IReadOnlyDictionary<uint, string> GuidToRegion { get; }

    public int PlacedRegionCount => RegionOffsets.Count;

    /// <summary>Regions never reached from the root via stitch walks. For contiguous maps
    /// this should be zero; non-zero means island sub-graphs (unused demo/test regions,
    /// or genuine parser misses).</summary>
    public int UnreachableRegionCount { get; }

    /// <summary>Stitch edges the walker saw but couldn't compose (missing door id on one
    /// side, un-invertible matrix, or far snode had no local transform in its region).</summary>
    public int UnresolvedStitchCount { get; }

    /// <summary>Stitch edges whose neighbor-side pair id wasn't found in the neighbor's
    /// stitch file. Indicates an asymmetric stitch — one side declared a pair the other
    /// didn't, or the neighbor's stitch file is missing.</summary>
    public int DanglingStitchCount { get; }

    private WorldLayout(
        string rootRegion,
        IReadOnlyDictionary<string, Matrix4x4> regionOffsets,
        IReadOnlyDictionary<uint, Matrix4x4> transforms,
        IReadOnlyDictionary<uint, string> guidToRegion,
        int unreachableRegionCount,
        int unresolvedStitchCount,
        int danglingStitchCount)
    {
        RootRegion = rootRegion;
        RegionOffsets = regionOffsets;
        Transforms = transforms;
        GuidToRegion = guidToRegion;
        UnreachableRegionCount = unreachableRegionCount;
        UnresolvedStitchCount = unresolvedStitchCount;
        DanglingStitchCount = danglingStitchCount;
    }

    /// <summary>One region's inputs to the world build. <see cref="Path"/> is the
    /// region's tank path (e.g. <c>/world/maps/multiplayer_world/regions/farmland_and_chapel</c>);
    /// the leaf name is taken from that path and used to match stitch <c>dest_region</c>
    /// references. <see cref="Stitches"/> may be null for regions with no stitch file.</summary>
    public readonly record struct RegionEntry(string Path, RegionGraph Graph, RegionLayout Layout, RegionStitchHelper? Stitches);

    /// <summary>Composes a world layout from pre-computed per-region layouts. The caller
    /// owns region enumeration and per-region layout construction — this keeps the
    /// Core assembly free of any map-tank IO knowledge. Pass <paramref name="rootHint"/>
    /// to pin a specific region as the world origin (e.g. the one holding the player's
    /// start snode); otherwise the first region with nodes wins.</summary>
    public static WorldLayout Build(
        IReadOnlyList<RegionEntry> regions,
        Func<uint, SnoModel?> resolveSno,
        string? rootHint = null)
    {
        if (regions.Count == 0)
            return new WorldLayout("", new Dictionary<string, Matrix4x4>(),
                new Dictionary<uint, Matrix4x4>(), new Dictionary<uint, string>(), 0, 0, 0);

        var entryByPath = new Dictionary<string, RegionEntry>(regions.Count);
        var pathByLeaf = new Dictionary<string, string>(regions.Count, StringComparer.OrdinalIgnoreCase);
        var guidToRegion = new Dictionary<uint, string>();
        foreach (var entry in regions)
        {
            entryByPath[entry.Path] = entry;
            pathByLeaf[LeafName(entry.Path)] = entry.Path;
            foreach (var n in entry.Graph.Nodes)
            {
                // A guid appearing in two regions would make the world graph ambiguous.
                // Shipped DS1 data is clean on this — fuzz once across every map if that
                // assumption ever starts to sag.
                if (!guidToRegion.TryAdd(n.Guid, entry.Path))
                    throw new InvalidDataException(
                        $"snode guid 0x{n.Guid:X8} appears in both {guidToRegion[n.Guid]} and {entry.Path}");
            }
        }

        // Precompute stitch pairs: for each (sourceRegion, destRegion, pairId), the
        // local side's (snode, door). Lets us match a source->dest stitch against the
        // dest->source entry by pairId in O(1) during BFS.
        var stitchIndex = new Dictionary<string, Dictionary<string, Dictionary<uint, (uint snode, int door)>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in regions)
        {
            if (entry.Stitches is null) continue;
            var srcLeaf = LeafName(entry.Path);
            var byDest = new Dictionary<string, Dictionary<uint, (uint snode, int door)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (dest, stitches) in entry.Stitches.ByDestination)
            {
                var byPair = new Dictionary<uint, (uint snode, int door)>(stitches.Count);
                foreach (var s in stitches)
                    byPair[s.PairId] = (s.SnodeGuid, s.DoorId);
                byDest[dest] = byPair;
            }
            stitchIndex[srcLeaf] = byDest;
        }

        // Pick a root — caller's hint wins if present and non-empty; else first region
        // with a non-empty layout. Empty layouts can happen if Phase 6b couldn't resolve
        // the anchor's SNO and bailed early.
        string? root = null;
        if (!string.IsNullOrEmpty(rootHint) && entryByPath.ContainsKey(rootHint))
            root = rootHint;
        else
        {
            foreach (var e in regions)
                if (e.Layout.Transforms.Count > 0) { root = e.Path; break; }
        }
        if (root is null)
            return new WorldLayout("", new Dictionary<string, Matrix4x4>(),
                new Dictionary<uint, Matrix4x4>(), new Dictionary<uint, string>(), regions.Count, 0, 0);

        var regionOffsets = new Dictionary<string, Matrix4x4> { [root] = Matrix4x4.Identity };
        var transforms = new Dictionary<uint, Matrix4x4>();

        // Seed: every snode in the root region gets its layout transform as-is.
        var rootEntry = entryByPath[root];
        foreach (var (guid, local) in rootEntry.Layout.Transforms)
            transforms[guid] = local;

        var unresolved = 0;
        var dangling = 0;
        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var curRegionPath = queue.Dequeue();
            var curEntry = entryByPath[curRegionPath];
            var curOffset = regionOffsets[curRegionPath];
            var curLeaf = LeafName(curRegionPath);
            if (!stitchIndex.TryGetValue(curLeaf, out var curStitchByDest)) continue;

            foreach (var (destLeaf, curPairs) in curStitchByDest)
            {
                if (!pathByLeaf.TryGetValue(destLeaf, out var farRegionPath)) { dangling += curPairs.Count; continue; }
                if (regionOffsets.ContainsKey(farRegionPath)) continue;
                var farEntry = entryByPath[farRegionPath];

                if (!stitchIndex.TryGetValue(destLeaf, out var farStitchByDest)) { dangling += curPairs.Count; continue; }
                if (!farStitchByDest.TryGetValue(curLeaf, out var farPairs)) { dangling += curPairs.Count; continue; }

                var placed = false;
                foreach (var (pairId, curSide) in curPairs)
                {
                    if (!farPairs.TryGetValue(pairId, out var farSide)) { dangling++; continue; }

                    // Our local snode's world frame.
                    if (!curEntry.Layout.Transforms.TryGetValue(curSide.snode, out var curLocal)) { unresolved++; continue; }
                    if (!curEntry.Graph.TryGetNode(curSide.snode, out var curNode)) { unresolved++; continue; }
                    var curSno = resolveSno(curNode.MeshGuid);
                    if (curSno is null) { unresolved++; continue; }
                    var localDoor = FindDoorMatrix(curSno, curSide.door);
                    if (localDoor is null) { unresolved++; continue; }

                    // Neighbor side.
                    if (!farEntry.Layout.Transforms.TryGetValue(farSide.snode, out var farLocal)) { unresolved++; continue; }
                    if (!farEntry.Graph.TryGetNode(farSide.snode, out var farNode)) { unresolved++; continue; }
                    var farSno = resolveSno(farNode.MeshGuid);
                    if (farSno is null) { unresolved++; continue; }
                    var farDoorXform = FindDoorMatrix(farSno, farSide.door);
                    if (farDoorXform is null) { unresolved++; continue; }

                    if (!Matrix4x4.Invert(farDoorXform.Value, out var invFar)) { unresolved++; continue; }
                    if (!Matrix4x4.Invert(farLocal, out var invFarLocal)) { unresolved++; continue; }

                    // Same composition as intra-region (RegionLayout.ComposeNeighborTransform),
                    // just in the world frame — curOffset * curLocal is our local snode's
                    // world transform.
                    var wCurGlobal = curOffset * curLocal;
                    var wFarGlobal = RegionLayout.ComposeNeighborTransform(wCurGlobal, localDoor.Value, invFar);

                    // wFarGlobal = farOffset * farLocal  ⇒  farOffset = wFarGlobal * inv(farLocal).
                    var farOffset = wFarGlobal * invFarLocal;
                    regionOffsets[farRegionPath] = farOffset;

                    foreach (var (g, local) in farEntry.Layout.Transforms)
                        transforms[g] = farOffset * local;

                    queue.Enqueue(farRegionPath);
                    placed = true;
                    break; // first good pair wins — other pairs are redundant checks.
                }

                // If no pair composed, every one of them failed; don't double-count dangling.
                _ = placed;
            }
        }

        var reachedGuidToRegion = new Dictionary<uint, string>(transforms.Count);
        foreach (var g in transforms.Keys)
            if (guidToRegion.TryGetValue(g, out var r)) reachedGuidToRegion[g] = r;

        var unreachableRegions = 0;
        foreach (var e in regions)
            if (!regionOffsets.ContainsKey(e.Path)) unreachableRegions++;

        return new WorldLayout(root, regionOffsets, transforms, reachedGuidToRegion,
            unreachableRegions, unresolved, dangling);
    }

    private static string LeafName(string path)
    {
        var i = path.LastIndexOf('/');
        return i < 0 ? path : path[(i + 1)..];
    }

    private static Matrix4x4? FindDoorMatrix(SnoModel sno, int doorId)
    {
        foreach (var d in sno.Doors)
            if (d.Id == (uint)doorId) return RegionLayout.ToMatrix4x4(d.Transform);
        return null;
    }
}
