using System.Collections.Generic;
using System.Text;

namespace SiegeSmith.Services;

/// <summary>A nav-flag override for one SNO logical grouping on a placed snode. The engine gates
/// pathing only on <c>lf_human_player</c> / <c>lf_computer_player</c>; the other <c>lf_*</c> tags are
/// inert surface labels (footstep audio). Walkable geometry itself is baked in the SNO art — this file
/// only toggles passability of an already-walkable grouping.</summary>
public sealed class LogicalFlag
{
    public uint SnodeGuid;
    public int Lnode;                 // SNO LogicalGrouping.Id (0 = the node's default grouping)
    public bool HumanPlayer = true;   // gated by the engine
    public bool ComputerPlayer = true;// gated by the engine
    public bool Water;                // impassable to stock actors (cost = +Infinity) — flag it
    public string SurfaceTag = "";    // inert, e.g. lf_dirt / lf_grass / lf_stone

    public string Label => $"snode 0x{SnodeGuid:X8} · lnode {Lnode}";
    public string Detail
    {
        get
        {
            var parts = new List<string>();
            if (HumanPlayer) parts.Add("human");
            if (ComputerPlayer) parts.Add("computer");
            if (!HumanPlayer && !ComputerPlayer) parts.Add("BLOCKED");
            if (Water) parts.Add("water");
            if (!string.IsNullOrWhiteSpace(SurfaceTag)) parts.Add(SurfaceTag.Trim());
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>Writes <c>&lt;region&gt;/editor/logical_flags.gas</c> — the ONLY authorable nav-FLAG file.
/// Path matters: an <c>editor/</c> sibling of terrain_nodes, NOT under terrain_nodes/, or gating never
/// fires. Flags nest snode → lnode → the <c>* = lf_*</c> flag list.</summary>
public static class LogicalFlagsWriter
{
    public static string Write(IReadOnlyList<LogicalFlag> flags)
    {
        var sb = new StringBuilder();
        sb.Append("[logical_flags]\r\n{\r\n");
        // Group by snode, then by lnode, preserving insertion order.
        var snodeOrder = new List<uint>();
        var bySnode = new Dictionary<uint, List<LogicalFlag>>();
        foreach (var f in flags)
        {
            if (!bySnode.TryGetValue(f.SnodeGuid, out var list)) { bySnode[f.SnodeGuid] = list = new(); snodeOrder.Add(f.SnodeGuid); }
            list.Add(f);
        }
        foreach (var snode in snodeOrder)
        {
            sb.Append($"\t[t:lf_snode,n:0x{snode:X8}]\r\n\t{{\r\n");
            foreach (var f in bySnode[snode])
            {
                sb.Append($"\t\t[t:lf_lnode,n:{f.Lnode}]\r\n\t\t{{\r\n");
                if (f.HumanPlayer) sb.Append("\t\t\t* = lf_human_player;\r\n");
                if (f.ComputerPlayer) sb.Append("\t\t\t* = lf_computer_player;\r\n");
                if (f.Water) sb.Append("\t\t\t* = lf_water;\r\n");
                if (!string.IsNullOrWhiteSpace(f.SurfaceTag)) sb.Append($"\t\t\t* = {f.SurfaceTag.Trim()};\r\n");
                sb.Append("\t\t}\r\n");
            }
            sb.Append("\t}\r\n");
        }
        sb.Append("}\r\n");
        return sb.ToString();
    }
}
