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
    ///   v9 -> v10: added <see cref="World"/> (ALPHA-2G: named world bools,
    ///              accumulate-trigger progress, opened chests, unlocked
    ///              usables, lever states, message-broken props, cleared
    ///              path blockers, elevator stops) and
    ///              <see cref="QuestSnapshot.DialogueLog"/>.
    ///   v10 -> v11: added <see cref="DisplayName"/> (the player-typed save
    ///              label shown in the Save Game window's list) and
    ///              <see cref="NextTipIndex"/>/<see cref="TipsDisabled"/>
    ///              (Adventurer's Handbook auto-popup progress).
    ///   v11 -> v12: added the Load Game window's preview fields —
    ///              <see cref="HeroName"/>, <see cref="MapName"/>,
    ///              <see cref="ElapsedSeconds"/> (play-clock) and
    ///              <see cref="Thumbnail"/> (a small PNG screenshot captured
    ///              at save time). All default-friendly.
    ///   v12 -> v13: added <see cref="Party"/> (SC-PARTY-PERSIST — recruited
    ///              companions: roster order, per-companion backpack +
    ///              equipment, vitals) so the party survives save/load.
    /// All bumps are deserializer-friendly — missing fields hit their defaults —
    /// so any v1..v12 file loads as a v13 with the new fields zero-initialized.
    /// IMPORTANT: bumping CurrentSchemaVersion requires extending the
    /// migration whitelist in SaveStore.Load too; the strict-equality check
    /// downstream throws InvalidDataException on any unmigrated version.</summary>
    public const int CurrentSchemaVersion = 13;

    /// <summary>Schema version of the file as written. Loader rejects when
    /// this doesn't match <see cref="CurrentSchemaVersion"/>.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Wall-clock time the save was written. Surfaced in save-pick
    /// UI later; today it just helps debug "which save am I looking at".</summary>
    public DateTime SavedAt { get; set; }

    /// <summary>v11 — the player-typed label shown in the Save Game window's
    /// list (e.g. "My hero"). Empty on pre-v11 saves and on quicksaves, in
    /// which case the UI falls back to the file stem. The list row renders as
    /// <c>DisplayName (SavedAt)</c>.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>v11 — Adventurer's Handbook progress: index of the next
    /// ordered tip (1..14) that should auto-pop as the player advances through
    /// the early game. 0 = intro not started; 15+ = all tips shown. Defaults
    /// to 0 so pre-v11 saves resume the handbook from the top (harmless — the
    /// player can dismiss or disable it). See RenderHost handbook wiring.</summary>
    public int NextTipIndex { get; set; }

    /// <summary>v11 — the "Disable tips" checkbox state, persisted so an
    /// annoyed player never sees an auto-popup again after ticking it. F12
    /// still recalls the handbook manually regardless.</summary>
    public bool TipsDisabled { get; set; }

    /// <summary>Region path this save is anchored to (e.g.
    /// <c>world/maps/multiplayer_world/regions/town_center</c>). Loader
    /// refuses if the active region doesn't match — trying to splice
    /// fh_r1 actor scids into a different region would hit-or-miss.</summary>
    public string RegionPath { get; set; } = "";

    /// <summary>SC-SAVE-REGION — the region the PLAYER was actually in at
    /// save time (RegionPath above is the session's coordinate-frame root;
    /// the two differ once the player travels). Informational + anchor hint;
    /// empty on saves from before the field.</summary>
    public string PlayerRegion { get; set; } = "";

    /// <summary>SC-SAVE-AUDIT — the campaign difficulty (GameDifficulty enum
    /// name) at save time. Empty = pre-field save; the loader then keeps the
    /// session's current difficulty (the old behavior, which silently reset
    /// a Hard campaign to Normal on a frontend load).</summary>
    public string Difficulty { get; set; } = "";

    /// <summary>v12 — the hero's name, mirrored from
    /// <see cref="PlayerSnapshot.HeroName"/> up to the top level so the Load
    /// Game window's lightweight header read can show "HERO: X" without
    /// deserializing the whole player payload. Empty on pre-v12 / nameless.</summary>
    public string HeroName { get; set; } = "";

    /// <summary>v12 — friendly map name for the Load window's "MAP:" line
    /// (e.g. "Kingdom of Ehb"), derived from <see cref="RegionPath"/> at save
    /// time. Empty on pre-v12 saves (the UI falls back to the region stem).</summary>
    public string MapName { get; set; } = "";

    /// <summary>v12 — total played time in seconds, shown as the Load window's
    /// "ELAPSED TIME: h:mm:ss". Accumulated only while the sim runs (paused /
    /// modal time doesn't count). 0 on pre-v12 saves.</summary>
    public double ElapsedSeconds { get; set; }

    /// <summary>v12 — a small screenshot of the scene captured when the Save
    /// window opened, shown in the Load window's preview box (DS1 stores a
    /// per-save thumbnail there). Encoded as raw RGBA with an 8-byte header —
    /// <c>[width:int32 LE][height:int32 LE][rgba…]</c> — so the UI can upload it
    /// straight to a texture without a PNG decoder (see <c>ThumbnailCodec</c>).
    /// Null on pre-v12 saves and quicksaves with no framebuffer to grab; the UI
    /// draws a placeholder then. Base64 in the JSON (~96×72 ≈ 36 KB).</summary>
    public byte[]? Thumbnail { get; set; }

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

    /// <summary>ALPHA-2G — cross-region world state that a 20h+ campaign run
    /// accumulates: quest-gating booleans, one-shot gizmo progress, opened
    /// containers, unlocked mechanisms, cleared blockers, lift positions.
    /// Null in pre-v10 saves.</summary>
    public WorldStateSnapshot? World { get; set; }

    /// <summary>v13 SC-PARTY-PERSIST — recruited companions in roster order
    /// (leader/player excluded; they're <see cref="Player"/>). Empty on
    /// pre-v13 saves and solo runs. On load each entry re-recruits its
    /// actor (matched by scid when it exists in the region, respawned from
    /// the template when it doesn't) and restores its backpack/equipment.</summary>
    public List<CompanionSnapshot> Party { get; set; } = new();
}

/// <summary>v13 SC-PARTY-PERSIST — one recruited companion.</summary>
public sealed class CompanionSnapshot
{
    public uint   Scid          { get; set; }
    public string TemplateName  { get; set; } = "";
    public int    PartyIndex    { get; set; }
    public Vec3   Position      { get; set; }
    public float  CurrentLife   { get; set; }
    public float  CurrentMana   { get; set; }
    /// <summary>The companion's backpack (GetMemberInventory list).</summary>
    public List<LootEntrySnapshot> Inventory { get; set; } = new();
    /// <summary>Worn-slot deltas (es_* → template ref), mirroring
    /// _memberEquipment. Empty = template defaults.</summary>
    public Dictionary<string, string> Equipment { get; set; } = new();
    /// <summary>SC-COMPANION-PROGRESSION — the member's earned XP pools.
    /// Zero/empty on saves written before the field existed; the load path
    /// then keeps the authored starting levels the recruit re-seed applied.</summary>
    public long TotalXp { get; set; }
    public List<long> SkillXp { get; set; } = new();
    /// <summary>SC-COMPANION-SPELLBOOK — the member's own spell panel:
    /// active slot 1/2 spell template names + the 10 placed rows (nulls as
    /// empty strings). All empty on saves written before the field existed —
    /// the load path then leaves the authored kit behavior untouched.</summary>
    public string PrimarySpell   { get; set; } = "";
    public string SecondarySpell { get; set; } = "";
    public List<string> PlacedSpells { get; set; } = new();
    /// <summary>SC-FC-ORDERS — the member's standing field-command orders
    /// (0/1/2 within each authored radio group: movement free/engage/hold,
    /// attack free/fightback/holdfire, target closest/strongest/weakest).
    /// -1 = save predates the field; the load path then keeps the DS1
    /// defaults (Engage / Defend / Target Closest).</summary>
    public int  MoveOrder { get; set; } = -1;
    public int  AtkOrder  { get; set; } = -1;
    public int  TgtOrder  { get; set; } = -1;
    public bool FollowOn  { get; set; } = true;
    /// <summary>SC-MEMBER-ACTIVE-SLOT — the member's selected combat slot
    /// (0 melee / 1 ranged / 2 Active Spell 1 / 3 Active Spell 2; -1 = auto
    /// or pre-field save).</summary>
    public int ActiveSlot { get; set; } = -1;
}

/// <summary>ALPHA-2G — see <see cref="SaveFile.World"/>.</summary>
public sealed class WorldStateSnapshot
{
    public Dictionary<string, bool> Bools { get; set; } = new();
    public List<AccumSnapshot> Accumulators { get; set; } = new();
    public List<uint> OpenedChests { get; set; } = new();
    public List<uint> UnlockedUsables { get; set; } = new();
    public List<uint> LeversOn { get; set; } = new();
    public List<uint> BrokenProps { get; set; } = new();
    public List<uint> ClearedBlockers { get; set; } = new();
    public List<ElevatorStopSnapshot> Elevators { get; set; } = new();
    /// <summary>SC-WORLD-SCRIPT-PERSIST — full trigger-runtime state so
    /// one-shot story choreography doesn't re-arm or replay on load.
    /// Empty on saves written before the field existed (triggers then
    /// boot at their authored defaults, the old behavior).</summary>
    public List<TriggerStateSnapshot> Triggers { get; set; } = new();
    /// <summary>SC-FC-ORDERS — the party's active formation
    /// (PartyFormation enum name). Empty = save predates the field;
    /// the load path keeps the DoubleColumn default.</summary>
    public string PartyFormation { get; set; } = "";
    /// <summary>SC-PARTY-LIFECYCLE — companions the player has paid the hire
    /// cost for at least once. A re-invite after disband stays free across
    /// save/load.</summary>
    public List<uint> HiredScids { get; set; } = new();
}

public sealed class AccumSnapshot
{
    public uint Scid { get; set; }
    public int Count { get; set; }
    public bool Fired { get; set; }
}

public sealed class ElevatorStopSnapshot
{
    public uint Scid { get; set; }
    public int AtStop { get; set; } = 1;
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
    /// <summary>SC-WORLD-SCRIPT-PERSIST — scripted presentation state: an
    /// actor hidden by a trigger/NIS stays hidden across loads.</summary>
    public bool   Hidden        { get; set; }
    /// <summary>SC-WORLD-SCRIPT-PERSIST — a long-pinned override animation
    /// (scripted death poses, NIS end-frame holds; -1 = none). Restored by
    /// re-pinning the same clip index so one-time story outcomes — the
    /// intro's dying NPC, any future set-piece — survive save/quit/load
    /// without per-case code.</summary>
    public int    PinnedAnim    { get; set; } = -1;
}

/// <summary>SC-WORLD-SCRIPT-PERSIST — one trigger instance's mutable state:
/// activation, fired one-shot rows, held condition edges, and any pending
/// delayed actions (by flattened action index) with their remaining time.</summary>
public sealed class TriggerStateSnapshot
{
    public uint Scid { get; set; }
    public bool IsActive { get; set; }
    public List<int> FiredRows { get; set; } = new();
    public List<int> HeldRows { get; set; } = new();
    public List<DelayedActionSnapshot> Delayed { get; set; } = new();
}

public sealed class DelayedActionSnapshot
{
    public int    FlatIndex    { get; set; }
    public double RemainingSec { get; set; }
}

/// <summary>Player-character extras beyond what <see cref="ActorSnapshot"/>
/// covers: XP / level / auto-grown attribute trio, plus current camera /
/// facing so the resumed view picks up where the user left off.</summary>
public sealed class PlayerSnapshot
{
    public uint  Scid         { get; set; }
    public long  TotalXp      { get; set; }
    public int   Level        { get; set; }

    /// <summary>SC-SUMMON-UI — the player's live summon (0 = none). Its
    /// actor row lives in Actors[]; the end script is the authored
    /// un_summon farewell replayed on dismiss after load.</summary>
    public uint   SummonScid      { get; set; }
    public string SummonEndScript { get; set; } = "";

    /// <summary>Auto-grown attributes from level-ups. Re-applied to the
    /// player's stats block on load so MaxLife/MaxMana stay consistent.</summary>
    public float Strength     { get; set; }
    public float Dexterity    { get; set; }
    public float Intelligence { get; set; }

    /// <summary>Per-skill XP pools (Melee, Ranged, NatureMagic, CombatMagic).
    /// Empty on pre-persistence saves — the loader then seeds each skill to the
    /// character level so attribute growth resumes without re-granting gains
    /// already baked into <see cref="Strength"/>/<see cref="Dexterity"/>/<see
    /// cref="Intelligence"/>.</summary>
    public List<long> SkillXp { get; set; } = new();

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

    // SC-CD-PERSIST (v14) — the creator's COMPOSITE axes (the look system
    // that actually drives the in-game body textures: skin/face tone,
    // hairstyle overlay, hair tint, shirt/pants overlays). -1 = not present
    // (pre-v14 save) — the loader then falls back to the legacy suffix
    // fields above. These were the documented v1 follow-up ("appearance
    // resets on load") — a load used to silently drop face/hair/shirt/pants.
    public int FaceIdx  { get; set; } = -1;
    public int StyleIdx { get; set; } = -1;
    public int ColorIdx { get; set; } = -1;
    public int ShirtIdx { get; set; } = -1;
    public int PantsIdx { get; set; } = -1;
}

/// <summary>One journal entry as stored in a save. Mirrors
/// <see cref="QuestEntry"/> but only the fields that round-trip through JSON.
/// <see cref="Definition"/> isn't persisted — the loader rebinds it from the
/// live <c>QuestCatalog</c> so a content patch picks up new goal numbers.</summary>
public sealed class QuestSnapshot
{
    public string     Key          { get; set; } = "";
    public QuestState State        { get; set; } = QuestState.Active;
    /// <summary>ALPHA-2G — the journal's recorded giver conversation for the
    /// Show Dialogue view; previously session-only.</summary>
    public List<string> DialogueLog { get; set; } = new();
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
