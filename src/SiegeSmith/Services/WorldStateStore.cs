using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SiegeSmith.Services;

/// <summary>One stitch endpoint as persisted world state.</summary>
public sealed class WorldStitchState
{
    public uint PairId { get; set; }
    public uint LocalSnode { get; set; }
    public int LocalDoor { get; set; }
    public string DestRegion { get; set; } = "";
}

/// <summary>An imported stitch neighbour as persisted world state. The full nodes.gas text
/// rides along because shipped regions live inside tanks — there is no loose file to re-read.</summary>
public sealed class WorldSiblingState
{
    public string Leaf { get; set; } = "";
    public string NodesGas { get; set; } = "";
    public List<WorldStitchState> Stitches { get; set; } = new();
}

public sealed class WorldState
{
    public List<WorldSiblingState> Siblings { get; set; } = new();
    public List<WorldStitchState> PrimaryStitches { get; set; } = new();
}

/// <summary>Persists world-stitching state per region under %APPDATA%\SiegeSmith\worldstate.
/// Stitching is WORLD data, not region data — it references other regions — so it can't live
/// in nodes.gas and previously evaporated with the view-model on every app restart (the
/// "where did my stitches go?" report). Keyed by the region's target-node guid, which shipped
/// content keeps world-unique; custom regions key by leaf name.</summary>
public static class WorldStateStore
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SiegeSmith", "worldstate");

    private static string PathFor(string key)
    {
        var sb = new System.Text.StringBuilder(key.Length);
        foreach (var c in key)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        return Path.Combine(Dir, sb.ToString() + ".json");
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static WorldState? Load(string key)
    {
        try
        {
            var p = PathFor(key);
            return File.Exists(p) ? JsonSerializer.Deserialize<WorldState>(File.ReadAllText(p)) : null;
        }
        catch { return null; }
    }

    public static void Save(string key, WorldState state)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(PathFor(key), JsonSerializer.Serialize(state, Options));
        }
        catch { /* persistence is a convenience — never block editing */ }
    }

    public static void Delete(string key)
    {
        try
        {
            var p = PathFor(key);
            if (File.Exists(p)) File.Delete(p);
        }
        catch { }
    }
}
