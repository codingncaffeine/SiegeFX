using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SiegeSmith.Services;

/// <summary>Small JSON-backed user settings under <c>%APPDATA%\SiegeSmith\settings.json</c>.
/// The remembered Dungeon Siege install path, and the ONE custom-assets folder shared by
/// the World Builder and the Effects Lab — set it once anywhere, every tool uses it,
/// and it survives sessions.</summary>
public sealed class AppSettings
{
    public string? InstallPath { get; set; }
    public string? AssetsFolder { get; set; }

    /// <summary>Last-used directory per purpose ("mesh", "texture", "audio", "region") —
    /// file dialogs open where the user last worked instead of making them hunt.</summary>
    public Dictionary<string, string>? LastDirs { get; set; }

    public static string? GetLastDir(string key)
    {
        var d = Load().LastDirs;
        return d is not null && d.TryGetValue(key, out var v) && Directory.Exists(v) ? v : null;
    }

    public static void SaveLastDir(string key, string? fileOrDir)
    {
        if (string.IsNullOrEmpty(fileOrDir)) return;
        var dir = Directory.Exists(fileOrDir) ? fileOrDir : Path.GetDirectoryName(fileOrDir);
        if (string.IsNullOrEmpty(dir)) return;
        var s = Load();
        s.LastDirs ??= new Dictionary<string, string>();
        s.LastDirs[key] = dir;
        s.Save();
    }

    /// <summary>Reads the shared custom-assets folder (null if unset or gone).</summary>
    public static string? LoadAssetsFolder()
    {
        var f = Load().AssetsFolder;
        return f is not null && Directory.Exists(f) ? f : null;
    }

    /// <summary>Persists the shared custom-assets folder for every tool.</summary>
    public static void SaveAssetsFolder(string folder)
    {
        var s = Load();
        s.AssetsFolder = folder;
        s.Save();
    }

    private static string DirPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SiegeSmith");
    private static string FilePath => Path.Combine(DirPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* corrupt/unreadable — start fresh */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DirPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best-effort persistence */ }
    }
}
