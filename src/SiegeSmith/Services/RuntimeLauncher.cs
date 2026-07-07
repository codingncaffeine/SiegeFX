using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SiegeSmith.Services;

/// <summary>Locates and launches the clean-room <c>SiegeFX.Runtime</c> engine against a map produced
/// by SiegeSmith. Distinct from <see cref="GameLauncher"/> (which starts the retail Dungeon Siege exe):
/// the engine loads a specific region only through its positional CLI verbs, so this builds the
/// <c>--region</c> / <c>--play-region</c> argv and starts it. Region selection is CLI-only — no env
/// var or in-game menu can reach a custom map.</summary>
public static class RuntimeLauncher
{
    /// <summary>Finds the built SiegeFX.Runtime by walking up from this app's directory to the
    /// sibling <c>SiegeFX.Runtime/bin/&lt;cfg&gt;/net11.0/</c> output. Returns the .exe if present, else
    /// the .dll (run via <c>dotnet</c>), else null.</summary>
    public static string? FindRuntime()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var bin = Path.Combine(dir.FullName, "SiegeFX.Runtime", "bin");
            if (!Directory.Exists(bin)) continue;
            foreach (var cfg in new[] { "Release", "Debug" })
            {
                var baseName = Path.Combine(bin, cfg, "net11.0", "SiegeFX.Runtime");
                if (File.Exists(baseName + ".exe")) return baseName + ".exe";
                if (File.Exists(baseName + ".dll")) return baseName + ".dll";
            }
        }
        return null;
    }

    /// <summary>Launches the terrain-only view of a region (always works from nodes.gas alone).</summary>
    public static Process LaunchRegion(string runtime, string mapTank, string terrainTank, string regionPath, bool noVideo = true)
    {
        var args = new List<string> { "--region", mapTank, terrainTank, regionPath };
        if (noVideo) args.Add("--noVideo");
        return Start(runtime, args);
    }

    /// <summary>Launches the full playable scene (needs a seed actor + start position in the map).
    /// When <paramref name="onEarlyExit"/> is set, the engine's stderr is captured and — if the process
    /// exits non-zero (a load failure rather than the user closing the window) — reported with its tail,
    /// so the test loop surfaces engine errors instead of failing silently.</summary>
    public static Process LaunchPlayRegion(string runtime, string mapTank, string terrainTank,
        string logicTank, string objectsTank, string regionPath, bool noVideo = true,
        Action<int, string>? onEarlyExit = null)
    {
        var args = new List<string> { "--play-region", mapTank, terrainTank, logicTank, objectsTank, regionPath };
        if (noVideo) args.Add("--noVideo");
        return Start(runtime, args, onEarlyExit);
    }

    private static Process Start(string runtime, List<string> args, Action<int, string>? onEarlyExit = null)
    {
        var isDll = runtime.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var psi = new ProcessStartInfo(isDll ? "dotnet" : runtime)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(runtime) ?? Environment.CurrentDirectory,
            RedirectStandardError = onEarlyExit is not null,
        };
        if (isDll) psi.ArgumentList.Add(runtime);
        foreach (var a in args) psi.ArgumentList.Add(a); // ArgumentList quotes each arg, so paths with spaces are safe

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = onEarlyExit is not null };
        if (onEarlyExit is not null)
        {
            var err = new System.Text.StringBuilder();
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) err.AppendLine(e.Data); };
            proc.Exited += (_, _) =>
            {
                if (proc.ExitCode != 0)
                {
                    var text = err.ToString();
                    var tail = text.Length > 1200 ? text[^1200..] : text;
                    onEarlyExit(proc.ExitCode, tail.Trim());
                }
            };
        }
        if (!proc.Start()) throw new InvalidOperationException("Failed to start SiegeFX.Runtime.");
        if (onEarlyExit is not null) proc.BeginErrorReadLine();
        return proc;
    }
}
