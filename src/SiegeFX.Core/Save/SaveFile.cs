using System.Numerics;

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
    public const int CurrentSchemaVersion = 1;

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
