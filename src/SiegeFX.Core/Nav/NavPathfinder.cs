using System.Numerics;

namespace SiegeFX.Core.Nav;

/// <summary>
/// A* over a <see cref="NavMesh"/>'s triangle adjacency.
///
/// Cost and heuristic are both raw centroid-to-centroid Euclidean distance. That picks
/// tight paths across a single-slope region but leaves the obvious improvements on the
/// table (actual crossing-point distance via the funnel algorithm, kind-aware cost
/// bumps for water tiles). Funnel smoothing lands alongside the actor follower when
/// lumpy paths become visible.
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
        internal SortedSet<(float f, int tri)>? Open;

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
            Open ??= new SortedSet<(float, int)>(FScoreComparer.Instance);
            Open.Clear();
        }
    }

    /// <summary>Finds a triangle-sequence path from <paramref name="startTri"/> to
    /// <paramref name="goalTri"/>. Clears <paramref name="pathDest"/> and appends the
    /// triangle sequence (including both endpoints) on success. Returns <c>false</c> and
    /// leaves <paramref name="pathDest"/> empty when the triangles are in disconnected
    /// components. Pass a reused <paramref name="ws"/> to avoid per-call allocation.</summary>
    public static bool TryFindPath(
        NavMesh mesh,
        int startTri,
        int goalTri,
        List<int> pathDest,
        Workspace? ws = null)
    {
        pathDest.Clear();
        if (startTri < 0 || startTri >= mesh.TriangleCount) return false;
        if (goalTri  < 0 || goalTri  >= mesh.TriangleCount) return false;
        if (startTri == goalTri) { pathDest.Add(startTri); return true; }

        ws ??= new Workspace();
        int triCount = mesh.TriangleCount;
        ws.Prepare(triCount);
        var cameFrom = ws.CameFrom;
        var gScore = ws.GScore;
        var fScore = ws.FScore;
        var closed = ws.Closed;
        var open = ws.Open!;

        gScore[startTri] = 0f;
        fScore[startTri] = Vector3.Distance(mesh.Centroids[startTri], mesh.Centroids[goalTri]);
        open.Add((fScore[startTri], startTri));

        while (open.Count > 0)
        {
            var current = open.Min;
            open.Remove(current);
            int curTri = current.tri;
            if (curTri == goalTri)
            {
                Reconstruct(cameFrom, curTri, pathDest);
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
        int startIdx = dest.Count;
        while (cur != -1)
        {
            dest.Add(cur);
            cur = cameFrom[cur];
        }
        dest.Reverse(startIdx, dest.Count - startIdx);
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
