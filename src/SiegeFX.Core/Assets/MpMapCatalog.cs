using System.Globalization;
using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>One of a map's worlds (main.gas [worlds]): Regular, Veteran, Elite.</summary>
public sealed record MpWorld(string Name, string ScreenName, string Description, int RequiredLevel);

/// <summary>A start location players may choose (start_positions.gas, not
/// dev_only): its group name, screen name, the level each world requires and
/// the region its first position lies in ("" when not found).</summary>
public sealed record MpStartLocation(string Group, string ScreenName, string Description, bool IsDefault,
                                     IReadOnlyDictionary<string, int> RequiredLevels, string Region)
{
    public int RequiredLevel(string world) => RequiredLevels.TryGetValue(world, out var lv) ? lv : 0;
}

/// <summary>What the staging area's Map Settings and Start Location column
/// offer for one map: its screen name and description, its worlds and its
/// start locations, read from the map's own files.</summary>
public sealed record MpMapInfo(string Name, string ScreenName, string Description,
                               IReadOnlyList<MpWorld> Worlds, IReadOnlyList<MpStartLocation> Starts);

public static class MpMapCatalog
{
    /// <summary>Reads world/maps/NAME/main.gas and info/start_positions.gas, and
    /// finds each start location's region from the regions' node lists.</summary>
    public static MpMapInfo Load(TankReader mapTank, string mapName)
    {
        string root = $"/world/maps/{mapName}";
        string screen = mapName, description = "";
        var worlds = new List<MpWorld>();
        if (mapTank.TryGetFile($"{root}/main.gas", out _))
        {
            var doc = GasDocument.Load(mapTank.ExtractToMemory($"{root}/main.gas"));
            foreach (var map in doc.Roots)
            {
                foreach (var a in map.Attributes)
                {
                    if (Is(a.Name, "screen_name")) screen = Unquote(a.Value);
                    else if (Is(a.Name, "description")) description = Unquote(a.Value).Replace("\\n", "\n");
                }
                foreach (var block in map.Children)
                {
                    if (!block.Header.Equals("worlds", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var w in block.Children)
                    {
                        string ws = w.Header.Trim(), wd = "";
                        int lv = 0;
                        foreach (var a in w.Attributes)
                        {
                            if (Is(a.Name, "screen_name")) ws = Unquote(a.Value);
                            else if (Is(a.Name, "description")) wd = Unquote(a.Value);
                            else if (Is(a.Name, "required_level"))
                                int.TryParse(a.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out lv);
                        }
                        worlds.Add(new MpWorld(w.Header.Trim(), ws, wd, lv));
                    }
                }
            }
        }
        if (worlds.Count == 0) worlds.Add(new MpWorld("normal", "Regular", "", 0));

        var (groups, _) = StartPositionsStore.Load(mapTank, $"{root}/info");
        var nodeRegion = NodeRegions(mapTank, root, groups.Where(g => !g.DevOnly && g.Positions.Count > 0)
                                                          .Select(g => g.Positions.OrderBy(p => p.Id).First().NodeGuid));
        var starts = groups
            .Where(g => !g.DevOnly && g.Positions.Count > 0)
            .Select(g => new MpStartLocation(g.Name, g.ScreenName.Length > 0 ? g.ScreenName : g.Name, g.Description, g.IsDefault,
                g.RequiredLevels, nodeRegion.TryGetValue(g.Positions.OrderBy(p => p.Id).First().NodeGuid, out var r) ? r : ""))
            .ToList();
        return new MpMapInfo(mapName, screen, description, worlds, starts);
    }

    /// <summary>The region holding each wanted node (by the regions' node lists).</summary>
    static Dictionary<uint, string> NodeRegions(TankReader tank, string root, IEnumerable<uint> wanted)
    {
        var want = new HashSet<uint>(wanted);
        var found = new Dictionary<uint, string>();
        if (want.Count == 0) return found;
        string prefix = $"{root}/regions/";
        foreach (var path in tank.ListFiles())
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !path.EndsWith("/terrain_nodes/nodes.gas", StringComparison.OrdinalIgnoreCase)) continue;
            string region = path[prefix.Length..].Split('/')[0];
            RegionGraph graph;
            try { graph = RegionGraph.Load(tank.ExtractToMemory(path)); }
            catch (Exception) { continue; }
            foreach (var g in want)
                if (!found.ContainsKey(g) && graph.TryGetNode(g, out _)) found[g] = region;
            if (found.Count == want.Count) break;
        }
        return found;
    }

    static bool Is(string attr, string name)
    {
        var n = attr.Trim();
        int sp = n.LastIndexOf(' ');
        if (sp >= 0) n = n[(sp + 1)..];
        return n.Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    static string Unquote(string v) => v.Trim().Trim(';').Trim().Trim('"');
}
