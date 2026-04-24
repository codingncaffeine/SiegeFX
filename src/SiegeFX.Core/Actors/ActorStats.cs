using System.Globalization;
using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Actors;

/// <summary>
/// Combat-relevant stats pulled from a template's specializes chain at spawn time.
/// DS1 authors define stats at whichever chain level is natural: base monster tiers
/// (<c>3W_base_goblin</c>) set gait and chore hooks, top-tier variants
/// (<c>3W_goblin_grunt</c>) override with per-instance life / damage. The chain walk
/// in <see cref="TemplateStore.GetAttribute"/> collapses that to a single lookup.
///
/// A missing attribute is a real possibility — not every archetype has every stat
/// (chickens don't fight, props have no life). Treat <see cref="MaxLife"/>&lt;=0 as
/// "not a combatant" rather than a bug.
/// </summary>
public sealed record ActorStats(
    float MaxLife,
    float MaxMana,
    float DamageMin,
    float DamageMax,
    float Defense,
    float AttackRange,
    float WalkSpeed,
    int   ExperienceValue)
{
    /// <summary>Chickens, props, and mood actors come through with zero life. The
    /// rest of the combat pipeline uses this to skip hit resolution on them.</summary>
    public bool IsCombatant => MaxLife > 0f && DamageMax > 0f;

    public static ActorStats FromTemplate(TemplateStore store, Template template)
    {
        // life first, max_life as fallback (a few templates set one or the other,
        // not both); same for mana. AttackRange / Defense / Damage are single-keyed.
        float maxLife = ParseFloat(store.GetAttribute(template, "aspect", "max_life")) ??
                        ParseFloat(store.GetAttribute(template, "aspect", "life")) ?? 0f;
        float maxMana = ParseFloat(store.GetAttribute(template, "aspect", "max_mana")) ??
                        ParseFloat(store.GetAttribute(template, "aspect", "mana")) ?? 0f;
        float dmgMin  = ParseFloat(store.GetAttribute(template, "attack", "damage_min")) ?? 0f;
        float dmgMax  = ParseFloat(store.GetAttribute(template, "attack", "damage_max")) ?? 0f;
        float defense = ParseFloat(store.GetAttribute(template, "defend", "defense")) ?? 0f;
        float range   = ParseFloat(store.GetAttribute(template, "attack", "attack_range")) ?? 0f;
        // avg_move_velocity is the DS1 idle-walk gait (krug ≈ 2.5, chickens ≈ 1.9).
        // Fall back to 4 u/s — what Phase 11d hardcoded — when no template in the
        // chain sets one, so non-combatants still wander at a visible pace.
        float walk    = ParseFloat(store.GetAttribute(template, "body", "avg_move_velocity")) ?? 4f;
        int   xp      = ParseInt  (store.GetAttribute(template, "aspect", "experience_value")) ?? 0;

        return new ActorStats(maxLife, maxMana, dmgMin, dmgMax, defense, range, walk, xp);
    }

    static float? ParseFloat(string? s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    static int? ParseInt(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
}
