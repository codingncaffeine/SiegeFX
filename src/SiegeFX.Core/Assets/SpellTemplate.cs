using System.Globalization;

namespace SiegeFX.Core.Assets;

/// <summary>Categorizes a parsed spell into the gameplay path that fires it.
/// Phase 17a only modeled <see cref="OffensiveInstantHit"/>; 17c added
/// <see cref="SelfHeal"/> so the spellbook can support a non-damage slot.</summary>
public enum SpellKind
{
    OffensiveInstantHit,
    SelfHeal,
}

/// <summary>
/// One playable spell, lifted out of a DS1 <c>spell_*</c> template's
/// <c>[magic]</c> block. The fields here are the bare minimum to fire a
/// click-to-cast loop: cost (mana), reach (range), pace (cooldown), bite
/// (damage roll). DS1 ships these as math expressions in <c>#magic</c>;
/// <see cref="SpellExpr"/> resolves them at cast time using the caster's
/// current combat-magic level.
///
/// Phase 17a covered offensive instant-hit spells only — <c>spell_zap</c>
/// is the canonical example. Phase 17c folded in self-target heal spells
/// (<c>spell_healing_wind</c> et al.) by recognizing the <c>state_name = "heal"</c>
/// magic-block marker and reading the alter_life enchantment value.
/// </summary>
public sealed class SpellTemplate
{
    public string Name { get; }
    public string ScreenName { get; }
    public SpellKind Kind { get; }

    /// <summary>Distance in DS1 world units (≈feet) the caster can be from
    /// the target when the cast fires. <c>cast_range</c> in the magic block.
    /// Self-target heals ignore this in <see cref="Actors.PlayerSpellbook"/>.</summary>
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
    /// caster's combat-magic level. Empty string → 0. Only meaningful when
    /// <see cref="Kind"/> is <see cref="SpellKind.OffensiveInstantHit"/>.</summary>
    public string AttackDamageMinExpr { get; }
    public string AttackDamageMaxExpr { get; }

    /// <summary>Heal magnitude expression (alter_life enchantment value).
    /// Used when <see cref="Kind"/> is <see cref="SpellKind.SelfHeal"/>.
    /// DS1 healing is HOT, but for the 17c slice we apply the per-tick
    /// value as one instant lump — keeps mana/cooldown semantics intact
    /// without standing up an effect-script runtime.</summary>
    public string HealAmountExpr { get; }

    SpellTemplate(string name, string screenName, SpellKind kind,
        float castRange, float castReloadDelay,
        float baseManaCost, string manaCostModifierExpr,
        string attackDamageMinExpr, string attackDamageMaxExpr,
        string healAmountExpr)
    {
        Name = name;
        ScreenName = screenName;
        Kind = kind;
        CastRange = castRange;
        CastReloadDelay = castReloadDelay;
        BaseManaCost = baseManaCost;
        ManaCostModifierExpr = manaCostModifierExpr;
        AttackDamageMinExpr = attackDamageMinExpr;
        AttackDamageMaxExpr = attackDamageMaxExpr;
        HealAmountExpr = healAmountExpr;
    }

    /// <summary>Mana to charge for a cast at <paramref name="magicLevel"/>.
    /// Returns <c>MathF.Max(BaseManaCost, modifier)</c> when a modifier is
    /// present — DS1 templates split into two patterns, both of which the max
    /// preserves: (a) zap-style with base=1 and a modifier crossing 1 near L1
    /// (max picks the modifier on the way up), and (b) healing_wind-style
    /// with base=0 and the modifier as the absolute cost. The pure
    /// multiplicative reading (Phase 17a's first pass) double-charged spells
    /// like spell_leech_life (base=3, modifier=#magic*2) by treating the
    /// modifier as a multiplier; the pure modifier-wins reading (17c first
    /// pass) under-charged the same template at low levels (mod=2 &lt; 3).</summary>
    public float ManaCost(float magicLevel)
    {
        if (string.IsNullOrEmpty(ManaCostModifierExpr)) return BaseManaCost;
        float mod = SpellExpr.Eval(ManaCostModifierExpr, magicLevel);
        if (mod <= 0f) return BaseManaCost;
        return MathF.Max(BaseManaCost, mod);
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

    /// <summary>Heal magnitude at <paramref name="magicLevel"/>. Always ≥ 0.</summary>
    public float HealAmount(float magicLevel)
    {
        if (string.IsNullOrEmpty(HealAmountExpr)) return 0f;
        float v = SpellExpr.Eval(HealAmountExpr, magicLevel);
        return v > 0f ? v : 0f;
    }

    /// <summary>Build a <see cref="SpellTemplate"/> from a parsed <see cref="Template"/>
    /// in the world template store. Returns null if the template lacks both a usable
    /// offensive-cast block AND a heal-pattern signature.</summary>
    public static SpellTemplate? FromTemplate(Template template, TemplateStore store)
    {
        string? rangeStr   = store.GetAttribute(template, "magic", "cast_range");
        string? reloadStr  = store.GetAttribute(template, "magic", "cast_reload_delay");
        string? costStr    = store.GetAttribute(template, "magic", "mana_cost");
        string? costModStr = store.GetAttribute(template, "magic", "mana_cost_modifier");
        string? screenName = store.GetAttribute(template, "common", "screen_name");

        if (string.IsNullOrEmpty(rangeStr)) return null;
        if (!float.TryParse(rangeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var range)) return null;
        if (range <= 0f) return null;

        float reload = 0f;
        if (!string.IsNullOrEmpty(reloadStr))
            float.TryParse(reloadStr, NumberStyles.Float, CultureInfo.InvariantCulture, out reload);
        float cost = 0f;
        if (!string.IsNullOrEmpty(costStr))
            float.TryParse(costStr, NumberStyles.Float, CultureInfo.InvariantCulture, out cost);

        string sn = (screenName ?? template.Name).Trim().Trim('"');

        // Offensive instant-hit path: needs a damage formula.
        string? dmgMaxStr = store.GetAttribute(template, "magic", "attack_damage_modifier_max");
        string? dmgMinStr = store.GetAttribute(template, "magic", "attack_damage_modifier_min");
        if (!string.IsNullOrEmpty(dmgMaxStr))
        {
            return new SpellTemplate(template.Name, sn, SpellKind.OffensiveInstantHit,
                range, reload, cost, (costModStr ?? "").Trim(),
                (dmgMinStr ?? "").Trim(), dmgMaxStr.Trim(), "");
        }

        // Self-heal path: spells DS1 marks with state_name = "heal". The heal
        // amount lives in [magic][enchantments][*] where alteration=alter_life;
        // we take its value expression as the per-cast heal magnitude.
        string? stateName = store.GetAttribute(template, "magic", "state_name");
        if (string.Equals((stateName ?? "").Trim().Trim('"'), "heal", StringComparison.OrdinalIgnoreCase))
        {
            var enchantments = store.GetSection(template, "magic", "enchantments");
            if (enchantments is not null)
            {
                foreach (var ench in enchantments.Children)
                {
                    string? alteration = TemplateStore.FindAttr(ench, "alteration");
                    if (!string.Equals((alteration ?? "").Trim().Trim('"'), "alter_life",
                                       StringComparison.OrdinalIgnoreCase)) continue;
                    string? valueExpr = TemplateStore.FindAttr(ench, "value");
                    if (string.IsNullOrWhiteSpace(valueExpr)) continue;
                    return new SpellTemplate(template.Name, sn, SpellKind.SelfHeal,
                        range, reload, cost, (costModStr ?? "").Trim(),
                        "", "", valueExpr.Trim());
                }
            }
        }

        return null;
    }
}
