using System.Diagnostics.CodeAnalysis;
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

    public bool TryGetNode(uint guid, [MaybeNullWhen(false)] out NodeInstance node) =>
        _byGuid.TryGetValue(guid, out node);

    private RegionGraph(uint targetNodeGuid, IReadOnlyList<NodeInstance> nodes)
    {
        TargetNodeGuid = targetNodeGuid;
        Nodes = nodes;
        _byGuid = new Dictionary<uint, NodeInstance>(nodes.Count);
        foreach (var n in nodes)
        {
            // Guid collisions within a region would make Phase 6b's door-graph walk
            // propagate the wrong transform. Fuzz shows this never happens in shipped
            // DS1 data, so treat it as a hard parse error if it ever does.
            if (!_byGuid.TryAdd(n.Guid, n))
            {
                var prior = _byGuid[n.Guid];
                throw new InvalidDataException(
                    $"nodes.gas: duplicate snode guid 0x{n.Guid:X8} " +
                    $"(prior mesh=0x{prior.MeshGuid:X8}, new mesh=0x{n.MeshGuid:X8})");
            }
        }
    }

    public static RegionGraph Load(byte[] nodesGasBytes) =>
        FromDocument(GasDocument.Load(nodesGasBytes));

    /// <summary>Phase 21a-2 — concatenate several regions' node lists into one
    /// graph. Used to feed nav-mesh and actor-spawner code that wants a single
    /// graph object spanning the player region plus its preloaded neighbors.
    /// <see cref="TargetNodeGuid"/> is taken from the first graph (the player
    /// region) since downstream consumers only use it as a fallback anchor.
    /// Throws if two regions share an snode guid — DS1 ships clean on this
    /// (verified by <see cref="WorldLayout.Build"/>) so a duplicate is a parser
    /// or asset-tank problem, not a routine condition.</summary>
    public static RegionGraph Combine(IReadOnlyList<RegionGraph> graphs)
    {
        if (graphs.Count == 0) throw new ArgumentException("Combine requires at least one graph", nameof(graphs));
        if (graphs.Count == 1) return graphs[0];
        var total = 0;
        foreach (var g in graphs) total += g.Nodes.Count;
        var combined = new List<NodeInstance>(total);
        foreach (var g in graphs) combined.AddRange(g.Nodes);
        return new RegionGraph(graphs[0].TargetNodeGuid, combined);
    }

    /// <summary>SC-NAV-CROSS-SNO-STITCH — combine like <see cref="Combine"/>,
    /// PLUS inject cross-region door links reconstructed from each region's
    /// <c>editor/stitch_helper.gas</c>. DS1 stores intra-region door pairs
    /// in <c>nodes.gas</c> (already on <see cref="NodeInstance.Doors"/>)
    /// and cross-region door pairs in <c>stitch_helper.gas</c> (only used
    /// for region placement until now). The nav-stitching pass in
    /// <see cref="NavMesh"/> consumes <c>NodeInstance.Doors</c> uniformly,
    /// so we append the cross-region pairs onto the relevant snodes here
    /// and the same pass wires both seam types.
    ///
    /// Matching is by <c>stitchPairId</c>: each region's stitch file lists
    /// "on my side, snode S door D corresponds to pair P toward neighbor R."
    /// The neighbor's matching entry with the same P gives the far side
    /// (snode S', door D'). We append <c>DoorLink(D, S', D')</c> onto S's
    /// node and the reverse onto S'. The original <see cref="NodeInstance"/>
    /// records are immutable so we recreate any node whose Doors list grows
    /// — that cost is paid once at world-load time, never per-frame.</summary>
    public static RegionGraph CombineWithCrossRegionDoors(
        IReadOnlyList<(string Path, RegionGraph Graph, RegionStitchHelper? Stitches)> entries)
    {
        if (entries.Count == 0) throw new ArgumentException("Combine requires at least one entry", nameof(entries));
        if (entries.Count == 1) return entries[0].Graph;

        // Index by region name (the leaf of the path). Stitch entries use
        // short names like "fh_r1", "hc_r1" to refer to each other.
        var byName = new Dictionary<string, (RegionGraph Graph, RegionStitchHelper? Stitches)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, graph, stitches) in entries)
        {
            var name = path;
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name[(slash + 1)..];
            byName[name] = (graph, stitches);
        }

        // Build the per-snode extra-door list by walking every pair (regionA -> regionB).
        // Each stitchAB with PairId P pairs with the stitchBA in regionB.ByDestination[regionA] that also has PairId P.
        var extraDoorsBySnode = new Dictionary<uint, List<DoorLink>>();
        foreach (var (nameA, (_, stitchesA)) in byName)
        {
            if (stitchesA is null) continue;
            foreach (var (nameB, stitchesABforB) in stitchesA.ByDestination)
            {
                if (!byName.TryGetValue(nameB, out var entryB)) continue;
                if (entryB.Stitches is null) continue;
                if (!entryB.Stitches.ByDestination.TryGetValue(nameA, out var stitchesBAforA)) continue;
                // Index B-side stitches by PairId for O(1) match lookup.
                var byPair = new Dictionary<uint, RegionStitchHelper.Stitch>(stitchesBAforA.Count);
                foreach (var s in stitchesBAforA) byPair[s.PairId] = s;
                foreach (var sA in stitchesABforB)
                {
                    if (!byPair.TryGetValue(sA.PairId, out var sB)) continue;
                    // Inject only the A-side here; the matching iteration when
                    // the loop visits regionB will inject the B-side. Avoids
                    // double-counting the same edge.
                    var link = new DoorLink(sA.DoorId, sB.SnodeGuid, sB.DoorId);
                    if (!extraDoorsBySnode.TryGetValue(sA.SnodeGuid, out var list))
                    {
                        list = new List<DoorLink>();
                        extraDoorsBySnode[sA.SnodeGuid] = list;
                    }
                    list.Add(link);
                }
            }
        }

        // Assemble the combined node list, swapping in extended-door versions
        // for snodes that picked up cross-region doors.
        var total = 0;
        foreach (var e in entries) total += e.Graph.Nodes.Count;
        var combined = new List<NodeInstance>(total);
        foreach (var (_, graph, _) in entries)
        {
            foreach (var node in graph.Nodes)
            {
                if (!extraDoorsBySnode.TryGetValue(node.Guid, out var extra))
                {
                    combined.Add(node);
                    continue;
                }
                var mergedDoors = new List<DoorLink>(node.Doors.Count + extra.Count);
                mergedDoors.AddRange(node.Doors);
                mergedDoors.AddRange(extra);
                combined.Add(new NodeInstance
                {
                    Guid = node.Guid,
                    MeshGuid = node.MeshGuid,
                    TexsetAbbr = node.TexsetAbbr,
                    BoundsCamera = node.BoundsCamera,
                    CameraFade = node.CameraFade,
                    OccludesCamera = node.OccludesCamera,
                    OccludesLight = node.OccludesLight,
                    Doors = mergedDoors,
                });
            }
        }
        int totalExtraLinks = 0;
        foreach (var kv in extraDoorsBySnode) totalExtraLinks += kv.Value.Count;
        if (totalExtraLinks > 0)
            System.Console.WriteLine($"  cross-region doors: {totalExtraLinks} link(s) injected across {extraDoorsBySnode.Count} snode(s)");
        return new RegionGraph(entries[0].Graph.TargetNodeGuid, combined);
    }

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
        bool boundsCamera = false, cameraFade = false, occludesCamera = false, occludesLight = false;
        foreach (var a in snode.Attributes)
        {
            if (a.Name.Equals("guid", StringComparison.OrdinalIgnoreCase))
                guid = ParseHexU32(a.Value, "guid");
            else if (a.Name.Equals("mesh_guid", StringComparison.OrdinalIgnoreCase))
                meshGuid = ParseHexU32(a.Value, "mesh_guid");
            else if (a.Name.Equals("texsetabbr", StringComparison.OrdinalIgnoreCase))
                texsetAbbr = a.Value;
            // SC-CAMERA-FADE — per-snode camera-related bools. DS1's
            // ReaderWriterSiegeNodeList.cpp:166 reads these directly off
            // nodes.gas and stages them as user-values on each xform; the
            // runtime then drives:
            //   bounds_camera   = camera collision blocker (already implicit
            //                     in our prop-footprint pass for now).
            //   camera_fade     = fade-when-occluding-player — the basement
            //                     reveal mechanic.
            //   occludes_camera = blocks camera-raycast tests.
            //   occludes_light  = blocks dynamic light raycast tests.
            // We parse all four but currently only consume camera_fade.
            else if (a.Name.Equals("bounds_camera", StringComparison.OrdinalIgnoreCase))
                boundsCamera = ParseBool(a.Value);
            else if (a.Name.Equals("camera_fade", StringComparison.OrdinalIgnoreCase))
                cameraFade = ParseBool(a.Value);
            else if (a.Name.Equals("occludes_camera", StringComparison.OrdinalIgnoreCase))
                occludesCamera = ParseBool(a.Value);
            else if (a.Name.Equals("occludes_light", StringComparison.OrdinalIgnoreCase))
                occludesLight = ParseBool(a.Value);
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
            BoundsCamera = boundsCamera,
            CameraFade = cameraFade,
            OccludesCamera = occludesCamera,
            OccludesLight = occludesLight,
            Doors = doors,
        };
    }

    private static bool ParseBool(string v)
    {
        var s = v.Trim();
        return s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               s.Equals("1", StringComparison.Ordinal) ||
               s.Equals("yes", StringComparison.OrdinalIgnoreCase);
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
        /// <summary>SC-CAMERA-FADE — camera-related per-snode flags from
        /// nodes.gas. Default false (gas omits the line on most snodes).</summary>
        public bool BoundsCamera { get; init; }
        public bool CameraFade { get; init; }
        public bool OccludesCamera { get; init; }
        public bool OccludesLight { get; init; }
        public required IReadOnlyList<DoorLink> Doors { get; init; }
    }

    /// <summary>A single door edge: this node's local door <paramref name="LocalId"/>
    /// connects to <paramref name="FarDoorId"/> on the node identified by
    /// <paramref name="FarGuid"/>. Reciprocal — the neighbor carries the mirror edge.</summary>
    public readonly record struct DoorLink(int LocalId, uint FarGuid, int FarDoorId);
}
