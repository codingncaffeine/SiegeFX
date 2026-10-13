using System.Globalization;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeFX.OriginalSaves;

/// <summary>A placement in a map: which region and objects file holds it,
/// and its <c>[t:template,n:scid]</c> header.</summary>
public sealed record MapPlacement(string Region, string File, string Template, uint Scid);

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
        uint? scid = TryParseScid(query);
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
                var id = TryParseScid(n);
                if (id is null) continue;
                bool hit = scid is not null ? id == scid : tpl.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (hit) matches.Add(new MapPlacement(region, path[(o + "/objects/".Length)..], tpl, id.Value));
            }
        }
        return new SearchResult(matches, files, regions.Count, errors);
    }

    static uint? TryParseScid(string s)
    {
        s = s.Trim();
        if (!s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return null;
        return uint.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
