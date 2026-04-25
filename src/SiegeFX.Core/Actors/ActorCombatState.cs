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
        if (IsDead) return 0f;
        // Use CanTakeDamage (MaxLife>0), not IsCombatant (also requires DamageMax>0).
        // Player characters have no template [attack] block — DS1 derives PC damage
        // from the equipped weapon — so IsCombatant is false even though they
        // absolutely have HP and absolutely take hits.
        if (!_stats.CanTakeDamage) return 0f;
        if (damage <= 0f) return 0f;
        float actual = MathF.Min(damage, CurrentLife);
        CurrentLife -= actual;
        if (CurrentLife <= 0f)
        {
            CurrentLife = 0f;
            JustDied = true;
        }
        return actual;
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
        if (IsDead || amount <= 0f) return;
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
