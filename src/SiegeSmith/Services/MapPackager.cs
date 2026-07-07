using System;
using System.IO;
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

    /// <summary>Writes <paramref name="nodesGas"/> under the required path tree and packs it into
    /// <c>map_&lt;map&gt;.dsmap</c> in <paramref name="outputDir"/>. Returns the tank path and the in-tank
    /// region path to hand to <see cref="RuntimeLauncher"/>. The map dir is always prefixed <c>map_</c>
    /// so the engine's ambient-audio gate (DeriveMapName) stays enabled.</summary>
    public static Packaged PackStartableMap(string nodesGas, string mapName, string regionName, string outputDir)
    {
        string map = "map_" + Sanitize(mapName, "custom");
        string region = Sanitize(regionName, "region_r1");
        string regionPath = $"/world/maps/{map}/regions/{region}";

        string staging = Path.Combine(Path.GetTempPath(), "SiegeSmith", "staging", map);
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        string nodesDir = Path.Combine(staging, "world", "maps", map, "regions", region, "terrain_nodes");
        Directory.CreateDirectory(nodesDir);
        File.WriteAllText(Path.Combine(nodesDir, "nodes.gas"), nodesGas);

        Directory.CreateDirectory(outputDir);
        string mapTank = Path.Combine(outputDir, map + ".dsmap");
        TankBuilder.BuildFromFolder(staging, mapTank,
            title: map, author: "SiegeSmith", description: $"Custom map {map}",
            priority: TankPriority.User, utcBuildTime: DateTime.UtcNow);

        return new Packaged(mapTank, regionPath, map, region);
    }

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
