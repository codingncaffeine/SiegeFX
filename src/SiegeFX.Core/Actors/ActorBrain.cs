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

    /// <summary>SC-MOB-CASTER / SC-MOB-RANGED — attack-mode identity,
    /// parameter-driven the way DS1's single shared brain skrit is: WP_MAGIC
    /// templates with a resolvable il_active_primary_spell cast from standoff
    /// range; WP_RANGED / il_active_ranged_weapon templates fire projectiles
    /// from standoff range; everything else fights melee. Casters/throwers
    /// fall back to melee inside the inner comfort zone when the template
    /// authors on_enemy_entered_icz_switch_to_melee.</summary>
    public enum AttackMode { Melee, Ranged, Magic }
    public AttackMode Mode { get; }
    public Assets.SpellTemplate? CastSpell { get; }

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

    /// <summary>One-shot edge + target position for the render layer's spell
    /// visual (beam/projectile from caster to victim). Set when a Magic-mode
    /// brain lands a cast.</summary>
    public bool ConsumeJustCast(out Vector3 target)
    {
        target = _lastFireTarget;
        if (!_justCast) return false;
        _justCast = false;
        return true;
    }

    /// <summary>One-shot edge + target position for the render layer's thrown
    /// projectile (krug rock etc.). Set when a Ranged-mode brain fires.</summary>
    public bool ConsumeJustFiredRanged(out Vector3 target)
    {
        target = _lastFireTarget;
        if (!_justFiredRanged) return false;
        _justFiredRanged = false;
        return true;
    }

    bool _justCast;
    bool _justFiredRanged;
    Vector3 _lastFireTarget;

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

    /// <summary>Ranged/Magic engagement distance — the brain stops chasing and
    /// starts firing here. Melee brains never read it.</summary>
    public float StandoffRange { get; }

    float EngageRange => Mode == AttackMode.Melee ? MeleeRange : StandoffRange;

    /// <summary>Seconds between swings while in Attack. 1.5s roughly matches
    /// the DS1 1H melee animation cadence; tuned by ear, not from gas.</summary>
    public float SwingPeriod { get; set; } = 1.5f;

    readonly ActorStats _selfStats;
    readonly Actor? _selfActor;
    readonly Random _swingRng;
    float _swingCooldown;
    Vector3? _attackFacing;

    public ActorBrain(ActorFollower wander, ActorStats selfStats, int rngSeed,
                      Actor? selfActor = null, Assets.SpellTemplate? castSpell = null)
    {
        Wander = wander;
        _selfStats = selfStats;
        _selfActor = selfActor;
        _swingRng = new Random(rngSeed);
        MeleeRange = selfStats.AttackRange > 0.1f ? selfStats.AttackRange : 2f;
        CastSpell = castSpell;
        if (castSpell is not null)
        {
            Mode = AttackMode.Magic;
            // Stand off inside the spell's reach with a small buffer so tiny
            // target drift doesn't immediately break the range gate.
            StandoffRange = MathF.Max(4f, castSpell.CastRange * 0.9f);
        }
        else if (string.Equals(selfStats.WeaponPreference, "WP_RANGED", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(selfStats.ActiveLocation, "il_active_ranged_weapon", StringComparison.OrdinalIgnoreCase))
        {
            Mode = AttackMode.Ranged;
            StandoffRange = selfStats.RangedEngageRange > 1f ? selfStats.RangedEngageRange * 0.8f : 10f;
        }
        else
        {
            Mode = AttackMode.Melee;
        }
        // SC-MOB-RANGES — authored perception wins over the tuned defaults.
        // base_krug ships sight_range=14 (the old hardcoded 8u bubble made
        // mobs oblivious until the player was almost on top of them).
        // Disengage keeps the same sticky-chase ratio the defaults had.
        if (selfStats.SightRange > 0.1f)
        {
            AggroRadius = selfStats.SightRange;
            DisengageRadius = MathF.Max(selfStats.SightRange * 1.75f, selfStats.SightRange + 4f);
        }
        // [attack] reload_delay is the authored swing cadence (krug_throw = 1.0s).
        // Values under half a second are "no extra delay" markers on melee
        // templates whose cadence DS1 derives from the animation — keep the
        // 1.5s animation-matched default there.
        if (selfStats.ReloadDelay >= 0.5f) SwingPeriod = selfStats.ReloadDelay;
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
                if (distXZ <= EngageRange) { EnterAttack(targetPos!.Value); break; }
                // Re-pin the follower target every tick — the player is a moving
                // goalpost, so a fire-and-forget SetTarget would have us chasing
                // a stale position. NavFollower replans on each SetTarget call.
                Wander.Follower.SetTarget(targetPos!.Value);
                Wander.Tick(dt);
                break;

            case BrainState.Attack:
                if (!targetAlive) { State = BrainState.Wander; _attackFacing = null; break; }
                // Melee when that's the mode, or when a caster/thrower with the
                // icz failsafe has the target inside its inner comfort zone.
                bool meleeNow = Mode == AttackMode.Melee ||
                                (_selfStats.IczSwitchToMelee && distXZ <= MeleeRange);
                // Step out of reach → fall back to chase. The 1.2x band is the
                // same hysteresis trick as Aggro/Disengage, scaled smaller because
                // melee distances are tight; standoff modes get a bit more slack.
                float holdRange = meleeNow ? MeleeRange * 1.2f : StandoffRange * 1.15f;
                if (distXZ > holdRange) { State = BrainState.Chase; _attackFacing = null; break; }
                FaceTarget(targetPos!.Value);
                _swingCooldown -= dt;
                if (_swingCooldown <= 0f)
                {
                    if (meleeNow)
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
                    else if (Mode == AttackMode.Magic)
                    {
                        CastAtTarget(targetPos!.Value, targetCombat!, targetStats);
                    }
                    else
                    {
                        // Ranged: same damage table as melee (the [attack] block
                        // IS the thrown-rock damage on krug_throw), projectile
                        // visual dispatched by the render layer off the edge.
                        _swingCooldown = SwingPeriod;
                        if (targetStats is not null)
                        {
                            float raw = CombatResolver.RollMeleeDamage(_selfStats, targetStats, _swingRng);
                            targetCombat!.ApplyDamage(raw);
                        }
                        _selfActor?.PlayChoreOnce("chore_attack", SwingPeriod * 0.85f);
                        JustSwung = true;
                        _justFiredRanged = true;
                        _lastFireTarget = targetPos!.Value;
                    }
                }
                break;
        }
    }

    /// <summary>Magic-mode fire: mirrors PlayerSpellbook.TryCast's gating
    /// (mana → roll → apply) with the caster's authored INT standing in for
    /// the player's magic level. Instant-hit like the player path — the
    /// projectile the render layer spawns is cosmetic. A dry mana pool
    /// retries shortly instead of consuming the whole cast cooldown.</summary>
    void CastAtTarget(Vector3 targetPos, ActorCombatState targetCombat, ActorStats? targetStats)
    {
        var spell = CastSpell!;
        var self = _selfActor;
        if (self is null || targetStats is null) { _swingCooldown = 1f; return; }

        float magicLevel = MathF.Max(1f, _selfStats.Intelligence);
        var costCtx = new Assets.SpellEvalContext(magicLevel,
            maxLife: _selfStats.MaxLife,
            life:    self.Combat.CurrentLife,
            srcMana: self.Combat.CurrentMana,
            srcLife: self.Combat.CurrentLife);
        float cost = spell.ManaCost(costCtx);
        if (self.Combat.CurrentMana < cost)
        {
            _swingCooldown = 1f;
            return;
        }
        self.Combat.SpendMana(cost);

        var dmgCtx = new Assets.SpellEvalContext(magicLevel,
            maxLife: targetStats.MaxLife,
            life:    targetCombat.CurrentLife,
            srcMana: self.Combat.CurrentMana,
            srcLife: self.Combat.CurrentLife);
        float damage = spell.RollDamage(dmgCtx, _swingRng);
        if (damage > 0f) targetCombat.ApplyDamage(damage);

        _swingCooldown = MathF.Max(0.75f, spell.CastReloadDelay);
        _selfActor?.PlayChoreOnce("chore_magic", _swingCooldown * 0.85f);
        // Deliberately NOT JustSwung — casts fire the authored 'cast' voice
        // state (123 DS1 templates), not the melee 'attack' cue.
        _justCast = true;
        _lastFireTarget = targetPos;
    }

    /// <summary>SC-MOB-PARTY — alert-friends hook. A packmate that spotted the
    /// enemy forces this brain out of Wander into Chase toward the target.
    /// No-op unless idle: an already-fighting brain keeps its own state, and
    /// the next Tick's disengage check still applies (an alert can't make a
    /// mob chase something beyond its own leash).</summary>
    public void ForceAggro(Vector3 targetPos)
    {
        if (State != BrainState.Wander) return;
        EnterChase(targetPos);
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
