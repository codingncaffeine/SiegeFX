using System.Text;
using SiegeFX.Core.Tank;
using SiegeFX.Guards;

namespace SiegeFX.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"siegefx-guard-{Guid.NewGuid():N}");
    public TempDir() => Directory.CreateDirectory(Path);
    public string Write(string rel, byte[] bytes)
    {
        var full = System.IO.Path.Combine(Path, rel);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return full;
    }
    public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch (IOException) { } }
}

public class RepoContentGuardTests
{
    [Fact]
    public void Game_formats_renamed_game_files_and_big_files_are_found()
    {
        using var d = new TempDir();
        d.Write("src/Ok.cs", "class Ok { }"u8.ToArray());
        d.Write("art/logo.png", new byte[1000]);
        d.Write("data/Logic.dsres", new byte[10]);
        d.Write("data/fh_r1.gas", "[a] { x = 1; }"u8.ToArray());
        d.Write("data/innocent.bin", [.. "DSigTank"u8, 0, 0, 0, 0]);
        d.Write("data/texture.dat", [.. "ipaR8888"u8]);
        d.Write("data/huge.bin", new byte[RepoContentGuard.MaxFileBytes + 1]);
        string[] paths = ["src/Ok.cs", "art/logo.png", "data/Logic.dsres", "data/fh_r1.gas", "data/innocent.bin", "data/texture.dat", "data/huge.bin", "gone.cs"];

        var found = RepoContentGuard.Check(d.Path, paths, new HashSet<string>()).ToDictionary(f => f.Path, f => f.Reason);

        Assert.Equal(["data/Logic.dsres", "data/fh_r1.gas", "data/huge.bin", "data/innocent.bin", "data/texture.dat"], found.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("tank", found["data/innocent.bin"]);
        Assert.Contains("signature", found["data/texture.dat"]);
        Assert.Contains("MB", found["data/huge.bin"]);
    }

    [Fact]
    public void Allowlisted_paths_pass_and_the_list_ignores_comments()
    {
        using var d = new TempDir();
        d.Write("mods/example.gas", "[a] { x = 1; }"u8.ToArray());
        var allowFile = d.Write("allow.txt", "# original work\n\nmods\\example.gas\n"u8.ToArray());

        var allow = RepoContentGuard.ReadAllowList(allowFile);
        Assert.Equal(["mods/example.gas"], allow);
        Assert.Empty(RepoContentGuard.Check(d.Path, ["mods/example.gas"], allow));
        Assert.Empty(RepoContentGuard.ReadAllowList(System.IO.Path.Combine(d.Path, "missing.txt")));
    }
}

public class GameTextIndexTests
{
    static readonly GameTextIndex Index = GameTextIndex.Build([
        ("Logic.dsres:/world/contentdb/quests/quests.gas", "Clear Edgaar's Basement"),
        ("World.dsmap:/conversations/edgaar.gas", "Krug have taken over my basement! Please, clear them out before they eat my turnips."),
        ("Logic.dsres:/ui/x.gas", "OK"),
        ("Logic.dsres:/spl_spell.gas", "Robo-Suit Minigun"),
        ("Logic.dsres:/paths.gas", "art/bitmaps/gui/in_game/inventory/backpack_open"),
        ("Logic.dsres:/ui/user_education_tooltips.gas", "Load a saved game"),
        ("MpWorld.dsmap:/conversations.gas", "Hail, friend. Are you sure you want to be exploring the Great Northern Forest?"),
        ("Logic.dsres:/config/input_bindings.gas", "Camera: Zoom Out"),
        ("Logic.dsres:/config/input_bindings.gas", "Chat Window (Send to Team)"),
        ("Logic.dsres:/ui/user_education_tooltips.gas", "Drag ring here to equip"),
    ]);

    [Theory]
    [InlineData("Krug have taken over my basement!")]                       // five words of a sentence
    [InlineData("Quest: KRUG  have taken, over my basement -- go!")]        // case and punctuation ignored
    [InlineData("clear edgaar's basement")]                                 // a whole short title
    [InlineData("Started: Clear Edgaar's Basement")]                        // ...inside a longer string
    [InlineData("Equipped the Robo-Suit Minigun")]                            // the spell's title, typed out
    [InlineData("Camera: Zoom Out")]                                          // a whole short string, however short its words
    [InlineData("Chat Window (Send to Team)")]                                // a whole five-word string with plain words
    [InlineData("Drag ring here to equip")]
    public void Copies_are_found(string s) => Assert.NotNull(Index.Match(s));

    [Theory]
    [InlineData("Krug have taken over")]          // four words of a long sentence: too common to judge
    [InlineData("clear the basement")]             // not the title's words in order
    [InlineData("Edgaar's Basement")]              // part of a short string
    [InlineData("OK")]                             // game strings under three words are not indexed
    [InlineData("SaveStore wrote {0} bytes to {1}")]
    [InlineData("# sfx timeline  script=robo_suit_minigun  ticks=40  dt=0.050  seed=1")]   // an identifier naming the spell
    [InlineData("ARE YOU SURE YOU WANT TO EXIT TO THE MAIN MENU WITHOUT SAVING?")]           // common phrasing shared with a conversation
    [InlineData("Do you want to load a saved game?")]                                       // a short, plain tooltip inside other words
    [InlineData("Press to zoom out the camera")]                                            // the short string's words, not in order
    [InlineData("Krug_have_taken_over_my_basement")]                                       // an identifier, no whitespace
    [InlineData("loading art/bitmaps/gui/in_game/inventory/backpack_open now")]             // a path: no whitespace in the game value
    public void Everything_else_passes(string s) => Assert.Null(Index.Match(s));

    [Fact]
    public void A_match_names_its_game_text_and_source()
    {
        var m = Index.Match("please clear them out before they eat")!;
        Assert.Equal("World.dsmap:/conversations/edgaar.gas", m.Source);
        Assert.StartsWith("Krug have taken", m.GameText);
    }

    [Fact]
    public void Quoted_gas_values_are_read_from_a_tank()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"siegefx-{Guid.NewGuid():N}.dsres");
        var w = new TankWriter();
        w.Add("/world/contentdb/quests/quests.gas", Encoding.ASCII.GetBytes(
            "[quests] { [q1] { screen_name = \"Clear Edgaar's Basement\"; order = 1; [step] { text = \"Find the basement door\"; } } }"));
        w.Add("/art/x.raw", [1, 2, 3]);
        w.Add("/world/broken.gas", Encoding.ASCII.GetBytes("[a] { x = 1"));
        try
        {
            w.Write(path, DateTime.UtcNow);
            using var tank = TankFile.Open(path);
            var bad = new List<string>();
            var texts = GameTextIndex.FromTank(new TankReader(tank), "Logic.dsres", bad.Add).ToList();
            Assert.Equal(["Clear Edgaar's Basement", "Find the basement door"], texts.Select(t => t.Text).Order(StringComparer.Ordinal));
            Assert.All(texts, t => Assert.Equal("Logic.dsres:/world/contentdb/quests/quests.gas", t.Source));
            Assert.Equal(["/world/broken.gas"], bad);
        }
        finally { File.Delete(path); }
    }
}

public class TextGuardTests
{
    const string Planted = "Krug have taken over my basement in the night";

    [Fact]
    public void Literals_are_read_from_a_built_assembly()
    {
        Assert.Contains(Planted, AssemblyLiterals.Read(typeof(TextGuardTests).Assembly.Location));
    }

    [Fact]
    public void Baseline_keeps_old_hits_passing_fails_new_ones_and_stores_no_text()
    {
        var index = GameTextIndex.Build([("World.dsmap:/c.gas", "Krug have taken over my basement! Please help.")]);
        var hits = TextGuard.Scan(index, [("SiegeFX.dll", Planted), ("SiegeFX.dll", "nothing to see"), ("SiegeFX.dll", Planted)]);
        Assert.Single(hits);

        using var d = new TempDir();
        var file = System.IO.Path.Combine(d.Path, "baseline.txt");
        TextGuard.WriteBaseline(file, hits);
        Assert.DoesNotContain("basement", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);

        var baseline = TextGuard.ReadBaseline(file);
        Assert.Empty(TextGuard.Compare(hits, baseline).New);

        var later = TextGuard.Scan(index, [("Core.dll", "they said krug have taken over my basement")]);
        var (fresh, cleared) = TextGuard.Compare(later, baseline);
        Assert.Equal("Core.dll", Assert.Single(fresh).Where);
        Assert.Single(cleared);
    }
}

public class InstallSnapshotTests
{
    [Fact]
    public void Changes_added_and_removed_files_are_reported()
    {
        using var d = new TempDir();
        d.Write("Resources/Logic.dsres", [1, 2, 3]);
        var kept = d.Write("Maps/World.dsmap", [4, 5]);
        var gone = d.Write("DungeonSiege.ini", [6]);
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"siegefx-snap-{Guid.NewGuid():N}.txt");
        try
        {
            InstallSnapshot.Take(d.Path).Write(file);
            Assert.Empty(InstallSnapshot.Take(d.Path).ChangesSince(InstallSnapshot.Read(file)));

            File.AppendAllText(kept, "x");
            File.Delete(gone);
            d.Write("Resources/siegefx.log", [7]);
            // Added and changed files in path order, then removed ones.
            Assert.Equal(["changed  Maps/World.dsmap", "added    Resources/siegefx.log", "removed  DungeonSiege.ini"],
                         InstallSnapshot.Take(d.Path).ChangesSince(InstallSnapshot.Read(file)));
        }
        finally { File.Delete(file); }
    }
}
