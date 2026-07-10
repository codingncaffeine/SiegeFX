using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SiegeSmith.Services;

/// <summary>GAME-4 — a map-local quest for the journal. Ships in the retail
/// per-map <c>quests/quests.gas</c> shape ([chapters] + [quests], each quest
/// with ordered <c>[*]</c> description states); the SiegeFX engine merges the
/// file into its quest catalog at map load, so custom quests display with
/// their own names and objective text.</summary>
public sealed class MapQuest
{
    public string Key = "quest_custom";
    public string ScreenName = "New Quest";
    public string Description = "";
    public int Order;

    public string Label => string.IsNullOrWhiteSpace(ScreenName) ? Key : ScreenName;
    public string Detail => $"{Key} · order {Order.ToString(CultureInfo.InvariantCulture)}";
}

public static class QuestAuthor
{
    /// <summary>Compose a retail-shaped quests.gas: one chapter (name +
    /// optional intro text) and the quest list ordered by <see cref="MapQuest.Order"/>.</summary>
    public static string Compose(string chapterName, string chapterIntro, IReadOnlyList<MapQuest> quests)
    {
        static string Q(string s) => '"' + (s ?? "").Replace("\"", "'") + '"';
        var sb = new StringBuilder();
        sb.Append("[chapters]\r\n{\r\n\t[chapter_1]\r\n\t{\r\n");
        sb.Append($"\t\tscreen_name = {Q(string.IsNullOrWhiteSpace(chapterName) ? "Chapter 1" : chapterName)};\r\n");
        sb.Append("\t\tchapter_image = b_gui_ig_mnu_jnl_chapter_01;\r\n");
        sb.Append("\t\t[*]\r\n\t\t{\r\n\t\t\torder = 0;\r\n");
        sb.Append($"\t\t\tdescription = {Q(chapterIntro)};\r\n");
        sb.Append("\t\t}\r\n\t}\r\n}\r\n\r\n[quests]\r\n{\r\n");

        var ordered = new List<MapQuest>(quests);
        ordered.Sort((a, b) => a.Order.CompareTo(b.Order));
        foreach (var q in ordered)
        {
            var key = TemplateAuthor.SanitizeName(q.Key);
            sb.Append($"\t[{key}]\r\n\t{{\r\n");
            sb.Append("\t\tchapter = chapter_1;\r\n");
            sb.Append($"\t\tscreen_name = {Q(q.ScreenName)};\r\n");
            sb.Append("\t\tquest_image = b_gui_ig_mnu_quest;\r\n");
            sb.Append("\t\tvictory_sample = s_e_level_up_quest;\r\n");
            sb.Append("\t\t[*]\r\n\t\t{\r\n\t\t\torder = 0;\r\n\t\t\trequired = true;\r\n");
            sb.Append($"\t\t\tdescription = {Q(q.Description)};\r\n");
            sb.Append("\t\t}\r\n\t}\r\n");
        }
        sb.Append("}\r\n");
        return sb.ToString();
    }
}
