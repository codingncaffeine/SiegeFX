using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SiegeFX.Core.IO;

/// <summary>Finds a Dungeon Siege 1 install, for the engine and the studio
/// alike. Candidates, in order: the <c>SIEGEFX_DS1</c> environment variable;
/// a <c>ds1path.txt</c> beside the program; a <c>ds1path.txt</c> in the
/// user's config folder (<c>~/.config/siegefx</c> on Linux,
/// <c>%APPDATA%\siegefx</c> on Windows); then where each platform's stores
/// and launchers put the game — on Windows the registry (GOG, the retail
/// Microsoft Games key, Steam) and well-known folders; on Linux the Steam
/// libraries (native, Flatpak, Snap), Heroic's GOG list and the usual Wine
/// prefixes (<c>~/.wine</c>, Lutris and umu prefixes under <c>~/Games</c>,
/// Bottles). <see cref="IsInstall"/> accepts a folder with a <c>Resources</c>
/// folder (the studio's rule); the engine walks <see cref="Candidates"/>
/// itself and also requires <c>Resources/Logic.dsres</c>.</summary>
public static class DsInstallLocator
{
    /// <summary>GOG's product id for Dungeon Siege (its goggame-1185868626.info).</summary>
    const string GogGameId = "1185868626";

    /// <summary>Install folders inside a Wine prefix's drive_c, as the
    /// Windows installers lay them out.</summary>
    static readonly string[] PrefixInstallFolders =
    {
        Path.Combine("GOG Games", "Dungeon Siege"),
        Path.Combine("Program Files (x86)", "GOG Galaxy", "Games", "Dungeon Siege"),
        Path.Combine("Program Files (x86)", "Steam", "steamapps", "common", "Dungeon Siege 1"),
        Path.Combine("Program Files (x86)", "Microsoft Games", "Dungeon Siege"),
        Path.Combine("Program Files", "Microsoft Games", "Dungeon Siege"),
    };

    /// <summary>Well-known Windows install roots, tried after the registry.</summary>
    static readonly string[] WindowsCommonPaths =
    {
        @"D:\GOG Games\Dungeon Siege",
        @"C:\GOG Games\Dungeon Siege",
        @"C:\Program Files (x86)\GOG Galaxy\Games\Dungeon Siege",
        @"C:\Program Files (x86)\Steam\steamapps\common\Dungeon Siege 1",
        @"C:\Program Files (x86)\Steam\steamapps\common\Dungeon Siege",
        @"C:\Program Files\Steam\steamapps\common\Dungeon Siege 1",
        @"C:\Program Files (x86)\Microsoft Games\Dungeon Siege",
        @"C:\Program Files\Microsoft Games\Dungeon Siege",
    };

    /// <summary>The per-user path file: <c>~/.config/siegefx/ds1path.txt</c>
    /// on Linux, <c>%APPDATA%\siegefx\ds1path.txt</c> on Windows. Its first
    /// non-empty, non-# line is the install (or its Resources) folder.</summary>
    public static string UserPathFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "siegefx", "ds1path.txt");

    /// <summary>The first candidate that is an install, or null.</summary>
    public static string? Locate()
    {
        foreach (var p in Candidates())
            if (IsInstall(p)) return p;
        return null;
    }

    /// <summary>True when <paramref name="path"/> looks like a DS1 install (has Resources).</summary>
    public static bool IsInstall(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Directory.Exists(Path.Combine(path, "Resources"));

    /// <summary>Every candidate folder in priority order, without duplicates.
    /// A candidate need not exist; callers test it (the engine also accepts a
    /// path that points at the Resources folder itself).</summary>
    public static IEnumerable<string> Candidates()
    {
        var seen = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var p in RawCandidates())
        {
            string key;
            try { key = Path.GetFullPath(p).TrimEnd('/', '\\'); }
            catch { continue; }
            if (seen.Add(key)) yield return p;
        }
    }

    static IEnumerable<string> RawCandidates()
    {
        var env = Environment.GetEnvironmentVariable("SIEGEFX_DS1");
        if (!string.IsNullOrWhiteSpace(env)) yield return env;
        if (ReadPathFile(Path.Combine(AppContext.BaseDirectory, "ds1path.txt")) is { } beside) yield return beside;
        if (ReadPathFile(UserPathFile) is { } user) yield return user;

        if (OperatingSystem.IsWindows())
        {
            foreach (var p in WindowsCandidates()) yield return p;
        }
        else if (OperatingSystem.IsLinux())
        {
            foreach (var p in LinuxCandidates()) yield return p;
        }
    }

    /// <summary>First non-empty, non-# line of a path file, quotes trimmed;
    /// null when the file is absent or unreadable.</summary>
    static string? ReadPathFile(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            return File.ReadLines(file)
                .Select(l => l.Trim().Trim('"'))
                .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
        }
        catch { return null; }
    }

    // ---- Windows -----------------------------------------------------------

    [SupportedOSPlatform("windows")]
    static IEnumerable<string> WindowsCandidates()
    {
        var results = new List<string>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            RegistryKey hklm;
            try { hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view); }
            catch { continue; }
            using (hklm)
            {
                // GOG.com — installed games, matched by name.
                try
                {
                    using var games = hklm.OpenSubKey(@"SOFTWARE\GOG.com\Games");
                    if (games is not null)
                    {
                        foreach (var id in games.GetSubKeyNames())
                        {
                            using var g = games.OpenSubKey(id);
                            var name = g?.GetValue("gameName") as string ?? "";
                            if (g?.GetValue("path") is string path && !string.IsNullOrEmpty(path) &&
                                (id == GogGameId || name.Contains("Dungeon Siege", StringComparison.OrdinalIgnoreCase)))
                                results.Add(path);
                        }
                    }
                }
                catch { /* ignore this hive/view */ }

                // Retail Microsoft Games installer.
                try
                {
                    using var ms = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft Games\Dungeon Siege\1.0");
                    if (ms?.GetValue("EXE Path") is string exe && !string.IsNullOrEmpty(exe))
                        results.Add(exe);
                }
                catch { /* ignore */ }
            }
        }

        try
        {
            using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var steam = hkcu.OpenSubKey(@"SOFTWARE\Valve\Steam");
            if (steam?.GetValue("SteamPath") is string steamPath && !string.IsNullOrEmpty(steamPath))
                results.AddRange(SteamLibraryGames(steamPath));
        }
        catch { /* Steam not installed / unreadable */ }

        results.AddRange(WindowsCommonPaths);
        return results;
    }

    // ---- Linux -------------------------------------------------------------

    static IEnumerable<string> LinuxCandidates()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) yield break;

        // Steam (native, Flatpak, Snap): every library's common/Dungeon Siege* folder.
        foreach (var root in new[]
                 {
                     ".local/share/Steam", ".steam/steam", ".steam/root",
                     ".var/app/com.valvesoftware.Steam/.local/share/Steam",
                     "snap/steam/common/.local/share/Steam",
                 })
            foreach (var p in SteamLibraryGames(Path.Combine(home, root)))
                yield return p;

        // Heroic's GOG installs (native and Flatpak).
        foreach (var cfg in new[] { ".config/heroic", ".var/app/com.heroicgameslauncher.hgl/config/heroic" })
            foreach (var p in HeroicGogInstalls(Path.Combine(home, cfg, "gog_store", "installed.json")))
                yield return p;

        // Wine prefixes: the default one, per-game prefixes under ~/Games
        // (Lutris, umu), Bottles' bottles — each with the installers' layouts.
        foreach (var prefix in WinePrefixes(home))
        {
            var driveC = Path.Combine(prefix, "drive_c");
            if (!Directory.Exists(driveC)) continue;
            foreach (var rel in PrefixInstallFolders)
                yield return Path.Combine(driveC, rel);
        }
    }

    static IEnumerable<string> WinePrefixes(string home)
    {
        yield return Path.Combine(home, ".wine");
        foreach (var games in new[] { Path.Combine(home, "Games"), Path.Combine(home, "Games", "gog") })
            foreach (var dir in SubDirectories(games))
            {
                yield return dir;                       // the game folder is the prefix (Lutris)
                yield return Path.Combine(dir, "pfx");  // Proton-style prefix (umu)
            }
        foreach (var bottles in new[] { ".local/share/bottles/bottles", ".var/app/com.usebottles.bottles/data/bottles/bottles" })
            foreach (var dir in SubDirectories(Path.Combine(home, bottles)))
                yield return dir;
    }

    /// <summary>Install folders Heroic recorded for this game: the entry whose
    /// appName is GOG's id, or whose install path names the game.</summary>
    static IEnumerable<string> HeroicGogInstalls(string installedJson)
    {
        var results = new List<string>();
        try
        {
            if (!File.Exists(installedJson)) return results;
            using var doc = JsonDocument.Parse(File.ReadAllText(installedJson));
            if (!doc.RootElement.TryGetProperty("installed", out var list) || list.ValueKind != JsonValueKind.Array)
                return results;
            foreach (var entry in list.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object ||
                    !entry.TryGetProperty("install_path", out var pathEl) ||
                    pathEl.GetString() is not { Length: > 0 } path)
                    continue;
                string appName = entry.TryGetProperty("appName", out var a) ? a.GetString() ?? "" : "";
                if (appName == GogGameId || path.Contains("Dungeon Siege", StringComparison.OrdinalIgnoreCase))
                    results.Add(path);
            }
        }
        catch { /* malformed or unreadable */ }
        return results;
    }

    // ---- shared ------------------------------------------------------------

    /// <summary>The steamapps/common/Dungeon Siege* folders of every library a
    /// Steam root lists in its libraryfolders.vdf (the root included).</summary>
    static IEnumerable<string> SteamLibraryGames(string steamRoot)
    {
        var results = new List<string>();
        try
        {
            if (!Directory.Exists(steamRoot)) return results;
            var libraries = new List<string> { steamRoot };
            var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (var line in File.ReadLines(vdf))
                {
                    var m = Regex.Match(line, "\"path\"\\s*\"([^\"]+)\"");
                    if (m.Success) libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                }
            }
            foreach (var lib in libraries)
            {
                var common = Path.Combine(lib, "steamapps", "common");
                if (!Directory.Exists(common)) continue;
                results.AddRange(Directory.EnumerateDirectories(common, "Dungeon Siege*"));
            }
        }
        catch { /* unreadable library */ }
        return results;
    }

    static IEnumerable<string> SubDirectories(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>The tank files (.dsres / .dsmap / .dsmod) under an install's
    /// Resources and Maps folders, de-duplicated and sorted by name.</summary>
    public static IReadOnlyList<string> FindTanks(string installPath)
    {
        var found = new List<string>();
        foreach (var sub in new[] { "Resources", "Maps" })
        {
            var dir = Path.Combine(installPath, sub);
            if (!Directory.Exists(dir)) continue;
            foreach (var pattern in new[] { "*.dsres", "*.dsmap", "*.dsmod" })
                found.AddRange(Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories));
        }
        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }
}
