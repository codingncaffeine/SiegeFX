using System.Numerics;

namespace SiegeFX.Core.Assets;

/// <summary>
/// Resolves region-space world transforms for every snode reachable from the region's anchor
/// via its door graph. DS1 regions store no world-space pose on disk — Scott Bilas's
/// "Continuous World" design derives them at load time by walking door pairs and composing
/// each SNO's local door transform against its neighbor's.
///
/// Given an anchor snode at identity, each connected neighbor's world transform is:
/// <c>W(neighbor) = W(this) * localDoor * Flip * farDoor^-1</c>, where <c>Flip</c> is a 180°
/// rotation that flips the door plane (doors are authored to face outward from their
/// own node, so the two frames meet back-to-back).
///
/// Cross-region stitching is out of scope here — <see cref="RegionGraph.TargetNodeGuid"/>
/// may point to a node in a neighbor region (that's the region-to-region anchor). This
/// class falls back to the first local snode in that case and records it as
/// <see cref="AnchorGuid"/>.
/// </summary>
public sealed class RegionLayout
{
    public uint AnchorGuid { get; }
    public IReadOnlyDictionary<uint, Matrix4x4> Transforms { get; }

    /// <summary>Door edges pointing to a snode in a neighbor region. Expected and routine —
    /// Phase 6c will resolve these by walking the global guid → (region, snode) map.</summary>
    public int CrossRegionDoorCount { get; }

    /// <summary>Door edges whose local or far door transform couldn't be resolved (missing
    /// SNO asset, missing door id, or un-invertible matrix). Actionable — non-zero means a
    /// genuine data or parser problem, not cross-region stitching.</summary>
    public int UnresolvedDoorCount { get; }

    /// <summary>Snodes present in the region that the door graph never reached from the anchor.
    /// Zero for a well-formed region; non-zero indicates island graphs or missing doors.</summary>
    public int UnreachableNodeCount { get; }

    private RegionLayout(
        uint anchorGuid,
        IReadOnlyDictionary<uint, Matrix4x4> transforms,
        int crossRegionDoorCount,
        int unresolvedDoorCount,
        int unreachableNodeCount)
    {
        AnchorGuid = anchorGuid;
        Transforms = transforms;
        CrossRegionDoorCount = crossRegionDoorCount;
        UnresolvedDoorCount = unresolvedDoorCount;
        UnreachableNodeCount = unreachableNodeCount;
    }

    public bool TryGetTransform(uint guid, out Matrix4x4 world) =>
        Transforms.TryGetValue(guid, out world);

    /// <summary>Phase 21a-2 — wraps a pre-built <c>guid → Matrix4x4</c> table as a
    /// <see cref="RegionLayout"/>. Lets callers compose a "unified" layout from a
    /// <see cref="WorldLayout"/>'s world-space transforms (player region + neighbors)
    /// and feed it to consumers like <see cref="Nav.NavMesh"/> / <c>ActorSpawner</c>
    /// that already key off <see cref="TryGetTransform"/>. Diagnostic counts are
    /// zeroed because they apply to graph-walk construction, which we skipped here.</summary>
    public static RegionLayout FromTransforms(
        uint anchorGuid,
        IReadOnlyDictionary<uint, Matrix4x4> transforms)
        => new(anchorGuid, transforms, 0, 0, 0);

    /// <summary>Builds the layout for <paramref name="graph"/>. <paramref name="resolveSno"/>
    /// must return each referenced snode's parsed <see cref="SnoModel"/> (by the snode's
    /// <c>mesh_guid</c>, NOT the instance guid). Returns null for unavailable assets;
    /// those snodes are skipped, and any door edges through them become unresolved.</summary>
    public static RegionLayout Build(RegionGraph graph, Func<uint, SnoModel?> resolveSno)
    {
        if (graph.Nodes.Count == 0)
            return new RegionLayout(0, new Dictionary<uint, Matrix4x4>(), 0, 0, 0);

        var anchor = graph.TargetNodeGuid;
        if (!graph.TryGetNode(anchor, out _))
            anchor = graph.Nodes[0].Guid;

        var transforms = new Dictionary<uint, Matrix4x4>(graph.Nodes.Count)
        {
            [anchor] = Matrix4x4.Identity,
        };

        var crossRegion = 0;
        var unresolved = 0;
        var queue = new Queue<uint>();
        queue.Enqueue(anchor);

        while (queue.Count > 0)
        {
            var currentGuid = queue.Dequeue();
            if (!graph.TryGetNode(currentGuid, out var current)) continue;

            var currentSno = resolveSno(current.MeshGuid);
            if (currentSno is null) continue;
            var wCurrent = transforms[currentGuid];

            foreach (var door in current.Doors)
            {
                if (!graph.TryGetNode(door.FarGuid, out var far))
                {
                    crossRegion++;
                    continue;
                }
                if (transforms.ContainsKey(far.Guid)) continue;

                var farSno = resolveSno(far.MeshGuid);
                if (farSno is null) { unresolved++; continue; }

                var localDoor = FindDoor(currentSno, door.LocalId);
                var farDoorXform = FindDoor(farSno, door.FarDoorId);
                if (localDoor is null || farDoorXform is null) { unresolved++; continue; }

                if (!Matrix4x4.Invert(farDoorXform.Value, out var invFar))
                {
                    // Door transforms are rigid (rotation + translation) so inversion should
                    // always succeed. A singular matrix means the SNO's door xform is
                    // degenerate — treat as an unresolved edge rather than poisoning the walk.
                    unresolved++;
                    continue;
                }

                var wFar = ComposeNeighborTransform(wCurrent, localDoor.Value, invFar);
                transforms[far.Guid] = wFar;
                queue.Enqueue(far.Guid);
            }
        }

        var unreachable = graph.Nodes.Count - transforms.Count;
        return new RegionLayout(anchor, transforms, crossRegion, unresolved, unreachable);
    }

    private static Matrix4x4? FindDoor(SnoModel sno, int doorId)
    {
        foreach (var d in sno.Doors)
            if (d.Id == (uint)doorId) return ToMatrix4x4(d.Transform);
        return null;
    }

    /// <summary>SC-ELEVATOR — public door-alignment for callers that place a
    /// node OUTSIDE the BFS walk. Computes the world transform of
    /// <paramref name="farSno"/> when its door <paramref name="farDoorId"/>
    /// mates with <paramref name="anchorSno"/>'s door
    /// <paramref name="anchorDoorId"/>, the anchor standing at
    /// <paramref name="wAnchor"/>. This is exactly how an elevator car's stop
    /// pose is defined: elevator_door_levelN mated to the connect node's
    /// connect_door_levelN. False when either door id is missing from its SNO
    /// or the far door transform is degenerate.</summary>
    public static bool TryAlignThroughDoor(
        SnoModel anchorSno, Matrix4x4 wAnchor, int anchorDoorId,
        SnoModel farSno, int farDoorId, out Matrix4x4 wFar)
    {
        wFar = Matrix4x4.Identity;
        var anchorDoor = FindDoor(anchorSno, anchorDoorId);
        var farDoor = FindDoor(farSno, farDoorId);
        if (anchorDoor is null || farDoor is null) return false;
        if (!Matrix4x4.Invert(farDoor.Value, out var invFar)) return false;
        wFar = ComposeNeighborTransform(wAnchor, anchorDoor.Value, invFar);
        return true;
    }

    /// <summary>Converts the on-disk 4x3 door frame (row-major 3x3 rotation + translation)
    /// into a 4x4 affine matrix in System.Numerics row-vector convention. DS1 axis swaps
    /// happen at render time, not here.</summary>
    internal static Matrix4x4 ToMatrix4x4(SnoModel.Xform4x3 xf) => new(
        xf.Row0.X, xf.Row0.Y, xf.Row0.Z, 0f,
        xf.Row1.X, xf.Row1.Y, xf.Row1.Z, 0f,
        xf.Row2.X, xf.Row2.Y, xf.Row2.Z, 0f,
        xf.Translation.X, xf.Translation.Y, xf.Translation.Z, 1f);

    /// <summary>Composes <c>W(far) = invFarDoor * Flip * localDoor * W(current)</c>. Operand
    /// order matches OpenSiege <c>SiegeNodeMesh::connect</c> (ReaderWriterSiegeNodeList.cpp
    /// in <c>_ds1refs/</c>). In row-vector convention a point transforms as
    /// <c>p' = p * M1 * M2 * ...</c> with M1 applied first — so to move a point expressed in
    /// the far node's local space into world we walk it inverse-far-door → 180° hinge →
    /// current-door → current-to-world, and that must be the composition order. The earlier
    /// <c>wCurrent * localDoor * flip * invFarDoor</c> form had the operands REVERSED, which
    /// applied the current-to-world lift before the door hinge — the hops composed to a near-
    /// identity cluster around the anchor instead of walking outward along the door chain
    /// (classic "the whole region clumps at origin" failure mode).
    ///
    /// The 180° flip is around Y: DS1 authors door frames in a Y-up door-local space with +Z
    /// pointing outward, so two mating doors face each other along opposite +Z — Y is the
    /// hinge axis. <paramref name="invFarDoor"/> is pre-inverted in the caller so a singular
    /// far-door matrix is diagnosable as an unresolved edge, not a silent identity fallback.</summary>
    internal static Matrix4x4 ComposeNeighborTransform(
        Matrix4x4 wCurrent, Matrix4x4 localDoor, Matrix4x4 invFarDoor)
    {
        var flip = Matrix4x4.CreateRotationY(MathF.PI);
        return invFarDoor * flip * localDoor * wCurrent;
    }
}
