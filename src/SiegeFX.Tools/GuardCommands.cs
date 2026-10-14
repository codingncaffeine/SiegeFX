using System.Diagnostics;
using SiegeFX.Guards;

/// <summary><c>siegefx guard</c> — the defensive checks tools/receipts.sh runs
/// so SiegeFX keeps respecting the original game's content: none of it in the
/// repository, and the install never changed by a run.</summary>
static class GuardCommands
{
    const string Usage =
        "usage: siegefx guard repo    [<repo>] [--allow=FILE]\n" +
        "           no game files, renamed game files or oversized files in git\n" +
        "           (allowlist default: <repo>/goldens/guard-repo.allow.txt)\n" +
        "       siegefx guard install <install> --snapshot=FILE | --compare=FILE\n" +
        "           record the install's files, or fail when any changed since";

    public static int Dispatch(string[] a)
    {
        if (a.Length < 1) { Console.Error.WriteLine(Usage); return 2; }
        var pos = a.Skip(1).Where(x => !x.StartsWith("--", StringComparison.Ordinal)).ToList();
        string? Opt(string name) => a.Skip(1).FirstOrDefault(o => o.StartsWith($"--{name}=", StringComparison.Ordinal))?[(name.Length + 3)..];
        return a[0] switch
        {
            "repo" => Repo(pos.FirstOrDefault() ?? ".", Opt("allow")),
            "install" when pos.Count == 1 && (Opt("snapshot") is not null ^ Opt("compare") is not null) =>
                Install(pos[0], Opt("snapshot"), Opt("compare")),
            _ => Fail(),
        };
        static int Fail() { Console.Error.WriteLine(Usage); return 2; }
    }

    static int Repo(string repo, string? allowFile)
    {
        allowFile ??= Path.Combine(repo, "goldens", "guard-repo.allow.txt");
        var paths = GitTrackedFiles(repo);
        if (paths is null) return 2;
        var findings = RepoContentGuard.Check(repo, paths, RepoContentGuard.ReadAllowList(allowFile));
        foreach (var f in findings) Console.WriteLine($"  {f.Path}: {f.Reason}");
        Console.WriteLine(findings.Count == 0
            ? $"{paths.Count} tracked files, none of them game content"
            : $"{findings.Count} of {paths.Count} tracked files look like game content or assets. SiegeFX ships none " +
              $"of the original game; remove them, or list a file that is original work in {allowFile}.");
        return findings.Count == 0 ? 0 : 1;
    }

    static List<string>? GitTrackedFiles(string repo)
    {
        try
        {
            var psi = new ProcessStartInfo("git", ["-C", repo, "ls-files", "-z"]) { RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) { Console.Error.WriteLine($"git ls-files failed in {repo}: {p.StandardError.ReadToEnd().Trim()}"); return null; }
            return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        catch (System.ComponentModel.Win32Exception) { Console.Error.WriteLine("guard repo needs git on PATH"); return null; }
    }

    static int Install(string install, string? snapshotFile, string? compareFile)
    {
        if (!Directory.Exists(install)) { Console.Error.WriteLine($"no install at {install}"); return 2; }
        var now = InstallSnapshot.Take(install);
        if (snapshotFile is not null)
        {
            now.Write(snapshotFile);
            Console.WriteLine($"{now.Files.Count} install files recorded in {snapshotFile}");
            return 0;
        }
        var changes = now.ChangesSince(InstallSnapshot.Read(compareFile!));
        foreach (var c in changes) Console.WriteLine($"  {c}");
        Console.WriteLine(changes.Count == 0
            ? $"{now.Files.Count} install files, none changed"
            : $"{changes.Count} install files changed during the run: SiegeFX must only read the original game's files");
        return changes.Count == 0 ? 0 : 1;
    }
}
