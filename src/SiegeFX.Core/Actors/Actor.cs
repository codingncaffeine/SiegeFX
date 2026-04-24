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
    /// <see cref="ActorStats.IsCombatant"/> false; the combat pipeline skips them.</summary>
    public ActorStats Stats { get; }

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
    }

    public override string ToString() =>
        $"Actor({Template.Name} scid=0x{Instance.Scid:x8} pos={WorldTransform.Translation})";
}
