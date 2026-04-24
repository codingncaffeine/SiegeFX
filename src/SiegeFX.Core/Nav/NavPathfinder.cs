using System.Numerics;

namespace SiegeFX.Core.Nav;

/// <summary>
/// A* over a <see cref="NavMesh"/>'s triangle adjacency. Phase 11b targets region-scope
/// pathing; Phase 11c will extend across region doors using the same Dijkstra frontier
/// logic but with portal edges injected by <c>WorldLayout</c>.
///
/// Cost and heuristic are both raw centroid-to-centroid Euclidean distance. That picks
/// tight paths across a single-slope region but leaves obvious improvements on the
/// table (actual crossing-point distance via the funnel algorithm, kind-aware cost
/// bumps for water tiles). Keeping it simple makes the 11b review easy to read; the
/// expensive refinements land with the first actor that actually follows a path.
/// </summary>
public static class NavPathfinder
{
    /// <summary>Finds a triangle-sequence path from <paramref name="startTri"/> to
    /// <paramref name="goalTri"/>. Returns <c>true</c> with <paramref name="path"/>
    /// populated (including both endpoints) when a route exists, <c>false</c> with an
    /// empty list when the triangles are in disconnected components.</summary>
    public static bool TryFindPath(NavMesh mesh, int startTri, int goalTri, out List<int> path)
    {
        path = new List<int>();
        if (startTri < 0 || startTri >= mesh.TriangleCount) return false;
        if (goalTri  < 0 || goalTri  >= mesh.TriangleCount) return false;
        if (startTri == goalTri) { path.Add(startTri); return true; }

        int triCount = mesh.TriangleCount;
        var cameFrom = new int[triCount];
        var gScore = new float[triCount];
        var fScore = new float[triCount];
        var closed = new bool[triCount];
        for (int i = 0; i < triCount; i++)
        {
            cameFrom[i] = -1;
            gScore[i] = float.PositiveInfinity;
            fScore[i] = float.PositiveInfinity;
        }

        gScore[startTri] = 0f;
        fScore[startTri] = Vector3.Distance(mesh.Centroids[startTri], mesh.Centroids[goalTri]);
        // Tiny region nav-meshes (most DS1 regions fit in a few thousand triangles) mean
        // a binary-heap open set is overkill — a sorted-by-f linear scan beats the heap
        // bookkeeping below ~10k triangles. Re-measure when Phase 11c goes world-scope.
        var open = new SortedSet<(float f, int tri)>(FScoreComparer.Instance) { (fScore[startTri], startTri) };

        while (open.Count > 0)
        {
            var current = open.Min;
            open.Remove(current);
            int curTri = current.tri;
            if (curTri == goalTri)
            {
                Reconstruct(cameFrom, curTri, path);
                return true;
            }
            if (closed[curTri]) continue;
            closed[curTri] = true;

            for (int slot = 0; slot < 3; slot++)
            {
                int nb = mesh.Neighbors[3 * curTri + slot];
                if (nb < 0 || closed[nb]) continue;
                float stepCost = Vector3.Distance(mesh.Centroids[curTri], mesh.Centroids[nb]);
                float tentative = gScore[curTri] + stepCost;
                if (tentative >= gScore[nb]) continue;
                cameFrom[nb] = curTri;
                gScore[nb] = tentative;
                fScore[nb] = tentative + Vector3.Distance(mesh.Centroids[nb], mesh.Centroids[goalTri]);
                open.Add((fScore[nb], nb));
            }
        }
        return false;
    }

    private static void Reconstruct(int[] cameFrom, int end, List<int> dest)
    {
        int cur = end;
        while (cur != -1)
        {
            dest.Add(cur);
            cur = cameFrom[cur];
        }
        dest.Reverse();
    }

    /// <summary>Lexicographic (f, tri) order so the SortedSet acts as a priority queue
    /// without two equal-f entries colliding (SortedSet treats equal keys as one).</summary>
    private sealed class FScoreComparer : IComparer<(float f, int tri)>
    {
        public static readonly FScoreComparer Instance = new();
        public int Compare((float f, int tri) a, (float f, int tri) b)
        {
            int c = a.f.CompareTo(b.f);
            return c != 0 ? c : a.tri.CompareTo(b.tri);
        }
    }
}
