using SiegeFX.Core.Parity;

namespace SiegeFX.Tests;

public class CommandCatalogTests
{
    [Fact]
    public void A_template_the_catalog_does_not_list_is_not_implemented()
    {
        // cmd_ai_t_drop is a real command template no region places.
        Assert.Equal(CommandHandling.None, CommandCatalog.Classify("cmd_ai_t_drop"));
        Assert.Equal(CommandHandling.None, CommandCatalog.Classify("no_such_template"));
        Assert.False(CommandCatalog.Handles("cmd_ai_t_drop", CommandHandling.Activate));
        Assert.Equal("", CommandCatalog.Describe("cmd_ai_t_drop"));
    }

    [Theory]
    [InlineData("cmd_ai_c_animate")]
    [InlineData("cmd_ai_c_equip")]
    [InlineData("cmd_ai_c_drop")]
    [InlineData("cmd_ai_c_stop")]
    public void The_catalyst_verbs_run_on_activation(string name)
    {
        Assert.Equal(CommandHandling.Activate, CommandCatalog.Classify(name));
    }

    [Theory]
    [InlineData("cmd_ai_t_move")]
    [InlineData("CMD_AI_T_MOVE")]
    [InlineData("Cmd_Ai_T_Move")]
    public void Template_names_compare_without_case_as_the_engine_reads_them(string name)
    {
        Assert.Equal(CommandHandling.Activate, CommandCatalog.Classify(name));
    }

    [Fact]
    public void A_template_can_take_more_than_one_path()
    {
        Assert.Equal(CommandHandling.Activate | CommandHandling.Nis, CommandCatalog.Classify("cmd_camera_move"));
        Assert.Equal(CommandHandling.Activate | CommandHandling.RegionLoad, CommandCatalog.Classify("light_enable"));
        Assert.True(CommandCatalog.Handles("cmd_camera_move", CommandHandling.Nis));
        Assert.True(CommandCatalog.Handles("cmd_camera_move", CommandHandling.Activate));
        Assert.False(CommandCatalog.Handles("cmd_camera_move", CommandHandling.PatrolRoute));
    }

    [Fact]
    public void The_NIS_indexer_takes_exactly_the_sequence_and_camera_gizmos()
    {
        var nis = CommandCatalog.HandledTemplates
            .Where(t => CommandCatalog.Handles(t, CommandHandling.Nis))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[]
        {
            "cmd_camera_command", "cmd_camera_move", "cmd_camera_waypoint", "cmd_enter_nis", "cmd_leave_nis",
        }, nis);
    }

    [Fact]
    public void Patrol_routes_are_the_c_patrol_pair_and_only_routes()
    {
        Assert.Equal(CommandHandling.PatrolRoute, CommandCatalog.Classify("cmd_ai_c_patrol"));
        Assert.Equal(CommandHandling.PatrolRoute, CommandCatalog.Classify("cmd_ai_c_patrol_orient"));
        // The t_ forms are orders an actor is given on activation, not routes.
        Assert.Equal(CommandHandling.Activate, CommandCatalog.Classify("cmd_ai_t_patrol"));
    }

    [Fact]
    public void Every_listed_template_has_a_path_and_says_what_it_does()
    {
        Assert.NotEmpty(CommandCatalog.HandledTemplates);
        foreach (var t in CommandCatalog.HandledTemplates)
        {
            Assert.NotEqual(CommandHandling.None, CommandCatalog.Classify(t));
            Assert.False(string.IsNullOrWhiteSpace(CommandCatalog.Describe(t)), $"{t} has no description");
            Assert.Equal(t.ToLowerInvariant(), t);
        }
    }
}
