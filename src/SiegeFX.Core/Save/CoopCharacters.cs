using System.Text.Json;

namespace SiegeFX.Core.Save;

/// <summary>One saved co-op character, as the Select Character list shows it
/// (staging_area_select_character.gas: portrait, name, class, the four skill
/// levels). <see cref="New"/>: created but not played yet (no level, gear or
/// stats of its own; the game gives it the starting kit).</summary>
public sealed record CoopCharacterEntry(
    string Path, string Name, int Level, string ClassName,
    int Melee, int Ranged, int NatureMagic, int CombatMagic,
    bool Girl, bool New, DateTime SavedAt, string Body = "boy");

/// <summary>The player's co-op characters: one file each in the save folder's
/// Multiplayer subfolder, kept on this machine and carried into every co-op
/// game the player hosts or joins (the original kept multiplayer characters the
/// same way, and let a player create one, import one from a single-player
/// save, or delete one in the staging area). The file is a <see cref="SaveFile"/>
/// holding the hero alone: <see cref="SaveFile.Player"/> without the world's
/// parts, plus the <see cref="SaveFile.Coop"/> line for the list.</summary>
public static class CoopCharacters
{
    /// <summary>The file naming the character chosen last (its file name).</summary>
    public const string SelectedFileName = "selected.txt";

    public static string DefaultFolder() =>
        Path.GetDirectoryName(SaveStore.CoopCharacterPath())!;

    /// <summary>Every co-op character in <paramref name="folder"/>, by name.
    /// A file that does not read is left out (and left alone).</summary>
    public static IReadOnlyList<CoopCharacterEntry> List(string? folder = null)
    {
        folder ??= DefaultFolder();
        var list = new List<CoopCharacterEntry>();
        if (!Directory.Exists(folder)) return list;
        foreach (var path in Directory.EnumerateFiles(folder, "*.save"))
        {
            SaveFile f;
            try { f = SaveStore.Load(path); }
            catch { continue; }
            list.Add(Entry(path, f));
        }
        list.Sort((a, b) =>
        {
            int c = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            return c != 0 ? c : string.CompareOrdinal(a.Path, b.Path);
        });
        return list;
    }

    public static CoopCharacterEntry Entry(string path, SaveFile f)
    {
        var p = f.Player;
        var c = f.Coop;
        string name = !string.IsNullOrWhiteSpace(p?.HeroName) ? p!.HeroName
                    : !string.IsNullOrWhiteSpace(f.HeroName) ? f.HeroName
                    : "Unnamed hero";
        bool girl = string.Equals(p?.Variant?.Gender, "girl", StringComparison.OrdinalIgnoreCase);
        string body = p?.Variant?.Gender is { Length: > 0 } g ? g.Trim().ToLowerInvariant() : "boy";
        return new CoopCharacterEntry(path, name, Math.Max(0, p?.Level ?? 0),
            c?.ClassName is { Length: > 0 } cls ? cls : "Farmer",
            c?.Melee ?? 0, c?.Ranged ?? 0, c?.NatureMagic ?? 0, c?.CombatMagic ?? 0,
            girl, IsNew(f), f.SavedAt, body);
    }

    /// <summary>Created in the staging area and not played yet.</summary>
    public static bool IsNew(SaveFile f) => f.Player is null || f.Player.Level <= 0;

    /// <summary>The character chosen last, when its file is still there.</summary>
    public static string? SelectedPath(string? folder = null)
    {
        folder ??= DefaultFolder();
        try
        {
            var marker = Path.Combine(folder, SelectedFileName);
            if (!File.Exists(marker)) return null;
            string name = File.ReadAllText(marker).Trim();
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            var path = Path.Combine(folder, name);
            return File.Exists(path) ? path : null;
        }
        catch { return null; }
    }

    /// <summary>Remember <paramref name="path"/> as the chosen character (null forgets it).</summary>
    public static void Select(string? path, string? folder = null)
    {
        folder ??= DefaultFolder();
        Directory.CreateDirectory(folder);
        var marker = Path.Combine(folder, SelectedFileName);
        if (path is null) { if (File.Exists(marker)) File.Delete(marker); return; }
        File.WriteAllText(marker, Path.GetFileName(path) + Environment.NewLine);
    }

    /// <summary>A file name of its own for a character called <paramref name="name"/>:
    /// the name's letters, digits, spaces, dashes and underscores, then " (2)",
    /// " (3)"... when taken. Never an existing file.</summary>
    public static string UniquePath(string name, string? folder = null)
    {
        folder ??= DefaultFolder();
        var safe = new string((name ?? "").Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_').ToArray()).Trim();
        if (safe.Length > 40) safe = safe[..40].TrimEnd();
        if (safe.Length == 0) safe = "Hero";
        string path = Path.Combine(folder, safe + ".save");
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{safe} ({n}).save");
        return path;
    }

    /// <summary>A new character: a name and a look, not played yet (the game
    /// gives it the starting kit and its first save fills the file).</summary>
    public static string Create(string name, HeroVariantSnapshot? look, string startingClass = "Farmer", string? folder = null)
    {
        folder ??= DefaultFolder();
        name = string.IsNullOrWhiteSpace(name) ? "Hero" : name.Trim();
        var path = UniquePath(name, folder);
        SaveStore.Save(path, new SaveFile
        {
            SavedAt = DateTime.UtcNow,
            DisplayName = $"Co-op character: {name}",
            HeroName = name,
            Player = new PlayerSnapshot { Level = 0, HeroName = name, Variant = look },
            Coop = new CoopCharacterSummary { ClassName = startingClass },
        });
        return path;
    }

    /// <summary>Copy a single-player save's hero into a new co-op character
    /// (the single-player game is not touched). The hero's own progress comes
    /// along; the world's parts stay behind. <paramref name="skillLevel"/> turns
    /// a skill's experience into its level for the list (null: levels 0 until
    /// the character is played).</summary>
    public static string Import(SaveFile from, Func<long, int>? skillLevel = null,
                                string startingClass = "Farmer", string? folder = null)
    {
        if (from.Player is null) throw new InvalidDataException("the saved game holds no hero");
        folder ??= DefaultFolder();
        var hero = StripWorld(Copy(from.Player));
        if (hero.Level <= 0) hero.Level = 1;
        string name = !string.IsNullOrWhiteSpace(hero.HeroName) ? hero.HeroName
                    : !string.IsNullOrWhiteSpace(from.HeroName) ? from.HeroName : "Hero";
        hero.HeroName = name;
        int Lv(int i) => skillLevel is not null && i < hero.SkillXp.Count ? skillLevel(hero.SkillXp[i]) : 0;
        int melee = Lv(0), ranged = Lv(1), nature = Lv(2), combat = Lv(3);
        var path = UniquePath(name, folder);
        SaveStore.Save(path, new SaveFile
        {
            SavedAt = DateTime.UtcNow,
            DisplayName = $"Co-op character: {name}",
            HeroName = name,
            Difficulty = from.Difficulty,
            Player = hero,
            Coop = new CoopCharacterSummary
            {
                ClassName = Assets.ClassTitleResolver.Resolve(startingClass, melee, ranged, nature, combat),
                Melee = melee, Ranged = ranged, NatureMagic = nature, CombatMagic = combat,
                ImportedFrom = from.DisplayName,
            },
        });
        return path;
    }

    /// <summary>The hero without what belongs to the world it was played in:
    /// the journal, picked-up world items, a summon, being knocked out, the
    /// camera and facing, the world's id for the hero.</summary>
    public static PlayerSnapshot StripWorld(PlayerSnapshot hero)
    {
        hero.Quests.Clear();
        hero.ConsumedInventoryScids.Clear();
        hero.SummonScid = 0; hero.SummonEndScript = "";
        hero.Downed = false;
        hero.Facing = default; hero.CameraPos = default;
        hero.CameraMode = 0; hero.ChaseYaw = 0; hero.ChaseDistance = 0; hero.ChaseHeight = 0;
        hero.CameraYaw = 0; hero.CameraPitch = 0;
        hero.Scid = 0;
        return hero;
    }

    /// <summary>Delete a character and its backup; forget it if it was chosen.</summary>
    public static void Delete(string path, string? folder = null)
    {
        folder ??= DefaultFolder();
        bool wasSelected = string.Equals(SelectedPath(folder), path, StringComparison.Ordinal);
        File.Delete(path);
        if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        if (wasSelected) Select(null, folder);
    }

    static PlayerSnapshot Copy(PlayerSnapshot p) =>
        JsonSerializer.Deserialize<PlayerSnapshot>(JsonSerializer.Serialize(p))!;
}
