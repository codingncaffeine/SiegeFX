using System.Globalization;
using System.Numerics;
using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>One authored spawn point. <see cref="NodeGuid"/> is the terrain
/// node the position is local to; combined with a <see cref="RegionLayout"/>
/// it resolves to a concrete world position. <see cref="LocalPosition"/> is
/// the in-node offset, <see cref="Orientation"/> is the camera/azimuth-derived
/// facing (DS1 stores camera orbit, not actor yaw, so this is approximate).</summary>
public sealed class StartPosition
{
    public int Id { get; init; }
    public uint NodeGuid { get; init; }
    public Vector3 LocalPosition { get; init; }
}

/// <summary>One named start group from <c>info/start_positions.gas</c> —
/// "farmhouse", "stonebridge", etc. The default group's id-1 position is
/// what the original engine drops a fresh PC onto when you click "New Game".</summary>
public sealed class StartGroup
{
    public string Name { get; init; } = "";
    public string ScreenName { get; init; } = "";
    public bool IsDefault { get; init; }
    public IReadOnlyList<StartPosition> Positions { get; init; } = Array.Empty<StartPosition>();
}

/// <summary>
/// Loads <c>world/maps/&lt;map&gt;/info/start_positions.gas</c> — the file
/// DS1 walks when starting a new game to decide where the party drops in.
/// We only need the default group's first slot for Phase 20a, but parsing
/// the whole tree leaves the door open for the chapter-select UI in Phase 21d.
/// </summary>
public static class StartPositionsStore
{
    public static (IReadOnlyList<StartGroup> Groups, IReadOnlyList<string> Diagnostics) Load(
        TankReader tank, string mapInfoPath)
    {
        var diags = new List<string>();
        var path = mapInfoPath.TrimEnd('/') + "/start_positions.gas";

        if (!tank.TryGetFile(path, out _))
        {
            diags.Add($"{path}: not present");
            return (Array.Empty<StartGroup>(), diags);
        }

        byte[] bytes;
        try { bytes = tank.ExtractToMemory(path); }
        catch (Exception ex) { diags.Add($"{path}: extract failed: {ex.Message}"); return (Array.Empty<StartGroup>(), diags); }

        GasDocument doc;
        try { doc = GasDocument.Load(bytes); }
        catch (Exception ex) { diags.Add($"{path}: parse failed: {ex.Message}"); return (Array.Empty<StartGroup>(), diags); }

        var groups = new List<StartGroup>();
        // The file root is `[start_positions]` containing `[t:start_group,n:NAME]`
        // entries. Drill in one level then enumerate the start-group children.
        foreach (var root in doc.Roots)
        {
            foreach (var sg in root.Children)
            {
                if (!sg.Header.Contains("start_group", StringComparison.OrdinalIgnoreCase)) continue;

                var name       = ExtractName(sg.Header);
                var screenName = "";
                bool isDefault = false;
                foreach (var attr in sg.Attributes)
                {
                    if (NameEq(attr.Name, "screen_name")) screenName = StripQuotes(attr.Value);
                    else if (NameEq(attr.Name, "default")) isDefault = ParseBool(attr.Value);
                }

                var positions = new List<StartPosition>();
                foreach (var sp in sg.Children)
                {
                    if (!sp.Header.Equals("start_position", StringComparison.OrdinalIgnoreCase)) continue;

                    int id = 0;
                    Vector3 local = default;
                    uint nodeGuid = 0u;
                    foreach (var attr in sp.Attributes)
                    {
                        if (NameEq(attr.Name, "id"))
                        {
                            int.TryParse(attr.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
                        }
                        else if (NameEq(attr.Name, "position"))
                        {
                            TryParsePosition(attr.Value, out local, out nodeGuid);
                        }
                    }
                    if (nodeGuid == 0u) continue; // useless without a node anchor
                    positions.Add(new StartPosition
                    {
                        Id = id,
                        NodeGuid = nodeGuid,
                        LocalPosition = local,
                    });
                }

                groups.Add(new StartGroup
                {
                    Name = name,
                    ScreenName = screenName,
                    IsDefault = isDefault,
                    Positions = positions,
                });
            }
        }

        return (groups, diags);
    }

    /// <summary>Convenience: pull the default group's lowest-id slot. Returns
    /// null when start_positions.gas is missing or has no default group with
    /// at least one valid position.</summary>
    public static StartPosition? FindDefault(IReadOnlyList<StartGroup> groups)
    {
        StartGroup? def = null;
        foreach (var g in groups) if (g.IsDefault) { def = g; break; }
        if (def is null || def.Positions.Count == 0) return null;
        StartPosition best = def.Positions[0];
        foreach (var p in def.Positions) if (p.Id < best.Id) best = p;
        return best;
    }

    static string ExtractName(string header)
    {
        // header is "t:start_group,n:farmhouse" — pull the bit after "n:".
        int idx = header.IndexOf("n:", StringComparison.OrdinalIgnoreCase);
        return idx < 0 ? "" : header[(idx + 2)..].Trim();
    }

    static bool TryParsePosition(string s, out Vector3 pos, out uint nodeGuid)
    {
        pos = default;
        nodeGuid = 0;
        var parts = s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) return false;
        if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return false;
        if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return false;
        if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) return false;
        var n = parts[3];
        if (n.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) n = n[2..];
        if (!uint.TryParse(n, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out nodeGuid)) return false;
        pos = new Vector3(x, y, z);
        return true;
    }

    static bool NameEq(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static bool ParseBool(string s)
    {
        var t = s.Trim().ToLowerInvariant();
        return t is "true" or "yes" or "1";
    }

    static string StripQuotes(string s)
    {
        var t = s.Trim();
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"') t = t[1..^1];
        return t;
    }
}
