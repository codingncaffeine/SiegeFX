namespace SiegeFX.Core.Actors;

/// <summary>
/// Stateless melee damage math. DS1's exact mitigation formula isn't publicly
/// documented so this is an educated approximation — flat defense reduction with a
/// minimum chip of 1 HP so a lucky hit always registers. Easy to swap with a
/// diminishing-returns curve later if it plays wrong in the viewer; the shape
/// "roll - defense/K" keeps stat scaling roughly in line with the shipped data
/// (a goblin grunt vs grunt resolves in ~8-10 hits, which matches DS1 pacing).
/// </summary>
public static class CombatResolver
{
    /// <summary>How aggressively defense subtracts from incoming damage. A value of
    /// 10 means defense 554 reduces every hit by 55. Tunable when we have a
    /// visible combat loop to watch (Phase 12c).</summary>
    public const float DefenseDivisor = 10f;

    /// <summary>Minimum damage any successful hit deals, regardless of defense.
    /// Prevents invincible tanks — if a raging goblin guard hits a fresh-spawned
    /// farmboy, they should always chip at least 1 HP.</summary>
    public const float MinChipDamage = 1f;

    /// <summary>Roll a melee damage number from <paramref name="attacker"/> against
    /// <paramref name="target"/>'s defense. Deterministic only if <paramref name="rng"/>
    /// is; for reproducibility pass a Random seeded from the attacker's scid.</summary>
    public static float RollMeleeDamage(ActorStats attacker, ActorStats target, Random rng)
    {
        if (attacker.DamageMax <= 0f) return 0f;
        float min = attacker.DamageMin;
        float max = attacker.DamageMax;
        if (min > max) (min, max) = (max, min);
        float roll = min + (float)rng.NextDouble() * (max - min);
        float mitigated = roll - target.Defense / DefenseDivisor;
        return MathF.Max(MinChipDamage, mitigated);
    }
}
