using System;
using System.Diagnostics;
using System.IO;

namespace SiegeSmith.Services;

/// <summary>Finds and launches the Dungeon Siege executable from a detected install, for
/// test-playing a mod. DS1 automatically loads User-priority tanks it finds under Resources, so
/// "build &amp; install then launch" is the full test loop.</summary>
public static class GameLauncher
{
    private static readonly string[] KnownExeNames =
    {
        "DungeonSiege.exe", "Dungeon Siege.exe", "DSLOA.exe",
    };

    /// <summary>Returns the game executable path under <paramref name="installPath"/>, or null.</summary>
    public static string? FindExecutable(string installPath)
    {
        foreach (var name in KnownExeNames)
        {
            var p = Path.Combine(installPath, name);
            if (File.Exists(p)) return p;
        }
        // Fall back to any top-level .exe whose name mentions "siege".
        try
        {
            foreach (var exe in Directory.EnumerateFiles(installPath, "*.exe", SearchOption.TopDirectoryOnly))
                if (Path.GetFileNameWithoutExtension(exe).Contains("siege", StringComparison.OrdinalIgnoreCase))
                    return exe;
        }
        catch { /* unreadable install dir */ }
        return null;
    }

    public static void Launch(string exePath)
    {
        Process.Start(new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
        });
    }
}
