using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace SiegeSmith.Services;

/// <summary>One side of a cross-region adjacency. The engine matches an edge only when the neighbour
/// region has a reciprocal stitch with the SAME <see cref="PairId"/> giving its own local snode+door,
/// so these are always created in mirrored pairs. <see cref="DestRegion"/> is the neighbour's
/// directory-leaf name.</summary>
public sealed class RegionStitch
{
    public uint PairId;
    public uint LocalSnode;
    public int LocalDoor;
    public string DestRegion = "";

    public string Label => $"→ {DestRegion}  (pair 0x{PairId:X8})";
    public string Detail => $"snode 0x{LocalSnode:X8} · door {LocalDoor}";
}

/// <summary>A free (unconnected) boundary door on a region — a candidate stitch endpoint.</summary>
public sealed record StitchDoor(uint Snode, int Door, string Mesh)
{
    /// <summary>False when the region's own nav mesh has no walkable route from the region
    /// start to this door's node — DS1 ships plenty of decorative terrain (backdrop cliffs,
    /// sealed caps) whose doors would stitch a connection no player can ever reach. Static
    /// estimate: elevator/lever/teleporter transport isn't simulated, so off-path doors stay
    /// pickable — just flagged and sorted last.</summary>
    public bool Reachable { get; init; } = true;

    public string Label => Reachable ? $"snode 0x{Snode:X8} · door {Door}" : $"⚠ snode 0x{Snode:X8} · door {Door} · off-path";
    public string Detail => Mesh;
    public string Tip => Reachable
        ? Mesh
        : Mesh + "\nOff-path: the nav mesh has no walkable route from the region start to this door "
              + "(static check — elevators/levers/teleporters aren't simulated). A stitch here lands "
              + "in an area players may never reach on foot.";
}

/// <summary>A region participating in the world graph — the primary (the region being edited) or a
/// sibling imported from its <c>nodes.gas</c>. Holds its world-unique region guid, its snode guids
/// (for collision checks) and its free boundary doors (stitch endpoints).</summary>
public sealed class StitchRegionRef
{
    public string LeafName = "";
    public uint SourceGuid;
    public bool IsPrimary;
    public string NodesGas = "";
    public List<uint> SnodeGuids = new();
    public List<StitchDoor> FreeDoors = new();
    public ObservableCollection<RegionStitch> Stitches = new();

    /// <summary>Snodes walkable from THIS region's own start, per its nav mesh — computed at
    /// import so validation can prove BOTH ends of a stitch are player-reachable, not just
    /// the primary's. Null = nav unavailable (treat every door as reachable).</summary>
    public HashSet<uint>? ReachableSnodes;

    public string Label => IsPrimary ? $"{LeafName}  (primary)" : LeafName;
    public string Detail => $"guid 0x{SourceGuid:X8} · {SnodeGuids.Count} snode(s) · {FreeDoors.Count} free door(s) · {Stitches.Count} stitch(es)";
}

/// <summary>Writes a region's <c>editor/stitch_helper.gas</c> — the ONLY inter-region adjacency source
/// the engine reads. The root header must be exactly <c>[stitch_helper_data]</c> or the load throws.
/// Stitches are grouped into one <c>[t:stitch_editor,n:&lt;dest&gt;]</c> per neighbour region.</summary>
public static class StitchHelperWriter
{
    public static string Write(uint sourceGuid, string sourceName, IReadOnlyList<RegionStitch> stitches)
    {
        var sb = new StringBuilder();
        sb.Append("[stitch_helper_data]\r\n{\r\n");
        sb.Append($"\tsource_region_guid = 0x{sourceGuid:X8};\r\n");
        sb.Append($"\tsource_region_name = {sourceName};\r\n");

        // One stitch_editor block per distinct dest region, preserving insertion order.
        var order = new List<string>();
        var byDest = new Dictionary<string, List<RegionStitch>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var s in stitches)
        {
            if (string.IsNullOrWhiteSpace(s.DestRegion)) continue;
            if (!byDest.TryGetValue(s.DestRegion, out var list)) { byDest[s.DestRegion] = list = new(); order.Add(s.DestRegion); }
            list.Add(s);
        }
        foreach (var dest in order)
        {
            sb.Append($"\t[t:stitch_editor,n:{dest}]\r\n\t{{\r\n");
            sb.Append($"\t\tdest_region = {dest};\r\n");
            sb.Append("\t\t[node_ids]\r\n\t\t{\r\n");
            foreach (var s in byDest[dest])
                sb.Append($"\t\t\t0x{s.PairId:X8} = 0x{s.LocalSnode:X8},{s.LocalDoor};\r\n");
            sb.Append("\t\t}\r\n\t}\r\n");
        }
        sb.Append("}\r\n");
        return sb.ToString();
    }
}
