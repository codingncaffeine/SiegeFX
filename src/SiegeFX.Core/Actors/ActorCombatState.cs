namespace SiegeFX.Core.Actors;

/// <summary>
/// Per-actor combat runtime: current life/mana, dead flag, damage sink.
/// Separate from <see cref="ActorStats"/> which is the immutable base-stat block —
/// CombatState mutates during play as hits land, heals tick, death fires. One per
/// <see cref="Actor"/>; non-combatants (<see cref="ActorStats.IsCombatant"/>=false)
/// still get one but its <see cref="ApplyDamage"/> is a no-op so props and chickens
/// shrug off strays.
/// </summary>
public sealed class ActorCombatState
{
    ActorStats _stats;

    public float CurrentLife { get; private set; }
    public float CurrentMana { get; private set; }

    /// <summary>True once life has dropped to zero. A dead actor ignores further
    /// damage and heals; revival is future-phase territory (Phase 16 spells may
    /// introduce raise-dead). The death transition fires exactly once — the
    /// <see cref="JustDied"/> flag is one-shot and cleared by <see cref="ConsumeJustDied"/>.</summary>
    public bool IsDead => CurrentLife <= 0f;

    /// <summary>One-shot edge detector for the life → death transition. The combat
    /// driver sets this when an ApplyDamage call takes the actor to zero, and the
    /// render/AI layer consumes it to fire the die-chore and drop loot exactly once.</summary>
    public bool JustDied { get; private set; }

    /// <summary>SC-DOWNED — retail's unconscious state. A player-class actor
    /// whose life hits zero goes DOWN rather than dying outright: they lie
    /// helpless, can be healed back to their feet (Heal works while downed),
    /// and only truly die when post-zero trauma crosses the authored
    /// formulas.gas death_threshold fraction of MaxLife. The render layer
    /// owns the transitions via <see cref="EnterDowned"/> /
    /// <see cref="ClearDowned"/> / <see cref="ConfirmDeath"/>.</summary>
    public bool Downed { get; private set; }

    /// <summary>Damage absorbed while downed. Compared by the render layer
    /// against death_threshold × MaxLife to turn unconsciousness into death.</summary>
    public float PostZeroTrauma { get; private set; }

    public void EnterDowned()
    {
        Downed = true;
        PostZeroTrauma = 0f;
    }

    /// <summary>Leave the downed state (revived, or cleaned up after the
    /// trauma death fired). Clears the death edge so a revived actor gets a
    /// fresh JustDied on their next fall.</summary>
    public void ClearDowned()
    {
        Downed = false;
        PostZeroTrauma = 0f;
        JustDied = false;
    }

    /// <summary>Trauma exceeded the authored threshold — turn the downed
    /// state into a real death edge. Downed stays set so the death sweep can
    /// tell "second edge: die for real" from "first edge: fall down".</summary>
    public void ConfirmDeath()
    {
        CurrentLife = 0f;
        JustDied = true;
    }

    /// <summary>SC-ENEMY-AUDIO-AUDIT runtime wire — one-shot edge for "actor
    /// just took a nonzero hit." Set by <see cref="ApplyDamage"/>, consumed
    /// by <see cref="ConsumeJustHit"/>. The render layer uses this to fire
    /// the authored <c>[aspect][voice][hit_glance/solid/critical]</c> cue
    /// based on the damage fraction (see PlayHitVoiceSfx).</summary>
    public bool JustHit { get; private set; }

    /// <summary>Damage applied by the most recent ApplyDamage call (only set
    /// when JustHit fires; zero otherwise). The render layer reads this to
    /// classify hit severity into glance / solid / critical buckets.</summary>
    public float LastDamageTaken { get; private set; }

    public ActorCombatState(ActorStats stats)
    {
        _stats = stats;
        CurrentLife = stats.MaxLife;
        CurrentMana = stats.MaxMana;
    }

    /// <summary>Applies a post-mitigation damage value. Returns the actual amount
    /// subtracted from CurrentLife — clamped to the remaining life so the accounting
    /// in callers (xp attribution, overkill stats) stays honest.</summary>
    public float ApplyDamage(float damage)
    {
        // SC-DOWNED — hits on an unconscious body accumulate trauma instead
        // of draining (already-zero) life. No life removed → no XP credit.
        if (Downed && CurrentLife <= 0f)
        {
            if (!_stats.CanTakeDamage || damage <= 0f) return 0f;
            PostZeroTrauma += damage;
            JustHit = true;
            LastDamageTaken = damage;
            return 0f;
        }
        if (IsDead) return 0f;
        // Use CanTakeDamage (MaxLife>0), not IsCombatant (also requires DamageMax>0).
        // Player characters have no template [attack] block — DS1 derives PC damage
        // from the equipped weapon — so IsCombatant is false even though they
        // absolutely have HP and absolutely take hits.
        if (!_stats.CanTakeDamage) return 0f;
        if (damage <= 0f) return 0f;
        float actual = MathF.Min(damage, CurrentLife);
        CurrentLife -= actual;
        if (actual > 0f)
        {
            JustHit = true;
            LastDamageTaken = actual;
        }
        if (CurrentLife <= 0f)
        {
            CurrentLife = 0f;
            JustDied = true;
        }
        return actual;
    }

    /// <summary>SC-ENEMY-AUDIO-AUDIT — consume the one-shot hit edge. Out
    /// param carries the most recent damage so the render layer can classify
    /// the hit severity without re-querying state. Returns true exactly once
    /// per landed hit; the caller that returns true owns the hit-voice play.</summary>
    public bool ConsumeJustHit(out float damage)
    {
        if (!JustHit) { damage = 0f; return false; }
        damage = LastDamageTaken;
        JustHit = false;
        return true;
    }

    /// <summary>Consume the one-shot death edge. Returns true exactly once per
    /// death — the caller that returns true owns the death-side effects.</summary>
    public bool ConsumeJustDied()
    {
        if (!JustDied) return false;
        JustDied = false;
        return true;
    }

    /// <summary>Restores life and mana to max. Used for respawn and test harnesses;
    /// also clears the death-edge flag so a revived actor gets a fresh JustDied on
    /// its next death.</summary>
    public void ResetToFull()
    {
        CurrentLife = _stats.MaxLife;
        CurrentMana = _stats.MaxMana;
        JustDied = false;
    }

    public void Heal(float amount)
    {
        // SC-DOWNED — healing works on an unconscious body (that's how the
        // manual says you get them back up); only true corpses refuse it.
        if ((IsDead && !Downed) || amount <= 0f) return;
        CurrentLife = MathF.Min(_stats.MaxLife, CurrentLife + amount);
    }

    /// <summary>Restore mana up to MaxMana. No-op while dead — DS1 doesn't tick
    /// recovery on corpses; the caller revives first. Negative amounts are ignored
    /// (drain goes through dedicated spell-cost paths, not this).</summary>
    public void RestoreMana(float amount)
    {
        if (IsDead || amount <= 0f) return;
        CurrentMana = MathF.Min(_stats.MaxMana, CurrentMana + amount);
    }

    /// <summary>Drain mana by <paramref name="amount"/>. Returns the amount actually
    /// spent (clamped to current mana). Spell casts and debug drains both go
    /// through here so the accounting is consistent.</summary>
    public float SpendMana(float amount)
    {
        if (amount <= 0f) return 0f;
        float actual = MathF.Min(amount, CurrentMana);
        CurrentMana -= actual;
        return actual;
    }

    /// <summary>Phase 19b — set life/mana directly from a save snapshot. Bypasses
    /// the normal mutators (which gate on IsDead and clamp to max) so a save
    /// that captured a near-death actor at 0.3 HP comes back at 0.3 HP, not at
    /// max. <paramref name="dead"/> is honored independently of the life value
    /// so a "dead with positive HP" inconsistency in a save file (theoretically
    /// possible after a manual edit) still presents as dead. Clears
    /// <see cref="JustDied"/> so the load path doesn't re-fire the death edge
    /// (loot drop, die-chore) for actors that were already dead at save time.</summary>
    public void RestoreFromSave(float currentLife, float currentMana, bool dead)
    {
        // Clamp to [0, max] so a corrupt save can't drive the bars out of range
        // and trip downstream divide-by-max calculations.
        CurrentLife = MathF.Max(0f, MathF.Min(_stats.MaxLife, currentLife));
        CurrentMana = MathF.Max(0f, MathF.Min(_stats.MaxMana, currentMana));
        if (dead) CurrentLife = 0f;
        JustDied = false;
        // SC-DOWNED — the load path re-establishes downed state explicitly
        // (EnterDowned after restore) — start clean here.
        Downed = false;
        PostZeroTrauma = 0f;
        // SC-ENEMY-AUDIO-AUDIT — clear the hit edge alongside the death
        // edge so a load doesn't fire a stale hit-voice cue on the first
        // post-load tick. Mirrors JustDied handling above.
        JustHit = false;
        LastDamageTaken = 0f;
    }

    /// <summary>Swap the underlying stats reference so subsequent <see cref="Heal"/>
    /// and <see cref="RestoreMana"/> calls clamp against the new MaxLife/MaxMana.
    /// Called from <see cref="Actor.ResyncStats"/> after a level-up. Current life
    /// and mana are left alone — DS1 doesn't auto-heal on level-up — but if the new
    /// max happens to be lower than current (a synthetic test case), we clamp down
    /// rather than carry an over-cap value that subsequent heals would never trim.</summary>
    public void ResyncStats(ActorStats newStats)
    {
        _stats = newStats;
        if (CurrentLife > newStats.MaxLife) CurrentLife = newStats.MaxLife;
        if (CurrentMana > newStats.MaxMana) CurrentMana = newStats.MaxMana;
    }
}
