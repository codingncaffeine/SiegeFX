using System;
using System.Collections.Generic;
using System.IO;

namespace SiegeSmith.Services;

/// <summary>Locates a Dungeon Siege 1 installation and enumerates its resource tanks,
/// mirroring the SiegeFX engine's resolution order: the <c>SIEGEFX_DS1</c> environment
/// variable first, then the common GOG / Steam / retail install locations. An install
/// is recognised by the presence of a <c>Resources</c> folder.</summary>
public static class DsInstallLocator
{
    /// <summary>Well-known install roots, tried in order after the env var.</summary>
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
        foreach (var p in CommonPaths)
            if (IsInstall(p)) return p;
        return null;
    }

    /// <summary>True when <paramref name="path"/> looks like a DS1 install (has Resources).</summary>
    public static bool IsInstall(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Directory.Exists(Path.Combine(path, "Resources"));

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
