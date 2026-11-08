using System.Text;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeFX.Tests;

/// <summary>Map Settings and the Start Location column read the map's own
/// files: main.gas [worlds], start_positions.gas (dev-only groups left out,
/// [world_levels] required levels) and the region each start lies in.</summary>
public class MpMapCatalogTests
{
    const string Main = """
        [t:map,n:map]
        {
            description = "The original single-player campaign.";
            name = map_world;
            screen_name = "Kingdom of Ehb";
            [worlds]
            {
                [normal] { screen_name = "Regular"; description = "Players of all levels can play here."; required_level = 0; }
            }
        }
        """;

    const string Starts = """
        [start_positions]
        {
            [t:start_group,n:farmhouse]
            {
              b default = true;
                description = "The Farmlands";
              b dev_only = false;
              i id = 1;
                screen_name = "Farmhouse";
                [start_position] { i id = 1; position = 1,0,1,0x000000AA; }
            }
            [t:start_group,n:stonebridge]
            {
              b default = false;
              b dev_only = false;
              i id = 2;
                screen_name = "Stonebridge";
                [world_levels] { [normal] { required_level = 3; } }
                [start_position] { i id = 2; position = 1,0,1,0x000000BB; }
                [start_position] { i id = 1; position = 1,0,1,0x000000BC; }
            }
            [t:start_group,n:dev_spot]
            {
              b dev_only = true;
                screen_name = "dev spot";
                [start_position] { i id = 1; position = 1,0,1,0x000000AA; }
            }
        }
        """;

    static string Nodes(params uint[] guids)
    {
        var sb = new StringBuilder("[t:terrain_nodes,n:siege_node_list]\n{\n  targetnode = 0x" + guids[0].ToString("x8") + ";\n");
        foreach (var g in guids)
            sb.Append($"  [t:snode,n:0x{g:x8}]\n  {{\n    guid = 0x{g:x8};\n    mesh_guid = 0x00000001;\n  }}\n");
        return sb.Append("}\n").ToString();
    }

    [Fact]
    public void TheMapsWorldsAndPlayerStartLocationsWithTheirRegions()
    {
        var writer = new TankWriter { Priority = TankPriority.Factory };
        writer.Add("/world/maps/map_world/main.gas", Encoding.UTF8.GetBytes(Main));
        writer.Add("/world/maps/map_world/info/start_positions.gas", Encoding.UTF8.GetBytes(Starts));
        writer.Add("/world/maps/map_world/regions/fh_r1/terrain_nodes/nodes.gas", Encoding.UTF8.GetBytes(Nodes(0xAA, 0xAB)));
        writer.Add("/world/maps/map_world/regions/sb_r1/terrain_nodes/nodes.gas", Encoding.UTF8.GetBytes(Nodes(0xBC, 0xBD)));
        var path = Path.Combine(Path.GetTempPath(), $"siegefx-mpmap-{Guid.NewGuid():N}.dsmap");
        try
        {
            writer.Write(path, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            MpMapInfo info;
            using (var tank = TankFile.Open(path))
                info = MpMapCatalog.Load(new TankReader(tank), "map_world");

            Assert.Equal("Kingdom of Ehb", info.ScreenName);
            Assert.Equal("The original single-player campaign.", info.Description);
            var w = Assert.Single(info.Worlds);
            Assert.Equal(("normal", "Regular", 0), (w.Name, w.ScreenName, w.RequiredLevel));

            Assert.Equal(new[] { "farmhouse", "stonebridge" }, info.Starts.Select(s => s.Group));
            var farm = info.Starts[0];
            Assert.True(farm.IsDefault);
            Assert.Equal(("Farmhouse", 0, "fh_r1"), (farm.ScreenName, farm.RequiredLevel("normal"), farm.Region));
            var sb = info.Starts[1];
            // The lowest-numbered position names the region (id 1 is 0xBC, in sb_r1).
            Assert.Equal(("Stonebridge", 3, "sb_r1"), (sb.ScreenName, sb.RequiredLevel("normal"), sb.Region));
        }
        finally { File.Delete(path); }
    }
}
