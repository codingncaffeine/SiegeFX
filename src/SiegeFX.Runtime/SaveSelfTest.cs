using SiegeFX.Core.Save;

namespace SiegeFX.Runtime;

/// <summary>
/// Phase 19a — verifies <see cref="SaveStore"/> round-trips a populated
/// <see cref="SaveFile"/> losslessly through JSON. Wired into
/// <c>test-all.bat</c> as a no-window check so a schema regression
/// shows up before F5/F9 lands in 19c.
///
/// Covers: schema version, region path, every PlayerSnapshot field,
/// the actor list (with negative-coord edge cases — DS1 region origins
/// often live in -X/-Z), atomic write (replaces an existing file).
/// Does not exercise live actor patching — that's Phase 19b's job.
/// </summary>
internal static class SaveSelfTest
{
    public static bool Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "siegefx_selftest_save");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "roundtrip.save");
        if (File.Exists(path)) File.Delete(path);

        var savedAt = new DateTime(2026, 4, 24, 22, 30, 0, DateTimeKind.Utc);
        var original = new SaveFile
        {
            SchemaVersion = SaveFile.CurrentSchemaVersion,
            SavedAt    = savedAt,
            RegionPath = "world/maps/multiplayer_world/regions/town_center",
            Player = new PlayerSnapshot
            {
                Scid          = 0xA1B2C3D4,
                TotalXp       = 12_345,
                Level         = 5,
                Strength      = 14.62f,
                Dexterity     = 11.18f,
                Intelligence  = 12.04f,
                Facing        = new Vec3(0.7071f, 0f, 0.7071f),
                CameraMode    = 1,
                ChaseYaw      = 1.234f,
                ChaseDistance = 12.5f,
                ChaseHeight   = 7.0f,
                CameraPos     = new Vec3(-42.5f, 18.25f, -113.75f),
                CameraYaw     = -2.34f,
                CameraPitch   = -0.42f,
                Inventory = new List<LootEntrySnapshot>
                {
                    new() { Slot = "weapon_hand", Reference = "dg_g_d_1h_fun" },
                    new() { Slot = "potion",      Reference = "potion_health_minor" },
                    new() { Slot = "armor_chest", Reference = "ar_c_l_pad" },
                },
                Spellbook = new SpellbookSnapshot
                {
                    PrimarySpell      = "spell_zap",
                    SecondarySpell    = "spell_healing_wind",
                    PrimaryCooldown   = 0.08f,
                    SecondaryCooldown = 1.42f,
                },
            },
            LootPiles = new List<LootPileSnapshot>
            {
                new()
                {
                    Position = new Vec3(-5.5f, 0.25f, 12.0f),
                    Entries = new List<LootEntrySnapshot>
                    {
                        new() { Slot = "weapon_hand", Reference = "hm_g_c_1h1m_low" },
                        new() { Slot = "gold",        Reference = "gold_pile_small" },
                    },
                },
            },
            Actors = new List<ActorSnapshot>
            {
                new() { Scid = 0x10000001, TemplateName = "3W_goblin_grunt",
                        Position = new Vec3(-12.5f, 1.0f, 24.75f),
                        CurrentLife = 18.5f, CurrentMana = 0f, IsDead = false },
                new() { Scid = 0x10000002, TemplateName = "3W_krug_scout",
                        Position = new Vec3(  3.25f, 0.5f, -18.0f),
                        CurrentLife = 0f, CurrentMana = 0f, IsDead = true },
                new() { Scid = 0x10000003, TemplateName = "3W_chicken",
                        Position = new Vec3( 17.0f, 1.2f,   8.5f),
                        CurrentLife = 1f, CurrentMana = 0f, IsDead = false },
            },
        };

        // Write twice to exercise the temp+replace branch (path already exists
        // on the second call). A bug in atomic-write commonly only shows up
        // when overwriting an existing file, so do it on purpose here.
        SaveStore.Save(path, original);
        SaveStore.Save(path, original);

        var loaded = SaveStore.Load(path);

        var failures = new List<string>();
        Check(failures, "SchemaVersion", original.SchemaVersion, loaded.SchemaVersion);
        Check(failures, "SavedAt",       original.SavedAt,       loaded.SavedAt);
        Check(failures, "RegionPath",    original.RegionPath,    loaded.RegionPath);
        if (loaded.Player is null) failures.Add("Player block was null after round-trip");
        else
        {
            var a = original.Player!; var b = loaded.Player;
            Check(failures, "Player.Scid",          a.Scid,         b.Scid);
            Check(failures, "Player.TotalXp",       a.TotalXp,      b.TotalXp);
            Check(failures, "Player.Level",         a.Level,        b.Level);
            Check(failures, "Player.Strength",      a.Strength,     b.Strength);
            Check(failures, "Player.Dexterity",     a.Dexterity,    b.Dexterity);
            Check(failures, "Player.Intelligence",  a.Intelligence, b.Intelligence);
            Check(failures, "Player.Facing",        a.Facing,       b.Facing);
            Check(failures, "Player.CameraMode",    a.CameraMode,   b.CameraMode);
            Check(failures, "Player.ChaseYaw",      a.ChaseYaw,     b.ChaseYaw);
            Check(failures, "Player.ChaseDistance", a.ChaseDistance, b.ChaseDistance);
            Check(failures, "Player.ChaseHeight",   a.ChaseHeight,  b.ChaseHeight);
            Check(failures, "Player.CameraPos",     a.CameraPos,    b.CameraPos);
            Check(failures, "Player.CameraYaw",     a.CameraYaw,    b.CameraYaw);
            Check(failures, "Player.CameraPitch",   a.CameraPitch,  b.CameraPitch);
            Check(failures, "Player.Inventory.Count", a.Inventory.Count, b.Inventory.Count);
            for (int i = 0; i < Math.Min(a.Inventory.Count, b.Inventory.Count); i++)
            {
                Check(failures, $"Player.Inventory[{i}].Slot",      a.Inventory[i].Slot,      b.Inventory[i].Slot);
                Check(failures, $"Player.Inventory[{i}].Reference", a.Inventory[i].Reference, b.Inventory[i].Reference);
            }
            if (a.Spellbook is null) Check(failures, "Player.Spellbook (orig null)", true, b.Spellbook is null);
            else if (b.Spellbook is null) failures.Add("Player.Spellbook null after round-trip");
            else
            {
                Check(failures, "Spellbook.PrimarySpell",      a.Spellbook.PrimarySpell,      b.Spellbook.PrimarySpell);
                Check(failures, "Spellbook.SecondarySpell",    a.Spellbook.SecondarySpell,    b.Spellbook.SecondarySpell);
                Check(failures, "Spellbook.PrimaryCooldown",   a.Spellbook.PrimaryCooldown,   b.Spellbook.PrimaryCooldown);
                Check(failures, "Spellbook.SecondaryCooldown", a.Spellbook.SecondaryCooldown, b.Spellbook.SecondaryCooldown);
            }
        }
        Check(failures, "LootPiles.Count", original.LootPiles.Count, loaded.LootPiles.Count);
        for (int i = 0; i < Math.Min(original.LootPiles.Count, loaded.LootPiles.Count); i++)
        {
            var op = original.LootPiles[i]; var lp = loaded.LootPiles[i];
            Check(failures, $"LootPiles[{i}].Position", op.Position, lp.Position);
            Check(failures, $"LootPiles[{i}].Entries.Count", op.Entries.Count, lp.Entries.Count);
            for (int j = 0; j < Math.Min(op.Entries.Count, lp.Entries.Count); j++)
            {
                Check(failures, $"LootPiles[{i}].Entries[{j}].Slot",
                      op.Entries[j].Slot, lp.Entries[j].Slot);
                Check(failures, $"LootPiles[{i}].Entries[{j}].Reference",
                      op.Entries[j].Reference, lp.Entries[j].Reference);
            }
        }
        Check(failures, "Actors.Count", original.Actors.Count, loaded.Actors.Count);
        for (int i = 0; i < Math.Min(original.Actors.Count, loaded.Actors.Count); i++)
        {
            var oa = original.Actors[i]; var la = loaded.Actors[i];
            Check(failures, $"Actors[{i}].Scid",          oa.Scid,         la.Scid);
            Check(failures, $"Actors[{i}].TemplateName",  oa.TemplateName, la.TemplateName);
            Check(failures, $"Actors[{i}].Position",      oa.Position,     la.Position);
            Check(failures, $"Actors[{i}].CurrentLife",   oa.CurrentLife,  la.CurrentLife);
            Check(failures, $"Actors[{i}].CurrentMana",   oa.CurrentMana,  la.CurrentMana);
            Check(failures, $"Actors[{i}].IsDead",        oa.IsDead,       la.IsDead);
        }

        // Schema-version mismatch must throw rather than silently load. Bump
        // on disk and confirm Load refuses, so a future schema change can't
        // silently destroy data by being read with the wrong shape.
        var bumpedJson = File.ReadAllText(path)
            .Replace($"\"SchemaVersion\": {SaveFile.CurrentSchemaVersion}",
                     $"\"SchemaVersion\": {SaveFile.CurrentSchemaVersion + 99}");
        var bumpedPath = Path.Combine(dir, "bumped.save");
        File.WriteAllText(bumpedPath, bumpedJson);
        bool refused = false;
        try { SaveStore.Load(bumpedPath); }
        catch (InvalidDataException) { refused = true; }
        if (!refused) failures.Add("SaveStore.Load accepted a future-version save (should refuse)");

        if (failures.Count == 0)
        {
            Console.WriteLine($"[selftest-save] OK — {original.Actors.Count} actor(s), " +
                              $"player + camera, schema v{SaveFile.CurrentSchemaVersion} round-tripped at {path}");
            return true;
        }

        Console.Error.WriteLine($"[selftest-save] FAIL ({failures.Count}):");
        foreach (var f in failures) Console.Error.WriteLine("  " + f);
        return false;
    }

    static void Check<T>(List<string> failures, string field, T expected, T actual)
    {
        if (!Equals(expected, actual))
            failures.Add($"{field}: expected {expected}, got {actual}");
    }
}
