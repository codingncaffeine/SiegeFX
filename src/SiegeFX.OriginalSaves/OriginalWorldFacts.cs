namespace SiegeFX.OriginalSaves;

/// <summary>A quest as the original's world tree records it
/// (<c>/world/m_pvictory/m_quests/N/_value</c>).</summary>
public sealed record OriginalQuest(string Name, string ScreenName, string Speaker, bool Active, bool Completed, bool Viewed);

/// <summary>A script flag from the original's game auditor
/// (<c>/world/m_pgameauditor/m_entries/N</c>): a key with one typed value.</summary>
public sealed record OriginalFlag(string Key, string Value);

/// <summary>Facts read out of the world tree at paths confirmed against saves
/// made one change apart (see docs/original-save-format.md). Each returns an
/// empty result when its path is absent rather than guessing.</summary>
public static class OriginalWorldFacts
{
    public const string QuestsPath = "/world/m_pvictory/m_quests";
    public const string FlagsPath = "/world/m_pgameauditor/m_entries";
    public const string ObjectsPath = "/godb/m_globaldb";
    public const string UiGamePath = "/uigame";

    public static IReadOnlyList<OriginalQuest> Quests(XferTree world)
    {
        var list = new List<OriginalQuest>();
        foreach (var entry in world.Find(QuestsPath)?.Children ?? [])
        {
            var q = entry.Child("_value") ?? entry;
            var name = q.Value("sname")?.AsText;
            if (name is null) continue;
            list.Add(new OriginalQuest(
                name,
                q.Value("sscreenname")?.AsAnyText ?? "",
                q.Value("sspeaker")?.AsAnyText ?? "",
                q.Value("bactive")?.AsBool ?? false,
                q.Value("bcompleted")?.AsBool ?? false,
                q.Value("bviewed")?.AsBool ?? false));
        }
        return list;
    }

    public static IReadOnlyList<OriginalFlag> Flags(XferTree world)
    {
        var list = new List<OriginalFlag>();
        foreach (var e in world.Find(FlagsPath)?.Children ?? [])
        {
            var key = e.Value("m_key")?.AsText;
            if (key is null) continue;
            list.Add(new OriginalFlag(key, FlagValue(e)));
        }
        return list;
    }

    /// <summary>The flag's value: whichever typed slot is set (an auditor entry
    /// carries all of m_bool, m_int, m_float, m_double and m_string).</summary>
    static string FlagValue(XferNode e)
    {
        if (e.Value("m_string")?.AsText is { Length: > 0 } s) return $"\"{s}\"";
        if (e.Value("m_int")?.AsInt32 is int i and not 0) return i.ToString();
        if (e.Value("m_float")?.AsSingle is float f and not 0) return f.ToString("G7");
        if (e.Value("m_double")?.AsDouble is double d and not 0) return d.ToString("G10");
        if (e.Value("m_bool")?.AsBool is bool b) return b ? "true" : "false";
        return "";
    }

    /// <summary>"Show tips" from the options, stored with the UI state.</summary>
    public static bool? TutorialTips(XferTree world) =>
        world.Find(UiGamePath)?.Value("tutorial_tips")?.AsBool;

    /// <summary>Game objects saved in the world. Decor (trees, bushes,
    /// furniture) is never saved, and untouched props only while their area
    /// is loaded; the map supplies the rest.</summary>
    public static int ObjectCount(XferTree world) =>
        world.Find(ObjectsPath)?.Children.Count() ?? 0;

    /// <summary>The map SCIDs of the saved game objects (<c>m_scid</c>;
    /// objects made at run time carry <c>0xfffffffe</c> and live in
    /// <c>/godb/m_localdb</c> instead).</summary>
    public static IReadOnlySet<uint> ObjectScids(XferTree world)
    {
        var set = new HashSet<uint>();
        foreach (var o in world.Find(ObjectsPath)?.Children ?? [])
            if (o.Value("m_scid")?.AsUInt32 is uint scid) set.Add(scid);
        return set;
    }

    public const string MapPath = "/m_pworldterrain/m_pmap";

    /// <summary>The difficulty world the game runs in (<c>m_mpworldname</c>:
    /// <c>"normal"</c> in DS1 saves, <c>"regular"</c> in the LoA save).</summary>
    public static string? WorldName(XferTree world) =>
        world.Find(MapPath)?.Value("m_mpworldname")?.AsText;

    public const string RetiredPath = "/godb/m_retiredscidcoll";

    /// <summary>Map placements the game must not create again: smashed
    /// barrels, killed monsters (whose corpses may still be saved), used-up
    /// gizmos. Kept for the whole game, whether or not the area is loaded.</summary>
    public static IReadOnlySet<uint> RetiredScids(XferTree world)
    {
        var set = new HashSet<uint>();
        foreach (var e in world.Find(RetiredPath)?.Children ?? [])
            if (e.Value("_value")?.AsUInt32 is uint scid) set.Add(scid);
        return set;
    }

    public const string FrustumsPath = "/m_pworldterrain/siegeengine/m_frustumcoll";

    /// <summary>The terrain nodes loaded when the game saved: the union of
    /// every frustum's <c>m_prevownednodes</c>. A node GUID is stored in the
    /// byte order its text form reads (as in a position's node), so it is
    /// read big-endian to compare with the map's <c>0x…</c> GUIDs.</summary>
    public static IReadOnlySet<uint> LoadedNodes(XferTree world)
    {
        var set = new HashSet<uint>();
        foreach (var frustum in world.Find(FrustumsPath)?.Children ?? [])
            foreach (var n in frustum.Child("m_prevownednodes")?.Children ?? [])
                if (n.Value("_value") is { Raw.Length: 4 } v)
                    set.Add(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(v.Raw.Span));
        return set;
    }
}
