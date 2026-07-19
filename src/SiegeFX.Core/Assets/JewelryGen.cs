using System.Globalization;
using System.Text;
using SiegeFX.Core.Actors;

namespace SiegeFX.Core.Assets;

/// <summary>SC-PCGEN — the authored jewelry-generation modifier database:
/// <c>Logic.dsres:/world/contentdb/pcontent.gas</c> <c>[modifiers]</c>, 488
/// named prefix/suffix enchant tiers (fixed alteration bundles priced by
/// their <c>power</c>). Retail "rolls" a ring by picking a tier whose power
/// fits the budget window — there are no per-point costs.</summary>
public sealed class PcontentModifierStore
{
    public sealed class Modifier
    {
        public string Key = "";           // sanitized header (stable name part)
        public string ScreenName = "";
        public bool IsPrefix;
        public float Power;
        public string ObjectTypes = "";   // csv: weapon, armor, ring, amulet, ...
        public string SpecialType = "";   // "", "rare", "unique"
        public GasNode Node = null!;      // raw block — alteration children copy verbatim
    }

    readonly List<Modifier> _mods = new();
    public int Count => _mods.Count;
    public IReadOnlyList<Modifier> All => _mods;

    public static PcontentModifierStore Load(byte[] pcontentGasBytes)
    {
        var store = new PcontentModifierStore();
        var doc = GasDocument.Load(pcontentGasBytes);
        GasNode? modsRoot = null;
        foreach (var root in doc.Roots)
            if (root.Header.Equals("modifiers", StringComparison.OrdinalIgnoreCase))
            { modsRoot = root; break; }
        if (modsRoot is null) return store;

        // First pass: raw rows keyed by header for specializes resolution.
        var byHeader = new Dictionary<string, GasNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in modsRoot.Children)
            byHeader[child.Header] = child;

        string? Inherited(GasNode node, string attr, int depth = 0)
        {
            var v = TemplateStore.FindAttr(node, attr);
            if (v is not null || depth > 4) return v;
            var sp = TemplateStore.FindAttr(node, "specializes")?.Trim();
            return sp is not null && byHeader.TryGetValue(sp, out var parent)
                ? Inherited(parent, attr, depth + 1) : null;
        }

        foreach (var child in modsRoot.Children)
        {
            var type = (Inherited(child, "type") ?? "").Trim().ToLowerInvariant();
            if (type is not ("prefix" or "suffix")) continue;
            var powerStr = Inherited(child, "power");
            if (!float.TryParse((powerStr ?? "").Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var power)) continue;
            var m = new Modifier
            {
                Key = Sanitize(child.Header),
                ScreenName = (Inherited(child, "screen_name") ?? "").Trim().Trim('"'),
                IsPrefix = type == "prefix",
                Power = power,
                ObjectTypes = (Inherited(child, "object_types") ?? "").Trim().ToLowerInvariant(),
                SpecialType = (Inherited(child, "special_type") ?? "").Trim().ToLowerInvariant(),
                Node = child,
            };
            if (m.ScreenName.Length == 0) continue;
            store._mods.Add(m);
        }
        return store;
    }

    public Modifier? ByKey(string key)
    {
        foreach (var m in _mods)
            if (m.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) return m;
        return null;
    }

    internal static string Sanitize(string header)
    {
        var sb = new StringBuilder(header.Length);
        foreach (var c in header)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}

/// <summary>SC-PCGEN — the jewelry roller: a faithful translation of
/// <c>pcontent.skrit</c>'s jewelry math over the authored
/// <c>ring_common</c>/<c>amulet_common</c> variant bands and the
/// <c>[modifiers]</c> tiers. Generated items get deterministic
/// <c>pcgen_…</c> names that fully encode the roll (class + variant +
/// modifier), so a saved reference re-materializes in any session via the
/// TemplateStore miss-synthesizer hook.</summary>
public sealed class JewelryRoller
{
    // pcontent.skrit:315-322 / 724-762 / 852-858 — jewelry constants.
    const float RingModFactor = 0.05f, AmuletModFactor = 0.10f;
    const float RingGoldK = 15f * 0.19f, AmuletGoldK = 8f * 0.19f;
    static float FuzzPct(float p) => p >= 70 ? .10f : p >= 50 ? .15f : p >= 35 ? .20f
                                   : p >= 13 ? .34f : p >= 2 ? .60f : p >= 1 ? 1.00f : 0f;

    // ring_common [pcontent] — trs_ring.gas (variant, min, max, icon).
    static readonly (string V, int Min, int Max, string Icon)[] RingVariants = {
        ("b_d_avg",1,2,"b_gui_ig_i_it_ring_003"), ("b_d_fin",2,3,"b_gui_ig_i_it_ring_004"), ("b_d_mag",3,4,"b_gui_ig_i_it_ring_005"),
        ("s_d_avg",4,5,"b_gui_ig_i_it_ring_012"), ("s_d_fin",5,6,"b_gui_ig_i_it_ring_013"), ("s_d_mag",6,7,"b_gui_ig_i_it_ring_014"),
        ("g_d_avg",7,8,"b_gui_ig_i_it_ring_021"), ("g_d_fin",8,9,"b_gui_ig_i_it_ring_022"), ("g_d_mag",9,10,"b_gui_ig_i_it_ring_023"),
        ("b_c_avg",10,11,"b_gui_ig_i_it_ring_006"),("b_c_fin",11,12,"b_gui_ig_i_it_ring_007"),("b_c_mag",12,13,"b_gui_ig_i_it_ring_008"),
        ("s_c_avg",13,14,"b_gui_ig_i_it_ring_015"),("s_c_fin",14,15,"b_gui_ig_i_it_ring_016"),("s_c_mag",15,18,"b_gui_ig_i_it_ring_017"),
        ("g_c_avg",18,20,"b_gui_ig_i_it_ring_024"),("g_c_fin",20,30,"b_gui_ig_i_it_ring_025"),("g_c_mag",30,40,"b_gui_ig_i_it_ring_026"),
        ("b_o_avg",40,50,"b_gui_ig_i_it_ring_009"),("b_o_fin",50,60,"b_gui_ig_i_it_ring_010"),("b_o_mag",60,70,"b_gui_ig_i_it_ring_011"),
        ("s_o_avg",70,80,"b_gui_ig_i_it_ring_018"),("s_o_fin",80,90,"b_gui_ig_i_it_ring_019"),("s_o_mag",90,100,"b_gui_ig_i_it_ring_020"),
        ("g_o_avg",100,120,"b_gui_ig_i_it_ring_027"),("g_o_fin",120,150,"b_gui_ig_i_it_ring_028"),("g_o_mag",150,500,"b_gui_ig_i_it_ring_029"),
    };
    // amulet_common — trs_amulet.gas (band holes 60-70/120-150: nearest-band fallback).
    static readonly (string V, int Min, int Max, string Icon)[] AmuletVariants = {
        ("b_d_avg",1,3,"b_gui_ig_i_it_amulet_003"),("b_d_fin",3,4,"b_gui_ig_i_it_amulet_004"),("b_d_mag",4,5,"b_gui_ig_i_it_amulet_005"),
        ("s_d_avg",5,6,"b_gui_ig_i_it_amulet_012"),("s_d_fin",6,7,"b_gui_ig_i_it_amulet_013"),("s_d_mag",7,8,"b_gui_ig_i_it_amulet_014"),
        ("g_d_avg",8,9,"b_gui_ig_i_it_amulet_021"),("g_d_fin",9,10,"b_gui_ig_i_it_amulet_022"),("g_d_mag",10,11,"b_gui_ig_i_it_amulet_023"),
        ("b_c_avg",11,12,"b_gui_ig_i_it_amulet_006"),("b_c_fin",12,13,"b_gui_ig_i_it_amulet_007"),("b_c_mag",13,14,"b_gui_ig_i_it_amulet_008"),
        ("s_c_avg",14,15,"b_gui_ig_i_it_amulet_015"),("s_c_fin",15,16,"b_gui_ig_i_it_amulet_016"),("s_c_mag",16,18,"b_gui_ig_i_it_amulet_017"),
        ("g_c_avg",18,20,"b_gui_ig_i_it_amulet_024"),("g_c_fin",20,30,"b_gui_ig_i_it_amulet_025"),("g_c_mag",30,40,"b_gui_ig_i_it_amulet_026"),
        ("b_o_avg",40,50,"b_gui_ig_i_it_amulet_009"),("b_o_fin",50,60,"b_gui_ig_i_it_amulet_010"),
        ("s_o_avg",70,80,"b_gui_ig_i_it_amulet_018"),("s_o_fin",80,90,"b_gui_ig_i_it_amulet_019"),("s_o_mag",90,100,"b_gui_ig_i_it_amulet_020"),
        ("g_o_avg",100,120,"b_gui_ig_i_it_amulet_027"),("g_o_mag",150,500,"b_gui_ig_i_it_amulet_029"),
    };
    // ring_ra_common / amulet_ra_common — rare + unique rolls.
    static readonly (string V, int Min, int Max, string Icon)[] RingRaVariants = {
        ("b_d_fin",1,22,"b_gui_ig_i_it_ring_030"),("s_d_fin",23,42,"b_gui_ig_i_it_ring_033"),("g_d_fin",43,61,"b_gui_ig_i_it_ring_036"),
        ("b_c_fin",62,80,"b_gui_ig_i_it_ring_031"),("s_c_fin",81,97,"b_gui_ig_i_it_ring_034"),("g_c_fin",98,114,"b_gui_ig_i_it_ring_037"),
        ("b_o_fin",114,130,"b_gui_ig_i_it_ring_032"),("s_o_fin",131,149,"b_gui_ig_i_it_ring_035"),("g_o_fin",150,500,"b_gui_ig_i_it_ring_038"),
    };
    static readonly (string V, int Min, int Max, string Icon)[] AmuletRaVariants = {
        ("b_d_fin",1,17,"b_gui_ig_i_it_amulet_030"),("s_d_fin",18,37,"b_gui_ig_i_it_amulet_033"),("g_d_fin",38,58,"b_gui_ig_i_it_amulet_036"),
        ("b_c_fin",59,79,"b_gui_ig_i_it_amulet_031"),("s_c_fin",80,100,"b_gui_ig_i_it_amulet_034"),("g_c_fin",101,124,"b_gui_ig_i_it_amulet_037"),
        ("b_o_fin",125,149,"b_gui_ig_i_it_amulet_032"),("s_o_fin",150,500,"b_gui_ig_i_it_amulet_035"),
    };

    readonly PcontentModifierStore _mods;
    readonly TemplateStore _templates;

    public JewelryRoller(PcontentModifierStore mods, TemplateStore templates)
    {
        _mods = mods;
        _templates = templates;
    }

    /// <summary>Roll a generated ring/amulet for a #ring/#amulet spec.
    /// Returns the registered synthetic template name (or null when even
    /// the plain fallback can't register).</summary>
    public string? Roll(bool isRing, PcontentResolver.Rarity rarity, int powerMin, int powerMax, Random rng, out int power)
    {
        float total = powerMin + (float)rng.NextDouble() * Math.Max(0, powerMax - powerMin);
        power = (int)MathF.Round(total);
        bool ra = rarity != PcontentResolver.Rarity.Normal;
        var variants = isRing ? (ra ? RingRaVariants : RingVariants)
                              : (ra ? AmuletRaVariants : AmuletVariants);
        // Band containing the total, else nearest.
        var variant = variants[0];
        float bestDist = float.MaxValue;
        foreach (var v in variants)
        {
            if (total >= v.Min && total <= v.Max) { variant = v; bestDist = 0f; break; }
            float d = total < v.Min ? v.Min - total : total - v.Max;
            if (d < bestDist) { bestDist = d; variant = v; }
        }

        // Modifier budget + fuzz window (rings and amulets always roll ONE
        // modifier per pcontent.skrit:178-181).
        string cls = isRing ? "ring" : "amulet";
        float modP = total * (isRing ? RingModFactor : AmuletModFactor);
        float fuzz = modP * FuzzPct(modP);
        var pool = new List<PcontentModifierStore.Modifier>();
        foreach (var m in _mods.All)
        {
            if (!m.ObjectTypes.Contains(cls, StringComparison.OrdinalIgnoreCase)) continue;
            bool special = m.SpecialType.Length > 0;
            if (ra != special) continue;
            if (ra && !m.SpecialType.Contains(
                    rarity == PcontentResolver.Rarity.Rare ? "rare" : "unique",
                    StringComparison.OrdinalIgnoreCase)) continue;
            if (m.Power < modP - fuzz || m.Power > modP + fuzz) continue;
            pool.Add(m);
        }

        string name = pool.Count == 0
            ? $"pcgen_{cls}__{variant.V}__plain"
            : $"pcgen_{cls}__{variant.V}__{pool[rng.Next(pool.Count)].Key}";
        return EnsureRegistered(name) ? name : null;
    }

    /// <summary>SC-PCGEN-WA — roll authored modifiers onto a concrete
    /// weapon/armor base (pcontent.skrit calc_modifier_count/power over
    /// the same [modifiers] tiers). Returns the wrapper template name, or
    /// null for a plain roll (the authored tables leave MOST items plain).
    /// </summary>
    public string? RollWeaponArmor(string baseName, bool isWeapon, float totalPower,
        PcontentResolver.Rarity rarity, Random rng)
    {
        // calc_modifier_count$ — authored 0/1/2 tables per type + power band.
        float chance = (float)rng.NextDouble();
        int count = 0;
        if (isWeapon)
        {
            if (totalPower is >= 20 and <= 40)       count = chance <= 0.4f ? 1 : chance <= 0.5f ? 2 : 0;
            else if (totalPower is > 40 and <= 82)   count = chance <= 0.35f ? 1 : chance <= 0.7f ? 2 : 0;
            else if (totalPower is > 83 and <= 198)  count = chance <= 0.4f ? 1 : chance <= 0.99f ? 2 : 0;
            else if (totalPower > 198)               count = chance <= 0.35f ? 1 : chance <= 0.99f ? 2 : 0;
        }
        else
        {
            if (totalPower is >= 30 and <= 73)       count = chance <= 0.3f ? 1 : chance <= 0.4f ? 2 : 0;
            else if (totalPower is > 73 and <= 147)  count = chance <= 0.45f ? 1 : chance <= 0.7f ? 2 : 0;
            else if (totalPower is > 147 and <= 310) count = chance <= 0.4f ? 1 : chance <= 0.9f ? 2 : 0;
            else if (totalPower is > 310 and <= 497) count = chance <= 0.45f ? 1 : chance <= 0.97f ? 2 : 0;
            else if (totalPower > 497)               count = chance <= 0.35f ? 1 : chance <= 0.99f ? 2 : 0;
        }
        if (count == 0) return null;

        bool ra = rarity != PcontentResolver.Rarity.Normal;
        string cls = isWeapon ? "weapon" : "armor";
        var picks = new List<PcontentModifierStore.Modifier>();
        for (int i = 0; i < count; i++)
        {
            // calc_modifier_power$ — normal rolls Rand(0.05,0.125)×total;
            // rare/unique tiers widen toward Rand(0.02,0.3), halved when
            // two modifiers share the budget.
            float lo = ra ? 0.02f : 0.05f;
            float hi = ra ? (count == 2 ? 0.15f : 0.3f) : 0.125f;
            float modP = (lo + (float)rng.NextDouble() * (hi - lo)) * totalPower;
            float fuzz = modP * FuzzPct(modP);
            var pool = new List<PcontentModifierStore.Modifier>();
            bool wantPrefix = i == 0;   // authored convention: prefix first, then suffix
            foreach (var m in _mods.All)
            {
                if (!m.ObjectTypes.Contains(cls, StringComparison.OrdinalIgnoreCase)) continue;
                bool special = m.SpecialType.Length > 0;
                if (ra != special) continue;
                if (ra && !m.SpecialType.Contains(
                        rarity == PcontentResolver.Rarity.Rare ? "rare" : "unique",
                        StringComparison.OrdinalIgnoreCase)) continue;
                if (count == 2 && m.IsPrefix != wantPrefix) continue;
                if (m.Power < modP - fuzz || m.Power > modP + fuzz) continue;
                if (picks.Contains(m)) continue;
                pool.Add(m);
            }
            if (pool.Count > 0) picks.Add(pool[rng.Next(pool.Count)]);
        }
        if (picks.Count == 0) return null;

        var nameSb = new StringBuilder($"pcgen_{(isWeapon ? "w" : "a")}__{baseName}");
        foreach (var m in picks) nameSb.Append("__").Append(m.Key);
        string name = nameSb.ToString();
        return EnsureRegistered(name) ? name : null;
    }

    /// <summary>TemplateStore miss hook — regenerate the gas for a
    /// <c>pcgen_…</c> name (saved references from an earlier session).</summary>
    public string? SynthesizeGasByName(string name)
    {
        var parts = name.Split("__", StringSplitOptions.None);
        // SC-PCGEN-WA — pcgen_w/pcgen_a wrap a concrete base template with
        // 1-2 rolled modifiers: parts = [pcgen_w, base, mod1, (mod2)].
        if (parts.Length is 3 or 4
            && (parts[0].Equals("pcgen_w", StringComparison.OrdinalIgnoreCase)
                || parts[0].Equals("pcgen_a", StringComparison.OrdinalIgnoreCase)))
            return SynthesizeWeaponArmorGas(name, parts);
        if (parts.Length != 3) return null;
        bool isRing = parts[0].Equals("pcgen_ring", StringComparison.OrdinalIgnoreCase);
        bool isAmulet = parts[0].Equals("pcgen_amulet", StringComparison.OrdinalIgnoreCase);
        if (!isRing && !isAmulet) return null;
        string variantKey = parts[1], modKey = parts[2];
        string icon = "";
        int bandMid = 1;
        foreach (var v in RingVariants.Concat(AmuletVariants).Concat(RingRaVariants).Concat(AmuletRaVariants))
            if (v.V.Equals(variantKey, StringComparison.OrdinalIgnoreCase)
                && v.Icon.Contains(isRing ? "ring" : "amulet", StringComparison.OrdinalIgnoreCase))
            { icon = v.Icon; bandMid = (v.Min + v.Max) / 2; break; }
        if (icon.Length == 0) return null;

        string baseName = isRing ? "Ring" : "Amulet";
        string root = isRing ? "ring" : "amulet";
        var sb = new StringBuilder();
        sb.AppendLine($"[t:template,n:{name}]");
        sb.AppendLine("{");
        sb.AppendLine($"\tspecializes = {root};");
        sb.AppendLine("\tcommon:is_pcontent_allowed = false;");
        sb.AppendLine($"\tgui:inventory_icon = {icon};");

        var mod = modKey.Equals("plain", StringComparison.OrdinalIgnoreCase) ? null : _mods.ByKey(modKey);
        if (mod is null)
        {
            sb.AppendLine($"\tcommon:screen_name = \"{baseName}\";");
        }
        else
        {
            string screen = mod.IsPrefix ? $"{mod.ScreenName} {baseName}" : $"{baseName} {mod.ScreenName}";
            sb.AppendLine($"\tcommon:screen_name = \"{screen}\";");
            // pcontent.skrit:852-858 — jewelry gold from the modifier power.
            float k = isRing ? RingGoldK : AmuletGoldK;
            long gold = (long)Math.Round(Math.Pow(Math.Max(1f, mod.Power) * k, 3.5));
            sb.AppendLine($"\taspect:gold_value = {Math.Max(isRing ? 50 : 100, gold)};");
            sb.AppendLine("\t[magic]");
            sb.AppendLine("\t{");
            sb.AppendLine("\t\t[enchantments]");
            sb.AppendLine("\t\t{");
            foreach (var alt in mod.Node.Children)
            {
                sb.AppendLine("\t\t\t[*]");
                sb.AppendLine("\t\t\t{");
                foreach (var a in alt.Attributes)
                    sb.AppendLine($"\t\t\t\t{a.Name} = {a.Value};");
                sb.AppendLine("\t\t\t}");
            }
            sb.AppendLine("\t\t}");
            sb.AppendLine("\t}");
        }
        _ = bandMid;
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>SC-PCGEN-WA — synthesize the wrapper gas: specializes the
    /// concrete base, composes the prefix/suffix screen name around the
    /// base's, and copies each modifier's alteration blocks verbatim.
    /// Gold stays the base's (magic sell-value premium noted as a gap).</summary>
    string? SynthesizeWeaponArmorGas(string name, string[] parts)
    {
        string baseName = parts[1];
        if (!_templates.TryGet(baseName, out var baseTpl) || baseTpl is null) return null;
        string baseScreen = (_templates.GetAttribute(baseTpl, "common", "screen_name") ?? baseName)
            .Trim().Trim('"');
        var mods = new List<PcontentModifierStore.Modifier>();
        for (int i = 2; i < parts.Length; i++)
        {
            var m = _mods.ByKey(parts[i]);
            if (m is null) return null;
            mods.Add(m);
        }
        string screen = baseScreen;
        foreach (var m in mods)
            screen = m.IsPrefix ? $"{m.ScreenName} {screen}" : $"{screen} {m.ScreenName}";

        var sb = new StringBuilder();
        sb.AppendLine($"[t:template,n:{name}]");
        sb.AppendLine("{");
        sb.AppendLine($"\tspecializes = {baseName};");
        sb.AppendLine("\tcommon:is_pcontent_allowed = false;");
        sb.AppendLine($"\tcommon:screen_name = \"{screen}\";");
        sb.AppendLine("\t[magic]");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\t[enchantments]");
        sb.AppendLine("\t\t{");
        foreach (var m in mods)
            foreach (var alt in m.Node.Children)
            {
                sb.AppendLine("\t\t\t[*]");
                sb.AppendLine("\t\t\t{");
                foreach (var a in alt.Attributes)
                    sb.AppendLine($"\t\t\t\t{a.Name} = {a.Value};");
                sb.AppendLine("\t\t\t}");
            }
        sb.AppendLine("\t\t}");
        sb.AppendLine("\t}");
        sb.AppendLine("}");
        return sb.ToString();
    }

    bool EnsureRegistered(string name)
    {
        if (_templates.TryGetRaw(name)) return true;
        var gas = SynthesizeGasByName(name);
        return gas is not null && _templates.RegisterFromGasText(gas) > 0;
    }
}
