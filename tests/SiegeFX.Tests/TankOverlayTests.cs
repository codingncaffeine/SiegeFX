using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeFX.Tests;

// TankOverlays is process-wide, so these run alone: any TankReader another
// test builds meanwhile would pick the overlay up.
[CollectionDefinition("tank-overlays", DisableParallelization = true)]
public class TankOverlayCollection { }

[Collection("tank-overlays")]
public class TankOverlayTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"siegefx-overlay-{Guid.NewGuid():N}");

    public TankOverlayTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        TankOverlays.Clear();
        Directory.Delete(_dir, recursive: true);
    }

    string Write(string name, TankPriority priority, Dictionary<string, byte[]> files)
    {
        var writer = new TankWriter { Priority = priority };
        foreach (var (path, bytes) in files) writer.Add(path, bytes);
        var path0 = Path.Combine(_dir, name);
        writer.Write(path0, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        return path0;
    }

    [Fact]
    public void UserTankShadowsFactoryFilesAndJoinsOnlyUnderItsOwnDirectories()
    {
        var logic = Write("Logic.dsres", TankPriority.Factory, new()
        {
            ["/world/contentdb/pcontent.gas"] = "english"u8.ToArray(),
            ["/world/contentdb/templates/a.gas"] = "a"u8.ToArray(),
        });
        var map = Write("World.dsmap", TankPriority.Factory, new()
        {
            ["/world/maps/map_world/regions/fh_r1/main.gas"] = "m"u8.ToArray(),
        });
        Write("Czech.dsres", TankPriority.User, new()
        {
            ["/world/contentdb/pcontent.gas"] = "cesky"u8.ToArray(),
            ["/world/contentdb/templates/new.gas"] = "n"u8.ToArray(),
            ["/world/maps/map_world/regions/fh_r1/conversations/conversations.gas"] = "c"u8.ToArray(),
        });

        var found = TankOverlays.Find(_dir);
        Assert.Equal("Czech.dsres", Path.GetFileName(Assert.Single(found).Path));
        TankOverlays.Configure(found.Select(f => f.Path));

        using var logicTank = TankFile.Open(logic);
        var logicReader = new TankReader(logicTank);
        Assert.Equal("cesky"u8.ToArray(), logicReader.ExtractToMemory("/world/contentdb/pcontent.gas"));
        Assert.Equal("a"u8.ToArray(), logicReader.ExtractToMemory("/world/contentdb/templates/a.gas"));
        Assert.True(logicReader.TryGetFile("/world/contentdb/templates/new.gas", out var added));
        Assert.Equal("n"u8.ToArray(), logicReader.ExtractEntry(added));
        Assert.False(logicReader.TryGetFile("/world/maps/map_world/regions/fh_r1/conversations/conversations.gas", out _));

        using var mapTank = TankFile.Open(map);
        var mapReader = new TankReader(mapTank);
        Assert.Equal("c"u8.ToArray(),
            mapReader.ExtractToMemory("/world/maps/map_world/regions/fh_r1/conversations/conversations.gas"));
        Assert.Contains("/world/maps/map_world/regions/fh_r1/conversations/", mapReader.ListDirectories());
        Assert.False(mapReader.TryGetFile("/world/contentdb/pcontent.gas", out _));
    }

    [Fact]
    public void NoOverlayConfiguredReadsTheFactoryTank()
    {
        var logic = Write("Logic.dsres", TankPriority.Factory, new()
        {
            ["/world/contentdb/pcontent.gas"] = "english"u8.ToArray(),
        });
        using var tank = TankFile.Open(logic);
        var reader = new TankReader(tank);
        Assert.Equal("english"u8.ToArray(), reader.ExtractToMemory("/world/contentdb/pcontent.gas"));
        Assert.Equal(0, reader.OverlaidFileCount);
    }

    [Fact]
    public void CodePageTextKeepsItsByteValues()
    {
        // "Říše" in cp1250: the font draws glyph 0xD8 for Ř.
        byte[] cp1250 = [0xD8, 0xED, 0x9A, 0x65];
        Assert.Equal("Øí\u009Ae", GasDocument.DecodeText(cp1250));
        Assert.Equal("Říše", GasDocument.DecodeText("Říše"u8));
    }
}
