using System.Globalization;

namespace SiegeFX.Core.Assets;

/// <summary>
/// The logical connectivity of one Dungeon Siege region, reconstructed from the
/// <c>terrain_nodes/nodes.gas</c> file inside a map tank. Each region is a graph:
/// nodes (SNO instances) are vertices, doors (with reciprocal <c>farguid</c> +
/// <c>fardoor</c> pointers) are edges.
///
/// No world-space transforms are stored on disk for DS1 regions — they're derived
/// at load time by walking the door graph from <see cref="TargetNodeGuid"/>, per
/// Scott Bilas's Continuous World design. That walk is Phase 6b; this phase just
/// provides the graph.
/// </summary>
public sealed class RegionGraph
{
    /// <summary>The "anchor" node whose local frame defines this region's origin.
    /// May reference a node in a neighbor region (cross-region stitch).</summary>
    public uint TargetNodeGuid { get; }

    public IReadOnlyList<NodeInstance> Nodes { get; }

    private readonly Dictionary<uint, NodeInstance> _byGuid;

    public bool TryGetNode(uint guid, out NodeInstance? node) =>
        (node = _byGuid.TryGetValue(guid, out var n) ? n : null) != null;

    private RegionGraph(uint targetNodeGuid, IReadOnlyList<NodeInstance> nodes)
    {
        TargetNodeGuid = targetNodeGuid;
        Nodes = nodes;
        _byGuid = new Dictionary<uint, NodeInstance>(nodes.Count);
        foreach (var n in nodes) _byGuid[n.Guid] = n;
    }

    public static RegionGraph Load(byte[] nodesGasBytes) =>
        FromDocument(GasDocument.Load(nodesGasBytes));

    public static RegionGraph FromDocument(GasDocument doc)
    {
        GasNode? root = null;
        foreach (var r in doc.Roots)
        {
            if (r.Header.StartsWith("t:terrain_nodes", StringComparison.OrdinalIgnoreCase))
            {
                root = r;
                break;
            }
        }
        if (root is null)
            throw new InvalidDataException("nodes.gas: missing [t:terrain_nodes] root block");

        uint target = 0;
        foreach (var a in root.Attributes)
            if (a.Name.Equals("targetnode", StringComparison.OrdinalIgnoreCase))
                target = ParseHexU32(a.Value, "targetnode");

        var nodes = new List<NodeInstance>(root.Children.Count);
        foreach (var child in root.Children)
        {
            if (!child.Header.StartsWith("t:snode", StringComparison.OrdinalIgnoreCase))
                continue;

            var snode = ParseSnode(child);
            nodes.Add(snode);
        }

        return new RegionGraph(target, nodes);
    }

    private static NodeInstance ParseSnode(GasNode snode)
    {
        uint guid = 0, meshGuid = 0;
        var texsetAbbr = "";
        foreach (var a in snode.Attributes)
        {
            if (a.Name.Equals("guid", StringComparison.OrdinalIgnoreCase))
                guid = ParseHexU32(a.Value, "guid");
            else if (a.Name.Equals("mesh_guid", StringComparison.OrdinalIgnoreCase))
                meshGuid = ParseHexU32(a.Value, "mesh_guid");
            else if (a.Name.Equals("texsetabbr", StringComparison.OrdinalIgnoreCase))
                texsetAbbr = a.Value;
        }
        if (guid == 0)
            throw new InvalidDataException($"snode '{snode.Header}' missing guid");

        var doors = new List<DoorLink>(snode.Children.Count);
        foreach (var child in snode.Children)
        {
            if (!child.Header.StartsWith("door", StringComparison.OrdinalIgnoreCase))
                continue;

            int id = 0, farDoor = 0;
            uint farGuid = 0;
            foreach (var a in child.Attributes)
            {
                if (a.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
                    id = int.Parse(a.Value, CultureInfo.InvariantCulture);
                else if (a.Name.Equals("fardoor", StringComparison.OrdinalIgnoreCase))
                    farDoor = int.Parse(a.Value, CultureInfo.InvariantCulture);
                else if (a.Name.Equals("farguid", StringComparison.OrdinalIgnoreCase))
                    farGuid = ParseHexU32(a.Value, "farguid");
            }
            doors.Add(new DoorLink(id, farGuid, farDoor));
        }

        return new NodeInstance
        {
            Guid = guid,
            MeshGuid = meshGuid,
            TexsetAbbr = texsetAbbr,
            Doors = doors,
        };
    }

    private static uint ParseHexU32(string text, string field)
    {
        var v = text.Trim();
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) v = v[2..];
        if (!uint.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u))
            throw new InvalidDataException($"nodes.gas: {field}='{text}' is not a hex u32");
        return u;
    }

    /// <summary>One SNO node instance placed inside a region. Transforms are derived
    /// at region-load time; see <see cref="RegionGraph"/> class remarks.</summary>
    public sealed class NodeInstance
    {
        /// <summary>Unique 32-bit instance id within the map.</summary>
        public required uint Guid { get; init; }
        /// <summary>Key into the mesh GUID→SNO filename index (resolution is Phase 6b).</summary>
        public required uint MeshGuid { get; init; }
        /// <summary>Texture-set abbreviation that selects the tile's terrain texture pack
        /// (e.g. <c>sn02</c>). SNO surface texture names are typically prefixed with this.</summary>
        public required string TexsetAbbr { get; init; }
        public required IReadOnlyList<DoorLink> Doors { get; init; }
    }

    /// <summary>A single door edge: this node's local door <paramref name="LocalId"/>
    /// connects to <paramref name="FarDoorId"/> on the node identified by
    /// <paramref name="FarGuid"/>. Reciprocal — the neighbor carries the mirror edge.</summary>
    public readonly record struct DoorLink(int LocalId, uint FarGuid, int FarDoorId);
}
