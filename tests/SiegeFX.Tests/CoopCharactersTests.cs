using SiegeFX.Core.Save;

namespace SiegeFX.Tests;

public class CoopCharactersTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("siegefx-coop-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static SaveFile SinglePlayerSave() => new()
    {
        DisplayName = "Before the crypt",
        HeroName = "Ada",
        Difficulty = "Normal",
        Player = new PlayerSnapshot
        {
            Scid = 0x01C00001, Level = 7, TotalXp = 12345, Gold = 250, HeroName = "Ada",
            SkillXp = new() { 900, 10, 0, 300 },
            Variant = new HeroVariantSnapshot { Gender = "girl", FaceIdx = 2 },
            Inventory = { new LootEntrySnapshot { Slot = "equipped:weapon_hand", Reference = "dg_g_c_1h_avg" } },
            Quests = { new QuestSnapshot { Key = "q_farm" } },
            ConsumedInventoryScids = { 7u },
            SummonScid = 9, Downed = true, CameraYaw = 1.5f,
        },
    };

    [Fact]
    public void ANewCharacterIsListedAsNewWithItsLook()
    {
        var path = CoopCharacters.Create("Bram", new HeroVariantSnapshot { Gender = "girl" }, folder: _dir);
        var e = Assert.Single(CoopCharacters.List(_dir));
        Assert.Equal(path, e.Path);
        Assert.Equal("Bram", e.Name);
        Assert.True(e.New);
        Assert.Equal(0, e.Level);
        Assert.Equal("Farmer", e.ClassName);
        Assert.True(e.Girl);
    }

    [Fact]
    public void ImportCopiesTheHeroAndLeavesTheWorld()
    {
        var sp = SinglePlayerSave();
        var path = CoopCharacters.Import(sp, xp => xp >= 900 ? 5 : xp >= 300 ? 3 : 0, folder: _dir);
        var f = SaveStore.Load(path);
        var p = f.Player!;
        Assert.Equal(7, p.Level);
        Assert.Equal(250, p.Gold);
        Assert.Equal("dg_g_c_1h_avg", Assert.Single(p.Inventory).Reference);
        Assert.Equal("girl", p.Variant!.Gender);
        Assert.Empty(p.Quests);
        Assert.Empty(p.ConsumedInventoryScids);
        Assert.Equal(0u, p.SummonScid);
        Assert.False(p.Downed);
        Assert.Equal(0f, p.CameraYaw);
        Assert.Equal(0u, p.Scid);
        Assert.Equal((5, 0, 0, 3), (f.Coop!.Melee, f.Coop.Ranged, f.Coop.NatureMagic, f.Coop.CombatMagic));
        Assert.Equal("Before the crypt", f.Coop.ImportedFrom);
        var e = Assert.Single(CoopCharacters.List(_dir));
        Assert.False(e.New);
        Assert.Equal("Ada", e.Name);
        // The single-player save itself is untouched.
        Assert.Single(sp.Player!.Quests);
        Assert.Equal(0x01C00001u, sp.Player.Scid);
    }

    [Fact]
    public void ImportingTwiceKeepsBothAndNeverOverwrites()
    {
        var a = CoopCharacters.Import(SinglePlayerSave(), folder: _dir);
        var b = CoopCharacters.Import(SinglePlayerSave(), folder: _dir);
        Assert.NotEqual(a, b);
        Assert.Equal("Ada (2).save", Path.GetFileName(b));
        Assert.Equal(2, CoopCharacters.List(_dir).Count);
    }

    [Fact]
    public void FileNamesKeepOnlySafeCharacters()
    {
        Assert.Equal("Sir Bob.save", Path.GetFileName(CoopCharacters.UniquePath("Sir Bob:", _dir)));
        Assert.Equal("SirBob.save", Path.GetFileName(CoopCharacters.UniquePath("Sir/Bob", _dir)));
        Assert.Equal("Hero.save", Path.GetFileName(CoopCharacters.UniquePath("../..", _dir)));
        Assert.Equal("Hero.save", Path.GetFileName(CoopCharacters.UniquePath("", _dir)));
    }

    [Fact]
    public void TheChosenCharacterIsRememberedAndForgottenWhenDeleted()
    {
        var a = CoopCharacters.Create("A", null, folder: _dir);
        var b = CoopCharacters.Create("B", null, folder: _dir);
        Assert.Null(CoopCharacters.SelectedPath(_dir));
        CoopCharacters.Select(b, _dir);
        Assert.Equal(b, CoopCharacters.SelectedPath(_dir));
        File.WriteAllText(b + ".bak", "old");
        CoopCharacters.Delete(b, _dir);
        Assert.False(File.Exists(b));
        Assert.False(File.Exists(b + ".bak"));
        Assert.Null(CoopCharacters.SelectedPath(_dir));
        Assert.Equal(new[] { a }, CoopCharacters.List(_dir).Select(e => e.Path));
    }

    [Fact]
    public void AnUnreadableFileIsLeftOutAndLeftAlone()
    {
        CoopCharacters.Create("Good", null, folder: _dir);
        var bad = Path.Combine(_dir, "broken.save");
        File.WriteAllText(bad, "{ not json");
        Assert.Equal(new[] { "Good" }, CoopCharacters.List(_dir).Select(e => e.Name));
        Assert.Equal("{ not json", File.ReadAllText(bad));
    }

    [Fact]
    public void AMarkerNamingAPathOutsideTheFolderIsIgnored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, CoopCharacters.SelectedFileName), "../elsewhere.save");
        Assert.Null(CoopCharacters.SelectedPath(_dir));
    }
}
