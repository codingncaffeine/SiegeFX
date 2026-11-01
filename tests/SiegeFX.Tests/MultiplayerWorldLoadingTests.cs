using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeFX.Tests;

public class MultiplayerWorldLoadingTests
{
    static TankReader Tank(params string[] files)
    {
        var writer = new TankWriter { Priority = TankPriority.Factory };
        foreach (var f in files) writer.Add(f, "[a] { x = 1; }"u8.ToArray());
        var path = Path.Combine(Path.GetTempPath(), $"siegefx-mpworld-{Guid.NewGuid():N}.dsmap");
        writer.Write(path, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var reader = new TankReader(TankFile.Open(path));
        File.Delete(path);   // the open handle keeps the bytes readable
        return reader;
    }

    [Fact]
    public void FlatObjectsWinWhereARegionHasThem()
    {
        var tank = Tank("/world/maps/map_world/regions/fh_r1/objects/actor.gas",
                        "/world/maps/map_world/regions/fh_r1/objects/regular/actor.gas");
        Assert.Equal("/world/maps/map_world/regions/fh_r1/objects/",
            RegionObjects.ObjectsPrefix(tank, "/world/maps/map_world/regions/fh_r1"));
    }

    [Fact]
    public void MultiplayerWorldReadsTheRegularWorldLevel()
    {
        var tank = Tank("/world/maps/multiplayer_world/regions/town_center/objects/regular/actor.gas",
                        "/world/maps/multiplayer_world/regions/town_center/objects/veteran/actor.gas",
                        "/world/maps/multiplayer_world/regions/town_center/objects/elite/actor.gas");
        Assert.Equal("/world/maps/multiplayer_world/regions/town_center/objects/regular/",
            RegionObjects.ObjectsPrefix(tank, "/world/maps/multiplayer_world/regions/town_center/"));
    }

    [Fact]
    public void ARegionWithNoObjectsKeepsTheFlatPrefix()
    {
        var tank = Tank("/world/maps/multiplayer_world/regions/empty/terrain_nodes/nodes.gas");
        Assert.Equal("/world/maps/multiplayer_world/regions/empty/objects/",
            RegionObjects.ObjectsPrefix(tank, "/world/maps/multiplayer_world/regions/empty"));
    }

    [Fact]
    public void MultiplayerWorldMoodsAreNamedAfterTheFullMapFolder()
    {
        var moods = new Dictionary<string, MoodSetting>(StringComparer.OrdinalIgnoreCase)
        {
            ["multiplayer_world_town_center_1"] = new() { Name = "multiplayer_world_town_center_1" },
            ["map_world_fh_r1_1"] = new() { Name = "map_world_fh_r1_1" },
        };
        Assert.Equal("multiplayer_world_town_center_1",
            MoodStore.FindRegionDefault(moods, "multiplayer_world", "town_center")?.Name);
        Assert.Equal("map_world_fh_r1_1", MoodStore.FindRegionDefault(moods, "world", "fh_r1")?.Name);
    }
}
