using System.Text.Json;

namespace SiegeFX.Core.Save;

/// <summary>
/// Phase 19a — JSON read/write for <see cref="SaveFile"/>. Atomic write
/// (temp file + replace) so a crashed save doesn't shred the previous
/// one. Pretty-printed because the diff value during dev outweighs the
/// few extra bytes; we can flip to compact later if file size matters.
/// </summary>
public static class SaveStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        IncludeFields = false,
    };

    public static void Save(string path, SaveFile data)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Atomic-ish: write to a sibling .tmp, then replace. On a crash
        // mid-write the original .save stays intact and the .tmp gets
        // cleaned up next save. Cross-volume Move would fail, but a save
        // file always lives in the same directory we're writing to so
        // they share a volume by construction.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json));
        if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
        else                    File.Move(tmp, path);
    }

    public static SaveFile Load(string path)
    {
        var json = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<SaveFile>(json, Json)
                   ?? throw new InvalidDataException($"save '{path}' deserialized to null");
        // Forward migrations.
        //   v1 -> v2 : added PlayerSnapshot.Quests (empty list default).
        //   v2 -> v3 : added QuestSnapshot.KillProgress (default 0).
        //   v3 -> v4 : added PlayerSnapshot.Gold (default 0).
        //   v4 -> v5 : added PlayerSnapshot.HeroName (empty string) +
        //              PlayerSnapshot.Variant (null = stock farmboy).
        //   v5 -> v6 : added SpellbookSnapshot.Placed (empty list default).
        //   v6 -> v7 : added QuestSnapshot.TalkProgress (default 0) for
        //              SC-QUEST-OBJ-A.
        //   v7 -> v8 : added PlayerSnapshot.ConsumedInventoryScids (default
        //              empty list) for SC-WORLD-INVENTORY-CONSUMED.
        //   v8 -> v9 : added QuestSnapshot.PickupProgress (default 0) for
        //              SC-QUEST-OBJ-C.
        // All deserializer-friendly — missing fields just hit their defaults —
        // so the work here is only the version-stamp bump. Pre-v1 shapes still
        // get rejected below. Forgetting to extend this whitelist when bumping
        // CurrentSchemaVersion silently breaks every prior save with an
        // InvalidDataException — caught by the SC-SCROLL-G review pass.
        //   v9 -> v10: added SaveFile.World (ALPHA-2G world state) +
        //              QuestSnapshot.DialogueLog — both default-friendly.
        if (file.SchemaVersion is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9)
        {
            file.SchemaVersion = SaveFile.CurrentSchemaVersion;
        }
        if (file.SchemaVersion != SaveFile.CurrentSchemaVersion)
            throw new InvalidDataException(
                $"save '{path}' schema v{file.SchemaVersion} != runtime v{SaveFile.CurrentSchemaVersion}");
        return file;
    }

    /// <summary>Default per-user save directory. Uses LocalApplicationData
    /// so saves survive uninstalls of the dev build but stay out of the
    /// roaming profile (the data isn't worth syncing). Same scheme on
    /// Linux/Mac via the .NET cross-platform special-folder mapping.</summary>
    public static string DefaultSaveDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SiegeFX", "Saves");

    /// <summary>Quicksave path under <see cref="DefaultSaveDirectory"/>.
    /// One slot for now; the F5/F9 wiring in 19c overwrites it on each
    /// save and reads it on each load.</summary>
    public static string QuicksavePath()
        => Path.Combine(DefaultSaveDirectory(), "quicksave.save");
}
