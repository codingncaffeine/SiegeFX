using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>One DS1 mood definition. A mood bundles fog/sun/wind/rain/music
/// settings the engine swaps to via <c>mood_change(name)</c> trigger actions.
/// Phase 21d-2a-xi only consumes <see cref="AmbientTrack"/> (the looping
/// ambient bed) plus <see cref="Interior"/> (interior moods skip the
/// outdoor-bed defaulting heuristic). Future polish slices can extend to
/// fog / sun / room_type without rebuilding the parse path.</summary>
public sealed class MoodSetting
{
    public string Name { get; init; } = "";
    public bool Interior { get; init; }
    /// <summary>Looping ambient SFX clip (e.g. <c>"s_e_ambient_woods_01"</c>).
    /// Empty when the mood has no bed — the engine should leave whatever's
    /// playing untouched (silence, or a previous bed). 42 of map_world's 232
    /// shipped moods carry a non-empty value.</summary>
    public string AmbientTrack { get; init; } = "";
    /// <summary>Standard music track (e.g. <c>"s_m_Farmhouse_02"</c>). Consumed
    /// by Phase 22-SC-MUSIC-C's <c>RenderHost.ApplyMoodMusic</c> which strips
    /// the <c>s_m_</c> prefix and routes to the streaming player. Empty
    /// means the mood inherits the previous mood's music — DS1 has bed-only
    /// moods that ride the surrounding region's track.</summary>
    public string StandardTrack { get; init; } = "";
    /// <summary>Battle music track (e.g. <c>"s_m_battle"</c>). Consumed by
    /// Phase 22-SC-MUSIC-D's combat-state machine — when any nearby hostile
    /// engages the player, <c>TickCombatMusic</c> swaps to this; reverts to
    /// <see cref="StandardTrack"/> 3s after disengagement.</summary>
    public string BattleTrack { get; init; } = "";
}

/// <summary>
/// Parses DS1 mood definitions out of <c>/world/global/moods/&lt;map&gt;/moods*.gas</c>.
/// Each file is a flat list of <c>[mood_setting*] { mood_name = ...; [music] { ambient_track = ...; } ... }</c>
/// blocks. Mood names follow a strict <c>map_world_&lt;region&gt;_&lt;index&gt;[_subindex]</c> scheme
/// (per the v4.0 naming-scheme comment Dave Tomandl left at the top of moods1.gas),
/// which lets us derive a per-region default mood without needing the trigger
/// system to actually fire <c>mood_change()</c> actions yet.
/// </summary>
public static class MoodStore
{
    /// <summary>Load every mood file under <c>/world/global/moods/</c> from
    /// <paramref name="logicTank"/>. Returns flattened mood list keyed by
    /// <see cref="MoodSetting.Name"/>; later definitions override earlier ones
    /// if names collide (DS1's own convention — names are globally unique).</summary>
    public static (IReadOnlyDictionary<string, MoodSetting> Moods, IReadOnlyList<string> Diagnostics) Load(
        TankReader logicTank)
    {
        var diags = new List<string>();
        var moods = new Dictionary<string, MoodSetting>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in logicTank.ListFiles())
        {
            if (!path.StartsWith("/world/global/moods/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!path.EndsWith(".gas", StringComparison.OrdinalIgnoreCase)) continue;
            // Skip mood_manager.gas (skrit pointer) and timeofday.gas (lighting only).
            if (path.EndsWith("/mood_manager.gas", StringComparison.OrdinalIgnoreCase)) continue;
            if (path.EndsWith("/timeofday.gas",    StringComparison.OrdinalIgnoreCase)) continue;

            byte[] bytes;
            try { bytes = logicTank.ExtractToMemory(path); }
            catch (Exception ex) { diags.Add($"{path}: extract failed: {ex.Message}"); continue; }

            GasDocument doc;
            try { doc = GasDocument.Load(bytes); }
            catch (Exception ex) { diags.Add($"{path}: parse failed: {ex.Message}"); continue; }

            int countBefore = moods.Count;
            ParseDocument(doc, moods);
            int added = moods.Count - countBefore;
            if (added == 0 && bytes.Length > 200)
            {
                // 200-byte threshold rules out the half-dozen comment-only stub
                // files (default_moods.gas is the canonical example — it ships
                // an "all possible settings" example commented out, no real
                // mood definitions). A larger file with zero parsed moods is
                // a real shape regression worth flagging.
                diags.Add($"{path}: parsed but produced 0 moods");
            }
        }

        return (moods, diags);
    }

    static void ParseDocument(GasDocument doc, Dictionary<string, MoodSetting> sink)
    {
        // Three shapes show up in shipped data:
        //   (a) each [mood_setting*] block is a top-level root (the most
        //       common shape in moods1.gas — every block is its own root)
        //   (b) wrapped: a single [moods] root containing the mood_setting blocks
        //   (c) commented-out template (default_moods.gas) — ignored, no roots match
        // Treat both the roots themselves and their depth-1 children as
        // candidate mood_setting nodes.
        foreach (var root in doc.Roots) TryConsumeOne(root, sink);
        foreach (var root in doc.Roots)
            foreach (var child in root.Children)
                TryConsumeOne(child, sink);
    }

    static void TryConsumeOne(GasNode node, Dictionary<string, MoodSetting> sink)
    {
        var hdr = node.Header.Trim();
        if (!hdr.StartsWith("mood_setting", StringComparison.OrdinalIgnoreCase)) return;

        string name = "";
        bool interior = false;
        string ambient = "", standard = "", battle = "";

        foreach (var attr in node.Attributes)
        {
            if (NameEq(attr.Name, "mood_name")) name = StripTail(attr.Value);
            else if (NameEq(attr.Name, "interior")) interior = ParseBool(attr.Value);
        }

        foreach (var sub in node.Children)
        {
            if (!sub.Header.Trim().Equals("music", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var attr in sub.Attributes)
            {
                if (NameEq(attr.Name, "ambient_track"))  ambient  = StripQuotes(attr.Value);
                else if (NameEq(attr.Name, "standard_track")) standard = StripQuotes(attr.Value);
                else if (NameEq(attr.Name, "battle_track"))   battle   = StripQuotes(attr.Value);
            }
        }

        if (string.IsNullOrEmpty(name)) return;
        sink[name] = new MoodSetting
        {
            Name = name,
            Interior = interior,
            AmbientTrack = ambient,
            StandardTrack = standard,
            BattleTrack = battle,
        };
    }

    /// <summary>Pick the canonical default mood for a given region. The DS1
    /// naming convention guarantees each region has a series
    /// <c>map_&lt;map&gt;_&lt;region&gt;_1</c>, <c>_2</c>, ...; this picks the
    /// lowest-numbered entry with a non-empty <see cref="MoodSetting.AmbientTrack"/>.
    /// Falls back to the lowest-numbered match without one (so the runtime
    /// can record the mood-name decision even when the bed is silence). Returns
    /// null when no mood matches the region prefix at all (likely a region we
    /// don't ship a mood for).</summary>
    public static MoodSetting? FindRegionDefault(
        IReadOnlyDictionary<string, MoodSetting> moods,
        string mapName,
        string regionName)
    {
        var prefix = $"map_{mapName.ToLowerInvariant()}_{regionName.ToLowerInvariant()}_";
        MoodSetting? bestWithBed = null;
        int bestWithBedRank = int.MaxValue;
        MoodSetting? bestAny = null;
        int bestAnyRank = int.MaxValue;

        foreach (var (key, m) in moods)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            // Reject intro variants (map_world_fh_r1_intro_1 etc.) — those
            // are NIS-only moods; the play default is the bare-numeric series.
            var tail = key[prefix.Length..];
            if (tail.StartsWith("intro", StringComparison.OrdinalIgnoreCase)) continue;
            if (!TryParseLeadingInt(tail, out var rank)) continue;
            if (rank < bestAnyRank) { bestAnyRank = rank; bestAny = m; }
            if (!string.IsNullOrEmpty(m.AmbientTrack) && rank < bestWithBedRank)
            {
                bestWithBedRank = rank;
                bestWithBed = m;
            }
        }
        return bestWithBed ?? bestAny;
    }

    static bool TryParseLeadingInt(string s, out int n)
    {
        // Accepts "1", "1_3", "12", "12_4_5" — only the leading run of digits.
        n = 0;
        int i = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        if (i == 0) return false;
        return int.TryParse(s[..i], out n);
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

    static string StripTail(string s)
    {
        // Strip a trailing comment fragment after a `;` (GAS allows
        // `key = value; // comment`) or unwanted whitespace.
        var t = s.Trim();
        // GasDocument already strips the trailing `;` and trailing comments,
        // but defensively trim again so future parser changes don't desync.
        return t;
    }
}
