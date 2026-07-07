using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SiegeFX.Core.Tank;

namespace SiegeSmith.Services;

/// <summary>Stages a World Builder region into the exact in-tank tree SiegeFX loads and packs it into
/// a startable <c>.dsmap</c>. The engine needs just one authored file —
/// <c>world/maps/map_&lt;map&gt;/regions/&lt;region&gt;/terrain_nodes/nodes.gas</c> — and rebuilds discovery,
/// layout, nav and the mesh-guid index at runtime from that plus the stock Terrain tank.</summary>
public static class MapPackager
{
    public readonly record struct Packaged(string MapTankPath, string RegionPath, string MapName, string RegionName);

    /// <summary>The PC drop point: a node-local X,Z (Y is nav-snapped at load) anchored to a real
    /// snode <paramref name="NodeGuid"/> in the region.</summary>
    public readonly record struct StartInfo(uint NodeGuid, float X, float Z, string Group, string ScreenName);

    /// <summary>One seed actor so LoadPlayActors runs the PC spawn (it skips it when no actor spawns).
    /// <paramref name="Template"/> must be a stock template that resolves in Logic/Objects.</summary>
    public readonly record struct SeedActor(string Template, uint Scid, uint NodeGuid, float X, float Z);

    /// <summary>Writes <paramref name="nodesGas"/> under the required path tree and packs it into
    /// <c>map_&lt;map&gt;.dsmap</c> in <paramref name="outputDir"/>. Returns the tank path and the in-tank
    /// region path to hand to <see cref="RuntimeLauncher"/>. The map dir is always prefixed <c>map_</c>
    /// so the engine's ambient-audio gate (DeriveMapName) stays enabled.</summary>
    public static Packaged PackStartableMap(string nodesGas, string mapName, string regionName, string outputDir,
        StartInfo? start = null, SeedActor? actor = null, string? assetsRoot = null)
    {
        string map = "map_" + Sanitize(mapName, "custom");
        string region = Sanitize(regionName, "region_r1");
        string regionPath = $"/world/maps/{map}/regions/{region}";
        string mapDir = Path.Combine(Path.GetTempPath(), "SiegeSmith", "staging", map, "world", "maps", map);

        string staging = Path.Combine(Path.GetTempPath(), "SiegeSmith", "staging", map);
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        // Overlay the user's custom-asset tree FIRST (it mirrors tank layout, e.g. art/terrain/...), then
        // write our authored files on top so a collision always resolves in the region's favour.
        if (!string.IsNullOrWhiteSpace(assetsRoot) && Directory.Exists(assetsRoot))
            CopyTree(assetsRoot!, staging);

        string nodesDir = Path.Combine(mapDir, "regions", region, "terrain_nodes");
        Directory.CreateDirectory(nodesDir);
        File.WriteAllText(Path.Combine(nodesDir, "nodes.gas"), nodesGas);

        if (start is { NodeGuid: not 0 } s)
        {
            string infoDir = Path.Combine(mapDir, "info");
            Directory.CreateDirectory(infoDir);
            File.WriteAllText(Path.Combine(infoDir, "start_positions.gas"), BuildStartPositions(s));
        }
        if (actor is { NodeGuid: not 0 } a && !string.IsNullOrWhiteSpace(a.Template))
        {
            string objDir = Path.Combine(mapDir, "regions", region, "objects");
            Directory.CreateDirectory(objDir);
            File.WriteAllText(Path.Combine(objDir, "actor.gas"), BuildActor(a));
        }

        Directory.CreateDirectory(outputDir);
        string mapTank = Path.Combine(outputDir, map + ".dsmap");
        TankBuilder.BuildFromFolder(staging, mapTank,
            title: map, author: "SiegeSmith", description: $"Custom map {map}",
            priority: TankPriority.User, utcBuildTime: DateTime.UtcNow);

        return new Packaged(mapTank, regionPath, map, region);
    }

    /// <summary>Emits the map-scoped start_positions.gas the engine reads (StartPositionsStore):
    /// one default start_group with a single start_position anchored to a real snode guid.</summary>
    private static string BuildStartPositions(StartInfo s)
    {
        string grp = string.IsNullOrWhiteSpace(s.Group) ? "default" : s.Group;
        string name = string.IsNullOrWhiteSpace(s.ScreenName) ? "Custom Start" : s.ScreenName;
        return
            "[start_positions]\r\n{\r\n" +
            $"\t[t:start_group,n:{grp}]\r\n\t{{\r\n" +
            "\t\tdefault = true;\r\n" +
            $"\t\tscreen_name = \"{name}\";\r\n" +
            "\t\t[start_position]\r\n\t\t{\r\n" +
            $"\t\t\tid = 1;\r\n\t\t\tposition = {F(s.X)},0,{F(s.Z)},0x{s.NodeGuid:X8};\r\n" +
            "\t\t}\r\n\t}\r\n}\r\n";
    }

    /// <summary>Emits an objects/actor.gas with one stock actor placement so LoadPlayActors runs the
    /// PC spawn (which it skips when zero actors spawn). Anchored to the start node.</summary>
    private static string BuildActor(SeedActor a)
    {
        return
            $"[t:{a.Template},n:0x{a.Scid:X8}]\r\n{{\r\n" +
            "\t[placement]\r\n\t{\r\n" +
            $"\t\tposition = {F(a.X)},0,{F(a.Z)},0x{a.NodeGuid:X8};\r\n" +
            "\t\torientation = 0,0,0,1;\r\n" +
            "\t}\r\n}\r\n";
    }

    private static string F(float v) => v.ToString("0.0######", CultureInfo.InvariantCulture);

    /// <summary>Mirror-copies a folder tree into <paramref name="dstRoot"/>, preserving relative paths.
    /// Used to overlay a custom-asset folder (laid out like a tank: art/…, world/…) into the map.</summary>
    private static void CopyTree(string src, string dstRoot)
    {
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var dst = Path.Combine(dstRoot, Path.GetRelativePath(src, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(file, dst, overwrite: true);
        }
    }

    /// <summary>Counts files under a would-be assets root (0 when unset/missing) — for UI feedback.</summary>
    public static int CountAssets(string? assetsRoot) =>
        !string.IsNullOrWhiteSpace(assetsRoot) && Directory.Exists(assetsRoot)
            ? Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories).Count()
            : 0;

    /// <summary>Lowercases and keeps only [a-z0-9_], collapsing everything else to '_'. DS1 tank paths
    /// are lowercased and the map/region name becomes part of the in-tank path.</summary>
    private static string Sanitize(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw.Trim().ToLowerInvariant())
            sb.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' ? c : '_');
        var s = sb.ToString().Trim('_');
        return s.Length == 0 ? fallback : s;
    }
}
