using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>One bullet inside an Adventurer's Handbook tip: a line of screen
/// text (may carry <c>&lt;c:0xAARRGGBB&gt;…&lt;/c&gt;</c> color markup — the
/// renderer parses it) and the name of the small icon drawn beside it.</summary>
public readonly record struct WorldTipBullet(string Text, string IconTexture);

/// <summary>One ordered tip page from <c>info/tips.gas</c>. DS1 auto-pops these
/// sequentially (by <see cref="Order"/>) through the early game and lets the
/// player recall the whole set with F12.</summary>
public sealed class WorldTip
{
    public int Order { get; init; }
    public IReadOnlyList<WorldTipBullet> Bullets { get; init; } = System.Array.Empty<WorldTipBullet>();
}

/// <summary>
/// Loads DS1's Adventurer's Handbook tip database — <c>world/maps/&lt;map&gt;/
/// info/tips.gas</c>, section <c>[world_tips]</c>. Only the numbered
/// <c>[t:tip,n:tip_N]</c> entries (those carrying an <c>order</c>) are returned,
/// sorted by order — the contextual <c>[t:filtered_tip]</c> entries (break a
/// barrel, pick up the scroll, defeat) are a separate event-driven set and are
/// skipped here. Text is read from the player's own DS1 install at runtime;
/// nothing is embedded in SiegeFX.
/// </summary>
public static class WorldTips
{
    private const string DefaultIcon = "b_gui_ig_mnu_tip_default";

    /// <summary>Read + parse the tip database from a map tank. Returns an empty
    /// list (never throws) if the file is absent or unparseable — the handbook
    /// then simply never pops.</summary>
    public static IReadOnlyList<WorldTip> Load(TankReader tank, string mapInfoTipsPath)
    {
        try
        {
            if (!tank.TryGetFile(mapInfoTipsPath, out _)) return System.Array.Empty<WorldTip>();
            return Parse(tank.ExtractToMemory(mapInfoTipsPath));
        }
        catch { return System.Array.Empty<WorldTip>(); }
    }

    /// <summary>Derive the <c>info/tips.gas</c> path from a region path such as
    /// <c>/world/maps/map_world/regions/fh_r1</c> → the map root is everything
    /// up to <c>/regions/…</c>. Returns null if the shape doesn't match.</summary>
    public static string? TipsPathForRegion(string regionPath)
    {
        if (string.IsNullOrEmpty(regionPath)) return null;
        int i = regionPath.IndexOf("/regions/", System.StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        return regionPath[..i] + "/info/tips.gas";
    }

    public static IReadOnlyList<WorldTip> Parse(byte[] gasBytes)
    {
        var doc = GasDocument.Load(gasBytes);
        var tips = new List<WorldTip>();
        foreach (var root in doc.Roots)
        {
            if (!string.Equals(root.Header.Trim(), "world_tips", System.StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var node in root.Children)
            {
                // Header is the raw bracket body, e.g. "t:tip,n:tip_1". Take the
                // ordered "tip" type only; "filtered_tip" is event-driven.
                if (!TryParseType(node.Header, out var type) ||
                    !string.Equals(type, "tip", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                int order = 0;
                foreach (var a in node.Attributes)
                    if (string.Equals(a.Name, "order", System.StringComparison.OrdinalIgnoreCase))
                        int.TryParse(a.Value.Trim(), out order);
                if (order <= 0) continue; // ordered tips always carry a positive order

                var bullets = new List<WorldTipBullet>();
                foreach (var child in node.Children)
                {
                    if (!child.Header.StartsWith("text", System.StringComparison.OrdinalIgnoreCase))
                        continue; // skip [actions] and any non-text child
                    string text = "", icon = DefaultIcon;
                    foreach (var a in child.Attributes)
                    {
                        if (string.Equals(a.Name, "screen_name", System.StringComparison.OrdinalIgnoreCase))
                            text = Unquote(a.Value);
                        else if (string.Equals(a.Name, "texture", System.StringComparison.OrdinalIgnoreCase))
                            icon = a.Value.Trim();
                    }
                    if (text.Length > 0)
                        bullets.Add(new WorldTipBullet(text, icon.Length > 0 ? icon : DefaultIcon));
                }
                if (bullets.Count > 0)
                    tips.Add(new WorldTip { Order = order, Bullets = bullets });
            }
        }
        tips.Sort((a, b) => a.Order.CompareTo(b.Order));
        return tips;
    }

    // "t:tip,n:tip_1" -> "tip". Tokenizes the comma-separated header and returns
    // the value after the "t:" tag.
    private static bool TryParseType(string header, out string type)
    {
        type = "";
        foreach (var tok in header.Split(','))
        {
            var t = tok.Trim();
            if (t.StartsWith("t:", System.StringComparison.OrdinalIgnoreCase))
            {
                type = t[2..].Trim();
                return type.Length > 0;
            }
        }
        return false;
    }

    private static string Unquote(string v)
    {
        v = v.Trim();
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') v = v[1..^1];
        return v;
    }
}
