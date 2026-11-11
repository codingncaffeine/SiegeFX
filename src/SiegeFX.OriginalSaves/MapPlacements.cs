using System.Globalization;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeFX.OriginalSaves;

/// <summary>A placement in a map: which region and objects file holds it,
/// its <c>[t:template,n:scid]</c> header, and the terrain node its
/// <c>[placement] position</c> is anchored to.</summary>
public sealed record MapPlacement(string Region, string File, string Template, uint Scid, uint? Node = null)
{
    /// <summary>The folder between <c>objects/</c> and the file, when there
    /// is one. LoA's map places each object once per difficulty world, under
    /// a folder per world, all with the same SCID; DS1's map has none.</summary>
    public string? World => File.IndexOf('/') is int i and > 0 ? File[..i] : null;
}

/// <summary>Finds placements anywhere in a map tank (every
/// <c>regions/*/objects/*.gas</c>), to match objects in an original save
/// (<c>m_scid</c>, <c>m_datatemplate</c>) to the map they were placed from.</summary>
public static class MapPlacements
{
    public sealed record SearchResult(IReadOnlyList<MapPlacement> Matches, int FilesScanned, int RegionsScanned,
                                      IReadOnlyList<string> Errors);

    /// <summary>Matches a SCID when <paramref name="query"/> is hex
    /// (<c>0x01c00b13</c>), otherwise a template name containing it.</summary>
    public static SearchResult Find(TankReader map, string query)
    {
        uint? scid = TryParseHex(query);
        return Where(map, p => scid is not null ? p.Scid == scid : p.Template.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The placements of map <paramref name="mapName"/> (the
    /// <c>map_name</c> in a save's info.gas) whose SCID is in
    /// <paramref name="scids"/>. SCIDs are only unique within one map: DS1's
    /// <c>map_world</c> and LoA's <c>map_expansion</c> reuse the same numbers.</summary>
    public static SearchResult WithScids(TankReader map, string mapName, IReadOnlySet<uint> scids) =>
        Where(map, p => scids.Contains(p.Scid), mapName);

    /// <summary>The placements of map <paramref name="mapName"/> anchored to
    /// one of <paramref name="nodes"/> whose template contains
    /// <paramref name="templateFilter"/> (every template when null).</summary>
    public static SearchResult OnNodes(TankReader map, string mapName, IReadOnlySet<uint> nodes, string? templateFilter) =>
        Where(map, p => p.Node is uint n && nodes.Contains(n)
                        && (templateFilter is null || p.Template.Contains(templateFilter, StringComparison.OrdinalIgnoreCase)),
              mapName);

    /// <summary>The instance blocks (the whole <c>[t:…,n:…]</c> node) of
    /// map <paramref name="mapName"/>'s placements with these SCIDs.</summary>
    public static IReadOnlyDictionary<uint, GasNode> Blocks(TankReader map, string mapName, IReadOnlySet<uint> scids)
    {
        var blocks = new Dictionary<uint, GasNode>();
        Where(map, p => scids.Contains(p.Scid), mapName, blocks);
        return blocks;
    }

    static SearchResult Where(TankReader map, Func<MapPlacement, bool> match, string? mapName = null,
                              Dictionary<uint, GasNode>? blocks = null)
    {
        var mapDir = mapName is null ? null : $"/maps/{mapName}/";
        var matches = new List<MapPlacement>();
        var errors = new List<string>();
        var regions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int files = 0;
        foreach (var path in map.ListFiles())
        {
            int r = path.IndexOf("/regions/", StringComparison.OrdinalIgnoreCase);
            int o = path.IndexOf("/objects/", StringComparison.OrdinalIgnoreCase);
            if (r < 0 || o < r || !path.EndsWith(".gas", StringComparison.OrdinalIgnoreCase)) continue;
            if (mapDir is not null && !path.Contains(mapDir, StringComparison.OrdinalIgnoreCase)) continue;
            var region = path[(r + "/regions/".Length)..o];
            regions.Add(region);
            files++;
            GasDocument doc;
            try { doc = GasDocument.Load(map.ExtractToMemory(path)); }
            catch (Exception ex) when (ex is InvalidDataException or TankException) { errors.Add($"{path}: {ex.Message}"); continue; }
            foreach (var node in doc.Roots)
            {
                if (!TemplateStore.TryParseHeader(node.Header, out var tpl, out var n)) continue;
                if (TryParseHex(n) is not uint id) continue;
                var p = new MapPlacement(region, path[(o + "/objects/".Length)..], tpl, id, PlacementNode(node));
                if (!match(p)) continue;
                matches.Add(p);
                blocks?.TryAdd(p.Scid, node);
            }
        }
        return new SearchResult(matches, files, regions.Count, errors);
    }

    /// <summary>The node GUID, the fourth field of <c>position = x,y,z,0xGUID</c>.</summary>
    static uint? PlacementNode(GasNode node)
    {
        var position = Attribute(Section(node, "placement"), "position");
        if (position is null) return null;
        var parts = position.Split(',');
        return parts.Length == 4 ? TryParseHex(parts[3]) : null;
    }

    static GasNode? Section(GasNode node, string header) =>
        node.Children.FirstOrDefault(c => string.Equals(c.Header, header, StringComparison.OrdinalIgnoreCase));

    static string? Attribute(GasNode? node, string name)
    {
        foreach (var a in node?.Attributes ?? [])
            if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a.Value;
        return null;
    }

    static uint? TryParseHex(string s)
    {
        s = s.Trim();
        if (!s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return null;
        return uint.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
