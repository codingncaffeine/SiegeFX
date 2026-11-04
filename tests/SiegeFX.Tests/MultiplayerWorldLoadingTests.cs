using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeFX.Tests;

public class MultiplayerWorldLoadingTests
{
    // Writes a throwaway tank holding the files, asks the engine for the region's objects prefix, then deletes it.
    // The handle is closed before the delete: Windows refuses to delete a file that is still open.
    static string ObjectsPrefix(string region, params string[] files)
    {
        var writer = new TankWriter { Priority = TankPriority.Factory };
        foreach (var f in files) writer.Add(f, "[a] { x = 1; }"u8.ToArray());
        var path = Path.Combine(Path.GetTempPath(), $"siegefx-mpworld-{Guid.NewGuid():N}.dsmap");
        try
        {
            writer.Write(path, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            using var tank = TankFile.Open(path);
            return RegionObjects.ObjectsPrefix(new TankReader(tank), region);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FlatObjectsWinWhereARegionHasThem()
    {
        Assert.Equal("/world/maps/map_world/regions/fh_r1/objects/",
            ObjectsPrefix("/world/maps/map_world/regions/fh_r1",
                "/world/maps/map_world/regions/fh_r1/objects/actor.gas",
                "/world/maps/map_world/regions/fh_r1/objects/regular/actor.gas"));
    }

    [Fact]
    public void MultiplayerWorldReadsTheRegularWorldLevel()
    {
        Assert.Equal("/world/maps/multiplayer_world/regions/town_center/objects/regular/",
            ObjectsPrefix("/world/maps/multiplayer_world/regions/town_center/",
                "/world/maps/multiplayer_world/regions/town_center/objects/regular/actor.gas",
                "/world/maps/multiplayer_world/regions/town_center/objects/veteran/actor.gas",
                "/world/maps/multiplayer_world/regions/town_center/objects/elite/actor.gas"));
    }

    [Fact]
    public void ARegionWithNoObjectsKeepsTheFlatPrefix()
    {
        Assert.Equal("/world/maps/multiplayer_world/regions/empty/objects/",
            ObjectsPrefix("/world/maps/multiplayer_world/regions/empty",
                "/world/maps/multiplayer_world/regions/empty/terrain_nodes/nodes.gas"));
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
