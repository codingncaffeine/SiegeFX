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

    /// <summary>Game objects saved in the world (only the area around the
    /// party — see the open questions in the format doc).</summary>
    public static int ObjectCount(XferTree world) =>
        world.Find(ObjectsPath)?.Children.Count() ?? 0;
}
