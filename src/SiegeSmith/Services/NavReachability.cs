using System;
using System.Collections.Generic;
using System.Numerics;
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

    /// <summary>Region-frame transform per snode — the same layout the nav mesh was built
    /// from, kept so door sockets can be projected into the region frame for
    /// <see cref="TryFindFloorNear"/> without the caller re-deriving the layout.</summary>
    public IReadOnlyDictionary<uint, Matrix4x4> NodeTransforms { get; private set; } =
        new Dictionary<uint, Matrix4x4>();

    // Vertices of every triangle in the seed-reachable component, region frame, flat
    // (3 per triangle). A few thousand tris even for the big shipped regions — cheap to
    // keep, and it makes door-socket floor queries exact instead of node-granular.
    private Vector3[] _walkVerts = Array.Empty<Vector3>();

    /// <summary>How close walkable floor must sit to a door socket to call the door a real
    /// doorway. DS1 authors walkable door sockets AT floor height (the engine's own nav
    /// door-stitcher pairs edges within 1.5 XZ / 0.6 Y); scenery alignment sockets on cliff
    /// and wall pieces sit metres from any floor.</summary>
    public const float DoorFloorXzRadius = 2.0f;
    public const float DoorFloorYTolerance = 0.75f;

    /// <summary>True when walkable floor (seed-reachable component) exists within
    /// <see cref="DoorFloorXzRadius"/>/<see cref="DoorFloorYTolerance"/> of a region-frame
    /// point. <paramref name="floorY"/> returns the floor height there (region frame) —
    /// the flame markers and Play spawn use it to sit at standing height.</summary>
    public bool TryFindFloorNear(Vector3 regionPos, out float floorY)
    {
        floorY = regionPos.Y;
        bool found = false;
        float bestDxz = float.MaxValue;
        for (int i = 0; i < _walkVerts.Length; i += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                var v = _walkVerts[i + k];
                float dy = v.Y - regionPos.Y;
                if (dy < -DoorFloorYTolerance || dy > DoorFloorYTolerance) continue;
                float dx = v.X - regionPos.X, dz = v.Z - regionPos.Z;
                float dxz = dx * dx + dz * dz;
                if (dxz > DoorFloorXzRadius * DoorFloorXzRadius) continue;
                if (dxz < bestDxz) { bestDxz = dxz; floorY = v.Y; }
                found = true;
            }
        }
        return found;
    }

    /// <summary>Door-granular walkability: projects <paramref name="doorLocal"/> (the SNO
    /// door socket, node-local) through the node's region transform and asks whether
    /// player-walkable floor reaches that opening. This is what separates a real doorway
    /// from a scenery alignment socket — a 32 m cliff-corner piece can be "reachable" as a
    /// NODE (its rim touches walkable terrain) while the socket itself hangs on a bare
    /// cliff face no player can stand at.</summary>
    public bool IsDoorWalkable(uint snodeGuid, Vector3 doorLocal, out float floorY)
    {
        floorY = 0f;
        if (!NodeTransforms.TryGetValue(snodeGuid, out var xf)) return false;
        return TryFindFloorNear(Vector3.Transform(doorLocal, xf), out floorY);
    }

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
        int walkTriCount = 0;
        for (int t = 0; t < triCount; t++)
        {
            if (comp[t] == seedComp) { r.ReachableSnodes.Add(nav.SourceSnodeGuid[t]); walkTriCount++; }
            if (comp[t] == largest) r.MainSnodes.Add(nav.SourceSnodeGuid[t]);
        }

        var walkVerts = new Vector3[walkTriCount * 3];
        int w = 0;
        for (int t = 0; t < triCount; t++)
        {
            if (comp[t] != seedComp) continue;
            walkVerts[w++] = nav.Vertices[nav.Indices[3 * t]];
            walkVerts[w++] = nav.Vertices[nav.Indices[3 * t + 1]];
            walkVerts[w++] = nav.Vertices[nav.Indices[3 * t + 2]];
        }
        r._walkVerts = walkVerts;
        r.NodeTransforms = new Dictionary<uint, Matrix4x4>(layout.Transforms);
        return r;
    }
}
