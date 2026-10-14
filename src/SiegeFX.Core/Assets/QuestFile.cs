using System.Globalization;

namespace SiegeFX.Core.Assets;

/// <summary>One state of a quest in a map's <c>quests/quests.gas</c>: a
/// <c>[*]</c> block, in the order its <c>order</c> field gives. A dialogue
/// node activates it as <c>quest_key</c> (order 0) or <c>quest_key,N</c>.</summary>
public sealed record QuestStage(int Order, string Description, string Speaker, string Sample, string Address);

/// <summary>A quest block under <c>[quests]</c>: its header is the quest key.</summary>
public sealed record QuestFileEntry(string Key, string ScreenName, string Chapter, string QuestImage,
                                   IReadOnlyList<QuestStage> Stages)
{
    /// <summary>The key a dialogue node uses for <paramref name="order"/>:
    /// the bare key for order 0, <c>key,N</c> after that.</summary>
    public string StageKey(int order) => order == 0 ? Key : $"{Key},{order}";

    public QuestStage? Stage(int order) => Stages.FirstOrDefault(s => s.Order == order);
}

/// <summary>Reads a map's <c>quests/quests.gas</c>: the quest blocks under
/// <c>[quests]</c> and their stages. The <c>[chapters]</c> block is not read
/// yet.</summary>
public static class QuestFile
{
    public static IReadOnlyList<QuestFileEntry> Read(GasDocument doc)
    {
        var list = new List<QuestFileEntry>();
        foreach (var root in doc.Roots)
        {
            if (!root.Header.Equals("quests", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var q in root.Children)
            {
                if (string.IsNullOrWhiteSpace(q.Header) || q.Header == "*") continue;
                var stages = new List<QuestStage>();
                foreach (var s in q.Children)
                {
                    if (s.Header != "*") continue;
                    // A stage without a readable order takes the next one.
                    int order = int.TryParse(Attr(s, "order"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var o)
                        ? o : stages.Count == 0 ? 0 : stages[^1].Order + 1;
                    stages.Add(new QuestStage(order, Attr(s, "description"), Attr(s, "speaker"),
                                              Attr(s, "sample"), Attr(s, "address")));
                }
                list.Add(new QuestFileEntry(q.Header, Attr(q, "screen_name"), Attr(q, "chapter"),
                                            Attr(q, "quest_image"), stages));
            }
        }
        return list;
    }

    static string Attr(GasNode node, string name)
    {
        foreach (var a in node.Attributes)
            if (a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return a.Value.Trim().Trim('"');
        return "";
    }
}
