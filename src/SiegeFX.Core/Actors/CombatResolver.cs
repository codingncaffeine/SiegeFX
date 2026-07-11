using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Actors;

/// <summary>
/// Melee hit resolution, faithful to DS1's <c>[combat_constants]</c> (formulas.gas):
/// a to-hit roll from attack-vs-defend ratings, then a weapon-damage roll, then armor
/// mitigation, then the difficulty multiplier.
///
/// Damage <i>magnitude</i> is the weapon's authored <c>damage_min..max</c> — DS1 has
/// NO strength/skill damage bonus. Skill, dexterity and intelligence feed the chance
/// TO HIT (attack/defend rating), not how hard a landed blow strikes. Armor subtracts
/// <c>defense * armor_scalar / 10</c> from a hit that connects (the shipped
/// <c>armor_scalar</c> is 1.0, and DS1's rule of thumb is "100 armor = 10 less
/// damage"). The rating coefficients, hit-chance curve, caps, armor scalar and
/// difficulty multipliers all come from <see cref="CombatConstants"/> (parsed from
/// formulas.gas by <see cref="FormulasStore"/>); <see cref="CombatConstants.Ds1Default"/>
/// mirrors the retail values so static call sites resolve identically to the game.
/// </summary>
public static class CombatResolver
{
    /// <summary>Engine armor scale: a landed hit loses <c>defense/10</c> HP, tuned by
    /// <c>[combat_constants]armor_scalar</c>. ("100 armor points → 10 less damage.")</summary>
    public const float ArmorDivisor = 10f;

    /// <summary>Minimum damage a LANDED hit deals after armor. A swing that fails the
    /// to-hit roll deals 0 (a whiff); a swing that connects always chips at least this,
    /// so a heavily-armored target still loses ground on a real hit.</summary>
    public const float MinChipDamage = 1f;

    /// <summary>Attack rating = <c>skill_scalar*skill + dex_scalar*dex + int_scalar*int</c>
    /// (shipped 0.45 / 0.55 / 0.15). Melee uses the Melee skill; ranged the Ranged skill.</summary>
    public static float AttackRating(ActorStats a, in CombatConstants cc, bool ranged = false)
    {
        float skill = ranged ? a.RangedSkill : a.MeleeSkill;
        return cc.AttackSkillScalar * skill + cc.AttackDexScalar * a.Dexterity + cc.AttackIntScalar * a.Intelligence;
    }

    /// <summary>Defend rating = same coefficient shape over the defender's Melee skill,
    /// dex and int. DS1 defense is dex-led (0.55), which is why DEX training raises
    /// both to-hit and to-block.</summary>
    public static float DefendRating(ActorStats d, in CombatConstants cc)
        => cc.DefendSkillScalar * d.MeleeSkill + cc.DefendDexScalar * d.Dexterity + cc.DefendIntScalar * d.Intelligence;

    /// <summary>Percent chance (0..100) that an attack lands: <c>hit_chance</c> (50)
    /// shifted by <c>(attack_rating - defend_rating) * attacker_diff_scalar</c> (2.1),
    /// clamped to <c>[defender_hit_cap, attacker_hit_cap]</c> (5..95) so even a hopeless
    /// attacker connects 5% of the time and a dominant one whiffs 5%.</summary>
    public static float HitChance(ActorStats attacker, ActorStats target, in CombatConstants cc, bool ranged = false)
    {
        float ar = AttackRating(attacker, cc, ranged);
        float dr = DefendRating(target, cc);
        float chance = cc.BaseHitChance + (ar - dr) * cc.AttackerDiffScalar;
        return Math.Clamp(chance, cc.DefenderHitCap, cc.AttackerHitCap);
    }

    /// <summary>Full DS1 resolution of one swing: roll to-hit, then (on a hit) roll
    /// weapon damage, apply the difficulty multiplier, and subtract armor. Returns the
    /// hit flag, the post-mitigation damage (0 on a miss), and the rolled hit chance.
    /// Deterministic only if <paramref name="rng"/> is — seed from the attacker's scid
    /// for reproducibility.</summary>
    public static AttackResult Resolve(ActorStats attacker, ActorStats target, Random rng,
        in CombatConstants cc, bool attackerIsPlayer = false, bool ranged = false)
    {
        // No weapon / non-combatant attacker: nothing to resolve.
        if (attacker.DamageMax <= 0f) return AttackResult.Miss(0f);

        float chance = HitChance(attacker, target, cc, ranged);
        if (rng.NextDouble() * 100.0 >= chance) return AttackResult.Miss(chance);

        float min = attacker.DamageMin, max = attacker.DamageMax;
        if (min > max) (min, max) = (max, min);
        float raw = min + (float)rng.NextDouble() * (max - min);

        float difficulty = attackerIsPlayer ? cc.DifficultyPlayer : cc.DifficultyComputer;
        float mitigated = raw * difficulty - target.Defense * cc.ArmorScalar / ArmorDivisor;
        return new AttackResult(true, MathF.Max(MinChipDamage, mitigated), chance);
    }

    // ---- session combat configuration ---------------------------------------
    // One difficulty + one loaded [combat_constants] per session. Configure()
    // is called at world load (with the tank-parsed constants) and whenever a
    // difficulty is chosen; every live swing/cast resolves through it so the
    // authored difficulty_easy/medium/hard multipliers actually bite.

    /// <summary>The session's active combat constants (tank-parsed when
    /// available; retail defaults otherwise).</summary>
    public static CombatConstants Active { get; private set; } = CombatConstants.Ds1Default;

    /// <summary>Multiplier applied to damage dealt BY the party (player +
    /// companions) at the session difficulty. Easy 1.35 / Medium 1.0 / Hard 0.85.</summary>
    public static float PlayerDamageMultiplier { get; private set; } = 1f;

    /// <summary>Multiplier applied to damage dealt BY computer-controlled
    /// attackers. Easy 0.5 / Medium 1.0 / Hard 1.45.</summary>
    public static float ComputerDamageMultiplier { get; private set; } = 1f;

    /// <summary>Install the session's constants + difficulty tier. Idempotent;
    /// call again when either changes.</summary>
    public static void Configure(in CombatConstants cc, CombatDifficulty difficulty)
    {
        Active = cc;
        (PlayerDamageMultiplier, ComputerDamageMultiplier) = cc.DifficultyFor(difficulty);
    }

    /// <summary>Session-configured resolution for live swings: to-hit + damage
    /// roll through <see cref="Active"/> with the party/computer difficulty
    /// multiplier picked by <paramref name="attackerIsPlayer"/> (companions
    /// count as the player side — DS1's difficulty scales party-dealt damage
    /// as a whole). Same RNG consumption pattern as <see cref="Resolve"/>
    /// (one draw per miss, two per landed swing). Returns post-mitigation
    /// damage, or 0 on a miss.</summary>
    public static float RollDamage(ActorStats attacker, ActorStats target, Random rng,
        bool attackerIsPlayer, bool ranged = false)
    {
        if (attacker.DamageMax <= 0f) return 0f;
        float chance = HitChance(attacker, target, Active, ranged);
        if (rng.NextDouble() * 100.0 >= chance) return 0f;
        float min = attacker.DamageMin, max = attacker.DamageMax;
        if (min > max) (min, max) = (max, min);
        float raw = min + (float)rng.NextDouble() * (max - min);
        float mult = attackerIsPlayer ? PlayerDamageMultiplier : ComputerDamageMultiplier;
        float mitigated = raw * mult - target.Defense * Active.ArmorScalar / ArmorDivisor;
        return MathF.Max(MinChipDamage, mitigated);
    }

    /// <summary>Back-compat float entry point used by the live call sites (player /
    /// follower / monster swings). Resolves against the shipped <c>[combat_constants]</c>
    /// at medium difficulty and returns the post-mitigation damage, or 0 on a miss
    /// (which <see cref="ActorCombatState.ApplyDamage"/> treats as a no-op / whiff).
    /// Prefer <see cref="Resolve"/> when a loaded <see cref="CombatConstants"/>, the
    /// attacker-is-player flag, or the ranged flag are available.</summary>
    public static float RollMeleeDamage(ActorStats attacker, ActorStats target, Random rng)
        => Resolve(attacker, target, rng, CombatConstants.Ds1Default).Damage;
}

/// <summary>Outcome of a single melee resolution: whether the swing connected, the
/// post-mitigation damage (0 on a miss), and the percent hit chance it rolled against.</summary>
public readonly record struct AttackResult(bool Hit, float Damage, float HitChancePercent)
{
    public static AttackResult Miss(float chance) => new(false, 0f, chance);
}
