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
