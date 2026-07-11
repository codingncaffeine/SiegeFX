using System.Globalization;
using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>One of DS1's four character disciplines. The proportional-gains table
/// (<see cref="FormulasStore.ProportionalGains"/>) keys off this enum.</summary>
public enum SkillKind { Melee, Ranged, NatureMagic, CombatMagic }

/// <summary>Tunable triple — proportions of STR / DEX / INT credited to a skill's
/// xp pool. Sums to ~1.0 per skill in shipped data.</summary>
public readonly record struct AttributeShare(float Str, float Dex, float Int);

/// <summary>
/// Parsed view of <c>/world/global/formula/formulas.gas</c> from Logic.dsres. Holds the
/// canonical gameplay constants — HP/MP base scalars, recovery rates, the per-skill
/// STR/DEX/INT influence table, and the 160-entry XP table — so combat code can
/// stay data-driven rather than hardcoding 2002-era spreadsheets.
///
/// Parallel to <see cref="TemplateStore"/>: load once at world start, keep alive for
/// the session, query as needed. Mutations require a re-extract; this class is read-only.
/// </summary>
public sealed class FormulasStore
{
    public float MaxLifeBase    { get; }
    public float MaxLifeConstant{ get; }
    public float MaxLifeStrPct  { get; }
    public float MaxLifeDexPct  { get; }
    public float MaxLifeIntPct  { get; }

    public float MaxManaBase    { get; }
    public float MaxManaConstant{ get; }
    public float MaxManaStrPct  { get; }
    public float MaxManaDexPct  { get; }
    public float MaxManaIntPct  { get; }

    /// <summary>Damage multiplier of max-life below which an unconscious hero
    /// actually dies. DS1 ships 0.66 → you can be hit 66% past zero before dying.</summary>
    public float DeathThreshold { get; }

    /// <summary>life_recovery = (lr_unit / lr_period) HP/sec when STR ≥ 10. Below
    /// 10 a one-ninth-scaled formula is used; <see cref="LifeRecoveryRate"/> picks
    /// the right branch.</summary>
    public float LifeRecoveryUnit   { get; }
    public float LifeRecoveryPeriod { get; }
    public float ManaRecoveryUnit   { get; }
    public float ManaRecoveryPeriod { get; }

    /// <summary>One row per <see cref="SkillKind"/>. Used to credit auto-grown
    /// STR/DEX/INT against the discipline that earned the xp.</summary>
    private readonly Dictionary<SkillKind, AttributeShare> _gains;

    /// <summary>Cumulative xp required to reach level N (index 0 == level 1 == 0 xp).
    /// 160 entries in shipped data; capped to 250 via skill max_level.</summary>
    public IReadOnlyList<long> XpTable { get; }

    /// <summary>DS1 <c>[combat_constants]</c> — attack/defend rating coefficients,
    /// to-hit curve, armor scalar, difficulty multipliers. Consumed by CombatResolver.</summary>
    public CombatConstants Combat { get; }

    /// <summary>DS1 <c>[experience_limiting_factors]</c> — a single XP award is
    /// capped to this fraction of the XP delta between the character's current
    /// and next level. Shipped: 0.10 while at level 1, 0.025 afterwards. Keeps
    /// one huge hit on a high-value monster from vaulting multiple levels.</summary>
    public float XpFirstLevelFactor { get; private set; } = 0.10f;
    public float XpLaterLevelsFactor { get; private set; } = 0.025f;

    private FormulasStore(
        float maxLifeBase, float maxLifeConstant, float maxLifeStr, float maxLifeDex, float maxLifeInt,
        float maxManaBase, float maxManaConstant, float maxManaStr, float maxManaDex, float maxManaInt,
        float deathThreshold,
        float lrUnit, float lrPeriod, float mrUnit, float mrPeriod,
        Dictionary<SkillKind, AttributeShare> gains, IReadOnlyList<long> xpTable,
        CombatConstants combat)
    {
        MaxLifeBase = maxLifeBase; MaxLifeConstant = maxLifeConstant;
        MaxLifeStrPct = maxLifeStr; MaxLifeDexPct = maxLifeDex; MaxLifeIntPct = maxLifeInt;
        MaxManaBase = maxManaBase; MaxManaConstant = maxManaConstant;
        MaxManaStrPct = maxManaStr; MaxManaDexPct = maxManaDex; MaxManaIntPct = maxManaInt;
        DeathThreshold = deathThreshold;
        LifeRecoveryUnit = lrUnit; LifeRecoveryPeriod = lrPeriod;
        ManaRecoveryUnit = mrUnit; ManaRecoveryPeriod = mrPeriod;
        _gains = gains;
        XpTable = xpTable;
        Combat = combat;
    }

    public AttributeShare ProportionalGains(SkillKind skill) =>
        _gains.TryGetValue(skill, out var v) ? v : default;

    /// <summary>Player MaxLife formula (templates with author-set <c>aspect.max_life=0</c>).
    /// Below 10 in any attribute is treated as 10 — DS1 doesn't subtract negative deltas.</summary>
    public float MaxLife(float str, float dex, float intl)
    {
        float s = MathF.Max(0f, str  - 9f);
        float d = MathF.Max(0f, dex  - 9f);
        float i = MathF.Max(0f, intl - 9f);
        return MaxLifeBase + (s * MaxLifeStrPct + d * MaxLifeDexPct + i * MaxLifeIntPct) * MaxLifeConstant;
    }

    public float MaxMana(float str, float dex, float intl)
    {
        float s = MathF.Max(0f, str  - 9f);
        float d = MathF.Max(0f, dex  - 9f);
        float i = MathF.Max(0f, intl - 9f);
        return MaxManaBase + (s * MaxManaStrPct + d * MaxManaDexPct + i * MaxManaIntPct) * MaxManaConstant;
    }

    /// <summary>Life regen in HP per second. STR &lt; 10 hits the "weak" branch
    /// (scaled to one-ninth); STR ≥ 10 uses the standard (str-9) * unit/period formula.</summary>
    public float LifeRecoveryRate(float str)
    {
        float lr = LifeRecoveryUnit / LifeRecoveryPeriod;
        if (str < 10f) return (lr / 9f) * MathF.Max(0f, str);
        return (str - 9f) * lr;
    }

    /// <summary>Mana regen in MP per second. INT &lt; 10 returns the flat unit/period
    /// rate; INT ≥ 10 uses the (int-9) scaling.</summary>
    public float ManaRecoveryRate(float intl)
    {
        float mr = ManaRecoveryUnit / ManaRecoveryPeriod;
        if (intl < 10f) return mr;
        return (intl - 9f) * mr;
    }

    /// <summary>Cumulative xp threshold to reach <paramref name="level"/> (1-based).
    /// Returns the last entry of the table for any level past the cap.</summary>
    public long XpForLevel(int level)
    {
        if (XpTable.Count == 0) return 0;
        int idx = Math.Clamp(level - 1, 0, XpTable.Count - 1);
        return XpTable[idx];
    }

    /// <summary>Skill level (1-based) corresponding to a cumulative xp pool.
    /// Linear scan — table is short (≤160) and only consulted on level-up checks.</summary>
    public int LevelForXp(long xp)
    {
        int level = 1;
        for (int i = 0; i < XpTable.Count; i++)
        {
            if (xp >= XpTable[i]) level = i + 1;
            else break;
        }
        return level;
    }

    /// <summary>Loads <c>/world/global/formula/formulas.gas</c> from a Logic.dsres-style
    /// tank. Path defaults match the shipped game; pass an explicit path for mods that
    /// move the file. Throws on missing file (Phase 16+ always needs this — fail loud).</summary>
    public static FormulasStore LoadFromTank(TankReader tank,
        string path = "/world/global/formula/formulas.gas")
    {
        var bytes = tank.ExtractToMemory(path);
        return Load(bytes);
    }

    public static FormulasStore Load(byte[] gasBytes)
    {
        var doc = GasDocument.Load(gasBytes);

        var recalc = FindBlock(doc.Roots, "recalculation_constants");
        var combat = FindBlock(doc.Roots, "combat_constants");
        var skills = FindBlock(doc.Roots, "actor_skills");
        var xpTbl  = FindBlock(doc.Roots, "experience_table");

        float lifeBase  = ReadFloat(recalc, "max_life_base", 0f);
        float lifeConst = ReadFloat(recalc, "max_life_constant", 14f);
        float lifeStr   = ReadFloat(recalc, "max_life_str_percent", 2.1f);
        float lifeDex   = ReadFloat(recalc, "max_life_dex_percent", 0.7f);
        float lifeInt   = ReadFloat(recalc, "max_life_int_percent", 0.7f);

        float manaBase  = ReadFloat(recalc, "max_mana_base", 0f);
        float manaConst = ReadFloat(recalc, "max_mana_constant", 1f);
        float manaStr   = ReadFloat(recalc, "max_mana_str_percent", 1f);
        float manaDex   = ReadFloat(recalc, "max_mana_dex_percent", 4f);
        float manaInt   = ReadFloat(recalc, "max_mana_int_percent", 25f);

        float death = ReadFloat(combat, "death_threshold", 0.66f);

        // Recovery rates live inside [general_formulas].skrit as `property float ... = N`
        // — easier to grep the raw bytes than to parse skrit source. Defaults (1/4, 1/3)
        // match the shipped values, so failure to find them silently degrades to canon.
        string text = System.Text.Encoding.ASCII.GetString(gasBytes);
        float lrUnit   = ReadSkritProp(text, "lr_unit$",   1f);
        float lrPeriod = ReadSkritProp(text, "lr_period$", 4f);
        float mrUnit   = ReadSkritProp(text, "mr_unit$",   1f);
        float mrPeriod = ReadSkritProp(text, "mr_period$", 3f);

        var gains = new Dictionary<SkillKind, AttributeShare>();
        if (skills is not null)
        {
            foreach (var skill in skills.Children)
            {
                if (!skill.Header.StartsWith("skill", StringComparison.OrdinalIgnoreCase)) continue;
                string? name = ReadString(skill, "name");
                if (name is null) continue;
                if (!TryMapSkill(name, out var kind)) continue;
                float s = ReadFloat(skill, "str_influence", 0f);
                float d = ReadFloat(skill, "dex_influence", 0f);
                float i = ReadFloat(skill, "int_influence", 0f);
                gains[kind] = new AttributeShare(s, d, i);
            }
        }

        var xp = ParseXpTable(xpTbl);
        var cc = ParseCombat(combat);

        var store = new FormulasStore(
            lifeBase, lifeConst, lifeStr, lifeDex, lifeInt,
            manaBase, manaConst, manaStr, manaDex, manaInt,
            death, lrUnit, lrPeriod, mrUnit, mrPeriod,
            gains, xp, cc);

        var limits = FindBlock(doc.Roots, "experience_limiting_factors");
        store.XpFirstLevelFactor  = ReadFloat(limits, "first_level",  store.XpFirstLevelFactor);
        store.XpLaterLevelsFactor = ReadFloat(limits, "later_levels", store.XpLaterLevelsFactor);
        return store;
    }

    /// <summary>Parse the <c>[combat_constants]</c> block. The attack/defend rating
    /// coefficients live in nested <c>[attack_rating]</c>/<c>[defend_rating]</c>
    /// sub-blocks (NOT the flat <c>skill_scalar/dex_scalar/int_scalar</c> at the block
    /// root — those are the ranged aiming-error terms). Everything else is a flat
    /// attribute of the block. Missing values fall back to the shipped defaults.</summary>
    private static CombatConstants ParseCombat(GasNode? combat)
    {
        var d = CombatConstants.Ds1Default;
        if (combat is null) return d;

        GasNode? ar = null, dr = null;
        foreach (var c in combat.Children)
        {
            if (string.Equals(c.Header, "attack_rating", StringComparison.OrdinalIgnoreCase)) ar = c;
            else if (string.Equals(c.Header, "defend_rating", StringComparison.OrdinalIgnoreCase)) dr = c;
        }

        return new CombatConstants(
            ReadFloat(ar, "skill_scalar", d.AttackSkillScalar), ReadFloat(ar, "dex_scalar", d.AttackDexScalar), ReadFloat(ar, "int_scalar", d.AttackIntScalar),
            ReadFloat(dr, "skill_scalar", d.DefendSkillScalar), ReadFloat(dr, "dex_scalar", d.DefendDexScalar), ReadFloat(dr, "int_scalar", d.DefendIntScalar),
            ReadFloat(combat, "hit_chance", d.BaseHitChance),
            ReadFloat(combat, "attacker_diff_scalar", d.AttackerDiffScalar),
            ReadFloat(combat, "victim_diff_scalar", d.VictimDiffScalar),
            ReadFloat(combat, "attacker_hit_cap", d.AttackerHitCap),
            ReadFloat(combat, "defender_hit_cap", d.DefenderHitCap),
            ReadFloat(combat, "armor_scalar", d.ArmorScalar),
            ReadFloat(combat, "difficulty_medium_player", d.DifficultyPlayer),
            ReadFloat(combat, "difficulty_medium_computer", d.DifficultyComputer),
            ReadFloat(combat, "difficulty_easy_player", d.DifficultyEasyPlayer),
            ReadFloat(combat, "difficulty_easy_computer", d.DifficultyEasyComputer),
            ReadFloat(combat, "difficulty_hard_player", d.DifficultyHardPlayer),
            ReadFloat(combat, "difficulty_hard_computer", d.DifficultyHardComputer));
    }

    private static GasNode? FindBlock(IReadOnlyList<GasNode> roots, string header)
    {
        foreach (var n in roots)
            if (string.Equals(n.Header, header, StringComparison.OrdinalIgnoreCase)) return n;
        return null;
    }

    private static float ReadFloat(GasNode? node, string attr, float fallback)
    {
        if (node is null) return fallback;
        foreach (var a in node.Attributes)
            if (string.Equals(a.Name, attr, StringComparison.OrdinalIgnoreCase) &&
                float.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v;
        return fallback;
    }

    private static string? ReadString(GasNode node, string attr)
    {
        foreach (var a in node.Attributes)
            if (string.Equals(a.Name, attr, StringComparison.OrdinalIgnoreCase))
                return a.Value.Trim().Trim('"');
        return null;
    }

    private static bool TryMapSkill(string name, out SkillKind kind)
    {
        var n = name.Trim().Trim('"');
        switch (n.ToLowerInvariant())
        {
            case "melee":         kind = SkillKind.Melee;       return true;
            case "ranged":        kind = SkillKind.Ranged;      return true;
            case "nature magic":  kind = SkillKind.NatureMagic; return true;
            case "combat magic":  kind = SkillKind.CombatMagic; return true;
            default:              kind = default;               return false;
        }
    }

    private static IReadOnlyList<long> ParseXpTable(GasNode? node)
    {
        // experience_table holds a single attribute named "*" whose value is "[[ N1, N2, ... ]]".
        // Parse the bracketed comma list.
        if (node is null) return Array.Empty<long>();
        foreach (var a in node.Attributes)
        {
            if (a.Name != "*") continue;
            int open = a.Value.IndexOf("[[", StringComparison.Ordinal);
            int close = a.Value.LastIndexOf("]]", StringComparison.Ordinal);
            if (open < 0 || close <= open) continue;
            var inner = a.Value.AsSpan(open + 2, close - open - 2);
            var list = new List<long>();
            foreach (var token in inner.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    list.Add(v);
            }
            return list;
        }
        return Array.Empty<long>();
    }

    private static float ReadSkritProp(string text, string propName, float fallback)
    {
        // Match `property <type> <name> = <value>` regardless of whitespace before/after `=`.
        // The skrit source ships lr_unit$/lr_period$/mr_unit$/mr_period$ etc. as float props.
        int idx = text.IndexOf(propName, StringComparison.Ordinal);
        if (idx < 0) return fallback;
        int eq = text.IndexOf('=', idx);
        if (eq < 0) return fallback;
        // Read decimal characters only; stop at whitespace/semicolon/keyword.
        int p = eq + 1;
        while (p < text.Length && char.IsWhiteSpace(text[p])) p++;
        int start = p;
        while (p < text.Length && (char.IsDigit(text[p]) || text[p] == '.' || text[p] == '-' || text[p] == '+' || text[p] == 'e' || text[p] == 'E'))
            p++;
        var slice = text.AsSpan(start, p - start);
        return float.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
