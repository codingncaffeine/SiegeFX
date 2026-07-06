namespace SiegeFX.Core.Assets;

/// <summary>
/// DS1's <c>[combat_constants]</c> block from <c>formulas.gas</c> — the attack /
/// defend rating coefficients, the to-hit chance curve, the armor tuning scalar,
/// and the difficulty multipliers. Read verbatim by <see cref="FormulasStore"/>
/// and consumed by <see cref="SiegeFX.Core.Actors.CombatResolver"/>.
///
/// The shipped block (Logic.dsres <c>/world/global/formula/formulas.gas</c>):
/// <code>
///   [attack_rating] { skill_scalar = 0.45; dex_scalar = 0.55; int_scalar = 0.15; }
///   [defend_rating] { skill_scalar = 0.45; dex_scalar = 0.55; int_scalar = 0.15; }
///   hit_chance = 50.0;  attacker_diff_scalar = 2.1;  victim_diff_scalar = 2.1;
///   attacker_hit_cap = 95.0;  defender_hit_cap = 5.0;  armor_scalar = 1.0;
///   difficulty_medium_player = 1.0;  difficulty_medium_computer = 1.0;
/// </code>
/// <see cref="Ds1Default"/> mirrors those retail values so code paths without a
/// loaded store still resolve exactly like the shipped game at medium difficulty.
/// </summary>
public readonly record struct CombatConstants(
    float AttackSkillScalar, float AttackDexScalar, float AttackIntScalar,
    float DefendSkillScalar, float DefendDexScalar, float DefendIntScalar,
    float BaseHitChance, float AttackerDiffScalar, float VictimDiffScalar,
    float AttackerHitCap, float DefenderHitCap,
    float ArmorScalar,
    float DifficultyPlayer, float DifficultyComputer)
{
    /// <summary>Retail shipped values at medium difficulty.</summary>
    public static CombatConstants Ds1Default => new(
        AttackSkillScalar: 0.45f, AttackDexScalar: 0.55f, AttackIntScalar: 0.15f,
        DefendSkillScalar: 0.45f, DefendDexScalar: 0.55f, DefendIntScalar: 0.15f,
        BaseHitChance: 50f, AttackerDiffScalar: 2.1f, VictimDiffScalar: 2.1f,
        AttackerHitCap: 95f, DefenderHitCap: 5f,
        ArmorScalar: 1.0f,
        DifficultyPlayer: 1.0f, DifficultyComputer: 1.0f);
}
