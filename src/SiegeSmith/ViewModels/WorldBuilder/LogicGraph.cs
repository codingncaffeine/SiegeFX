using System;
using System.Collections.Generic;
using System.Linq;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels.WorldBuilder;

/// <summary>ED-10 — one box in a logic graph view. Graph views are read-only
/// VISUALIZATIONS over the real gas models (the trigger matrix, conversation
/// lines, and quest list stay the source of truth); clicking a box selects
/// the underlying piece in the Inspector.</summary>
public sealed class GraphNode
{
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; init; } = 196;
    public double H { get; init; } = 46;
    public System.Windows.Media.Brush Stroke { get; init; } = Frozen("#8A8F98");
    /// <summary>What clicking this box selects (a trigger, command, placed
    /// object, conversation, or quest model). Null = informational only.</summary>
    public object? Target { get; init; }

    internal static System.Windows.Media.Brush Frozen(string hex)
    {
        var b = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

/// <summary>ED-10 — a labeled arrow between two graph boxes.</summary>
public sealed class GraphEdge
{
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
    public string Label { get; init; } = "";
    public double LabelX => (X1 + X2) / 2 - 30;
    public double LabelY => (Y1 + Y2) / 2 - 14;
}

/// <summary>Builds the three ED-10 graph views from the live editor models.
/// Layout is a simple layered flow: roots (nothing points at them) in the
/// left column, each edge pushing its target one column right.</summary>
public static class LogicGraphBuilder
{
    const double ColW = 236, RowH = 62, Pad = 16;

    static readonly System.Windows.Media.Brush TriggerBrush = GraphNode.Frozen("#33C2A6");
    static readonly System.Windows.Media.Brush CommandBrush = GraphNode.Frozen("#4C8DF0");
    static readonly System.Windows.Media.Brush ObjectBrush  = GraphNode.Frozen("#B0A890");
    static readonly System.Windows.Media.Brush ConvBrush    = GraphNode.Frozen("#C77DD8");
    static readonly System.Windows.Media.Brush QuestBrush   = GraphNode.Frozen("#E0709A");
    static readonly System.Windows.Media.Brush MutedBrush   = GraphNode.Frozen("#8A8F98");

    /// <summary>Trigger flow: triggers/commands as boxes, every SCID an action
    /// or command field references as an arrow (unknown SCIDs get a stub box,
    /// so dangling wiring is visible instead of silent).</summary>
    public static (List<GraphNode> Nodes, List<GraphEdge> Edges, double W, double H) BuildTriggerFlow(
        IEnumerable<RegionTrigger> triggers, IEnumerable<CommandPlacement> commands,
        IReadOnlyList<PlacedObject> objects)
    {
        var byScid = new Dictionary<uint, GraphNode>();
        var edgeSpecs = new List<(uint From, uint To, string Label)>();

        foreach (var tg in triggers)
        {
            int conds = 0, acts = 0;
            foreach (var r in tg.Rows) { conds += r.Conditions.Count; acts += r.Actions.Count; }
            byScid[tg.Scid] = new GraphNode
            {
                Label = tg.Template,
                Detail = $"0x{tg.Scid:X8} · {conds} cond → {acts} act",
                Stroke = TriggerBrush,
                Target = tg,
            };
            foreach (var r in tg.Rows)
                foreach (var a in r.Actions)
                    foreach (var scid in ScidsIn(a.Args))
                        edgeSpecs.Add((tg.Scid, scid, a.Verb));
        }
        foreach (var cm in commands)
        {
            byScid[cm.Scid] = new GraphNode
            {
                Label = cm.Template,
                Detail = $"0x{cm.Scid:X8}" + (string.IsNullOrWhiteSpace(cm.Order) ? "" : $" · {cm.Order}"),
                Stroke = CommandBrush,
                Target = cm,
            };
            if (cm.NextScid is { } nx && nx != 0) edgeSpecs.Add((cm.Scid, nx, "next"));
            if (cm.Target1 is { } t1 && t1 != 0) edgeSpecs.Add((cm.Scid, t1, "target"));
            if (cm.Target2 is { } t2 && t2 != 0) edgeSpecs.Add((cm.Scid, t2, "target 2"));
            if (cm.ClientScid is { } cs && cs != 0) edgeSpecs.Add((cm.Scid, cs, "client"));
        }
        // Referenced-but-not-logic SCIDs resolve to placed objects (named) or
        // a dangling stub — both stay clickable/visible.
        foreach (var (_, to, _) in edgeSpecs)
        {
            if (byScid.ContainsKey(to)) continue;
            var obj = objects.FirstOrDefault(o => o.Scid == to);
            byScid[to] = obj is not null
                ? new GraphNode { Label = obj.Template, Detail = $"0x{to:X8} · placed object", Stroke = ObjectBrush, Target = obj }
                : new GraphNode { Label = "(unknown scid)", Detail = $"0x{to:X8} — nothing owns this id", Stroke = MutedBrush };
        }

        LayerLayout(byScid, edgeSpecs);
        return Finish(byScid.Values.ToList(), byScid, edgeSpecs);
    }

    /// <summary>Dialogue: each conversation chains its ordered lines left to
    /// right; activate_quest lines arrow into shared quest boxes parked in a
    /// column right of the longest chain. Nodes are positioned FIRST, then
    /// every edge is derived from final positions.</summary>
    public static (List<GraphNode> Nodes, List<GraphEdge> Edges, double W, double H) BuildDialogue(
        IEnumerable<Conversation> conversations, IEnumerable<MapQuest> quests)
    {
        var convs = conversations.ToList();
        var mapQuests = quests.ToList();
        var nodes = new List<GraphNode>();
        var chains = new List<(Conversation Conv, List<(GraphNode Box, DialogueLine? Line)> Chain)>();
        var questKeys = new List<string>();
        double y = Pad, maxX = 0;

        foreach (var c in convs)
        {
            var chain = new List<(GraphNode, DialogueLine?)>();
            var head = new GraphNode
            {
                Label = c.FullKey,
                Detail = c.BoundActorScid != 0 ? $"actor 0x{c.BoundActorScid:X8}" : "unbound — bind to an NPC",
                Stroke = ConvBrush, Target = c, X = Pad, Y = y,
            };
            nodes.Add(head);
            chain.Add((head, null));
            double x = Pad;
            foreach (var line in c.Nodes.OrderBy(n => n.Order))
            {
                x += ColW;
                var ln = new GraphNode
                {
                    Label = Trunc(line.Label, 30),
                    Detail = (line.QuestDialog ? "quest dialog" : "") + (line.Nis ? " · NIS" : ""),
                    Stroke = ConvBrush, Target = c, X = x, Y = y,
                };
                nodes.Add(ln);
                chain.Add((ln, line));
                if (!string.IsNullOrWhiteSpace(line.ActivateQuest))
                {
                    var qk = line.ActivateQuest.Split(',')[0].Trim();
                    if (!questKeys.Contains(qk, StringComparer.OrdinalIgnoreCase)) questKeys.Add(qk);
                }
                maxX = Math.Max(maxX, x + ln.W);
            }
            maxX = Math.Max(maxX, head.X + head.W);
            chains.Add((c, chain));
            y += RowH + 14;
        }

        // Quest boxes in their own column, bound to the map's quest models
        // when the keys match (clicking one selects the quest).
        var questNodes = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        double qx = maxX + 60, qy = Pad;
        foreach (var qk in questKeys)
        {
            var model = mapQuests.FirstOrDefault(q => q.Key.Equals(qk, StringComparison.OrdinalIgnoreCase));
            var qn = new GraphNode
            {
                Label = qk,
                Detail = model is not null ? "quest (this map)" : "quest (shipped)",
                Stroke = QuestBrush, Target = model, X = qx, Y = qy,
            };
            questNodes[qk] = qn;
            nodes.Add(qn);
            qy += RowH;
        }

        var edges = new List<GraphEdge>();
        foreach (var (_, chain) in chains)
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                edges.Add(Arrow(chain[i].Box, chain[i + 1].Box, ""));
                var line = chain[i + 1].Line;
                if (line is not null && !string.IsNullOrWhiteSpace(line.ActivateQuest)
                    && questNodes.TryGetValue(line.ActivateQuest.Split(',')[0].Trim(), out var qn))
                    edges.Add(Arrow(chain[i + 1].Box, qn, "activates"));
            }

        double w = qx + ColW, h = Math.Max(y, qy) + Pad;
        return (nodes, edges, Math.Max(w, 400), Math.Max(h, 200));
    }

    /// <summary>Quest flow: the chapter at the left, its quests fanning out,
    /// and every dialogue line that activates a quest arrowing in.</summary>
    public static (List<GraphNode> Nodes, List<GraphEdge> Edges, double W, double H) BuildQuestFlow(
        string chapterName, IReadOnlyList<MapQuest> quests, IEnumerable<Conversation> conversations)
    {
        var nodes = new List<GraphNode>();
        var edges = new List<GraphEdge>();
        var chapter = new GraphNode
        {
            Label = string.IsNullOrWhiteSpace(chapterName) ? "(chapter)" : chapterName,
            Detail = $"{quests.Count} quest(s)",
            Stroke = QuestBrush, X = Pad, Y = Pad + Math.Max(0, quests.Count - 1) * RowH / 2,
        };
        nodes.Add(chapter);
        var byKey = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        double y = Pad;
        foreach (var q in quests)
        {
            var qn = new GraphNode
            {
                Label = string.IsNullOrWhiteSpace(q.ScreenName) ? q.Key : q.ScreenName,
                Detail = q.Key, Stroke = QuestBrush, Target = q,
                X = Pad + ColW, Y = y,
            };
            byKey[q.Key] = qn;
            nodes.Add(qn);
            edges.Add(Arrow(chapter, qn, ""));
            y += RowH;
        }
        double cy = Pad;
        foreach (var c in conversations)
        {
            foreach (var line in c.Nodes)
            {
                if (string.IsNullOrWhiteSpace(line.ActivateQuest)) continue;
                var qk = line.ActivateQuest.Split(',')[0].Trim();
                if (!byKey.TryGetValue(qk, out var qn)) continue;
                var cn = new GraphNode
                {
                    Label = c.FullKey, Detail = $"line {line.Order} activates",
                    Stroke = ConvBrush, Target = c,
                    X = Pad + ColW * 2 + 40, Y = cy,
                };
                nodes.Add(cn);
                edges.Add(Arrow(cn, qn, "activates"));
                cy += RowH;
            }
        }
        double w = Pad + ColW * 3 + 80, h = Math.Max(y, cy) + Pad;
        return (nodes, edges, Math.Max(w, 400), Math.Max(h, 200));
    }

    // ── shared plumbing ─────────────────────────────────────────────

    static IEnumerable<uint> ScidsIn(string args)
    {
        foreach (System.Text.RegularExpressions.Match m in
            System.Text.RegularExpressions.Regex.Matches(args ?? "", @"0x([0-9A-Fa-f]{1,8})"))
            if (uint.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) && v != 0)
                yield return v;
    }

    /// <summary>Layered layout: roots at column 0, each edge pushes its target
    /// right of its source (relaxed a few passes; cycles just stop moving).</summary>
    static void LayerLayout(Dictionary<uint, GraphNode> byScid, List<(uint From, uint To, string Label)> edges)
    {
        var layer = byScid.Keys.ToDictionary(k => k, _ => 0);
        for (int pass = 0; pass < 6; pass++)
        {
            bool moved = false;
            foreach (var (from, to, _) in edges)
            {
                if (!layer.ContainsKey(from) || !layer.ContainsKey(to) || from == to) continue;
                if (layer[to] <= layer[from] && layer[from] < 12)
                {
                    layer[to] = layer[from] + 1;
                    moved = true;
                }
            }
            if (!moved) break;
        }
        var perLayer = new Dictionary<int, int>();
        foreach (var (scid, node) in byScid)
        {
            int l = layer[scid];
            perLayer.TryGetValue(l, out var row);
            perLayer[l] = row + 1;
            node.X = Pad + l * ColW;
            node.Y = Pad + row * RowH;
        }
    }

    static (List<GraphNode>, List<GraphEdge>, double, double) Finish(
        List<GraphNode> nodes, Dictionary<uint, GraphNode> byScid,
        List<(uint From, uint To, string Label)> edgeSpecs)
    {
        var edges = new List<GraphEdge>();
        foreach (var (from, to, label) in edgeSpecs)
            if (byScid.TryGetValue(from, out var a) && byScid.TryGetValue(to, out var b))
                edges.Add(Arrow(a, b, label));
        double w = 400, h = 200;
        foreach (var n in nodes) { w = Math.Max(w, n.X + n.W + Pad); h = Math.Max(h, n.Y + n.H + Pad); }
        return (nodes, edges, w, h);
    }

    static GraphEdge Arrow(GraphNode a, GraphNode b, string label) => new()
    {
        X1 = a.X + a.W, Y1 = a.Y + a.H / 2,
        X2 = b.X, Y2 = b.Y + b.H / 2,
        Label = label,
    };

    static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
