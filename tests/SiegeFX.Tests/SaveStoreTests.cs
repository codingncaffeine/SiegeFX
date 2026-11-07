using SiegeFX.Core.Save;

namespace SiegeFX.Tests;

public class SaveStoreTests
{
    [Fact]
    public void SerializeRoundTripsAndStampsTheCurrentSchema()
    {
        var bytes = SaveStore.Serialize(new SaveFile());
        var back = SaveStore.Deserialize(bytes);

        Assert.NotNull(back);
        Assert.Equal(SaveFile.CurrentSchemaVersion, back.SchemaVersion);
        Assert.Equal(bytes, SaveStore.Serialize(back));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"SchemaVersion\": \"thirteen\"}")]
    public void DeserializeReturnsNullOnGarbage(string text)
    {
        // The MP late-join path feeds network bytes straight in; it must never throw.
        Assert.Null(SaveStore.Deserialize(System.Text.Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void TheCoopCharacterStaysOutOfTheLoadGameList()
    {
        // The co-op character is a hero without a world: loading it as a game
        // would be wrong, so it lives one folder down from the saves.
        Assert.Equal(SaveStore.DefaultSaveDirectory(),
                     Path.GetDirectoryName(Path.GetDirectoryName(SaveStore.CoopCharacterPath())));
        var dir = Directory.CreateTempSubdirectory("siegefx-saves-").FullName;
        try
        {
            SaveStore.Save(Path.Combine(dir, "game.save"), new SaveFile { DisplayName = "A game" });
            SaveStore.Save(Path.Combine(dir, "Multiplayer", "character.save"), new SaveFile { DisplayName = "Co-op character: X" });
            var slots = SaveStore.ListSaves(dir);
            Assert.Equal(new[] { "A game" }, slots.Select(s => s.DisplayName));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ReplacingASaveCanKeepTheOldOne()
    {
        var dir = Directory.CreateTempSubdirectory("siegefx-saves-").FullName;
        try
        {
            string path = Path.Combine(dir, "character.save"), bak = path + ".bak";
            SaveStore.Save(path, new SaveFile { HeroName = "first" }, backupPath: bak);
            Assert.False(File.Exists(bak));   // nothing replaced yet
            SaveStore.Save(path, new SaveFile { HeroName = "second" }, backupPath: bak);
            Assert.Equal("second", SaveStore.Load(path).HeroName);
            Assert.Equal("first", SaveStore.Load(bak).HeroName);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
