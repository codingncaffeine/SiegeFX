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

    /// <summary>Clip catalogue driven by the skrit. Index 0 is always chore_default
    /// (the idle the renderer falls back to). Phase 10-SC-2: every other chore_* section
    /// the template authors lands here too — chore_walk, chore_die, chore_attack,
    /// chore_fidget, chore_magic, chore_misc, chore_get_hit, chore_pickup, chore_cast,
    /// etc. <see cref="ClipIndexByName"/> is the lookup; <see cref="GetClipIndex"/> is
    /// the convenience wrapper.</summary>
    public PrsAnimation[] Clips { get; }

    /// <summary>Phase 10-SC-2 — chore-section name → index into <see cref="Clips"/>.
    /// Keys are the GAS section header verbatim (<c>chore_die</c>, <c>chore_attack</c>,
    /// …) and the lookup is case-insensitive. Built once at spawn from whatever sections
    /// resolved to a loadable PRS; missing sections are absent (not -1 entries) so a
    /// caller using <see cref="GetClipIndex"/> can branch on -1 to mean "this actor has
    /// no death anim, fall back".</summary>
    public IReadOnlyDictionary<string, int> ClipIndexByName { get; }

    /// <summary>Returns the <see cref="Clips"/> index for <paramref name="choreName"/>
    /// (e.g. <c>"chore_die"</c>) or -1 if this actor's template doesn't ship that chore
    /// or its PRS failed to load. Combat / loot / death code address chores by name and
    /// branch to the bind-pose fallback on -1.</summary>
    public int GetClipIndex(string choreName) =>
        ClipIndexByName.TryGetValue(choreName, out var i) ? i : -1;

    /// <summary>Phase 21c-4 — index into <see cref="Clips"/> for the walk cycle, or -1
    /// if the template doesn't author a chore_walk (or its PRS failed to load). The
    /// renderer reads this to decide whether to swap from idle to walk while the
    /// actor is moving.</summary>
    public int WalkClipIndex { get; }

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
        ActorStats stats,
        int walkClipIndex = -1,
        IReadOnlyDictionary<string, int>? clipIndexByName = null)
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
        WalkClipIndex = walkClipIndex;
        ClipIndexByName = clipIndexByName
            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>Phase 12-SC-2 — pin <paramref name="choreName"/> on top of the
    /// skrit blender for <paramref name="durationSec"/> seconds. No-op if the
    /// template doesn't ship that chore. Used by combat code so a swing actually
    /// plays chore_attack instead of staying frozen on the default idle.</summary>
    public void PlayChoreOnce(string choreName, float durationSec)
    {
        int idx = GetClipIndex(choreName);
        if (idx < 0) return;
        Host.OverrideAnimIndex(idx, durationSec);
    }

    /// <summary>Phase 21-SC-BARREL-FOLD — DS1 chore_attack ships up to 5
    /// sub-anims per stance (0mid / high / loww / extr / qffg). The Skrit
    /// runtime's <c>select_attack</c> picks among them per swing — alternating
    /// 0mid + high gives the "horizontal R→L / backhand L→R" cadence the
    /// player expects. <see cref="AttackVariants"/> stores each loaded
    /// sub-anim PRS for the player template's resolved stance, parallel to
    /// the single-clip slot in <see cref="Clips"/>; <see cref="SwingIndex"/>
    /// counts swings and lets PerformPlayerSwing rotate which variant to
    /// publish into <c>Clips[chore_attack_idx]</c> before <see cref="PlayChoreOnce"/>
    /// fires. Empty / null when the template only authored one sub-anim.</summary>
    public PrsAnimation[]? AttackVariants { get; internal set; }

    /// <summary>Phase 21-SC-BARREL-FOLD — running swing counter; rotates the
    /// active <see cref="AttackVariants"/> entry on each successful melee
    /// swing. Wraps modulo the variant count so the alternation stays even.</summary>
    public int SwingIndex { get; internal set; }

    /// <summary>Phase 21-SC-BARREL-FOLD — call before each swing to swap
    /// the currently-active chore_attack clip to the next variant. No-op if
    /// the template only loaded a single attack sub-anim. Returns the
    /// authored AnimLength of the picked variant so the caller can pass it
    /// straight to <see cref="PlayChoreOnce"/> (the Phase 12 hardcoded 0.6s
    /// truncated DS1's 0.83s fs1 swing by ~27%).</summary>
    public float PrepNextSwingClip()
    {
        // Pad the swing-clip duration to give the post-swing pose a beat
        // before the override expires. NPC brains pass SwingPeriod * 0.85
        // (≈1.28s for SwingPeriod=1.5) for the same reason — without the
        // pad the clip plays in 0.83s and snaps straight back to idle,
        // which reads as "sped up" because there's no follow-through.
        // 1.5x matches the NPC cadence on a 0.83s clip (0.83 * 1.5 ≈ 1.25)
        // and lets the per-actor advance loop hold the final frame via
        // its non-dead end-hold path while the override drains.
        const float RecoveryPadding = 1.5f;
        int idx = GetClipIndex("chore_attack");
        if (idx < 0) return 0.6f;
        var variants = AttackVariants;
        if (variants is not null && variants.Length > 0)
        {
            var pick = variants[SwingIndex % variants.Length];
            SwingIndex++;
            Clips[idx] = pick;
            return (pick.AnimLength > 0f ? pick.AnimLength : 0.6f) * RecoveryPadding;
        }
        var current = Clips[idx];
        return (current.AnimLength > 0f ? current.AnimLength : 0.6f) * RecoveryPadding;
    }
}
