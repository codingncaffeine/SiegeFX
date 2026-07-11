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
            var text = File.ReadAllText(path);
            var s = JsonSerializer.Deserialize<OptionsMenuPanel.Settings>(text, JsonOpts);
            // SC-OPTIONS-GAME — legacy migration: files without PrefsVersion
            // predate the live GameSpeed mapping (their 100 was a dead knob's
            // default, not a 2.0x request). Missing JSON properties keep the
            // C# initializer value, so presence must be checked on the raw
            // document rather than the deserialized object.
            if (s is not null)
            {
                using var doc = JsonDocument.Parse(text);
                if (!doc.RootElement.TryGetProperty("PrefsVersion", out _))
                    s.GameSpeed = 50;
                s.PrefsVersion = 2;
            }
            return s;
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
