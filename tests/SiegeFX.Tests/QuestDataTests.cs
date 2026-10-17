using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;

namespace SiegeFX.Tests;

public class QuestDataTests
{
    // Invented quests in the shape of a map's quests/quests.gas.
    const string QuestsGas = """
        [chapters]
        {
            [chapter_1] { screen_name = "Chapter One"; }
        }
        [quests]
        {
            [quest_kill_moles]
            // a comment between the header and the block
            {
                chapter     = chapter_1;
                screen_name = "Moles in the Garden";
                quest_image = b_test_image;
                [*]
                {
                    order       = 0;
                    speaker     = "Gardener";
                    description = "Chase the moles out of the garden.";
                    address     = test_r1:conversations:conversation_gardener;
                }
                [*]
                {
                    order       = 1;
                    description = "Tell the gardener the moles are gone.";
                }
            }
            [quest_lost_hat]
            {
                screen_name = "The Lost Hat";
                [*] { order = 0; description = "Find the scarecrow's hat."; }
                [*] { order = 1; description = "Bring the hat back."; }
            }
        }
        """;

    static IReadOnlyList<QuestFileEntry> Read() => QuestFile.Read(GasDocument.Parse(QuestsGas));

    [Fact]
    public void The_quest_file_gives_each_quest_its_stages_in_order()
    {
        var quests = Read();
        Assert.Equal(["quest_kill_moles", "quest_lost_hat"], quests.Select(q => q.Key));
        var moles = quests[0];
        Assert.Equal("Moles in the Garden", moles.ScreenName);
        Assert.Equal("chapter_1", moles.Chapter);
        Assert.Equal("b_test_image", moles.QuestImage);
        Assert.Equal([0, 1], moles.Stages.Select(s => s.Order));
        Assert.Equal("Gardener", moles.Stage(0)!.Speaker);
        Assert.Equal("test_r1:conversations:conversation_gardener", moles.Stage(0)!.Address);
        Assert.Equal("Tell the gardener the moles are gone.", moles.Stage(1)!.Description);
    }

    [Fact]
    public void A_stage_is_activated_by_the_bare_key_then_key_comma_order()
    {
        var moles = Read()[0];
        Assert.Equal("quest_kill_moles", moles.StageKey(0));
        Assert.Equal("quest_kill_moles,1", moles.StageKey(1));
    }

    static Dictionary<string, QuestDefinition> Catalog() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Quest_Kill_Moles"] = new QuestDefinition
        {
            Key = "Quest_Kill_Moles", ScreenName = "old name", ObjectiveText = "old text",
            KillTargetTemplate = "mole", KillCountGoal = 4, NextQuestKey = "quest_kill_moles,1",
        },
        ["quest_lost_hat,1"] = new QuestDefinition
        {
            Key = "quest_lost_hat,1", ScreenName = "old name", TalkTargetTemplate = "scarecrow", TalkCountGoal = 1,
        },
    };

    [Fact]
    public void Merging_takes_the_text_from_the_data_and_keeps_the_catalog_goals()
    {
        var defs = Catalog();
        var dataOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        QuestCatalog.Merge(defs, dataOnly, Read());

        var moles = defs["quest_kill_moles"];
        Assert.Equal("Quest_Kill_Moles", moles.Key);
        Assert.Equal("Moles in the Garden", moles.ScreenName);
        Assert.Equal("Chase the moles out of the garden.", moles.ObjectiveText);
        Assert.Equal("mole", moles.KillTargetTemplate);
        Assert.Equal(4, moles.KillCountGoal);
        Assert.Equal("quest_kill_moles,1", moles.NextQuestKey);

        var hat1 = defs["quest_lost_hat,1"];
        Assert.Equal("The Lost Hat", hat1.ScreenName);
        Assert.Equal("Bring the hat back.", hat1.ObjectiveText);
        Assert.Equal("scarecrow", hat1.TalkTargetTemplate);
        Assert.Equal(1, hat1.TalkCountGoal);
    }

    [Fact]
    public void A_stage_the_catalog_lacks_gets_a_text_only_row()
    {
        var defs = Catalog();
        var dataOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        QuestCatalog.Merge(defs, dataOnly, Read());

        var hat = defs["quest_lost_hat"];
        Assert.Equal("Find the scarecrow's hat.", hat.ObjectiveText);
        Assert.Equal(0, hat.KillCountGoal + hat.TalkCountGoal + hat.PickupCountGoal);
        Assert.Equal("Tell the gardener the moles are gone.", defs["quest_kill_moles,1"].ObjectiveText);
        Assert.Equal(["quest_kill_moles,1", "quest_lost_hat"], dataOnly.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Merging_twice_changes_nothing()
    {
        var defs = Catalog();
        var dataOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        QuestCatalog.Merge(defs, dataOnly, Read());
        QuestCatalog.Merge(defs, dataOnly, Read());

        Assert.Equal(4, defs.Count);
        Assert.Equal(4, defs["quest_kill_moles"].KillCountGoal);
        Assert.Equal(2, dataOnly.Count);
    }

    [Fact]
    public void A_base_key_only_the_data_defines_still_resolves_to_the_catalog_stages()
    {
        var defs = Catalog();
        var dataOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        QuestCatalog.Merge(defs, dataOnly, Read());

        // A trigger naming the bare key flips the staged entry the catalog defines.
        Assert.Equal(["quest_lost_hat,1"], QuestCatalog.ResolveKeyAliases(defs, dataOnly, "quest_lost_hat"));
        // A base key the catalog defines resolves to itself.
        Assert.Equal(["quest_kill_moles"], QuestCatalog.ResolveKeyAliases(defs, dataOnly, "quest_kill_moles"));
        // A quest only the data defines resolves to itself.
        Assert.Equal(["quest_new"], QuestCatalog.ResolveKeyAliases(defs, dataOnly, "quest_new"));
    }
}
