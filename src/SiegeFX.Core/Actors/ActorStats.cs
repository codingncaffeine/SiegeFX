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

    // SC-MOB-RANGES — authored [mind] perception/engagement distances and the
    // [attack] swing cadence. 0 = unauthored (brain falls back to its tuned
    // defaults). base_krug ships 14u sight/engage + 8u com_range; the old
    // hardcoded 8u aggro under-read DS1 by 43%.
    public float SightRange { get; init; }
    public float MeleeEngageRange { get; init; }
    public float RangedEngageRange { get; init; }
    public float ComRange { get; init; }
    public float ReloadDelay { get; init; }
    public bool  AlertFriends { get; init; }

    // SC-MOB-CASTER / SC-MOB-RANGED — attack-mode identity, parameter-driven
    // exactly like DS1's single shared brain skrit: WP_MELEE / WP_RANGED /
    // WP_MAGIC preference plus the active-slot template refs.
    public string? WeaponPreference { get; init; }
    public bool  AutoSwitchToMagic { get; init; }
    public bool  AutoSwitchToRanged { get; init; }
    public bool  IczSwitchToMelee { get; init; }
    public string? ActiveLocation { get; init; }
    public string? PrimarySpell { get; init; }
    public string? SecondarySpell { get; init; }

    // SC-SKILL-LEVELS — the four DS1 class skill levels + cumulative uberlevel,
    // authored in [actor][skills] as "level, ?, base" triples (base 0 for skills).
    // Companions ship differentiated (gyorn melee 2, sikra combat_magic 38); the
    // hero and most monsters leave them at 0. Skill level feeds armor-penetration
    // (dmg = f(skill) / defense), hire_stats display, and follower XP-leveling.
    public float MeleeSkill { get; init; }
    public float RangedSkill { get; init; }
    public float CombatMagicSkill { get; init; }
    public float NatureMagicSkill { get; init; }
    public float UberLevel { get; init; }

    public static ActorStats FromTemplate(TemplateStore store, Template template)
    {
        // DS1 authors [actor][skills] entries as "value, ?, base" triples, where the
        // effective score = value + base (base 10 for attributes, 0 for skills/monsters).
        // gyorn's "strength = 1.28, 0, 10" is a level-1.28 gain over base 10 = 11.28,
        // NOT a raw 1.28. Read the [actor][skills] block (fall back to a root [skills]
        // for any template that authors the flat form); default missing attributes to
        // 10 to match DS1's PC starting block.
        string? Skill(string name) =>
            store.GetAttribute(template, "actor", "skills", name)
            ?? store.GetAttribute(template, "skills", name);

        float strength     = ParseAttribute(Skill("strength"))     ?? 10f;
        float dexterity    = ParseAttribute(Skill("dexterity"))    ?? 10f;
        float intelligence = ParseAttribute(Skill("intelligence")) ?? 10f;
        float meleeSkill   = ParseAttribute(Skill("melee"))         ?? 0f;
        float rangedSkill  = ParseAttribute(Skill("ranged"))        ?? 0f;
        float cmagSkill    = ParseAttribute(Skill("combat_magic"))  ?? 0f;
        float nmagSkill    = ParseAttribute(Skill("nature_magic"))  ?? 0f;
        float uberLevel    = ParseAttribute(Skill("uber"))          ?? 0f;

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
        // Clamp the attribute-derived pools at 0: a low-stat monster that authors no
        // explicit mana (e.g. krug_scout at 5/4/3) would otherwise derive a negative
        // pool. Monsters don't use mana, but a negative max is never valid.
        float maxLife = templateLife > 0f
            ? templateLife
            : MathF.Max(0f, ((strength - 9f) * 29.4f) + ((dexterity - 9f) * 9.8f) + ((intelligence - 9f) * 9.8f));
        float maxMana = templateMana > 0f
            ? templateMana
            : MathF.Max(0f, (strength - 9f) + ((dexterity - 9f) * 4f) + ((intelligence - 9f) * 25f));

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
                              strength, dexterity, intelligence)
        {
            SightRange        = ParseFloat(store.GetAttribute(template, "mind", "sight_range")) ?? 0f,
            MeleeEngageRange  = ParseFloat(store.GetAttribute(template, "mind", "melee_engage_range")) ?? 0f,
            RangedEngageRange = ParseFloat(store.GetAttribute(template, "mind", "ranged_engage_range")) ?? 0f,
            ComRange          = ParseFloat(store.GetAttribute(template, "mind", "com_range")) ?? 0f,
            ReloadDelay       = ParseFloat(store.GetAttribute(template, "attack", "reload_delay")) ?? 0f,
            AlertFriends      = ParseBool(store.GetAttribute(template, "mind", "on_enemy_spotted_alert_friends")),
            WeaponPreference  = Clean(store.GetAttribute(template, "mind", "actor_weapon_preference")),
            AutoSwitchToMagic = ParseBool(store.GetAttribute(template, "mind", "actor_auto_switches_to_magic")),
            AutoSwitchToRanged = ParseBool(store.GetAttribute(template, "mind", "actor_auto_switches_to_ranged")),
            IczSwitchToMelee  = ParseBool(store.GetAttribute(template, "mind", "on_enemy_entered_icz_switch_to_melee")),
            ActiveLocation    = Clean(store.GetAttribute(template, "inventory", "selected_active_location")),
            PrimarySpell      = Clean(store.GetAttribute(template, "inventory", "other", "il_active_primary_spell")),
            SecondarySpell    = Clean(store.GetAttribute(template, "inventory", "other", "il_active_secondary_spell")),
            MeleeSkill        = meleeSkill,
            RangedSkill       = rangedSkill,
            CombatMagicSkill  = cmagSkill,
            NatureMagicSkill  = nmagSkill,
            UberLevel         = uberLevel,
        };
    }

    static bool ParseBool(string? s) =>
        s is not null && s.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

    static string? Clean(string? s)
    {
        if (s is null) return null;
        var t = s.Trim().Trim('"');
        return t.Length == 0 ? null : t;
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

    /// <summary>Parses a DS1 "value, ?, base" skill/attribute triple to its effective
    /// score (value + base). The base half is optional — monster attributes author
    /// "16, 0" (base 0 → 16); PC/companion attributes author "1.28, 0, 10" (→ 11.28).</summary>
    static float? ParseAttribute(string? s)
    {
        if (s is null) return null;
        var parts = s.Split(',');
        if (parts.Length == 0 ||
            !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;
        float baseScore = 0f;
        if (parts.Length >= 3)
            float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out baseScore);
        return value + baseScore;
    }

    static int? ParseInt(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
}
