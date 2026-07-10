using System.Text.Json;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>ALPHA-2V — options persistence (closes splinter SC-OPTIONS-PERSIST).
/// The whole <see cref="OptionsMenuPanel.Settings"/> object round-trips as JSON
/// at %LocalAppData%\SiegeFX\prefs.json (sibling of the Saves directory), the
/// modern stand-in for DS1's prefs.gas + DungeonSiege.ini split. Saved on OK
/// and on window close (captures the last windowed size); loaded once at boot
/// before the window is created so resolution/fullscreen apply from frame one.</summary>
internal static class OptionsPrefs
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        IncludeFields = true,
        WriteIndented = true,
    };

    public static string PrefsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SiegeFX", "prefs.json");

    public static OptionsMenuPanel.Settings? Load()
    {
        try
        {
            var path = PrefsPath;
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<OptionsMenuPanel.Settings>(
                File.ReadAllText(path), JsonOpts);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[prefs] load failed ({ex.Message}); using defaults");
            return null;
        }
    }

    public static void Save(OptionsMenuPanel.Settings s)
    {
        try
        {
            var path = PrefsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(s, JsonOpts));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[prefs] save failed: {ex.Message}");
        }
    }
}
