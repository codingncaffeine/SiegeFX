using System;
using System.IO;
using System.Text.Json;

namespace SiegeSmith.Services;

/// <summary>Small JSON-backed user settings under <c>%APPDATA%\SiegeSmith\settings.json</c>.
/// Currently just the remembered Dungeon Siege install path, so detection only has to
/// succeed (or be answered by the user) once.</summary>
public sealed class AppSettings
{
    public string? InstallPath { get; set; }

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
