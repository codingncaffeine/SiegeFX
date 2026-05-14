using System.Numerics;

namespace SiegeFX.Core.Nav;

/// <summary>
/// A* over a <see cref="NavMesh"/>'s triangle adjacency.
///
/// Step cost is centroid-to-centroid Euclidean distance scaled by the destination
/// triangle's <see cref="NavTraversal.GetMultiplier"/>; the heuristic is raw straight-
/// line distance to the goal. The heuristic is admissible only for actors whose
/// minimum kind-multiplier is <c>1</c> (i.e. <c>Floor</c> is always cheapest). DS1's
/// shipped templates all satisfy that, so optimality holds in practice.
/// </summary>
public static class NavPathfinder
{
    /// <summary>Reusable per-caller state so actors that replan every tick don't allocate
    /// a full set of scratch arrays per call. Safe to cache per-actor; auto-grows when
    /// the target mesh exceeds the last seen triangle count.</summary>
    public sealed class Workspace
    {
        internal int[] CameFrom = Array.Empty<int>();
        internal float[] GScore = Array.Empty<float>();
        internal float[] FScore = Array.Empty<float>();
        internal bool[] Closed = Array.Empty<bool>();
        // Binary min-heap, replacing the original SortedSet. The classic A*-with-decrease-
        // key tradeoff: instead of removing a stale entry when a node's f-score improves
        // we just push a fresh one and let the pop loop discard duplicates by checking
        // Closed[] (already in place for SortedSet). Fewer per-op compares, no red-black
        // rebalancing, and no IComparer<T> indirection. fh_r1 bench (10k probes, 173-tri
        // avg path): 504 us/probe SortedSet → 247 us/probe heap = 2.0x speedup.
        internal (float f, int tri)[] OpenHeap = Array.Empty<(float, int)>();
        internal int OpenCount;

        internal void Prepare(int triCount)
        {
            if (CameFrom.Length < triCount)
            {
                CameFrom = new int[triCount];
                GScore = new float[triCount];
                FScore = new float[triCount];
                Closed = new bool[triCount];
            }
            else
            {
                Array.Clear(Closed, 0, triCount);
            }
            for (int i = 0; i < triCount; i++)
            {
                CameFrom[i] = -1;
                GScore[i] = float.PositiveInfinity;
                FScore[i] = float.PositiveInfinity;
            }
            // Heap capacity grows with the mesh; we never shrink — workspaces are
            // long-lived and the upper bound on simultaneously-open nodes is bounded
            // by triCount (one entry per node, plus stale duplicates). 4× headroom is
            // empirically enough for the worst probe across all 81 World regions.
            int needed = triCount * 4;
            if (OpenHeap.Length < needed) OpenHeap = new (float, int)[needed];
            OpenCount = 0;
        }

        internal void HeapPush(float f, int tri)
        {
            // Auto-grow on overflow — extremely rare (only when many staleness duplicates
            // pile up before any get popped) but cheaper than dropping the probe.
            if (OpenCount == OpenHeap.Length)
            {
                var bigger = new (float, int)[OpenHeap.Length * 2];
                Array.Copy(OpenHeap, bigger, OpenCount);
                OpenHeap = bigger;
            }
            int i = OpenCount++;
            OpenHeap[i] = (f, tri);
            // Sift up. (f, tri) lex order matches the original FScoreComparer.
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                var p = OpenHeap[parent];
                var c = OpenHeap[i];
                if (p.f < c.f || (p.f == c.f && p.tri <= c.tri)) break;
                OpenHeap[i] = p;
                OpenHeap[parent] = c;
                i = parent;
            }
        }

        internal bool HeapPop(out float f, out int tri)
        {
            if (OpenCount == 0) { f = 0; tri = -1; return false; }
            var top = OpenHeap[0];
            f = top.f;
            tri = top.tri;
            OpenCount--;
            if (OpenCount == 0) return true;
            var moved = OpenHeap[OpenCount];
            OpenHeap[0] = moved;
            // Sift down.
            int i = 0;
            int n = OpenCount;
            while (true)
            {
                int left = (i << 1) + 1;
                if (left >= n) break;
                int right = left + 1;
                int best = left;
                var lv = OpenHeap[left];
                if (right < n)
                {
                    var rv = OpenHeap[right];
                    if (rv.f < lv.f || (rv.f == lv.f && rv.tri < lv.tri)) best = right;
                }
                var bv = OpenHeap[best];
                var cur = OpenHeap[i];
                if (cur.f < bv.f || (cur.f == bv.f && cur.tri <= bv.tri)) break;
                OpenHeap[i] = bv;
                OpenHeap[best] = cur;
                i = best;
            }
            return true;
        }
    }

    /// <summary>Finds a triangle-sequence path from <paramref name="startTri"/> to
    /// <paramref name="goalTri"/>. Clears <paramref name="pathDest"/> and appends the
    /// triangle sequence (including both endpoints) on success. Returns <c>false</c> and
    /// leaves <paramref name="pathDest"/> empty when the triangles are in disconnected
    /// components, when either endpoint is impassable under <paramref name="traversal"/>,
    /// or when no passable corridor connects them. Pass a reused <paramref name="ws"/>
    /// to avoid per-call allocation. <paramref name="traversal"/> defaults to
    /// <see cref="NavTraversal.LandOnly"/> — DS1 stock behavior, water blocks.</summary>
    public static bool TryFindPath(
        NavMesh mesh,
        int startTri,
        int goalTri,
        List<int> pathDest,
        Workspace? ws = null,
        NavTraversal? traversal = null)
    {
        pathDest.Clear();
        if (startTri < 0 || startTri >= mesh.TriangleCount) return false;
        if (goalTri  < 0 || goalTri  >= mesh.TriangleCount) return false;
        traversal ??= NavTraversal.LandOnly;
        // Refuse impassable endpoints up front. Without this, A* would walk the whole
        // open set looking for a goal it can never enter.
        if (!traversal.CanEnter(mesh.Kinds[startTri])) { LastFailure = $"start tri {startTri} kind={mesh.Kinds[startTri]} impassable under {traversal.GetType().Name}"; return false; }
        if (!traversal.CanEnter(mesh.Kinds[goalTri])) { LastFailure = $"goal tri {goalTri} kind={mesh.Kinds[goalTri]} impassable under {traversal.GetType().Name}"; return false; }
        // Phase 24-NAV-LOGICAL-FLAGS — per-triangle actor-class gate.
        // When the region's logical_flags.gas tags a triangle's lnode
        // as e.g. computer-only, a human player path-request rejects
        // it as start/goal AND skips it during expansion. Local helper
        // captures mesh + traversal for the inner loop. SourceLnodeIndex
        // is always 0..255 (byte cast from group.Id), so no sentinel
        // check — the store returns CanEnter=true for unflagged
        // (snode,lnode) pairs already.
        bool TriPasses(int tri) =>
            mesh.Flags is null ||
            mesh.Flags.CanEnter(mesh.SourceSnodeGuid[tri],
                (byte)mesh.SourceLnodeIndex[tri], traversal.Actor);
        if (!TriPasses(startTri)) { LastFailure = $"start tri {startTri} fails logical-flags gate"; return false; }
        // SC-NAV-OBSTACLE-AVOID — refuse pathing INTO an obstacle.
        // Start triangle can be blocked (actor wedged against a wall
        // at spawn / after a knockback / etc) but the goal must not
        // be blocked, and we'll filter blocked triangles out of A*
        // expansion below.
        if (mesh.IsBlocked(goalTri)) { LastFailure = $"goal tri {goalTri} obstacle-blocked"; return false; }
        // SC-FADE-NODES-LNODE — refuse pathing into a faded-out
        // dungeon layer. Start can be fade-hidden (e.g. a fade
        // just fired while the actor stood on the now-hidden
        // tile — they'll recover via stuck-replan), but the goal
        // must be a visible/revealed surface.
        if (mesh.IsFadeHidden(goalTri)) { LastFailure = $"goal tri {goalTri} fade-hidden"; return false; }
        if (!TriPasses(goalTri)) { LastFailure = $"goal tri {goalTri} fails logical-flags gate (snode=0x{mesh.SourceSnodeGuid[goalTri]:X8} lnode={mesh.SourceLnodeIndex[goalTri]})"; return false; }
        if (startTri == goalTri) { pathDest.Add(startTri); return true; }

        ws ??= new Workspace();
        int triCount = mesh.TriangleCount;
        ws.Prepare(triCount);
        var cameFrom = ws.CameFrom;
        var gScore = ws.GScore;
        var fScore = ws.FScore;
        var closed = ws.Closed;

        gScore[startTri] = 0f;
        fScore[startTri] = Vector3.Distance(mesh.Centroids[startTri], mesh.Centroids[goalTri]);
        ws.HeapPush(fScore[startTri], startTri);

        while (ws.HeapPop(out _, out int curTri))
        {
            if (curTri == goalTri)
            {
                Reconstruct(cameFrom, curTri, pathDest);
                return true;
            }
            // Stale duplicate: the heap can hold multiple entries per tri (one per
            // f-score improvement). Closed[] catches that — first pop wins, the rest
            // are no-ops.
            if (closed[curTri]) continue;
            closed[curTri] = true;

            for (int slot = 0; slot < 3; slot++)
            {
                int nb = mesh.Neighbors[3 * curTri + slot];
                if (nb < 0 || closed[nb]) continue;
                // SC-NAV-OBSTACLE-AVOID — A* never expands into an
                // obstacle-blocked triangle.
                if (mesh.IsBlocked(nb)) continue;
                // SC-FADE-NODES-LNODE — A* never expands into a
                // fade-hidden triangle.
                if (mesh.IsFadeHidden(nb)) continue;
                float mul = traversal.GetMultiplier(mesh.Kinds[nb]);
                if (float.IsPositiveInfinity(mul)) continue;
                if (!TriPasses(nb)) continue;
                float stepCost = Vector3.Distance(mesh.Centroids[curTri], mesh.Centroids[nb]) * mul;
                float tentative = gScore[curTri] + stepCost;
                if (tentative >= gScore[nb]) continue;
                cameFrom[nb] = curTri;
                gScore[nb] = tentative;
                fScore[nb] = tentative + Vector3.Distance(mesh.Centroids[nb], mesh.Centroids[goalTri]);
                ws.HeapPush(fScore[nb], nb);
            }
        }
        LastFailure = $"no corridor from start tri {startTri} (snode=0x{mesh.SourceSnodeGuid[startTri]:X8} lnode={mesh.SourceLnodeIndex[startTri]}) to goal tri {goalTri} (snode=0x{mesh.SourceSnodeGuid[goalTri]:X8} lnode={mesh.SourceLnodeIndex[goalTri]}) (disconnected components)";
        return false;
    }

    /// <summary>Most recent failure reason from <see cref="TryFindPath"/>. Set
    /// whenever the pathfinder returns false; left stale on success. Read by
    /// callers that want to surface "why blocked" diagnostics; not threadsafe
    /// (single-threaded by design — see NavFollower remarks).</summary>
    public static string LastFailure { get; private set; } = "";

    private static void Reconstruct(int[] cameFrom, int end, List<int> dest)
    {
        int cur = end;
        int startIdx = dest.Count;
        while (cur != -1)
        {
            dest.Add(cur);
            cur = cameFrom[cur];
        }
        dest.Reverse(startIdx, dest.Count - startIdx);
    }

}
