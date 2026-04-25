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
    readonly ActorStats _stats;

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
}
