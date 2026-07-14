using System;
using System.Collections.Generic;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Nav;

namespace SiegeSmith.Services;

/// <summary>Walkability analysis over a region's REAL nav mesh — the same
/// <see cref="NavMesh.BuildForRegion"/> the engine runs at load, so "reachable" here means
/// the pathfinder itself could route a character there. Used to flag stitch doors that sit
/// on decorative/sealed terrain (backdrop cliffs, capped peaks) the player can never walk to.
/// Static estimate only: elevators, levers and teleporters change reachability at run time
/// and are NOT simulated, which is why callers tag rather than hide off-path doors.</summary>
public sealed class NavReachability
{
    /// <summary>Snode guids owning at least one nav triangle in the component reachable
    /// from the seed node (the region's target/anchor — where Play starts by default).</summary>
    public HashSet<uint> ReachableSnodes { get; } = new();

    /// <summary>Snode guids of the LARGEST walkable component — the region's "main area",
    /// used when the seed node itself contributes no nav triangles.</summary>
    public HashSet<uint> MainSnodes { get; } = new();

    public int ComponentCount { get; private set; }
    public int TriangleCount { get; private set; }

    public static NavReachability? Compute(
        RegionGraph graph, RegionLayout layout, Func<uint, SnoModel?> resolveSno, uint seedSnode)
    {
        NavMesh nav;
        try { nav = NavMesh.BuildForRegion(graph, layout, resolveSno); }
        catch { return null; }
        int triCount = nav.Indices.Length / 3;
        if (triCount == 0) return null;

        // Connected components over the welded triangle adjacency. Floor and Water both
        // count as walkable — whether a given actor may enter water is a pathfinder-time
        // decision, and treating it as passable keeps the check from crying wolf on fords.
        var comp = new int[triCount];
        for (int i = 0; i < triCount; i++) comp[i] = -1;
        var sizes = new List<int>();
        var stack = new Stack<int>();
        for (int t = 0; t < triCount; t++)
        {
            if (comp[t] >= 0) continue;
            int c = sizes.Count, size = 0;
            comp[t] = c;
            stack.Push(t);
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                size++;
                for (int s = 0; s < 3; s++)
                {
                    int nb = nav.Neighbors[3 * cur + s];
                    if (nb >= 0 && comp[nb] < 0) { comp[nb] = c; stack.Push(nb); }
                }
            }
            sizes.Add(size);
        }

        int largest = 0;
        for (int i = 1; i < sizes.Count; i++) if (sizes[i] > sizes[largest]) largest = i;

        // Seed component: the component holding the most of the seed node's triangles.
        // A seed with no nav at all (pure-decorative anchor) falls back to the main area.
        var seedHits = new Dictionary<int, int>();
        for (int t = 0; t < triCount; t++)
            if (nav.SourceSnodeGuid[t] == seedSnode)
                seedHits[comp[t]] = seedHits.TryGetValue(comp[t], out var v) ? v + 1 : 1;
        int seedComp = largest, best = -1;
        foreach (var kv in seedHits) if (kv.Value > best) { best = kv.Value; seedComp = kv.Key; }

        var r = new NavReachability { ComponentCount = sizes.Count, TriangleCount = triCount };
        for (int t = 0; t < triCount; t++)
        {
            if (comp[t] == seedComp) r.ReachableSnodes.Add(nav.SourceSnodeGuid[t]);
            if (comp[t] == largest) r.MainSnodes.Add(nav.SourceSnodeGuid[t]);
        }
        return r;
    }
}
