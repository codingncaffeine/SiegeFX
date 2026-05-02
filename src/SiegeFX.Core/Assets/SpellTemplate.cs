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

/// <summary>Element bucket used by the render layer to pick projectile + impact
/// color/style. DS1's [magic] block doesn't ship an explicit damage-type field
/// for offensive spells (it's implicit in the per-spell sfx_script the
/// template calls on we_req_cast), so we classify by name keyword — the same
/// taxonomy DS1 uses for its UI's Combat-Magic vs Nature-Magic split. Phase
/// 17-SC-B only needs five buckets to tell fireball from iceshard from
/// lightning at a glance.</summary>
public enum SpellElement
{
    Generic,
    Fire,
    Ice,
    Lightning,
    Acid,
    Death,
    Holy,
}

internal static class SpellElementClassifier
{
    /// <summary>Pick an element bucket from a spell template name. Conservative
    /// — anything we don't recognize falls back to <see cref="SpellElement.Generic"/>
    /// so the render layer keeps the original blue-bolt look. The keyword set
    /// covers every offensive spell in shipped DS1 (verified against the
    /// SpellCatalog dump: 69 OffensiveInstantHit templates).</summary>
    public static SpellElement FromName(string name)
    {
        if (string.IsNullOrEmpty(name)) return SpellElement.Generic;
        var n = name.ToLowerInvariant();
        // Two-pass classify. Pass 1 runs the strong (unambiguous) element
        // keywords so e.g. spell_ice_storm and spell_meteor_storm match Ice
        // and Fire respectively before pass 2 sees their "storm" suffix.
        // "ice" needs a word-boundary check or it would tag spell_apprentice_zap
        // — "ice" lives inside "apprent_ice_". The other keywords are unique
        // enough to use plain Contains.
        bool HasIceWord(string s)
        {
            int i = s.IndexOf("ice", StringComparison.Ordinal);
            while (i >= 0)
            {
                bool boundaryBefore = i == 0 || s[i - 1] == '_';
                if (boundaryBefore) return true;
                i = s.IndexOf("ice", i + 1, StringComparison.Ordinal);
            }
            return false;
        }
        if (n.Contains("fire") || n.Contains("flame") || n.Contains("burn") || n.Contains("flare")
         || n.Contains("dragon") || n.Contains("meteor") || n.Contains("inferno")
         || n.Contains("incinerate")) return SpellElement.Fire;
        if (HasIceWord(n) || n.Contains("frost") || n.Contains("freeze") || n.Contains("snow")
         || n.Contains("cold") || n.Contains("frigid")) return SpellElement.Ice;
        if (n.Contains("acid") || n.Contains("poison") || n.Contains("toxic")
         || n.Contains("plague") || n.Contains("pestilence")) return SpellElement.Acid;
        if (n.Contains("death") || n.Contains("drain") || n.Contains("decay")
         || n.Contains("necro") || n.Contains("void") || n.Contains("soul")
         || n.Contains("explode_body")) return SpellElement.Death;
        // Lightning before Holy because Holy's "light" keyword would
        // otherwise swallow spell_lightning / spell_chain_lightning. Holy
        // still picks up spell_light_ray (no electric keyword present).
        if (n.Contains("zap") || n.Contains("lightning") || n.Contains("shock")
         || n.Contains("electric") || n.Contains("blaster") || n.Contains("thunder")
         || n.Contains("chain")) return SpellElement.Lightning;
        if (n.Contains("heal") || n.Contains("cure") || n.Contains("bless")
         || n.Contains("light") || n.Contains("starburst") || n.Contains("sun")
         || n.Contains("nova")) return SpellElement.Holy;
        // Pass 2 — weaker tinge keywords: a bare "storm" or "spark" defaults
        // to Lightning, "bomb"/"blast"/"explod" to Fire. Only reached if no
        // strong keyword matched above.
        if (n.Contains("storm") || n.Contains("spark") || n.Contains("flash")) return SpellElement.Lightning;
        if (n.Contains("bomb") || n.Contains("blast") || n.Contains("explod")
         || n.Contains("explos") || n.Contains("implo")) return SpellElement.Fire;
        return SpellElement.Generic;
    }
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

    /// <summary>Phase 17-SC-B — element bucket derived from <see cref="Name"/>.
    /// Drives projectile + impact tinting in the render layer; doesn't touch
    /// damage math.</summary>
    public SpellElement Element { get; }

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

    /// <summary>Phase 17-SC-H — name of the sfx_script the template's
    /// <c>[common][template_triggers]</c> matrix invokes on
    /// <c>we_req_cast</c> (the actual cast event, not the charge-up).
    /// Empty string when the template chain has no such trigger row, in
    /// which case the renderer falls back to the legacy dot-trail bolt.
    /// SfxRuntime.Spawn with this name reproduces DS1's per-spell
    /// projectile + impact visuals (fireball -> fire+smoke columns,
    /// zap -> lightning burst, etc.) instead of the placeholder trail.</summary>
    public string CastSfxScript { get; }

    /// <summary>Phase 21-SC-SPELL-A — basename of the spell's icon RAW in the
    /// <c>b_gui_ig_i_ic_sp_*_inv</c> set. Pulled from the template's
    /// <c>[gui]inventory_icon</c> attribute. Empty string when the chain has
    /// no gui block (creator-defined or stub spells), in which case the
    /// SpellBookPanel falls back to an element-tinted placeholder.</summary>
    public string InventoryIcon { get; }

    SpellTemplate(string name, string screenName, SpellKind kind,
        float castRange, float castReloadDelay,
        float baseManaCost, string manaCostModifierExpr,
        string attackDamageMinExpr, string attackDamageMaxExpr,
        string healAmountExpr,
        string castSfxScript,
        string inventoryIcon)
    {
        Name = name;
        ScreenName = screenName;
        Kind = kind;
        Element = SpellElementClassifier.FromName(name);
        CastRange = castRange;
        CastReloadDelay = castReloadDelay;
        BaseManaCost = baseManaCost;
        ManaCostModifierExpr = manaCostModifierExpr;
        AttackDamageMinExpr = attackDamageMinExpr;
        AttackDamageMaxExpr = attackDamageMaxExpr;
        HealAmountExpr = healAmountExpr;
        CastSfxScript = castSfxScript;
        InventoryIcon = inventoryIcon;
    }

    /// <summary>Mana to charge for a cast against <paramref name="ctx"/>.
    /// Returns <c>MathF.Max(BaseManaCost, modifier)</c> when a modifier is
    /// present — DS1 templates split into two patterns, both of which the max
    /// preserves: (a) zap-style with base=1 and a modifier crossing 1 near L1
    /// (max picks the modifier on the way up), and (b) healing_wind-style
    /// with base=0 and the modifier as the absolute cost. The pure
    /// multiplicative reading (Phase 17a's first pass) double-charged spells
    /// like spell_leech_life (base=3, modifier=#magic*2) by treating the
    /// modifier as a multiplier; the pure modifier-wins reading (17c first
    /// pass) under-charged the same template at low levels (mod=2 &lt; 3).</summary>
    public float ManaCost(in SpellEvalContext ctx)
    {
        if (string.IsNullOrEmpty(ManaCostModifierExpr)) return BaseManaCost;
        float mod = SpellExpr.Eval(ManaCostModifierExpr, ctx);
        if (mod <= 0f) return BaseManaCost;
        return MathF.Max(BaseManaCost, mod);
    }

    /// <summary>Magic-only convenience overload (survey CLIs, callers that
    /// don't yet plumb caster/target stats).</summary>
    public float ManaCost(float magicLevel) => ManaCost(new SpellEvalContext(magicLevel));

    /// <summary>Roll a damage value in [min, max] resolved against
    /// <paramref name="ctx"/>. Returns 0 if both expressions evaluate to 0
    /// (broken template) so the caller can short-circuit instead of dividing
    /// by zero.</summary>
    public float RollDamage(in SpellEvalContext ctx, Random rng)
    {
        float lo = SpellExpr.Eval(AttackDamageMinExpr, ctx);
        float hi = SpellExpr.Eval(AttackDamageMaxExpr, ctx);
        if (hi < lo) (lo, hi) = (hi, lo);
        if (hi <= 0f) return 0f;
        if (hi <= lo) return lo;
        return lo + (float)rng.NextDouble() * (hi - lo);
    }

    public float RollDamage(float magicLevel, Random rng)
        => RollDamage(new SpellEvalContext(magicLevel), rng);

    /// <summary>Heal magnitude against <paramref name="ctx"/>. Always ≥ 0.</summary>
    public float HealAmount(in SpellEvalContext ctx)
    {
        if (string.IsNullOrEmpty(HealAmountExpr)) return 0f;
        float v = SpellExpr.Eval(HealAmountExpr, ctx);
        return v > 0f ? v : 0f;
    }

    public float HealAmount(float magicLevel) => HealAmount(new SpellEvalContext(magicLevel));

    /// <summary>Phase 21-SC-SPELL-VFX-3p — build a synthetic
    /// <see cref="SpellTemplate"/> from any <see cref="Template"/>, including
    /// summon templates and non-spell templates that the offensive/heal
    /// predicates in <see cref="FromTemplate"/> would skip. Used by the
    /// <c>SIEGEFX_DEBUG_SPELLS</c> launch override so the user can slot any
    /// spell template (summons, charm/buff, vendor-test scrolls, etc.) and
    /// see/hear its authored cast effect on Q-press, without needing the
    /// gameplay payload (creature spawn / charm-target / etc.) wired up.
    ///
    /// <para>The synthetic spell is classified as <see cref="SpellKind.OffensiveInstantHit"/>
    /// with zero damage and zero mana cost — the user RMB-clicks any nearby
    /// enemy (fh_r1 has krug in abundance) and Q-presses to fire. The cast
    /// site's offensive branch runs the resolved cast sfx_script and plays
    /// the authored sound; no actual damage applies. Range defaults to 30u
    /// (DS1 typical), cooldown to 1s.</para></summary>
    public static SpellTemplate FromTemplateForDebug(Template template, TemplateStore store)
    {
        string? screenName = store.GetAttribute(template, "common", "screen_name");
        string sn = (screenName ?? template.Name).Trim().Trim('"');
        string castSfx = ResolveCastSfxScript(template);
        string invIcon = (store.GetAttribute(template, "gui", "inventory_icon") ?? "")
                         .Trim().Trim('"');

        return new SpellTemplate(template.Name, sn, SpellKind.OffensiveInstantHit,
            castRange: 30f, castReloadDelay: 1f,
            baseManaCost: 0f, manaCostModifierExpr: "",
            attackDamageMinExpr: "0", attackDamageMaxExpr: "0",
            healAmountExpr: "",
            castSfx, invIcon);
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
        string castSfx = ResolveCastSfxScript(template);
        string invIcon = (store.GetAttribute(template, "gui", "inventory_icon") ?? "")
                         .Trim().Trim('"');

        // Offensive instant-hit path: needs a damage formula.
        string? dmgMaxStr = store.GetAttribute(template, "magic", "attack_damage_modifier_max");
        string? dmgMinStr = store.GetAttribute(template, "magic", "attack_damage_modifier_min");
        if (!string.IsNullOrEmpty(dmgMaxStr))
        {
            return new SpellTemplate(template.Name, sn, SpellKind.OffensiveInstantHit,
                range, reload, cost, (costModStr ?? "").Trim(),
                (dmgMinStr ?? "").Trim(), dmgMaxStr.Trim(), "",
                castSfx, invIcon);
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
                        "", "", valueExpr.Trim(),
                        castSfx, invIcon);
                }
            }
        }

        return null;
    }

    /// <summary>Phase 17-SC-H — pull the sfx_script name out of the template
    /// chain. DS1 ships two ways to bind a cast effect, and shipped templates
    /// use them about evenly (19 / 50 in the 69-spell offensive catalog):
    ///
    /// (1) <c>[common][template_triggers][*]</c> with
    ///     <c>condition* = receive_world_message("we_req_cast")</c> and
    ///     <c>action* = call_sfx_script("&lt;name&gt;")</c>. Used by fireball,
    ///     fireshot, implosion, etc. — the "trigger matrix" path.
    ///
    /// (2) A category block <c>[spell_lightning]</c> / <c>[spell_fire]</c> /
    ///     <c>[spell_summon]</c> / etc. on the template root with a flat
    ///     <c>effect_script = &lt;name&gt;;</c> attribute. Used by zap,
    ///     iceshard, healing_wind, etc.
    ///
    /// Walks specializes-chain leaf-first so a derived template's override
    /// wins over the base — matches DS1's <c>specializes</c> resolution. Path
    /// (1) is checked first since it's the more explicit binding when both
    /// happen to be present.</summary>
    static string ResolveCastSfxScript(Template template)
    {
        // Path 1: [common][template_triggers] matrix, condition we_req_cast.
        // DS1 templates occasionally split [common] into two sibling blocks
        // (one with screen_name, one with description + template_triggers —
        // see spell_iceshard). FindChild only returns the first, so iterate
        // all sibling [common] nodes and check each for a triggers block.
        for (var t = template; t is not null; t = t.Specializes)
        {
            foreach (var common in t.Node.Children)
            {
                if (!common.Header.Equals("common", StringComparison.OrdinalIgnoreCase)) continue;
                var triggers = TemplateStore.FindChild(common, "template_triggers");
                if (triggers is null) continue;
                foreach (var row in triggers.Children)
                {
                    if (!row.Header.Equals("*", StringComparison.Ordinal)) continue;

                    // condition* / action* are repeating attributes; for the
                    // cast-script lookup we only care that *some* condition is
                    // we_req_cast and *some* action is call_sfx_script in the
                    // same row.
                    bool isCastRow = false;
                    string? scriptName = null;
                    foreach (var attr in row.Attributes)
                    {
                        if (attr.Name.Equals("condition*", StringComparison.Ordinal)
                            && IsCastCondition(attr.Value))
                            isCastRow = true;
                        else if (attr.Name.Equals("action*", StringComparison.Ordinal))
                        {
                            var name = ExtractCallSfxScriptArg(attr.Value);
                            if (!string.IsNullOrEmpty(name)) scriptName = name;
                        }
                    }
                    if (isCastRow && !string.IsNullOrEmpty(scriptName)) return scriptName!;
                }
            }
        }

        // Path 2: any [spell_*] root block with effect_script. Bare attribute
        // value (unquoted, no parens) — TemplateStore.FindAttr returns the
        // literal string verbatim.
        for (var t = template; t is not null; t = t.Specializes)
        {
            foreach (var child in t.Node.Children)
            {
                if (!child.Header.StartsWith("spell_", StringComparison.OrdinalIgnoreCase))
                    continue;
                var name = TemplateStore.FindAttr(child, "effect_script");
                if (string.IsNullOrWhiteSpace(name)) continue;
                return name.Trim().Trim('"');
            }
        }
        return string.Empty;
    }

    static bool IsCastCondition(string raw)
    {
        var s = raw.Trim();
        if (!s.StartsWith("receive_world_message", StringComparison.OrdinalIgnoreCase)) return false;
        int open = s.IndexOf('(');
        int close = s.LastIndexOf(')');
        if (open < 0 || close <= open) return false;
        var arg = s.Substring(open + 1, close - open - 1).Trim().Trim('"');
        return arg.Equals("we_req_cast", StringComparison.OrdinalIgnoreCase);
    }

    static string ExtractCallSfxScriptArg(string raw)
    {
        var s = raw.Trim();
        if (!s.StartsWith("call_sfx_script", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        int open = s.IndexOf('(');
        int close = s.LastIndexOf(')');
        if (open < 0 || close <= open) return string.Empty;
        var arg = s.Substring(open + 1, close - open - 1).Trim().Trim('"');
        return arg;
    }
}
