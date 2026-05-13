using System.Numerics;
using SiegeFX.Core.Actors;

namespace SiegeFX.Core.Save;

/// <summary>
/// Phase 19a — versioned snapshot of one mid-dungeon session. Not
/// DS1-binary-compatible; SiegeFX rolls its own JSON-friendly schema so
/// the save/load loop can ship before we have full save-game parity with
/// the original.
///
/// Versioning is explicit: <see cref="SchemaVersion"/> bumps every time
/// the on-disk shape changes incompatibly. Loaders refuse anything they
/// don't know how to read rather than silently dropping fields.
/// </summary>
public sealed class SaveFile
{
    /// <summary>Schema history (most recent last):
    ///   v1 -> v2 : added <see cref="PlayerSnapshot.Quests"/>.
    ///   v2 -> v3 : added <see cref="QuestSnapshot.KillProgress"/>.
    ///   v3 -> v4 : added <see cref="PlayerSnapshot.Gold"/>.
    ///   v4 -> v5 : added <see cref="PlayerSnapshot.HeroName"/> and
    ///              <see cref="PlayerSnapshot.Variant"/> (gender + body/skin/
    ///              pants picks from the character creator).
    ///   v5 -> v6 : added <see cref="SpellbookSnapshot.Placed"/> (the 10
    ///              user-organized inactive rows in the spellbook UI).
    ///   v6 -> v7 : added <see cref="QuestSnapshot.TalkProgress"/>
    ///              (SC-QUEST-OBJ-A talk-to-NPC objective counter).
    ///   v7 -> v8 : added <see cref="PlayerSnapshot.ConsumedInventoryScids"/>
    ///              (SC-WORLD-INVENTORY-CONSUMED — picked-up world-pickups
    ///              that should stay gone across save-reload).
    ///   v8 -> v9 : added <see cref="QuestSnapshot.PickupProgress"/>
    ///              (SC-QUEST-OBJ-C pickup-objective counter).
    /// All bumps are deserializer-friendly — missing fields hit their defaults —
    /// so any v1..v8 file loads as a v9 with the new fields zero-initialized.
    /// IMPORTANT: bumping CurrentSchemaVersion requires extending the
    /// migration whitelist in SaveStore.Load too; the strict-equality check
    /// downstream throws InvalidDataException on any unmigrated version.</summary>
    public const int CurrentSchemaVersion = 9;

    /// <summary>Schema version of the file as written. Loader rejects when
    /// this doesn't match <see cref="CurrentSchemaVersion"/>.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Wall-clock time the save was written. Surfaced in save-pick
    /// UI later; today it just helps debug "which save am I looking at".</summary>
    public DateTime SavedAt { get; set; }

    /// <summary>Region path this save is anchored to (e.g.
    /// <c>world/maps/multiplayer_world/regions/town_center</c>). Loader
    /// refuses if the active region doesn't match — trying to splice
    /// fh_r1 actor scids into a different region would hit-or-miss.</summary>
    public string RegionPath { get; set; } = "";

    /// <summary>Player progression block. Null when no PC was active at
    /// save time (viewer modes, headless test scenes).</summary>
    public PlayerSnapshot? Player { get; set; }

    /// <summary>One entry per live actor at save time. Keyed by Scid so
    /// the loader can patch the right actor even if the spawn order
    /// changes between runs.</summary>
    public List<ActorSnapshot> Actors { get; set; } = new();

    /// <summary>Loot piles still on the ground at save time. Re-spawned in
    /// place on load so an unpicked drop doesn't vanish.</summary>
    public List<LootPileSnapshot> LootPiles { get; set; } = new();
}

/// <summary>One unpicked loot pile. Position + the same template-ref + slot
/// pairs that <c>LootEntry</c> uses. We only persist the references — the
/// actual item templates are looked up fresh on load against the active
/// template store, so a save that's half-imported into a future content
/// patch picks up any data updates instead of freezing the old payload.</summary>
public sealed class LootPileSnapshot
{
    public Vec3 Position { get; set; }
    public List<LootEntrySnapshot> Entries { get; set; } = new();
}

public sealed class LootEntrySnapshot
{
    public string Slot { get; set; } = "";
    public string Reference { get; set; } = "";
}

/// <summary>Spellbook state. Slots are the template names of the slotted
/// spells (resolved against <c>SpellCatalog</c> on load); cooldowns are
/// the live remaining-second counters.</summary>
public sealed class SpellbookSnapshot
{
    public string? PrimarySpell        { get; set; }
    public string? SecondarySpell      { get; set; }
    public float   PrimaryCooldown     { get; set; }
    public float   SecondaryCooldown   { get; set; }

    /// <summary>Phase 21-SC-SCROLL-G — the 10 user-organized "placed"
    /// rows below the actives. Stored as template names; null entries
    /// stay null (empty cells). Empty list / null on a v5 save → all
    /// placed slots load as empty (matches pre-G behavior).</summary>
    public List<string?> Placed { get; set; } = new();
}

/// <summary>Per-actor mutable state. Position is the world-space root the
/// follower last advanced to; the loader will <c>WorldTransform</c>-replace
/// it. Animation pose is not captured — the next tick re-derives it.</summary>
public sealed class ActorSnapshot
{
    public uint   Scid          { get; set; }
    public string TemplateName  { get; set; } = "";
    public Vec3   Position      { get; set; }
    public float  CurrentLife   { get; set; }
    public float  CurrentMana   { get; set; }
    public bool   IsDead        { get; set; }
}

/// <summary>Player-character extras beyond what <see cref="ActorSnapshot"/>
/// covers: XP / level / auto-grown attribute trio, plus current camera /
/// facing so the resumed view picks up where the user left off.</summary>
public sealed class PlayerSnapshot
{
    public uint  Scid         { get; set; }
    public long  TotalXp      { get; set; }
    public int   Level        { get; set; }

    /// <summary>Auto-grown attributes from level-ups. Re-applied to the
    /// player's stats block on load so MaxLife/MaxMana stay consistent.</summary>
    public float Strength     { get; set; }
    public float Dexterity    { get; set; }
    public float Intelligence { get; set; }

    public Vec3  Facing       { get; set; }

    /// <summary>Camera mode index (0=fly, 1=chase) plus orbit params so a
    /// chase-cam resume comes back behind the player at the same yaw/zoom.</summary>
    public int   CameraMode      { get; set; }
    public float ChaseYaw        { get; set; }
    public float ChaseDistance   { get; set; }
    public float ChaseHeight     { get; set; }
    public Vec3  CameraPos       { get; set; }
    public float CameraYaw       { get; set; }
    public float CameraPitch     { get; set; }

    /// <summary>PC inventory contents — flat list of slot+template-ref pairs,
    /// same shape as drop-pile entries. Equipment lives in this list too;
    /// re-equipping happens on load by walking entries with a slot starting
    /// with <c>weapon_</c> through the equipment path.</summary>
    public List<LootEntrySnapshot> Inventory { get; set; } = new();

    /// <summary>Spellbook state (slotted spells + cooldowns). Null when no
    /// spellbook was active (a viewer-mode boot, headless test).</summary>
    public SpellbookSnapshot? Spellbook { get; set; }

    /// <summary>Phase 20b — quest journal entries. Empty when no quests have
    /// ever been activated (the common case for first-region saves).</summary>
    public List<QuestSnapshot> Quests { get; set; } = new();

    /// <summary>Phase 20d — player gold purse. Defaults to 0 so old v3 saves
    /// (which lacked the field) load with a broke PC, matching the v3 contract.</summary>
    public long Gold { get; set; }

    /// <summary>Phase 21d-2a-viii-c — player-typed hero name from the
    /// character creator. Empty string when the player skipped or cancelled
    /// the creator (env-var spawn path) or for v4-and-earlier loads.</summary>
    public string HeroName { get; set; } = "";

    /// <summary>Phase 21d-2a-viii-c — character creator variant pick. Null
    /// for v4-and-earlier loads or when the player cancelled the creator
    /// (env-var spawn falls through with no variant record). When present,
    /// the load path uses these values to rebuild the variant override
    /// instead of reading the env vars again.</summary>
    public HeroVariantSnapshot? Variant { get; set; }

    /// <summary>SC-WORLD-INVENTORY-CONSUMED — SCIDs of region-level
    /// inventory.gas placements the player has already picked up. Mirrors
    /// <c>RenderHost._consumedInventoryScids</c>. On load, world-inventory
    /// is re-derived from each region's inventory.gas; placements whose
    /// SCID is in this list are skipped so picked-up items stay gone.
    /// Empty list on v7-and-earlier loads — those saves predate the SCID-
    /// tracking and re-spawn every world inventory item on reload.</summary>
    public List<uint> ConsumedInventoryScids { get; set; } = new();
}

/// <summary>Phase 21d-2a-viii-c — frozen snapshot of the character creator's
/// picker state. Mirrors <c>HeroVariantPicker</c> but lives in Core so the
/// Runtime-side picker can be rebuilt without a back-reference.</summary>
public sealed class HeroVariantSnapshot
{
    /// <summary>"boy" or "girl" — matches <c>SIEGEFX_HERO_GENDER</c> values.</summary>
    public string Gender { get; set; } = "boy";

    /// <summary>Body axis index 0..6 corresponding to pos_a1..pos_a7. -1 = no
    /// override (template default).</summary>
    public int BodyTypeIdx { get; set; } = -1;

    /// <summary>Two-digit zero-padded skin suffix (e.g. "07"). Null = no override.</summary>
    public string? SkinSuffix { get; set; }

    /// <summary>Three-digit zero-padded pants suffix (e.g. "015"). Null = no override.</summary>
    public string? PantsSuffix { get; set; }
}

/// <summary>One journal entry as stored in a save. Mirrors
/// <see cref="QuestEntry"/> but only the fields that round-trip through JSON.
/// <see cref="Definition"/> isn't persisted — the loader rebinds it from the
/// live <c>QuestCatalog</c> so a content patch picks up new goal numbers.</summary>
public sealed class QuestSnapshot
{
    public string     Key          { get; set; } = "";
    public QuestState State        { get; set; } = QuestState.Active;
    public int        KillProgress { get; set; }

    /// <summary>SC-QUEST-OBJ-A — persisted talk-to-NPC counter. Mirrors
    /// <see cref="QuestEntry.TalkProgress"/>. Defaults to 0 so v6 saves load
    /// cleanly without losing any state.</summary>
    public int        TalkProgress { get; set; }

    /// <summary>SC-QUEST-OBJ-C — persisted pickup-objective counter. Mirrors
    /// <see cref="QuestEntry.PickupProgress"/>. Defaults to 0 so pre-v9
    /// saves load with no pickup progress (zero quests use it today).</summary>
    public int        PickupProgress { get; set; }
}

/// <summary>JSON-serializable Vector3 stand-in. <see cref="System.Numerics.Vector3"/>
/// uses field-not-property X/Y/Z which System.Text.Json doesn't pick up by
/// default, so we round-trip through this struct instead of pulling in a
/// custom converter.</summary>
public readonly record struct Vec3(float X, float Y, float Z)
{
    public static Vec3 From(Vector3 v) => new(v.X, v.Y, v.Z);
    public Vector3 ToVector3() => new(X, Y, Z);
}
