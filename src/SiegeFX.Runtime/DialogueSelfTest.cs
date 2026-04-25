using System.Text;
using SiegeFX.Core.Assets;

namespace SiegeFX.Runtime;

/// <summary>
/// Phase 20a — verifies <see cref="ConversationStore"/> parses a DS1-shaped
/// <c>conversations.gas</c> back into the expected node tree. Uses a synthetic
/// gas string mirroring fh_r1's <c>conversation_edgaar</c> (the canonical
/// branching case: one <c>choice = more</c> node, one <c>quest_dialog</c>
/// node with an <c>activate_quest*</c>, and one no-order tail) so the test
/// passes without needing the user's DS1 install on disk.
///
/// Wired into <c>test-all.bat</c> as a no-window check so a regression in the
/// dialogue parser surfaces before the visual walkthrough step.
/// </summary>
internal static class DialogueSelfTest
{
    public static bool Run()
    {
        const string gas = """
[conversation_edgaar]
{
    [text*]
    {
        order = 0;
        sample = s_v_fh_edgaar1.mp3;
        screen_text = "I should have guessed you'd be cleaving your way to Stonebridge to find out what's got the Krug all stirred up.";
        choice = more;
    }
    [text*]
    {
        order = 1;
        sample = s_v_fh_edgaar2.mp3;
        screen_text = "If you need any supplies for the long trek to Stonebridge, you can help yourself.";
        activate_quest* = quest_edgaar_basement;
        quest_dialog = true;
    }
    [text*]
    {
        sample = s_v_fh_edgaar3.mp3;
        screen_text = "Don't worry about me. Once you've cleared the Krug out, I should be able to stay safe.";
    }
}
[conversation_narrator]
{
    [text*]
    {
        order = 0;
        sample = s_v_king_intro;
        nis = true;
        screen_text = "A long time ago, on the continent of Aranna...";
        scroll_rate = 2.7;
    }
}
""";

        GasDocument doc;
        try { doc = GasDocument.Parse(gas); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[selftest-dialogue] FAIL — parse threw: {ex.Message}");
            return false;
        }

        var convs = ConversationStore.LoadFromDocument(doc);
        var failures = new List<string>();

        if (!convs.TryGetValue("conversation_edgaar", out var edgaar))
        {
            failures.Add("conversation_edgaar missing from parsed dictionary");
            return Report(failures);
        }
        if (!convs.TryGetValue("conversation_narrator", out var narrator))
        {
            failures.Add("conversation_narrator missing from parsed dictionary");
        }

        // Edgaar — the branching shape. Three nodes: order=0+choice=more,
        // order=1+quest_dialog with activate_quest, no-order tail.
        if (edgaar.Nodes.Count != 3)
            failures.Add($"edgaar node count: expected 3, got {edgaar.Nodes.Count}");
        else
        {
            var n0 = edgaar.Nodes[0]; var n1 = edgaar.Nodes[1]; var n2 = edgaar.Nodes[2];
            if (n0.Order != 0)            failures.Add($"edgaar[0].Order: expected 0, got {n0.Order}");
            if (n0.Choice != "more")      failures.Add($"edgaar[0].Choice: expected 'more', got '{n0.Choice}'");
            if (n0.IsQuestDialog)         failures.Add("edgaar[0].IsQuestDialog should be false");
            if (n0.Text.Length == 0)      failures.Add("edgaar[0].Text empty");

            if (n1.Order != 1)            failures.Add($"edgaar[1].Order: expected 1, got {n1.Order}");
            if (!n1.IsQuestDialog)        failures.Add("edgaar[1].IsQuestDialog should be true");
            if (n1.ActivateQuest != "quest_edgaar_basement")
                failures.Add($"edgaar[1].ActivateQuest: expected 'quest_edgaar_basement', got '{n1.ActivateQuest}'");

            if (n2.Order != -1)           failures.Add($"edgaar[2].Order: expected -1 (no-order tail), got {n2.Order}");
            if (n2.IsQuestDialog)         failures.Add("edgaar[2].IsQuestDialog should be false");
            if (!string.IsNullOrEmpty(n2.Choice)) failures.Add($"edgaar[2].Choice should be empty, got '{n2.Choice}'");
        }

        // Narrator — single nis line, validates non-quest single-shot path.
        if (narrator is not null)
        {
            if (narrator.Nodes.Count != 1)
                failures.Add($"narrator node count: expected 1, got {narrator.Nodes.Count}");
            else if (!narrator.Nodes[0].IsNonInteractive)
                failures.Add("narrator[0].IsNonInteractive should be true");
        }

        return Report(failures);
    }

    static bool Report(List<string> failures)
    {
        if (failures.Count == 0)
        {
            Console.WriteLine("[selftest-dialogue] OK — edgaar branching tree (3 nodes: more / quest_dialog / no-order tail) parsed correctly");
            return true;
        }
        Console.Error.WriteLine($"[selftest-dialogue] FAIL ({failures.Count}):");
        foreach (var f in failures) Console.Error.WriteLine("  " + f);
        return false;
    }
}
