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

    public enum BrainState { Wander, Chase, Attack, Flee }
    public BrainState State { get; private set; } = BrainState.Wander;

    // SC-AI-FLEE — authored [mind] flee-at-low-health. 9 evil template
    // families author on_life_ratio_low_flee=true with flee_distance (goblin
    // 20, krug 21) and actor_life_ratio_low_threshold (0.25-0.75; engine
    // default 0.33). flee_count limits how many times the actor runs before
    // it fights to the death (authored 1 everywhere it appears).
    bool  _fleeEnabled;
    float _fleeThreshold = 0.33f;
    int   _fleeChargesLeft;
    float _fleeDistance = 20f;
    float _fleeTimer;

    /// <summary>Enable the authored flee-at-low-health behavior
    /// ([mind] on_life_ratio_low_flee). Call once at brain construction.</summary>
    public void ConfigureFlee(float lifeRatioThreshold, int fleeCount, float fleeDistance)
    {
        _fleeEnabled = fleeCount > 0;
        _fleeThreshold = Math.Clamp(lifeRatioThreshold, 0.01f, 0.95f);
        _fleeChargesLeft = fleeCount;
        _fleeDistance = MathF.Max(4f, fleeDistance);
    }

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

    /// <summary>Phase 26 — drop the brain to a neutral, non-combat state
    /// without ticking its wander. Party followers call this while they are
    /// moving to a formation slot (their host drives the nav follower
    /// directly), so a stale Chase/Attack from the last fight doesn't keep
    /// the combat-music gate hot or leave the attack-facing pinned.</summary>
    public void ForceIdle()
    {
        State = BrainState.Wander;
        _attackFacing = null;
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

    /// <summary>One-shot edge for the render layer's thrown projectile (krug
    /// rock, skeleton arrow). Set when a Ranged-mode brain fires. SC-RANGED-
    /// PROJECTILE — the payload carries the pre-rolled damage and the target
    /// combat sink; the HOST applies it at projectile impact (DS1 resolves
    /// ranged damage on collision, not at the FIRE note). A consumer that
    /// drops the payload drops the hit — by design, that's a miss.</summary>
    public bool ConsumeJustFiredRanged(out Vector3 target, out float damage,
                                       out ActorCombatState? targetCombat)
    {
        target = _lastFireTarget;
        damage = _pendingRangedDamage;
        targetCombat = _pendingRangedTargetCombat;
        if (!_justFiredRanged) return false;
        _justFiredRanged = false;
        _pendingRangedTargetCombat = null;
        return true;
    }

    bool _justCast;
    bool _justFiredRanged;
    Vector3 _lastFireTarget;
    float _pendingRangedDamage;
    ActorCombatState? _pendingRangedTargetCombat;
    float _pendingCastDamage;
    ActorCombatState? _pendingCastTargetCombat;

    /// <summary>SC-SPELL-LAUNCH — payload companion to <see cref="ConsumeJustCast"/>
    /// for [spell_launch] ammo spells: the pre-rolled damage + victim combat
    /// sink, applied by the HOST at ammo impact (DS1 resolves launch-spell
    /// damage on collision, exactly like ranged weapons). Set only when the
    /// cast spell IsLaunch; a consumer that drops it drops the hit — a miss.</summary>
    public bool TakeCastLaunchPayload(out float damage, out ActorCombatState? targetCombat)
    {
        damage = _pendingCastDamage;
        targetCombat = _pendingCastTargetCombat;
        _pendingCastDamage = 0f;
        _pendingCastTargetCombat = null;
        return targetCombat is not null;
    }

    /// <summary>XZ distance at which we transition Wander → Chase. ~8u is one
    /// krug-sized stride; the PC has to actively step into a mob's bubble.</summary>
    public float AggroRadius { get; set; } = 8f;

    /// <summary>Sticky-chase cutoff. Once aggro'd we follow past
    /// <see cref="AggroRadius"/>; only beyond this do we drop back to wander.
    /// Hysteresis prevents flicker at the radius boundary.</summary>
    public float DisengageRadius { get; set; } = 14f;

    /// <summary>SC-COMPANION-PROGRESSION — fired when THIS brain rolls damage
    /// at its FIRE moment: (damage, victimStats, skill). The host routes party
    /// followers' awards to their own XP pools; enemy brains leave it null.
    /// Ranged/launch payloads award at FIRE too — projectiles home on their
    /// captured target, so impact is effectively guaranteed; a death
    /// mid-flight forfeits nothing worth modeling.</summary>
    public Action<float, ActorStats, Assets.SkillKind>? OnDamageDealt;

    /// <summary>SC-MOB-AGGRO-VERTICAL — max height difference for the initial
    /// spot. ~One story; combined with the host's <see cref="SightBlocked"/>
    /// terrain-aware test (SC-LOS-TERRAIN) for shallow floors within the band.</summary>
    public const float AggroVerticalBand = 4f;

    /// <summary>SC-LOS-MELEE-VERTICAL — max height difference a melee swing can
    /// span. A mob 1u away in XZ but a storey above/below (surface krug over a
    /// de-roofed crypt room) must not land hits through the floor. Generous
    /// enough for stair fights and big-vs-small reach.</summary>
    public const float MeleeVerticalReach = 2.5f;

    /// <summary>Melee reach. Pulled from the actor's <see cref="ActorStats.AttackRange"/>
    /// when the template authors one (krug grunt = 1.8u); otherwise a 2u fallback.</summary>
    public float MeleeRange { get; }

    /// <summary>Ranged/Magic engagement distance — the brain stops chasing and
    /// starts firing here. Melee brains never read it.</summary>
    public float StandoffRange { get; }

    /// <summary>SC-PATHING-LOS — host-injected sight test (from feet, to feet
    /// → true when a tall occluder crosses the line). DS1 gates ranged/magic
    /// jobs on requires_line_of_sight and repositions via FindClearLosPoint;
    /// our proxy: a sight-blocked standoff target keeps the brain CHASING
    /// (the follower's A* routes around the blocker) instead of firing
    /// through a building. Null (headless sims, tests) = always clear.
    /// Height-aware on the host side so low fences stay shootable-over.</summary>
    public Func<Vector3, Vector3, bool>? SightBlocked;

    float EngageRange => Mode == AttackMode.Melee ? MeleeRange : StandoffRange;

    /// <summary>Seconds between swings while in Attack. 1.5s roughly matches
    /// the DS1 1H melee animation cadence; tuned by ear, not from gas.</summary>
    public float SwingPeriod { get; set; } = 1.5f;

    readonly ActorStats _selfStats;
    readonly Actor? _selfActor;
    readonly Random _swingRng;
    float _swingCooldown;
    Vector3? _attackFacing;

    /// <summary>True for recruited party followers — their swings/casts scale
    /// by the session's PLAYER difficulty multiplier (the party is the player
    /// side in DS1's difficulty model); monsters use the computer multiplier.</summary>
    public bool PartyAligned { get; set; }

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
        // Phase 18c — [attack] reload_delay is ADDITIVE: DS1's swing period is
        // reload_delay + the stance's base attack-anim duration
        // (job_attack_object_melee.skrit). SwingPeriod survives only as the
        // fallback for actors with no attack clip. The old ">= 0.5 replaces
        // the period" reading underrated slow throwers (krug rock: 1.0
        // authored + ~1.2s anim ≈ 2.2s true cadence, not 1.0).
        _swingCooldown = 0f;
    }

    // Phase 18c — the in-flight attack iteration (see SwingSchedule). Damage
    // lands on the clip's FIRE note(s); the iteration completes at
    // period = reload_delay + base attack duration, and only then can the
    // next swing start. Advanced at the top of Tick regardless of state so
    // a target that dies or steps away mid-swing still gets the follow-
    // through (DS1's CleaningUpAndExiting lets the animation finish).
    SwingSchedule? _activeSwing;
    bool _activeSwingRanged;
    bool _activeSwingIsCast;

    void StartSwing(bool ranged, float targetElevationDelta = 0f)
    {
        var clip = _selfActor?.PickNextSwingClip(targetElevationDelta);
        if (clip is null || _selfActor is null)
        {
            // No attack chore authored — legacy instant cadence.
            _activeSwing = null;
            return;
        }
        float clipLen = clip.AnimLength > 0f ? clip.AnimLength : 1.0f;
        float baseDur = _selfActor.AttackBaseDuration > 0f ? _selfActor.AttackBaseDuration : clipLen;
        _activeSwing = new SwingSchedule(clip, baseDur + MathF.Max(0f, _selfStats.ReloadDelay));
        _activeSwingRanged = ranged;
        _activeSwingIsCast = false;
        _selfActor.PlayChoreOnce("chore_attack", clipLen);
        JustSwung = true;
    }

    void AdvanceSwing(float dt, Vector3? targetPos, ActorCombatState? targetCombat, ActorStats? targetStats)
    {
        if (_activeSwing is not { } sw) return;
        int fires = sw.Advance(dt, out _, out bool pad);
        if (pad)
        {
            // Between-iteration filler: the fighting fidget, on whichever
            // chore slot this iteration runs (attack or magic).
            if (_activeSwingIsCast
                ? _selfActor?.SwapCastToFightingFidget() == true
                : _selfActor?.SwapToPadClip() == true)
            {
                _selfActor!.PlayChoreOnce(_activeSwingIsCast ? "chore_magic" : "chore_attack",
                    MathF.Max(0.1f, sw.Period - sw.Elapsed));
            }
        }
        for (; fires > 0; fires--)
        {
            if (targetPos is null || targetCombat is null || targetStats is null || targetCombat.IsDead)
                continue;
            if (_activeSwingIsCast)
            {
                ApplyCastHit(targetPos.Value, targetCombat, targetStats);
                continue;
            }
            if (!_activeSwingRanged)
            {
                // The target had the whole windup to step away — out of
                // reach at the FIRE moment is a whiff. Generous slack: the
                // engage/hold band already keeps the fight at MeleeRange-ish
                // center distance, so only a real escape should whiff.
                if (DistXZ(Wander.Position, targetPos.Value) > MeleeRange * 1.5f + 1.5f) continue;
                // SC-LOS-MELEE-VERTICAL — reach is 3D: a mob 1u away in XZ
                // but a storey above/below (surface krug over a de-roofed
                // crypt room) cannot land a swing through the floor.
                if (MathF.Abs(Wander.Position.Y - targetPos.Value.Y) > MeleeVerticalReach) continue;
            }
            float raw = CombatResolver.RollDamage(_selfStats, targetStats, _swingRng,
                attackerIsPlayer: PartyAligned, ranged: _activeSwingRanged);
            if (_activeSwingRanged)
            {
                // SC-RANGED-PROJECTILE — ranged damage rides the projectile
                // now: the host consumes this payload, spawns the ammo GO,
                // and applies the damage at IMPACT (DS1 resolves ranged hits
                // on collision — job_attack_object_ranged only launches).
                _justFiredRanged = true;
                _lastFireTarget = targetPos.Value;
                _pendingRangedDamage = raw;
                _pendingRangedTargetCombat = targetCombat;
                if (raw > 0f) OnDamageDealt?.Invoke(raw, targetStats, Assets.SkillKind.Ranged);
            }
            else
            {
                float removedNow = targetCombat.ApplyDamage(raw);
                if (removedNow > 0f) OnDamageDealt?.Invoke(removedNow, targetStats, Assets.SkillKind.Melee);
            }
        }
        if (sw.Complete) _activeSwing = null;
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
        // Review fold — NPC mana trickle so casters don't permanently run
        // dry (DS1 regenerates mana; shipped casters hold ~80-90 max). An
        // apprentice-tier INT recovers a zap's cost in a few seconds of
        // downtime; melee/ranged brains never spend mana so they skip it.
        if (Mode == AttackMode.Magic)
            _selfActor?.Combat.RestoreMana(dt * MathF.Max(0.5f, _selfStats.Intelligence * 0.06f));

        bool targetAlive = targetPos.HasValue && targetCombat is not null && !targetCombat.IsDead;
        float distXZ = targetAlive ? DistXZ(Wander.Position, targetPos!.Value) : float.PositiveInfinity;

        // Phase 18c — follow the in-flight swing through regardless of state
        // transitions (FIRE-note damage, qffg pad, iteration completion).
        AdvanceSwing(dt, targetPos, targetCombat, targetStats);

        // SC-AI-FLEE — engaged + dropped under the authored life ratio →
        // break off and run flee_distance away from the target. One charge
        // per authored flee_count; once spent the actor fights to the death
        // (re-aggro after the run happens naturally through Wander).
        if (_fleeEnabled && _fleeChargesLeft > 0 && targetAlive
            && (State == BrainState.Chase || State == BrainState.Attack)
            && _selfActor is not null && !_selfActor.Combat.IsDead
            && _selfActor.Combat.CurrentLife <= _fleeThreshold * MathF.Max(1f, _selfStats.MaxLife))
        {
            EnterFlee(targetPos!.Value);
        }

        switch (State)
        {
            case BrainState.Wander:
                // SC-MOB-AGGRO-VERTICAL — the XZ-only distance let mobs spot
                // the player through floors/ceilings (a surface krug aggroing
                // at the player in the cellar directly below reads as
                // "spotted me from really far"). Require the target within
                // roughly one story vertically; engaged states keep chasing
                // across height changes (stairs mid-fight don't drop aggro).
                // SC-LOS-TERRAIN — the spot itself also needs LINE OF SIGHT:
                // the vertical band alone let shallow-dungeon mobs and the
                // party see each other through a floor less than one story
                // thick (crypt end room, ~4u under the surface). The host
                // test now includes terrain, so a floor/ceiling/ridge between
                // the two blocks the spot outright.
                if (targetAlive && distXZ <= AggroRadius &&
                    MathF.Abs(Wander.Position.Y - targetPos!.Value.Y) <= AggroVerticalBand &&
                    SightBlocked?.Invoke(Wander.Position, targetPos!.Value) != true)
                    EnterChase(targetPos!.Value);
                else if (PatrolRoute is not null) { _attackFacing = null; TickPatrol(dt); }
                else { _attackFacing = null; Wander.Tick(dt); }
                break;

            case BrainState.Chase:
                if (!targetAlive || distXZ > DisengageRadius) { State = BrainState.Wander; _attackFacing = null; break; }
                // SC-PATHING — "in range" must mean REACHABLE: a target
                // across a fence is close by XZ but the melee approach is
                // obstacle-blocked; keep chasing (the follower's A* now
                // routes around the fence's blocked triangles) instead of
                // standing at the rail reaching through it.
                if (distXZ <= EngageRange
                    && !(Mode == AttackMode.Melee
                         && Wander.Follower.Mesh.SegmentCrossesBlocked(Wander.Position, targetPos!.Value))
                    // SC-LOS-MELEE-VERTICAL — melee reach is 3D: directly
                    // above/below in XZ range is NOT engageable (surface mob
                    // over a de-roofed crypt room swung "through" the floor).
                    && !(Mode == AttackMode.Melee
                         && MathF.Abs(Wander.Position.Y - targetPos!.Value.Y) > MeleeVerticalReach)
                    // SC-PATHING-LOS — standoff modes need SIGHT, not floor:
                    // an archer/caster with a building between it and the
                    // target keeps chasing around it instead of firing
                    // through the wall. Low fences don't block (height-aware
                    // host test) so bows still loose over them like retail.
                    // SC-LOS-TERRAIN — the host test now also treats floors/
                    // ceilings as occluders, so this same gate stops fire
                    // through a de-roofed dungeon's missing ceiling.
                    && !(Mode != AttackMode.Melee
                         && SightBlocked?.Invoke(Wander.Position, targetPos!.Value) == true))
                { EnterAttack(targetPos!.Value); break; }
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
                // SC-PATHING — a target that stepped behind blocked ground
                // mid-fight (fence, cart) breaks the hold: back to Chase so
                // the follower paths around instead of zombie-reaching.
                if (meleeNow && Wander.Follower.Mesh.SegmentCrossesBlocked(Wander.Position, targetPos!.Value))
                { State = BrainState.Chase; _attackFacing = null; break; }
                // SC-LOS-MELEE-VERTICAL — a melee target that dropped/climbed
                // a level mid-hold is out of reach: back to Chase (whose
                // pathing finds the stairs, or gives up at the leash).
                if (meleeNow && MathF.Abs(Wander.Position.Y - targetPos!.Value.Y) > MeleeVerticalReach)
                { State = BrainState.Chase; _attackFacing = null; break; }
                // SC-PATHING-LOS — a standoff target that stepped behind a
                // tall occluder mid-fight breaks the hold the same way.
                if (!meleeNow && SightBlocked?.Invoke(Wander.Position, targetPos!.Value) == true)
                { State = BrainState.Chase; _attackFacing = null; break; }
                FaceTarget(targetPos!.Value);
                if (meleeNow || Mode != AttackMode.Magic)
                {
                    // Phase 18c — schedule-driven cadence: a new iteration
                    // starts only when no swing is in flight; damage lands
                    // on the clip's FIRE note inside AdvanceSwing. Actors
                    // with no attack clip (rare) fall back to the legacy
                    // instant hit on the SwingPeriod cooldown.
                    _swingCooldown -= dt;
                    if (_activeSwing is null && _swingCooldown <= 0f)
                    {
                        StartSwing(ranged: !meleeNow,
                            targetElevationDelta: targetPos is { } tep
                                ? tep.Y - Wander.Position.Y : 0f);
                        if (_activeSwing is null)
                        {
                            // Legacy clip-less path.
                            _swingCooldown = SwingPeriod;
                            if (targetStats is not null)
                            {
                                float raw = CombatResolver.RollDamage(_selfStats, targetStats, _swingRng,
                                    attackerIsPlayer: PartyAligned, ranged: !meleeNow);
                                if (meleeNow)
                                {
                                    float removedNow = targetCombat!.ApplyDamage(raw);
                                    if (removedNow > 0f) OnDamageDealt?.Invoke(removedNow, targetStats, Assets.SkillKind.Melee);
                                }
                                else
                                {
                                    // SC-RANGED-PROJECTILE — same impact-time
                                    // payload as the scheduled-swing path.
                                    _pendingRangedDamage = raw;
                                    _pendingRangedTargetCombat = targetCombat;
                                    if (raw > 0f) OnDamageDealt?.Invoke(raw, targetStats, Assets.SkillKind.Ranged);
                                }
                            }
                            JustSwung = true;
                            if (!meleeNow)
                            {
                                _justFiredRanged = true;
                                _lastFireTarget = targetPos!.Value;
                            }
                        }
                    }
                }
                else
                {
                    // Phase 19 — a scheduled cast in flight gates the next
                    // one; _swingCooldown carries only the mana-dry retry.
                    _swingCooldown -= dt;
                    if (_activeSwing is null && _swingCooldown <= 0f)
                        CastAtTarget(targetPos!.Value, targetCombat!, targetStats);
                }
                break;

            case BrainState.Flee:
                // SC-AI-FLEE — keep running until arrival / a blocked path /
                // the timer expires, then drop to Wander (which re-aggros
                // naturally if the threat pursued; with the flee charge spent
                // the next engagement is to the death, matching authored
                // flee_count=1 semantics).
                _fleeTimer -= dt;
                Wander.Tick(dt);
                if (_fleeTimer <= 0f || Wander.Follower.ReachedGoal || Wander.Follower.PathBlocked)
                {
                    State = BrainState.Wander;
                    _attackFacing = null;
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

        // Phase 19 — the release rides the mg clip's FIRE note via the shared
        // schedule; cast cadence = clip length + cast_reload_delay (additive,
        // same shape as the melee formula). Mana was spent at initiation —
        // DS1's cast job commits the cost when the cast starts.
        var clip = self.PickNextCastClip(spell.CastSubAnimation);
        if (clip is not null)
        {
            float len = clip.AnimLength > 0f ? clip.AnimLength : 0.7f;
            _activeSwing = new SwingSchedule(clip, len + MathF.Max(0.75f, spell.CastReloadDelay));
            _activeSwingRanged = false;
            _activeSwingIsCast = true;
            _selfActor?.PlayChoreOnce("chore_magic", len);
            return;
        }

        // Legacy clip-less path: instant release + cooldown.
        ApplyCastHit(targetPos, targetCombat, targetStats);
        _swingCooldown = MathF.Max(0.75f, spell.CastReloadDelay);
    }

    /// <summary>Phase 19 — the spell actually lands (FIRE note, or instantly
    /// on clip-less templates). Damage rolls against the CURRENT tick's
    /// target state; the render layer's projectile/voice cues key off
    /// <see cref="_justCast"/> here, at the release, not at wind-up.</summary>
    void ApplyCastHit(Vector3 targetPos, ActorCombatState targetCombat, ActorStats targetStats)
    {
        var spell = CastSpell;
        var self = _selfActor;
        if (spell is null || self is null) return;
        float magicLevel = MathF.Max(1f, _selfStats.Intelligence);
        var dmgCtx = new Assets.SpellEvalContext(magicLevel,
            maxLife: targetStats.MaxLife,
            life:    targetCombat.CurrentLife,
            srcMana: self.Combat.CurrentMana,
            srcLife: self.Combat.CurrentLife);
        float damage = spell.RollDamage(dmgCtx, _swingRng)
            * (PartyAligned ? CombatResolver.PlayerDamageMultiplier
                            : CombatResolver.ComputerDamageMultiplier);
        // SC-SPELL-LAUNCH — ammo spells (phrak dart, skrubb spit) defer the
        // hit to projectile impact: park the payload for the host's ammo GO,
        // exactly like the ranged-weapon FIRE deferral above. Instant spells
        // keep applying at the FIRE note.
        float castRemoved;
        if (spell.IsLaunch)
        {
            _pendingCastDamage = damage;
            _pendingCastTargetCombat = targetCombat;
            castRemoved = damage;
        }
        else
        {
            castRemoved = damage > 0f ? targetCombat.ApplyDamage(damage) : 0f;
        }
        if (castRemoved > 0f)
            OnDamageDealt?.Invoke(castRemoved, targetStats,
                spell.Class == Assets.SpellClass.NatureMagic
                    ? Assets.SkillKind.NatureMagic : Assets.SkillKind.CombatMagic);
        // Deliberately NOT JustSwung — casts fire the authored 'cast' voice
        // state (123 DS1 templates), not the melee 'attack' cue.
        _justCast = true;
        _lastFireTarget = targetPos;
    }

    /// <summary>SC-MOB-COMMANDS — authored patrol route from cmd_ai_c_patrol
    /// chains (via the placement's [mind] initial_command). Replaces idle
    /// random wander while assigned; aggro interrupts exactly like wander
    /// (the target checks in Tick run first), and the route resumes when the
    /// brain drops back to Wander. One-way command chains clear on arrival.</summary>
    public IReadOnlyList<Vector3>? PatrolRoute { get; private set; }
    /// <summary>True once any route has been assigned — distinguishes "never
    /// had a route" from "one-way route completed" so streaming events and
    /// idempotent re-assignment passes don't replay consumed run-ins.</summary>
    public bool HasHadPatrol { get; private set; }
    bool _patrolLoops;
    int _patrolIdx;
    int _patrolBlockedSkips;

    public void AssignPatrol(IReadOnlyList<Vector3> route, bool loops)
    {
        if (route.Count == 0) return;
        PatrolRoute = route;
        HasHadPatrol = true;
        _patrolLoops = loops;
        _patrolIdx = 0;
        _patrolBlockedSkips = 0;
    }

    void TickPatrol(float dt)
    {
        var route = PatrolRoute!;
        var wp = route[_patrolIdx];
        if (DistXZ(Wander.Position, wp) <= 1.2f)
        {
            if (_patrolIdx + 1 < route.Count) _patrolIdx++;
            else if (_patrolLoops) _patrolIdx = 0;
            else { PatrolRoute = null; Wander.Tick(dt); return; }
            wp = route[_patrolIdx];
        }
        // Re-pin only when the follower isn't already en route to this
        // waypoint — SetTarget replans, and idle patrollers shouldn't A*
        // at 20 Hz the way an active chase justifiably does.
        if (Wander.Follower.ReachedGoal || DistXZ(Wander.Follower.Target, wp) > 0.1f)
        {
            Wander.Follower.SetTarget(wp);
            // Review fold — an unreachable waypoint (navmesh hole, obstacle
            // on the goal tile) previously ping-ponged between this re-pin
            // and the wander fallback at two full A* plans per tick, forever.
            // Skip blocked waypoints; a fully-blocked route clears.
            if (Wander.Follower.PathBlocked)
            {
                _patrolBlockedSkips++;
                if (_patrolBlockedSkips >= route.Count)
                {
                    PatrolRoute = null;
                    Wander.Tick(dt);
                    return;
                }
                if (_patrolIdx + 1 < route.Count) _patrolIdx++;
                else if (_patrolLoops) _patrolIdx = 0;
                else { PatrolRoute = null; Wander.Tick(dt); return; }
            }
            else
            {
                _patrolBlockedSkips = 0;
            }
        }
        Wander.Tick(dt);
    }

    /// <summary>SC-MOB-PARTY — alert-friends hook. A packmate that spotted the
    /// enemy forces this brain out of Wander into Chase toward the target.
    /// No-op unless idle: an already-fighting brain keeps its own state, and
    /// the next Tick's disengage check still applies (an alert can't make a
    /// mob chase something beyond its own leash).</summary>
    public void ForceAggro(Vector3 targetPos)
    {
        if (State != BrainState.Wander) return;
        // Same vertical band as the direct spot — a packmate's shout doesn't
        // carry through a floor.
        if (MathF.Abs(Wander.Position.Y - targetPos.Y) > AggroVerticalBand) return;
        // SC-LOS-TERRAIN — nor through a shallow floor within the band.
        if (SightBlocked?.Invoke(Wander.Position, targetPos) == true) return;
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

    // SC-AI-FLEE — run flee_distance directly away from the attacker. The
    // timer bounds the run (distance at gait + slack) so a blocked path
    // can't leave the actor pinned in Flee forever.
    void EnterFlee(Vector3 threatPos)
    {
        _fleeChargesLeft--;
        State = BrainState.Flee;
        _attackFacing = null;
        float dx = Wander.Position.X - threatPos.X;
        float dz = Wander.Position.Z - threatPos.Z;
        float len = MathF.Sqrt(dx * dx + dz * dz);
        Vector3 away = len > 1e-4f
            ? new Vector3(dx / len, 0f, dz / len)
            : new Vector3(MathF.Sin(_swingRng.Next(0, 628) / 100f), 0f, MathF.Cos(_swingRng.Next(0, 628) / 100f));
        var fleePoint = Wander.Position + away * _fleeDistance;
        Wander.Follower.SetTarget(fleePoint);
        // Gait from stats; the +2s slack covers nav detours around obstacles.
        float gait = _selfStats.WalkSpeed > 0.5f ? _selfStats.WalkSpeed : 4f;
        _fleeTimer = _fleeDistance / gait + 2f;
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
