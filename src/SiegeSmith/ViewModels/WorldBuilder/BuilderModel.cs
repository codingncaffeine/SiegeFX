using System.Collections.Generic;

namespace SiegeSmith.ViewModels.WorldBuilder;

/// <summary>An editable region — a graph of placed SNO nodes joined by doors, mirroring DS1's
/// <c>terrain_nodes/nodes.gas</c>. World positions are NOT stored: the engine (and our preview)
/// derive them by walking the door graph from <see cref="TargetGuid"/>. Building a world here
/// means connecting node doors, not typing coordinates.</summary>
public sealed class BuilderRegion
{
    public List<BuilderNode> Nodes { get; } = new();

    /// <summary>Anchor node whose local frame is the region origin (identity). The first node
    /// placed becomes the target; every other node's pose is composed relative to it.</summary>
    public uint TargetGuid { get; set; }

    public BuilderNode? Find(uint guid)
    {
        foreach (var n in Nodes) if (n.Guid == guid) return n;
        return null;
    }
}

/// <summary>One placed SNO instance. <see cref="Doors"/> holds the reciprocal door edges this
/// node participates in (each edge is mirrored on the far node).</summary>
public sealed class BuilderNode
{
    public required uint Guid { get; init; }
    // set: ED-5 replace-mesh-in-place swaps the mesh while keeping the guid
    // and door edges, so everything anchored to the node survives the swap.
    public required uint MeshGuid { get; set; }
    public string TexsetAbbr { get; set; } = "";
    public List<BuilderDoor> Doors { get; } = new();

    public bool UsesDoor(int localId)
    {
        foreach (var d in Doors) if (d.LocalId == localId) return true;
        return false;
    }
}

/// <summary>A door edge: this node's local door <see cref="LocalId"/> mates with
/// <see cref="FarDoorId"/> on node <see cref="FarGuid"/>.</summary>
public readonly record struct BuilderDoor(int LocalId, uint FarGuid, int FarDoorId);
