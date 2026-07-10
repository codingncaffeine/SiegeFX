using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
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
        StartInfo? start = null, SeedActor? actor = null, string? assetsRoot = null,
        IReadOnlyList<PlacedObject>? placements = null,
        IReadOnlyList<AuthoredLight>? lights = null, AuthoredMood? mood = null,
        IReadOnlyList<RegionEmitter>? emitters = null, IReadOnlyList<RegionDecal>? decals = null,
        IReadOnlyList<RegionTrigger>? triggers = null, IReadOnlyList<CommandPlacement>? commands = null,
        IReadOnlyList<Conversation>? conversations = null,
        uint sourceGuid = 0, IReadOnlyList<RegionStitch>? stitches = null,
        IReadOnlyList<StitchRegionRef>? siblings = null,
        IReadOnlyList<LogicalFlag>? logicalFlags = null,
        string? questsGas = null)
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

        // GAME-4 — map-local quest catalog (retail quests/quests.gas shape).
        if (!string.IsNullOrWhiteSpace(questsGas))
        {
            string questsDir = Path.Combine(mapDir, "quests");
            Directory.CreateDirectory(questsDir);
            File.WriteAllText(Path.Combine(questsDir, "quests.gas"), questsGas);
        }
        // Every placed object (props + the seed actor) is written through the one placement writer,
        // grouped into its objects/<file>.gas bucket.
        var objs = new List<PlacedObject>();
        if (placements is not null) objs.AddRange(placements);
        if (actor is { NodeGuid: not 0 } a && !string.IsNullOrWhiteSpace(a.Template))
            objs.Add(new PlacedObject
            {
                Scid = a.Scid, Template = a.Template, NodeGuid = a.NodeGuid,
                LocalPos = new Vector3(a.X, 0f, a.Z), Orientation = Quaternion.Identity, File = "actor.gas",
            });
        // Actor↔conversation bindings: a conversation bound to a placed actor injects the
        // [conversation][conversations] block into that actor's placement.
        Dictionary<uint, string>? convBindings = null;
        if (conversations is not null)
            foreach (var c in conversations)
                if (c.BoundActorScid != 0)
                    (convBindings ??= new Dictionary<uint, string>())[c.BoundActorScid] = c.FullKey;
        foreach (var (rel, gas) in PlacementWriter.WriteByFile(objs, convBindings))
        {
            string full = Path.Combine(mapDir, "regions", region, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, gas);
        }

        // Region lights → regions/<region>/lights/lights.gas (directional lights reach the renderer).
        if (lights is { Count: > 0 })
        {
            string lightsDir = Path.Combine(mapDir, "regions", region, "lights");
            Directory.CreateDirectory(lightsDir);
            File.WriteAllText(Path.Combine(lightsDir, "lights.gas"), LightsGasWriter.Write(lights));
        }

        // Mood audio is MAP-GLOBAL: /world/global/moods/<map>/moods.gas, not under the region tree.
        if (mood is { } md && !string.IsNullOrWhiteSpace(md.Name) && md.HasAudio)
        {
            string moodDir = Path.Combine(staging, "world", "global", "moods", map);
            Directory.CreateDirectory(moodDir);
            File.WriteAllText(Path.Combine(moodDir, "moods.gas"), MoodsGasWriter.Write(md));
        }

        // Particle emitters → objects/emitter.gas (the only per-region live-particle path).
        if (emitters is { Count: > 0 })
        {
            string objDir = Path.Combine(mapDir, "regions", region, "objects");
            Directory.CreateDirectory(objDir);
            File.WriteAllText(Path.Combine(objDir, "emitter.gas"), EmitterGasWriter.Write(emitters));
        }
        // Decals → decals/decals.gas.
        if (decals is { Count: > 0 })
        {
            string decalDir = Path.Combine(mapDir, "regions", region, "decals");
            Directory.CreateDirectory(decalDir);
            File.WriteAllText(Path.Combine(decalDir, "decals.gas"), DecalGasWriter.Write(decals));
        }

        // Trigger volumes → objects/special.gas; command/NIS gizmos → objects/command.gas.
        if (triggers is { Count: > 0 } || commands is { Count: > 0 })
        {
            string objDir = Path.Combine(mapDir, "regions", region, "objects");
            Directory.CreateDirectory(objDir);
            if (triggers is { Count: > 0 })
                File.WriteAllText(Path.Combine(objDir, "special.gas"), TriggerGasWriter.Write(triggers));
            if (commands is { Count: > 0 })
                File.WriteAllText(Path.Combine(objDir, "command.gas"), CommandGasWriter.Write(commands));
        }
        // Conversations → conversations/conversations.gas.
        if (conversations is { Count: > 0 })
        {
            string convDir = Path.Combine(mapDir, "regions", region, "conversations");
            Directory.CreateDirectory(convDir);
            File.WriteAllText(Path.Combine(convDir, "conversations.gas"), ConversationGasWriter.Write(conversations));
        }

        // World stitching: the primary region's editor/stitch_helper.gas + each sibling region staged
        // whole (its nodes.gas + its reciprocal stitch_helper.gas) so the map is a real multi-region world.
        if (sourceGuid != 0 && stitches is { Count: > 0 })
        {
            string edDir = Path.Combine(mapDir, "regions", region, "editor");
            Directory.CreateDirectory(edDir);
            File.WriteAllText(Path.Combine(edDir, "stitch_helper.gas"), StitchHelperWriter.Write(sourceGuid, region, stitches));
        }
        // Nav flags (optional) → <region>/editor/logical_flags.gas (editor/ sibling of terrain_nodes).
        if (logicalFlags is { Count: > 0 })
        {
            string edDir = Path.Combine(mapDir, "regions", region, "editor");
            Directory.CreateDirectory(edDir);
            File.WriteAllText(Path.Combine(edDir, "logical_flags.gas"), LogicalFlagsWriter.Write(logicalFlags));
        }
        if (siblings is { Count: > 0 })
        {
            foreach (var sib in siblings)
            {
                string leaf = Sanitize(sib.LeafName, "region_r2");
                if (leaf == region) continue; // never clobber the primary
                string sibNodes = Path.Combine(mapDir, "regions", leaf, "terrain_nodes");
                Directory.CreateDirectory(sibNodes);
                File.WriteAllText(Path.Combine(sibNodes, "nodes.gas"), sib.NodesGas);
                if (sib.SourceGuid != 0 && sib.Stitches.Count > 0)
                {
                    string sibEd = Path.Combine(mapDir, "regions", leaf, "editor");
                    Directory.CreateDirectory(sibEd);
                    File.WriteAllText(Path.Combine(sibEd, "stitch_helper.gas"), StitchHelperWriter.Write(sib.SourceGuid, leaf, sib.Stitches));
                }
            }
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
