using System.IO;
using System.Text.Json;

namespace SiegeSmith.Services;

/// <summary>A SiegeSmith mod project (.ssproj) — a saved association between a source folder of
/// loose files and the tank metadata used to package it, so the build → install → test loop is
/// one click each time instead of re-entering paths.</summary>
public sealed class ModProject
{
    public string Name { get; set; } = "MyMod";
    public string SourceFolder { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static ModProject Load(string path) =>
        JsonSerializer.Deserialize<ModProject>(File.ReadAllText(path)) ?? new ModProject();

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
}
