using System.Globalization;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeFX.OriginalSaves;

/// <summary>A placement in a map: which region and objects file holds it,
/// its <c>[t:template,n:scid]</c> header, and the terrain node its
/// <c>[placement] position</c> is anchored to.</summary>
public sealed record MapPlacement(string Region, string File, string Template, uint Scid, uint? Node = null);

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

    /// <summary>The placements whose SCID is in <paramref name="scids"/>.</summary>
    public static SearchResult WithScids(TankReader map, IReadOnlySet<uint> scids) =>
        Where(map, p => scids.Contains(p.Scid));

    static SearchResult Where(TankReader map, Func<MapPlacement, bool> match)
    {
        var matches = new List<MapPlacement>();
        var errors = new List<string>();
        var regions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int files = 0;
        foreach (var path in map.ListFiles())
        {
            int r = path.IndexOf("/regions/", StringComparison.OrdinalIgnoreCase);
            int o = path.IndexOf("/objects/", StringComparison.OrdinalIgnoreCase);
            if (r < 0 || o < r || !path.EndsWith(".gas", StringComparison.OrdinalIgnoreCase)) continue;
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
                if (match(p)) matches.Add(p);
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
