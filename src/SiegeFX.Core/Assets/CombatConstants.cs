namespace SiegeFX.Core.Assets;

/// <summary>Session difficulty tier — selects which authored
/// <c>difficulty_*_player/computer</c> multiplier pair from
/// <c>[combat_constants]</c> applies to live damage.</summary>
public enum CombatDifficulty { Easy, Medium, Hard }

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
///   difficulty_easy_player   = 1.35;  difficulty_easy_computer   = 0.5;
///   difficulty_medium_player = 1.0;   difficulty_medium_computer = 1.0;
///   difficulty_hard_player   = 0.85;  difficulty_hard_computer   = 1.45;
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
    float DifficultyPlayer, float DifficultyComputer,
    float DifficultyEasyPlayer = 1.35f, float DifficultyEasyComputer = 0.5f,
    float DifficultyHardPlayer = 0.85f, float DifficultyHardComputer = 1.45f,
    // SC-AIM-ERROR — ranged aiming error: shot deviates by up to
    // ±ErrorScalar degrees scaled by (100 − accuracy)/100 where accuracy =
    // ATan((dex·Dex + int·Int + skill·Skill)/14.7)·63 (authored comment).
    float AimErrorScalar = 4.0f, float AimDexScalar = 0.35f,
    float AimIntScalar = 0.10f, float AimSkillScalar = 0.55f,
    // SC-DOWNED — authored unconsciousness gates: minimum time down, and
    // the enemy-proximity sphere that blocks natural recovery.
    float MinUnconsciousDuration = 5.0f, float EnemyNearSphere = 8.0f)
{
    /// <summary>Retail shipped values at medium difficulty.</summary>
    public static CombatConstants Ds1Default => new(
        AttackSkillScalar: 0.45f, AttackDexScalar: 0.55f, AttackIntScalar: 0.15f,
        DefendSkillScalar: 0.45f, DefendDexScalar: 0.55f, DefendIntScalar: 0.15f,
        BaseHitChance: 50f, AttackerDiffScalar: 2.1f, VictimDiffScalar: 2.1f,
        AttackerHitCap: 95f, DefenderHitCap: 5f,
        ArmorScalar: 1.0f,
        DifficultyPlayer: 1.0f, DifficultyComputer: 1.0f);

    /// <summary>The authored (player, computer) damage-multiplier pair for a
    /// difficulty tier. "Player" scales party-dealt damage, "computer" scales
    /// monster-dealt damage — DS1's easy mode makes the party hit harder
    /// (×1.35) AND monsters hit softer (×0.5).</summary>
    public (float Player, float Computer) DifficultyFor(CombatDifficulty d) => d switch
    {
        CombatDifficulty.Easy => (DifficultyEasyPlayer, DifficultyEasyComputer),
        CombatDifficulty.Hard => (DifficultyHardPlayer, DifficultyHardComputer),
        _                     => (DifficultyPlayer, DifficultyComputer),
    };
}
