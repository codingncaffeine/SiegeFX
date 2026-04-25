using System.Numerics;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Skrit;

namespace SiegeFX.Core.Actors;

/// <summary>One live actor in a region — the runtime union of (template archetype,
/// placed instance, loaded mesh + anims, per-instance Skrit VM). Headless by design:
/// everything GL lives in the Runtime's actor-render layer. The viewer grabs
/// <see cref="WorldTransform"/> and <see cref="CurrentClipIndex"/> every frame to
/// decide what to draw where; the skrit bridge inside <see cref="Host"/> keeps
/// producing those values as its VM ticks.</summary>
public sealed class Actor
{
    public ActorInstance Instance { get; }
    public Template Template { get; }

    /// <summary>World-space pose baked from the region layout's node transform,
    /// the placement's node-local position, and its orientation quaternion. Constant
    /// for the lifetime of the actor in Phase 10 — movement (pathing) is Phase 11.</summary>
    public Matrix4x4 WorldTransform { get; }

    public AspMesh Mesh { get; }

    /// <summary>Clip catalogue driven by the skrit. Index 0 is the single chore_default
    /// clip for Phase 10c; Phase 11+ will layer the rest of the chore dictionary here.</summary>
    public PrsAnimation[] Clips { get; }

    public SkritInstance Skrit { get; }
    public ActorHostBridge Host { get; }

    /// <summary>Combat-relevant stats pulled from the specializes chain at spawn
    /// (Phase 12a). Non-combatants (chickens, props) come through with
    /// <see cref="ActorStats.IsCombatant"/> false; the combat pipeline skips them.
    /// Mutable via <see cref="ResyncStats"/> so the player's progression layer can
    /// publish auto-grown attributes after a level-up.</summary>
    public ActorStats Stats { get; private set; }

    /// <summary>Mutable combat runtime (Phase 12b). Current life/mana + death edge.
    /// Seeded from <see cref="Stats"/> so every actor starts at full health; combat
    /// code mutates via <see cref="ActorCombatState.ApplyDamage"/>.</summary>
    public ActorCombatState Combat { get; }

    /// <summary>Current blender-selected clip. Clamped to the legal range so the caller
    /// can index <see cref="Clips"/> blindly; defaults to 0 before the first dispatch
    /// resolves.</summary>
    public int CurrentClipIndex =>
        Host.CurrentAnimIndex < 0 || Host.CurrentAnimIndex >= Clips.Length ? 0 : Host.CurrentAnimIndex;

    internal Actor(
        ActorInstance instance,
        Template template,
        Matrix4x4 worldTransform,
        AspMesh mesh,
        PrsAnimation[] clips,
        SkritInstance skrit,
        ActorHostBridge host,
        ActorStats stats)
    {
        Instance = instance;
        Template = template;
        WorldTransform = worldTransform;
        Mesh = mesh;
        Clips = clips;
        Skrit = skrit;
        Host = host;
        Stats = stats;
        Combat = new ActorCombatState(stats);
    }

    public override string ToString() =>
        $"Actor({Template.Name} scid=0x{Instance.Scid:x8} pos={WorldTransform.Translation})";

    /// <summary>Replace the actor's stats block. Used by <c>PlayerProgression</c>
    /// after a level-up to publish new STR/DEX/INT and the recomputed MaxLife/MaxMana.
    /// Forwarded to <see cref="ActorCombatState"/> so its max clamps see the new
    /// values immediately. Does not heal — current life/mana ride through unchanged.</summary>
    public void ResyncStats(ActorStats newStats)
    {
        Stats = newStats;
        Combat.ResyncStats(newStats);
    }
}
