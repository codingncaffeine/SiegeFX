using System.Diagnostics.CodeAnalysis;
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

    /// <summary>Converts the on-disk 4x3 door frame (row-major 3x3 rotation + translation)
    /// into a 4x4 affine matrix in System.Numerics row-vector convention. DS1 axis swaps
    /// happen at render time, not here.</summary>
    internal static Matrix4x4 ToMatrix4x4(SnoModel.Xform4x3 xf) => new(
        xf.Row0.X, xf.Row0.Y, xf.Row0.Z, 0f,
        xf.Row1.X, xf.Row1.Y, xf.Row1.Z, 0f,
        xf.Row2.X, xf.Row2.Y, xf.Row2.Z, 0f,
        xf.Translation.X, xf.Translation.Y, xf.Translation.Z, 1f);

    /// <summary>Composes <c>W(far) = W(current) * localDoor * Flip * invFarDoor</c>. The
    /// 180° flip is around Y because DS1 authors door frames in a Y-up door-local space with
    /// +Z pointing out of the node; two mating doors must therefore face each other along
    /// opposite +Z, which is the Y-axis rotation. <paramref name="invFarDoor"/> must be the
    /// far door's matrix already inverted — invert in the caller so failure is diagnosable.
    ///
    /// KNOWN LIMITATION: some neighbors past the first hop render below and crossed — one
    /// cluster around the anchor looks correct, outward chains spike below the good surface.
    /// CreateRotationY tested as best; Z made everything wrong, X flattened the region.
    /// Likely cause: a per-door orientation flag we're ignoring, or DS1 door xforms stored
    /// in a non-row-vector convention that needs transposing. Needs a data-driven fix
    /// (dump mating pair xforms, solve by hand) rather than more flip-axis guessing.</summary>
    internal static Matrix4x4 ComposeNeighborTransform(
        Matrix4x4 wCurrent, Matrix4x4 localDoor, Matrix4x4 invFarDoor)
    {
        var flip = Matrix4x4.CreateRotationY(MathF.PI);
        return wCurrent * localDoor * flip * invFarDoor;
    }
}
