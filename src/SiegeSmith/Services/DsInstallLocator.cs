using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SiegeSmith.Services;

/// <summary>Locates a Dungeon Siege 1 installation and enumerates its resource tanks. Resolution
/// order: the <c>SIEGEFX_DS1</c> environment variable, then the registry (GOG game entries, the
/// retail Microsoft Games key, and Steam library folders), then well-known install paths. An
/// install is recognised by the presence of a <c>Resources</c> folder.</summary>
public static class DsInstallLocator
{
    /// <summary>Well-known install roots, tried last as a fallback.</summary>
    public static readonly string[] CommonPaths =
    {
        @"D:\GOG Games\Dungeon Siege",
        @"C:\GOG Games\Dungeon Siege",
        @"C:\Program Files (x86)\Steam\steamapps\common\Dungeon Siege 1",
        @"C:\Program Files (x86)\Steam\steamapps\common\Dungeon Siege",
        @"C:\Program Files (x86)\Microsoft Games\Dungeon Siege",
        @"C:\Program Files\Microsoft Games\Dungeon Siege",
    };

    /// <summary>Returns the first valid install path found, or null if none.</summary>
    public static string? Locate()
    {
        var env = Environment.GetEnvironmentVariable("SIEGEFX_DS1");
        if (IsInstall(env)) return env;

        foreach (var p in RegistryCandidates())
            if (IsInstall(p)) return p;

        foreach (var p in CommonPaths)
            if (IsInstall(p)) return p;

        return null;
    }

    /// <summary>True when <paramref name="path"/> looks like a DS1 install (has Resources).</summary>
    public static bool IsInstall(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Directory.Exists(Path.Combine(path, "Resources"));

    /// <summary>Install roots harvested from the registry: GOG game entries (matched by name),
    /// the retail Microsoft Games key, and Steam library folders. Every lookup is defensive —
    /// missing keys, permissions, and malformed values are swallowed.</summary>
    public static IEnumerable<string> RegistryCandidates()
    {
        var results = new List<string>();

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            RegistryKey hklm;
            try { hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view); }
            catch { continue; }
            using (hklm)
            {
                // GOG.com — enumerate installed games, match "Dungeon Siege" by name.
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
                                name.Contains("Dungeon Siege", StringComparison.OrdinalIgnoreCase))
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

        results.AddRange(SteamCandidates());
        return results;
    }

    /// <summary>Scans Steam library folders for a "Dungeon Siege*" common folder.</summary>
    private static IEnumerable<string> SteamCandidates()
    {
        var results = new List<string>();
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var steam = baseKey.OpenSubKey(@"SOFTWARE\Valve\Steam");
            if (steam?.GetValue("SteamPath") is not string steamPath || string.IsNullOrEmpty(steamPath))
                return results;

            var libraries = new List<string> { steamPath };
            var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
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
                foreach (var dir in Directory.EnumerateDirectories(common, "Dungeon Siege*"))
                    results.Add(dir);
            }
        }
        catch { /* Steam not installed / unreadable */ }
        return results;
    }

    /// <summary>Enumerates the tank files (.dsres / .dsmap / .dsmod) under an install's
    /// <c>Resources</c> and <c>Maps</c> folders, de-duplicated and sorted by name.</summary>
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
