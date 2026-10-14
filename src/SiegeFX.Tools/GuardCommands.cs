using System.Diagnostics;
using SiegeFX.Core.Tank;
using SiegeFX.Guards;

/// <summary><c>siegefx guard</c> — the defensive checks tools/receipts.sh runs
/// so SiegeFX keeps respecting the original game's content: none of it in the
/// repository, none of its text retyped into the code, and the install never
/// changed by a run.</summary>
static class GuardCommands
{
    const string Usage =
        "usage: siegefx guard repo    [<repo>] [--allow=FILE]\n" +
        "           no game files, renamed game files or oversized files in git\n" +
        "           (allowlist default: <repo>/goldens/guard-repo.allow.txt)\n" +
        "       siegefx guard text    <install> [--repo=DIR] [--engine=DIR] [--baseline=FILE] [--write-baseline=FILE]\n" +
        "           no string in the built code or in goldens/ copies the game's text\n" +
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
            "text" when pos.Count == 1 => Text(pos[0], Opt("repo") ?? ".", Opt("engine"), Opt("baseline"), Opt("write-baseline")),
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

    static int Text(string install, string repo, string? engineDir, string? baselineFile, string? writeBaseline)
    {
        var tanks = new List<string>();
        foreach (var (dir, pattern) in new[] { ("Resources", "*.dsres"), ("Maps", "*.dsmap") })
        {
            var d = Path.Combine(install, dir);
            if (Directory.Exists(d)) tanks.AddRange(Directory.GetFiles(d, pattern).Order(StringComparer.OrdinalIgnoreCase));
        }
        if (tanks.Count == 0) { Console.Error.WriteLine($"no Resources/*.dsres or Maps/*.dsmap under {install}"); return 2; }

        int unreadable = 0;
        var texts = new List<(string, string)>();
        foreach (var t in tanks)
        {
            using var tank = TankFile.Open(t);
            texts.AddRange(GameTextIndex.FromTank(new TankReader(tank), Path.GetFileName(t), _ => unreadable++));
        }
        var index = GameTextIndex.Build(texts);

        engineDir ??= ParityLedger.FindEngineDir();
        if (engineDir is null) { Console.Error.WriteLine("no Release build of src/SiegeFX.Runtime to scan; build first"); return 2; }
        var assemblies = new[] { "SiegeFX.dll", "SiegeFX.Core.dll", "SiegeFX.Audio.dll" }.Select(f => Path.Combine(engineDir, f))
            .Concat(new[] { "siegefx.dll", "SiegeFX.OriginalSaves.dll" }.Select(f => Path.Combine(AppContext.BaseDirectory, f)))
            .Where(File.Exists).ToList();
        var strings = new List<(string Where, string Text)>();
        foreach (var asm in assemblies)
            strings.AddRange(AssemblyLiterals.Read(asm).Select(s => (Path.GetFileName(asm), s)));
        var goldens = Path.Combine(repo, "goldens");
        var baselinePath = baselineFile ?? writeBaseline;
        if (Directory.Exists(goldens))
            foreach (var f in Directory.EnumerateFiles(goldens, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                if (baselinePath is not null && Path.GetFullPath(f) == Path.GetFullPath(baselinePath)) continue;
                var rel = Path.GetRelativePath(repo, f).Replace('\\', '/');
                strings.AddRange(File.ReadLines(f).Select(l => (rel, l)));
            }

        var hits = TextGuard.Scan(index, strings);
        Console.WriteLine($"{index.TextCount:N0} game strings from {tanks.Count} tanks{(unreadable > 0 ? $" ({unreadable} GAS files unreadable)" : "")}; " +
                          $"{strings.Count:N0} strings from {assemblies.Count} assemblies and goldens/; {hits.Count} copy game text");
        if (writeBaseline is not null)
        {
            TextGuard.WriteBaseline(writeBaseline, hits);
            Console.WriteLine($"baseline written: {writeBaseline} ({hits.Count} entries)");
            return 0;
        }
        var baseline = baselineFile is null ? new HashSet<string>() : TextGuard.ReadBaseline(baselineFile);
        var (fresh, cleared) = TextGuard.Compare(hits, baseline);
        foreach (var h in fresh)
            Console.WriteLine($"  NEW  {h.Where}: \"{Clip(h.Literal)}\" copies \"{Clip(h.Match.GameText)}\" ({h.Match.Source})");
        foreach (var k in cleared) Console.WriteLine($"  gone from the code, can leave the baseline: {k}");
        Console.WriteLine(fresh.Count == 0
            ? $"no new copies of game text ({hits.Count - fresh.Count} recorded in the baseline)"
            : $"{fresh.Count} new copies of game text: read it from the game data instead of retyping it");
        return fresh.Count == 0 ? 0 : 1;

        static string Clip(string s) => s.Length <= 90 ? s : s[..87] + "...";
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
