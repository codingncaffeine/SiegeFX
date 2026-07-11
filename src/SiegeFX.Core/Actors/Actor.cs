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

    /// <summary>Phase 18 — every authored chore_attack sub-anim for the
    /// resolved stance EXCLUDING the trailing <c>qffg</c> entry (the
    /// quick-fighting fidget filler, held in <see cref="AttackPadClip"/>).
    /// DS1 picks uniformly at random among these per swing
    /// (job_attack_object_melee.skrit: <c>RandomInt(0, numSubAnims-2)</c>).
    /// Null / empty when the template authored a single sub-anim.</summary>
    public PrsAnimation[]? AttackVariants { get; internal set; }

    /// <summary>Phase 18 — the chore_attack <c>qffg</c> clip (usually the
    /// fighting-stance fidget <c>dff</c>): the filler DS1 blends in when the
    /// weapon's reload_delay stretches the attack period past the swing
    /// clip. Null when the template doesn't author one.</summary>
    public PrsAnimation? AttackPadClip { get; internal set; }

    /// <summary>Phase 18 — the authored <c>[anim_durations] fsN</c> value
    /// for the resolved stance (heroes author these; monsters don't).
    /// 0 = not authored, use the picked clip's own AnimLength. This is
    /// DS1's <c>GetBaseDuration(CHORE_ATTACK, stance)</c> input to the
    /// swing period.</summary>
    public float AttackBaseDuration { get; internal set; }

    /// <summary>Phase 18 — the fs# the attack set actually loaded at
    /// (after preferred-stance fallback). Diagnostic.</summary>
    public int AttackStance { get; internal set; } = -1;

    // Per-actor variant RNG, seeded from the placement so a given spawn's
    // swing sequence is stable across identical runs (same convention as
    // the loot roller).
    Random? _swingRng;

    /// <summary>Phase 18 fix — bumped every time combat swaps the CONTENT of
    /// a clip slot (next swing variant, cast variant, qffg pad). The render
    /// layer resets its per-actor anim clock when this changes: the
    /// index-only change detection can't see a swap that reuses the same
    /// slot, which left back-to-back swings starting with the clock clamped
    /// at the previous clip's end — the "robotic" frozen swing.</summary>
    public int ClipEpoch { get; private set; }

    /// <summary>Phase 18 — pick the next attack clip DS1-style: uniform
    /// random over the non-qffg variants, published into
    /// <c>Clips[chore_attack]</c> for the render layer. Returns the clip
    /// (null when the template ships no attack chore).</summary>
    public PrsAnimation? PickNextSwingClip()
    {
        int idx = GetClipIndex("chore_attack");
        if (idx < 0) return null;
        ClipEpoch++;
        var variants = AttackVariants;
        if (variants is { Length: > 0 })
        {
            _swingRng ??= new Random(unchecked((int)Instance.Scid ^ 0x5157_4E47));
            var pick = variants[_swingRng.Next(variants.Length)];
            Clips[idx] = pick;
            return pick;
        }
        return Clips[idx];
    }

    /// <summary>Phase 18 — swap the active chore_attack slot to the qffg pad
    /// clip (between-swings fighting fidget). Returns false when none is
    /// authored — callers then just end-hold the swing clip.</summary>
    public bool SwapToPadClip()
    {
        int idx = GetClipIndex("chore_attack");
        if (idx < 0 || AttackPadClip is null) return false;
        ClipEpoch++;
        Clips[idx] = AttackPadClip;
        return true;
    }

    /// <summary>Phase 19 — every authored chore_magic sub-anim (farmboy: mg +
    /// mg-02) at the resolved casting stance. Null/empty = single or no clip.</summary>
    public PrsAnimation[]? MagicVariants { get; internal set; }

    /// <summary>Phase 19 — pick the next cast clip (uniform random over the
    /// authored mg variants), published into <c>Clips[chore_magic]</c>.
    /// Returns null when the template ships no magic chore.</summary>
    public PrsAnimation? PickNextCastClip()
    {
        int idx = GetClipIndex("chore_magic");
        if (idx < 0) return null;
        ClipEpoch++;
        var variants = MagicVariants;
        if (variants is { Length: > 0 })
        {
            _swingRng ??= new Random(unchecked((int)Instance.Scid ^ 0x5157_4E47));
            var pick = variants[_swingRng.Next(variants.Length)];
            Clips[idx] = pick;
            return pick;
        }
        return Clips[idx];
    }
}
