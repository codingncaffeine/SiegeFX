using System.Numerics;

namespace SiegeFX.Core.Actors;

/// <summary>
/// Per-actor state machine that wraps an <see cref="ActorFollower"/> and adds
/// aggro / chase / melee-swing on top of pure wander. Wander stays the default;
/// when a hostile target enters <see cref="AggroRadius"/> the brain redirects
/// the follower toward it, and once inside <see cref="ActorStats.AttackRange"/>
/// (or a fallback) the follower halts and a cooldown-gated swing rolls damage
/// through <see cref="CombatResolver"/> into the target's <see cref="ActorCombatState"/>.
///
/// Targeting is single-actor in Phase 16c — caller passes the PC's position
/// and combat state each tick; null means "no live target", which collapses
/// the brain back to wander. Friendly-fire (NPC-on-NPC) is out of scope.
///
/// Only <see cref="ActorStats.IsCombatant"/> actors should get a brain;
/// chickens and butterflies have DamageMax=0 and would fail the swing roll
/// anyway, but creating brains for them just wastes ticks.
/// </summary>
public sealed class ActorBrain
{
    public ActorFollower Wander { get; }
    public Vector3 Position => Wander.Position;

    /// <summary>Heading. Tracks the wander follower in Wander/Chase, and points
    /// at the latched target during Attack so the actor visually faces its
    /// victim while pausing to swing.</summary>
    public Vector3 Facing => _attackFacing ?? Wander.Facing;

    public enum BrainState { Wander, Chase, Attack }
    public BrainState State { get; private set; } = BrainState.Wander;

    /// <summary>SC-ENEMY-AUDIO-AUDIT runtime wire — one-shot edge that fires
    /// when the brain just resolved a melee swing (swing-cooldown reset +
    /// chore_attack triggered). Consumed by the render layer to fire the
    /// authored <c>[aspect][voice][attack]</c> cue. Only ~27 DS1 templates
    /// author attack — boss-tier/elite enemies — so the common case is the
    /// flag flips but PlayAttackVoiceSfx finds no cue and no-ops.</summary>
    public bool JustSwung { get; private set; }

    public bool ConsumeJustSwung()
    {
        if (!JustSwung) return false;
        JustSwung = false;
        return true;
    }

    /// <summary>XZ distance at which we transition Wander → Chase. ~8u is one
    /// krug-sized stride; the PC has to actively step into a mob's bubble.</summary>
    public float AggroRadius { get; set; } = 8f;

    /// <summary>Sticky-chase cutoff. Once aggro'd we follow past
    /// <see cref="AggroRadius"/>; only beyond this do we drop back to wander.
    /// Hysteresis prevents flicker at the radius boundary.</summary>
    public float DisengageRadius { get; set; } = 14f;

    /// <summary>Melee reach. Pulled from the actor's <see cref="ActorStats.AttackRange"/>
    /// when the template authors one (krug grunt = 1.8u); otherwise a 2u fallback.</summary>
    public float MeleeRange { get; }

    /// <summary>Seconds between swings while in Attack. 1.5s roughly matches
    /// the DS1 1H melee animation cadence; tuned by ear, not from gas.</summary>
    public float SwingPeriod { get; set; } = 1.5f;

    readonly ActorStats _selfStats;
    readonly Actor? _selfActor;
    readonly Random _swingRng;
    float _swingCooldown;
    Vector3? _attackFacing;

    public ActorBrain(ActorFollower wander, ActorStats selfStats, int rngSeed, Actor? selfActor = null)
    {
        Wander = wander;
        _selfStats = selfStats;
        _selfActor = selfActor;
        _swingRng = new Random(rngSeed);
        MeleeRange = selfStats.AttackRange > 0.1f ? selfStats.AttackRange : 2f;
        // First swing fires immediately on entering Attack (no warmup), then the
        // cooldown gates subsequent ones. Initial value is irrelevant since
        // Attack-state entry resets it; explicit zero documents the intent.
        _swingCooldown = 0f;
    }

    /// <summary>
    /// Step the brain. <paramref name="targetPos"/>/<paramref name="targetCombat"/>/<paramref name="targetStats"/>
    /// are the player; pass null when there is no live PC (no spawn, dead) and the brain
    /// will idle in wander.
    /// </summary>
    public void Tick(
        float dt,
        Vector3? targetPos,
        ActorCombatState? targetCombat,
        ActorStats? targetStats)
    {
        bool targetAlive = targetPos.HasValue && targetCombat is not null && !targetCombat.IsDead;
        float distXZ = targetAlive ? DistXZ(Wander.Position, targetPos!.Value) : float.PositiveInfinity;

        switch (State)
        {
            case BrainState.Wander:
                if (targetAlive && distXZ <= AggroRadius) EnterChase(targetPos!.Value);
                else { _attackFacing = null; Wander.Tick(dt); }
                break;

            case BrainState.Chase:
                if (!targetAlive || distXZ > DisengageRadius) { State = BrainState.Wander; _attackFacing = null; break; }
                if (distXZ <= MeleeRange) { EnterAttack(targetPos!.Value); break; }
                // Re-pin the follower target every tick — the player is a moving
                // goalpost, so a fire-and-forget SetTarget would have us chasing
                // a stale position. NavFollower replans on each SetTarget call.
                Wander.Follower.SetTarget(targetPos!.Value);
                Wander.Tick(dt);
                break;

            case BrainState.Attack:
                if (!targetAlive) { State = BrainState.Wander; _attackFacing = null; break; }
                // Step out of melee → fall back to chase. The 1.2x band is the
                // same hysteresis trick as Aggro/Disengage, scaled smaller because
                // melee distances are tight.
                if (distXZ > MeleeRange * 1.2f) { State = BrainState.Chase; _attackFacing = null; break; }
                FaceTarget(targetPos!.Value);
                _swingCooldown -= dt;
                if (_swingCooldown <= 0f)
                {
                    _swingCooldown = SwingPeriod;
                    if (targetStats is not null)
                    {
                        float raw = CombatResolver.RollMeleeDamage(_selfStats, targetStats, _swingRng);
                        targetCombat!.ApplyDamage(raw);
                    }
                    // Phase 12-SC-2 — play the swing chore for ~85% of the cooldown
                    // so the next swing's clip swap reads as a fresh strike instead
                    // of looping a still-running animation. Falls back silently if
                    // the template doesn't ship a chore_attack (chickens, props).
                    _selfActor?.PlayChoreOnce("chore_attack", SwingPeriod * 0.85f);
                    // SC-ENEMY-AUDIO-AUDIT — surface the swing event so the
                    // render layer can fire the attack voice cue if authored.
                    JustSwung = true;
                }
                break;
        }
    }

    /// <summary>Phase 19b — drop the actor at <paramref name="pos"/> and reset
    /// the brain back to Wander with cooldowns cleared. Saved+loaded actors
    /// always come back as Wander; chase/attack states are mid-fight bookkeeping
    /// not worth persisting (the AI will re-aggro on the next tick if the PC
    /// is still in radius).</summary>
    public void Teleport(Vector3 pos)
    {
        Wander.Teleport(pos);
        State = BrainState.Wander;
        _attackFacing = null;
        _swingCooldown = 0f;
    }

    void EnterChase(Vector3 targetPos)
    {
        State = BrainState.Chase;
        _attackFacing = null;
        Wander.Follower.SetTarget(targetPos);
    }

    void EnterAttack(Vector3 targetPos)
    {
        State = BrainState.Attack;
        FaceTarget(targetPos);
        // Fire the first swing on entry — feels more responsive than waiting a
        // full cooldown after the chase stops. Subsequent swings honor the gate.
        _swingCooldown = 0f;
    }

    void FaceTarget(Vector3 targetPos)
    {
        float dx = targetPos.X - Wander.Position.X;
        float dz = targetPos.Z - Wander.Position.Z;
        float len2 = dx * dx + dz * dz;
        if (len2 > 1e-6f)
        {
            float len = MathF.Sqrt(len2);
            _attackFacing = new Vector3(dx / len, 0f, dz / len);
        }
    }

    static float DistXZ(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
