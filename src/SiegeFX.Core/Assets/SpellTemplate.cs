using System.Globalization;

namespace SiegeFX.Core.Assets;

/// <summary>
/// One playable spell, lifted out of a DS1 <c>spell_*</c> template's
/// <c>[magic]</c> block. The fields here are the bare minimum to fire a
/// click-to-cast loop: cost (mana), reach (range), pace (cooldown), bite
/// (damage roll). DS1 ships these as math expressions in <c>#magic</c>;
/// <see cref="SpellExpr"/> resolves them at cast time using the caster's
/// current combat-magic level.
///
/// Phase 17a covers offensive instant-hit spells only — <c>spell_zap</c>
/// is the canonical example. Heals, buffs, summons, AoE etc. can reuse
/// this shape (they all live in the same template forest) once their
/// spell-skrit equivalents land in later slices.
/// </summary>
public sealed class SpellTemplate
{
    public string Name { get; }
    public string ScreenName { get; }

    /// <summary>Distance in DS1 world units (≈feet) the caster can be from
    /// the target when the cast fires. <c>cast_range</c> in the magic block.</summary>
    public float CastRange { get; }

    /// <summary>Seconds between successive casts of this spell. <c>cast_reload_delay</c>
    /// in the magic block; defaults to 0 for "no cooldown" rather than throwing.</summary>
    public float CastReloadDelay { get; }

    /// <summary>Base mana cost before <see cref="ManaCostModifierExpr"/> scaling.</summary>
    public float BaseManaCost { get; }

    /// <summary>Multiplier applied to <see cref="BaseManaCost"/> via
    /// <see cref="SpellExpr"/>. Empty string → multiplier of 1.</summary>
    public string ManaCostModifierExpr { get; }

    /// <summary>Damage range — both bounds resolved per cast against the
    /// caster's combat-magic level. Empty string → 0.</summary>
    public string AttackDamageMinExpr { get; }
    public string AttackDamageMaxExpr { get; }

    public SpellTemplate(string name, string screenName,
        float castRange, float castReloadDelay,
        float baseManaCost, string manaCostModifierExpr,
        string attackDamageMinExpr, string attackDamageMaxExpr)
    {
        Name = name;
        ScreenName = screenName;
        CastRange = castRange;
        CastReloadDelay = castReloadDelay;
        BaseManaCost = baseManaCost;
        ManaCostModifierExpr = manaCostModifierExpr;
        AttackDamageMinExpr = attackDamageMinExpr;
        AttackDamageMaxExpr = attackDamageMaxExpr;
    }

    /// <summary>Mana to charge for a cast at <paramref name="magicLevel"/>.
    /// Modifier expression returning &lt;= 0 collapses to the base cost — DS1
    /// formulas can produce sub-1 multipliers but never zero on legit data.</summary>
    public float ManaCost(float magicLevel)
    {
        if (string.IsNullOrEmpty(ManaCostModifierExpr)) return BaseManaCost;
        float mod = SpellExpr.Eval(ManaCostModifierExpr, magicLevel);
        if (mod <= 0f) return BaseManaCost;
        return BaseManaCost * mod;
    }

    /// <summary>Roll a damage value in [min, max] resolved at the caster's
    /// magic level. Returns 0 if both expressions evaluate to 0 (broken
    /// template) so the caller can short-circuit instead of dividing by zero.</summary>
    public float RollDamage(float magicLevel, Random rng)
    {
        float lo = SpellExpr.Eval(AttackDamageMinExpr, magicLevel);
        float hi = SpellExpr.Eval(AttackDamageMaxExpr, magicLevel);
        if (hi < lo) (lo, hi) = (hi, lo);
        if (hi <= 0f) return 0f;
        if (hi <= lo) return lo;
        return lo + (float)rng.NextDouble() * (hi - lo);
    }

    /// <summary>Build a <see cref="SpellTemplate"/> from a parsed <see cref="Template"/>
    /// in the world template store. Returns null if the template lacks a usable
    /// <c>[magic]</c> block (e.g. <c>spell</c> base or non-spell entries that
    /// happen to share the spell_* naming). Walks the specializes chain when
    /// fields are missing on the leaf.</summary>
    public static SpellTemplate? FromTemplate(Template template, TemplateStore store)
    {
        // Walk the chain to find a magic block — base templates don't carry one;
        // some leaves rely on inherited fields. We collect from the deepest
        // ancestor down so leaf overrides win.
        string? rangeStr = store.GetAttribute(template, "magic", "cast_range");
        string? reloadStr = store.GetAttribute(template, "magic", "cast_reload_delay");
        string? costStr = store.GetAttribute(template, "magic", "mana_cost");
        string? costModStr = store.GetAttribute(template, "magic", "mana_cost_modifier");
        string? dmgMaxStr = store.GetAttribute(template, "magic", "attack_damage_modifier_max");
        string? dmgMinStr = store.GetAttribute(template, "magic", "attack_damage_modifier_min");
        string? screenName = store.GetAttribute(template, "common", "screen_name");

        // Spells without a cast_range and a damage formula aren't offensive
        // instant-hit spells in the Phase 17a sense — heals, buffs, summons,
        // etc. — so they're filtered out here and revisited in 17b+.
        if (string.IsNullOrEmpty(rangeStr) || string.IsNullOrEmpty(dmgMaxStr)) return null;
        if (!float.TryParse(rangeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var range)) return null;
        if (range <= 0f) return null;

        float reload = 0f;
        if (!string.IsNullOrEmpty(reloadStr))
            float.TryParse(reloadStr, NumberStyles.Float, CultureInfo.InvariantCulture, out reload);

        float cost = 0f;
        if (!string.IsNullOrEmpty(costStr))
            float.TryParse(costStr, NumberStyles.Float, CultureInfo.InvariantCulture, out cost);

        return new SpellTemplate(
            template.Name,
            (screenName ?? template.Name).Trim().Trim('"'),
            range,
            reload,
            cost,
            (costModStr ?? "").Trim(),
            (dmgMinStr ?? "").Trim(),
            (dmgMaxStr ?? "").Trim());
    }
}
