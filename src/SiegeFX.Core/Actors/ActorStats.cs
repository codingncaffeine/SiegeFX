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
/// MaxLife/MaxMana are computed by DS1's two paths:
///   - Monster (template sets a non-zero <c>aspect.max_life</c>): use it verbatim.
///   - Player (template sets max_life = 0): derive from STR/DEX/INT via the
///     formulas <c>49 = ((str-9)*29.4) + ((dex-9)*9.8) + ((int-9)*9.8)</c> and
///     <c>30 = (str-9) + ((dex-9)*4) + ((int-9)*25)</c>, both at 10/10/10.
/// Players do NOT get a max_life of 0 — they get a derived 49/30 even with no
/// equipment, because every PC in DS1 is multiclass and always has mana.
/// </summary>
public sealed record ActorStats(
    float MaxLife,
    float MaxMana,
    float DamageMin,
    float DamageMax,
    float Defense,
    float AttackRange,
    float WalkSpeed,
    int   ExperienceValue,
    float Strength,
    float Dexterity,
    float Intelligence)
{
    /// <summary>Can deal damage. Filters chickens/props (zero life) AND non-combat
    /// archetypes (mood actors with life but no [attack] block) out of the
    /// "nearest enemy to swing at" pickers.</summary>
    public bool IsCombatant => MaxLife > 0f && DamageMax > 0f;

    /// <summary>Can be hit. Used by <see cref="ActorCombatState.ApplyDamage"/> so
    /// player characters — who have no [attack] block on the template (DS1 derives
    /// PC damage from the equipped weapon) — still take damage. The chicken filter
    /// is <c>MaxLife &gt; 0</c> alone; <see cref="IsCombatant"/> additionally requires
    /// <c>DamageMax &gt; 0</c> which excludes PCs.</summary>
    public bool CanTakeDamage => MaxLife > 0f;

    public static ActorStats FromTemplate(TemplateStore store, Template template)
    {
        // Skill block uses "value, level" pairs (e.g. "strength = 16, 0;") — strip
        // the level half before parsing. Default to 10 for any missing attribute,
        // matching DS1's PC starting block.
        float strength     = ParseFirstFloat(store.GetAttribute(template, "skills", "strength"))     ?? 10f;
        float dexterity    = ParseFirstFloat(store.GetAttribute(template, "skills", "dexterity"))    ?? 10f;
        float intelligence = ParseFirstFloat(store.GetAttribute(template, "skills", "intelligence")) ?? 10f;

        // life first, max_life as fallback (a few templates set one or the other,
        // not both); same for mana. AttackRange / Defense / Damage are single-keyed.
        float templateLife = ParseFloat(store.GetAttribute(template, "aspect", "max_life")) ??
                             ParseFloat(store.GetAttribute(template, "aspect", "life")) ?? 0f;
        float templateMana = ParseFloat(store.GetAttribute(template, "aspect", "max_mana")) ??
                             ParseFloat(store.GetAttribute(template, "aspect", "mana")) ?? 0f;

        // Monster vs player: a non-zero template max_life means the author set an
        // explicit value (monster), so we honor it. A zero means the actor is
        // intended to derive its pool from STR/DEX/INT (player). Mana follows the
        // same convention even though most monsters skip mana entirely.
        float maxLife = templateLife > 0f
            ? templateLife
            : ((strength - 9f) * 29.4f) + ((dexterity - 9f) * 9.8f) + ((intelligence - 9f) * 9.8f);
        float maxMana = templateMana > 0f
            ? templateMana
            : (strength - 9f) + ((dexterity - 9f) * 4f) + ((intelligence - 9f) * 25f);

        float dmgMin  = ParseFloat(store.GetAttribute(template, "attack", "damage_min")) ?? 0f;
        float dmgMax  = ParseFloat(store.GetAttribute(template, "attack", "damage_max")) ?? 0f;
        float defense = ParseFloat(store.GetAttribute(template, "defend", "defense")) ?? 0f;
        float range   = ParseFloat(store.GetAttribute(template, "attack", "attack_range")) ?? 0f;
        // avg_move_velocity is the DS1 idle-walk gait (krug ≈ 2.5, chickens ≈ 1.9).
        // Fall back to 4 u/s — what Phase 11d hardcoded — when no template in the
        // chain sets one, so non-combatants still wander at a visible pace.
        float walk    = ParseFloat(store.GetAttribute(template, "body", "avg_move_velocity")) ?? 4f;
        int   xp      = ParseInt  (store.GetAttribute(template, "aspect", "experience_value")) ?? 0;

        return new ActorStats(maxLife, maxMana, dmgMin, dmgMax, defense, range, walk, xp,
                              strength, dexterity, intelligence);
    }

    static float? ParseFloat(string? s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    static float? ParseFirstFloat(string? s)
    {
        if (s is null) return null;
        var comma = s.IndexOf(',');
        var head = comma >= 0 ? s.AsSpan(0, comma) : s.AsSpan();
        return float.TryParse(head, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    static int? ParseInt(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
}
