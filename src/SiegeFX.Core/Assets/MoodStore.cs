using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>Mood <c>[fog]</c> block. DS1 fog is linear camera-distance fog;
/// every one of map_world's 232 moods authors one, so this is effectively the
/// game's global atmosphere. Distances are meters from the camera; color is
/// 0xAARRGGBB (default_moods.gas). The <c>lowdetail</c> pair is the reduced
/// draw-distance variant for the retail "low detail" setting — we render the
/// full-detail pair and keep lowdetail for audit completeness.</summary>
public sealed class MoodFog
{
    public float NearDist { get; init; }
    public float FarDist { get; init; }
    public float LowDetailNearDist { get; init; }
    public float LowDetailFarDist { get; init; }
    public uint Color { get; init; } = 0xFFFFFFFF;
    public float Density { get; init; } = 1.0f;
}

/// <summary>Mood <c>[rain]</c> block. Density = drops spawned per second of
/// game time (shipped range 30–225). <c>lightning</c> authors storms — only
/// map_world_fh_r1_3 ships it, but mood_manager.skrit also forces lightning
/// on whenever drifted density ≥ 200 (and off below, unless authored).</summary>
public sealed class MoodRain
{
    public float Density { get; init; }
    public bool Lightning { get; init; }
}

/// <summary>Mood <c>[snow]</c> block. Density = flakes per second
/// (shipped range 75–500, alpine/Glacern regions).</summary>
public sealed class MoodSnow
{
    public float Density { get; init; }
}

/// <summary>Mood <c>[wind]</c> block. Velocity in m/s; direction in radians
/// clockwise from north (per default_moods.gas: PI/2 = east, PI = south).
/// Wind shears precipitation fall vectors; mood_manager drifts velocity.</summary>
public sealed class MoodWind
{
    public float Velocity { get; init; }
    public float Direction { get; init; }
}

/// <summary>One <c>[sun]</c> timed color key: <c>[XXhYYm] { color = 0xAARRGGBB; }</c>.
/// Stored for the audit + a future time-of-day lighting slice; the runtime
/// has no world clock yet so these are parsed but not applied.</summary>
public readonly record struct MoodSunKey(int Minutes, uint Color);

/// <summary>One DS1 mood definition. A mood bundles fog/sun/wind/rain/music
/// settings the engine swaps to via <c>mood_change(name)</c> trigger actions.
/// Weather components follow default_moods.gas semantics: a block OMITTED from
/// the mood definition means that component is DISABLED — so a mood change
/// with no [rain] block turns rain off. Null component properties model that
/// directly.</summary>
public sealed class MoodSetting
{
    public string Name { get; init; } = "";
    public bool Interior { get; init; }
    /// <summary>Seconds to blend from the previous mood to this one
    /// (fog distances/color, precipitation density, wind). Shipped values
    /// run 0–15s; absent means instant.</summary>
    public float TransitionTime { get; init; }
    /// <summary>Non-null when the mood authors a [fog] block.</summary>
    public MoodFog? Fog { get; init; }
    /// <summary>Non-null when the mood authors a [rain] block.</summary>
    public MoodRain? Rain { get; init; }
    /// <summary>Non-null when the mood authors a [snow] block.</summary>
    public MoodSnow? Snow { get; init; }
    /// <summary>Non-null when the mood authors a [wind] block.</summary>
    public MoodWind? Wind { get; init; }
    /// <summary>Timed sun color keys; empty when the mood has no [sun] block.</summary>
    public IReadOnlyList<MoodSunKey> Sun { get; init; } = Array.Empty<MoodSunKey>();
    /// <summary>[music] room_type reverb preset (rt_cave, rt_forest, ...);
    /// empty or rt_generic = no environmental reverb.</summary>
    public string RoomType { get; init; } = "";
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

    /// <summary>SS-CUSTOM (ED-8) — merge mood definitions from an already-parsed
    /// document into <paramref name="sink"/> (a SiegeSmith map bundles its own
    /// moods.gas inside the MAP tank; the loader merges them over the stock
    /// store so custom fog/weather/music apply in-engine). Returns how many
    /// mood definitions the document contributed.</summary>
    public static int MergeFromDocument(GasDocument doc, Dictionary<string, MoodSetting> sink)
    {
        int before = sink.Count;
        ParseDocument(doc, sink);
        // Same-name overrides don't change Count; report parse hits instead.
        int parsed = 0;
        foreach (var root in doc.Roots)
        {
            if (root.Header.TrimStart().StartsWith("mood_setting", StringComparison.OrdinalIgnoreCase)) parsed++;
            foreach (var child in root.Children)
                if (child.Header.TrimStart().StartsWith("mood_setting", StringComparison.OrdinalIgnoreCase)) parsed++;
        }
        return Math.Max(parsed, sink.Count - before);
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
        float transition = 0f;
        string ambient = "", standard = "", battle = "", roomType = "";
        MoodFog? fog = null;
        MoodRain? rain = null;
        MoodSnow? snow = null;
        MoodWind? wind = null;
        List<MoodSunKey>? sun = null;

        foreach (var attr in node.Attributes)
        {
            if (NameEq(attr.Name, "mood_name")) name = StripTail(attr.Value);
            else if (NameEq(attr.Name, "interior")) interior = ParseBool(attr.Value);
            else if (NameEq(attr.Name, "transition_time")) transition = ParseFloat(attr.Value, 0f);
        }

        foreach (var sub in node.Children)
        {
            var sh = sub.Header.Trim();
            if (sh.Equals("music", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var attr in sub.Attributes)
                {
                    if (NameEq(attr.Name, "ambient_track"))  ambient  = StripQuotes(attr.Value);
                    else if (NameEq(attr.Name, "standard_track")) standard = StripQuotes(attr.Value);
                    else if (NameEq(attr.Name, "battle_track"))   battle   = StripQuotes(attr.Value);
                    else if (NameEq(attr.Name, "room_type"))      roomType = StripQuotes(attr.Value);
                }
            }
            else if (sh.Equals("fog", StringComparison.OrdinalIgnoreCase))
            {
                float near = 0f, far = 0f, ldNear = -1f, ldFar = -1f, density = 1f;
                uint color = 0xFFFFFFFF;
                foreach (var attr in sub.Attributes)
                {
                    if (NameEq(attr.Name, "fog_near_dist")) near = ParseFloat(attr.Value, 0f);
                    else if (NameEq(attr.Name, "fog_far_dist")) far = ParseFloat(attr.Value, 0f);
                    else if (NameEq(attr.Name, "fog_lowdetail_near_dist")) ldNear = ParseFloat(attr.Value, -1f);
                    else if (NameEq(attr.Name, "fog_lowdetail_far_dist")) ldFar = ParseFloat(attr.Value, -1f);
                    else if (NameEq(attr.Name, "fog_color")) color = ParseColor(attr.Value, 0xFFFFFFFF);
                    else if (NameEq(attr.Name, "fog_density")) density = ParseFloat(attr.Value, 1f);
                }
                fog = new MoodFog
                {
                    NearDist = near,
                    FarDist = far,
                    // Absent lowdetail pair inherits the full-detail distances.
                    LowDetailNearDist = ldNear >= 0f ? ldNear : near,
                    LowDetailFarDist = ldFar >= 0f ? ldFar : far,
                    Color = color,
                    Density = density,
                };
            }
            else if (sh.Equals("rain", StringComparison.OrdinalIgnoreCase))
            {
                float density = 0f;
                bool lightning = false;
                foreach (var attr in sub.Attributes)
                {
                    if (NameEq(attr.Name, "rain_density")) density = ParseFloat(attr.Value, 0f);
                    else if (NameEq(attr.Name, "lightning")) lightning = ParseBool(attr.Value);
                }
                rain = new MoodRain { Density = density, Lightning = lightning };
            }
            else if (sh.Equals("snow", StringComparison.OrdinalIgnoreCase))
            {
                float density = 0f;
                foreach (var attr in sub.Attributes)
                    if (NameEq(attr.Name, "snow_density")) density = ParseFloat(attr.Value, 0f);
                snow = new MoodSnow { Density = density };
            }
            else if (sh.Equals("wind", StringComparison.OrdinalIgnoreCase))
            {
                float vel = 0f, dir = 0f;
                foreach (var attr in sub.Attributes)
                {
                    if (NameEq(attr.Name, "wind_velocity")) vel = ParseFloat(attr.Value, 0f);
                    else if (NameEq(attr.Name, "wind_direction")) dir = ParseFloat(attr.Value, 0f);
                }
                wind = new MoodWind { Velocity = vel, Direction = dir };
            }
            else if (sh.Equals("sun", StringComparison.OrdinalIgnoreCase))
            {
                // Shipped map_world data authors the timed keys in GAS colon
                // shorthand (`00h00m:color = 0xFF000000;`), so they surface as
                // attributes on the [sun] node. default_moods.gas documents the
                // expanded nested form (`[00h00m] { color = ...; }`) — accept both.
                foreach (var attr in sub.Attributes)
                {
                    var colon = attr.Name.IndexOf(':');
                    if (colon <= 0) continue;
                    if (!attr.Name[(colon + 1)..].Trim().Equals("color", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!TryParseTimeHeader(attr.Name[..colon].Trim(), out var attrMinutes)) continue;
                    (sun ??= new List<MoodSunKey>()).Add(
                        new MoodSunKey(attrMinutes, ParseColor(attr.Value, 0xFFFFFFFF)));
                }
                foreach (var key in sub.Children)
                {
                    if (!TryParseTimeHeader(key.Header.Trim(), out var minutes)) continue;
                    foreach (var attr in key.Attributes)
                    {
                        if (!NameEq(attr.Name, "color")) continue;
                        (sun ??= new List<MoodSunKey>()).Add(
                            new MoodSunKey(minutes, ParseColor(attr.Value, 0xFFFFFFFF)));
                    }
                }
                sun?.Sort((a, b) => a.Minutes.CompareTo(b.Minutes));
            }
        }

        if (string.IsNullOrEmpty(name)) return;
        sink[name] = new MoodSetting
        {
            Name = name,
            Interior = interior,
            TransitionTime = transition,
            AmbientTrack = ambient,
            StandardTrack = standard,
            BattleTrack = battle,
            RoomType = roomType,
            Fog = fog,
            Rain = rain,
            Snow = snow,
            Wind = wind,
            Sun = (IReadOnlyList<MoodSunKey>?)sun ?? Array.Empty<MoodSunKey>(),
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

    /// <summary>Mood floats ship as <c>5.0f</c>, <c>200</c>, <c>.0</c> (path2ac_4's
    /// transition_time), or <c>3.14</c> — strip the GAS float suffix and let
    /// invariant parsing take the rest.</summary>
    internal static float ParseFloat(string s, float fallback)
    {
        var t = s.Trim();
        if (t.EndsWith("f", StringComparison.OrdinalIgnoreCase)) t = t[..^1];
        return float.TryParse(t, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    /// <summary>0xAARRGGBB hex color (fog/sun). Plain decimal accepted defensively.</summary>
    internal static uint ParseColor(string s, uint fallback)
    {
        var t = s.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(t[2..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : fallback;
        return uint.TryParse(t, out var d) ? d : fallback;
    }

    /// <summary>[sun] child headers are <c>XXhYYm</c> (e.g. <c>06h30m</c>);
    /// returns minutes since midnight.</summary>
    static bool TryParseTimeHeader(string header, out int minutes)
    {
        minutes = 0;
        var m = System.Text.RegularExpressions.Regex.Match(
            header, @"^(\d{1,2})h(\d{1,2})m$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        int h = int.Parse(m.Groups[1].Value);
        int mm = int.Parse(m.Groups[2].Value);
        if (h > 23 || mm > 59) return false;
        minutes = h * 60 + mm;
        return true;
    }

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
