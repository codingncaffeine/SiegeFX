using System.Numerics;

namespace SiegeFX.Core.Nav;

/// <summary>
/// "Simple stupid funnel" string-pull over a triangle-sequence path. Given the A* path
/// (triangle indices) plus start/goal world positions, produces a smoothed list of
/// waypoints that hugs the inside corners of the corridor instead of zig-zagging
/// through every triangle centroid.
///
/// Implementation follows Mikko Mononen's published pseudocode. The funnel is operated
/// in 2D (XZ) — Y is resampled by <see cref="NavFollower"/> against whatever triangle
/// the walker is currently standing on, so the smoother doesn't need to think about it.
/// </summary>
public static class NavFunnel
{
    /// <summary>Builds the funnel-smoothed waypoint list for a triangle path.
    /// The output is appended to (caller clears if they care). The first waypoint is
    /// always the corner where the path first turns away from a straight line to the
    /// goal; the last waypoint is always <paramref name="goal"/>. If the start and
    /// goal are mutually visible through the corridor, the output contains just
    /// <paramref name="goal"/>.</summary>
    public static void BuildWaypoints(
        NavMesh mesh,
        IReadOnlyList<int> trianglePath,
        Vector3 start,
        Vector3 goal,
        List<Vector3> output)
    {
        if (trianglePath.Count == 0)
        {
            output.Add(goal);
            return;
        }

        // Build the portal sequence: portal[0] is degenerate (left==right==start);
        // portal[k] (1..N) is the shared edge of trianglePath[k-1]→trianglePath[k]
        // with left/right ordered so "left" is on the left of the centroid travel
        // direction; portal[N+1] is degenerate (left==right==goal).
        var portalLeft = new Vector3[trianglePath.Count + 1];
        var portalRight = new Vector3[trianglePath.Count + 1];
        portalLeft[0] = start;
        portalRight[0] = start;

        for (var k = 1; k < trianglePath.Count; k++)
        {
            var triA = trianglePath[k - 1];
            var triB = trianglePath[k];
            if (!TryGetSharedEdge(mesh, triA, triB, out var p, out var q, out var r))
            {
                // Adjacency guarantees this doesn't fire on real A*-produced paths,
                // but a malformed input shouldn't crash the smoother — fall back to
                // the centroid as a degenerate portal so the walker keeps moving.
                p = q = mesh.Centroids[triB];
                r = mesh.Centroids[triA];
            }

            // Order p/q so p is on the LEFT when crossing from triA into triB.
            // Reference direction = portal midpoint minus triA's vertex opposite
            // the shared edge. That direction points out of triA through the
            // portal. (Centroid→centroid travel direction is NOT usable here:
            // on steep stair treads consecutive centroids nearly coincide in
            // XZ, the cross sign becomes noise, and every mis-ordered portal
            // forces an apex emission — the "waypoint per tread" zigzag.)
            var midX = 0.5f * (p.X + q.X);
            var midZ = 0.5f * (p.Z + q.Z);
            var dirX = midX - r.X;
            var dirZ = midZ - r.Z;
            // 2D cross of (dir) × (p - r) — sign tells us which side of the dir line p lies on.
            var cross = dirX * (p.Z - r.Z) - dirZ * (p.X - r.X);
            // Degeneracy guard: stair RISER faces are walkable in DS1 nav
            // meshes but project to a line in XZ (all three verts share one
            // plan-view line). There the opposite vertex sits ON the portal
            // line, |cross| collapses to float noise, and a randomly-swapped
            // portal makes the funnel believe the corridor crosses itself —
            // it emits both wall corners alternately (the 56-waypoint stair
            // descent). Fall back to continuity: keep the side assignment
            // closest to the previous portal's.
            var edgeLenXZ = MathF.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z));
            if (MathF.Abs(cross) > 0.01f * MathF.Max(edgeLenXZ, 0.01f))
            {
                if (cross > 0f) { portalLeft[k] = p; portalRight[k] = q; }
                else            { portalLeft[k] = q; portalRight[k] = p; }
            }
            else
            {
                var prevL = portalLeft[k - 1];
                var prevR = portalRight[k - 1];
                if (PointEqualsXZ(prevL, prevR))
                {
                    // Previous portal is degenerate (the start portal, or a
                    // pinch point) — the continuity distances tie exactly and
                    // the tie-break would be slot-order noise. Order against
                    // the crossing direction out of the degenerate point
                    // instead.
                    var ddx = midX - prevL.X;
                    var ddz = midZ - prevL.Z;
                    var c2 = ddx * (p.Z - prevL.Z) - ddz * (p.X - prevL.X);
                    if (c2 > 0f) { portalLeft[k] = p; portalRight[k] = q; }
                    else         { portalLeft[k] = q; portalRight[k] = p; }
                }
                else
                {
                    float keep = DistXZ(p, prevL) + DistXZ(q, prevR);
                    float swap = DistXZ(q, prevL) + DistXZ(p, prevR);
                    if (keep <= swap) { portalLeft[k] = p; portalRight[k] = q; }
                    else              { portalLeft[k] = q; portalRight[k] = p; }
                }
            }
        }

        portalLeft[trianglePath.Count] = goal;
        portalRight[trianglePath.Count] = goal;

        // SSF main loop.
        var apex = start;
        var apexIdx = 0;
        var leftIdx = 0;
        var rightIdx = 0;
        var left = start;
        var right = start;

        for (var i = 1; i <= trianglePath.Count; i++)
        {
            var newLeft = portalLeft[i];
            var newRight = portalRight[i];

            // Update right side.
            if (TriArea2XZ(apex, right, newRight) <= 0f)
            {
                if (PointEqualsXZ(apex, right) || TriArea2XZ(apex, left, newRight) > 0f)
                {
                    right = newRight;
                    rightIdx = i;
                }
                else
                {
                    // Right would cross left — emit left as new apex and restart.
                    if (output.Count == 0 || !PointEqualsXZ(output[^1], left)) output.Add(left);
                    apex = left;
                    apexIdx = leftIdx;
                    left = apex;
                    right = apex;
                    leftIdx = apexIdx;
                    rightIdx = apexIdx;
                    i = apexIdx;
                    continue;
                }
            }

            // Update left side (mirror).
            if (TriArea2XZ(apex, left, newLeft) >= 0f)
            {
                if (PointEqualsXZ(apex, left) || TriArea2XZ(apex, right, newLeft) < 0f)
                {
                    left = newLeft;
                    leftIdx = i;
                }
                else
                {
                    if (output.Count == 0 || !PointEqualsXZ(output[^1], right)) output.Add(right);
                    apex = right;
                    apexIdx = rightIdx;
                    left = apex;
                    right = apex;
                    leftIdx = apexIdx;
                    rightIdx = apexIdx;
                    i = apexIdx;
                    continue;
                }
            }
        }

        // Always emit the goal as the final waypoint.
        if (output.Count == 0 || !PointEqualsXZ(output[^1], goal)) output.Add(goal);
    }

    private static bool TryGetSharedEdge(NavMesh mesh, int triA, int triB, out Vector3 p, out Vector3 q, out Vector3 opposite)
    {
        for (var slot = 0; slot < 3; slot++)
        {
            if (mesh.Neighbors[triA * 3 + slot] != triB) continue;
            // The edge opposite vertex `slot` is (vert[(slot+1)%3], vert[(slot+2)%3]).
            var v1 = mesh.Indices[triA * 3 + (slot + 1) % 3];
            var v2 = mesh.Indices[triA * 3 + (slot + 2) % 3];
            p = mesh.Vertices[v1];
            q = mesh.Vertices[v2];
            opposite = mesh.Vertices[mesh.Indices[triA * 3 + slot]];
            return true;
        }
        p = q = opposite = default;
        return false;
    }

    private static float DistXZ(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static float TriArea2XZ(Vector3 a, Vector3 b, Vector3 c)
    {
        // Signed 2D triangle area × 2 in the XZ plane, using Recast/Detour's
        // convention: result is negative when (a, b, c) wraps CCW viewed from +Y up.
        // The published SSF pseudocode is calibrated against this sign — `<= 0`
        // means "newRight tightens the right edge", `>= 0` means "newLeft tightens
        // the left edge". Using the standard math sign here makes both comparisons
        // miss every tightening opportunity, so the funnel devolves into chasing
        // every portal corner unsmoothed (verified empirically: with the standard
        // sign, `region follow fh_r1 (10,10)→(30,30)` produced 37 waypoints over
        // a 39-triangle path; with this sign, expect a single-digit count).
        return (c.X - a.X) * (b.Z - a.Z) - (b.X - a.X) * (c.Z - a.Z);
    }

    private static bool PointEqualsXZ(Vector3 a, Vector3 b)
    {
        // Coarse equality is fine — funnel apex/portal vertices come from the same
        // canonical NavMesh.Vertices pool, so when they're "the same" they are
        // bit-identical floats. A slack threshold avoids relying on that contract.
        const float eps = 1e-5f;
        return MathF.Abs(a.X - b.X) < eps && MathF.Abs(a.Z - b.Z) < eps;
    }
}
