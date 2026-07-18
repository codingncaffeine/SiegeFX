using System.Globalization;
using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Actors;

/// <summary>Phase 9-SC-7 / SC-16 — pcontent roller. DS1 authors equipped
/// weapons and drops as specs like <c>#club/2-3</c> ("a club, item-power
/// 2-3") or <c>#weapon/-rare(1)/200-314</c> ("a rare weapon, power
/// 200-314"). Phase B-1 honors the literal class bucket and the power
/// range so low-tier krug drops can't roll into <c>cb_un_2h_troll_rock</c>;
/// Phase B-2 adds wildcard classes (<c>#weapon</c>, <c>#armor</c>,
/// <c>#melee</c>, <c>#*</c>) and rarity modifiers (<c>-rare(N)</c>,
/// <c>-unique(N)</c>). Spell-only specs (<c>#cmagic</c>, <c>#nmagic</c>,
/// <c>#spell</c>) and stance sub-classes (<c>#body,f</c>) remain
/// unimplemented — bookshelf and shop drops still fall through to the
/// raw template lookup.</summary>
public sealed class PcontentResolver
{
    public enum Rarity { Normal, Rare, Unique }

    [Flags]
    public enum Group
    {
        None  = 0,
        Melee = 1 << 0,
        Ranged = 1 << 1,
        Armor = 1 << 2,
        // Phase 24a — spells (combat + nature schools). Bucketed under
        // the DS1 spec class names "cmagic" / "nmagic"; "#spell" folds
        // both.
        Spell = 1 << 3,
    }

    public readonly record struct Entry(string Name, int Power, Rarity Rarity, Group Group, bool PcontentAllowed);

    private readonly TemplateStore _store;
    private readonly Dictionary<string, List<Entry>> _byClass;
    private readonly List<Entry> _all;
    private bool _indexed;
    // SC-PCGEN — the jewelry roller (authored generation tables); null =
    // generic root-item fallback.
    private SiegeFX.Core.Assets.JewelryRoller? _jewelry;

    public void AttachJewelry(SiegeFX.Core.Assets.JewelryRoller roller) => _jewelry = roller;

    public PcontentResolver(TemplateStore store)
    {
        _store = store;
        _byClass = new(StringComparer.OrdinalIgnoreCase);
        _all = new();
    }

    /// <summary>True iff <paramref name="spec"/> is a pcontent spec
    /// (starts with <c>#</c>) — callers can short-circuit before
    /// attempting a direct template lookup.</summary>
    public static bool IsSpec(string? spec) =>
        !string.IsNullOrEmpty(spec) && spec[0] == '#';

    /// <summary>Resolves a pcontent spec to a concrete template name.
    /// Returns false for non-spec strings, unknown classes, and empty
    /// candidate sets. The chosen template's power is reported through
    /// <paramref name="chosenPower"/> for diagnostic logging.</summary>
    public bool TryResolve(string spec, Random rng, out string templateName, out int chosenPower)
    {
        templateName = "";
        chosenPower = 0;
        if (!IsSpec(spec)) return false;

        EnsureIndex();

        var parsed = ParseSpec(spec);
        if (string.IsNullOrEmpty(parsed.Class)) return false;

        // SC-PCGEN — #ring/#amulet run the authored GENERATION tables
        // (variant bands + modifier tiers) instead of a catalog pick;
        // the roller registers a synthetic template and returns its name.
        if (_jewelry is not null
            && (parsed.Class.Equals("ring", StringComparison.OrdinalIgnoreCase)
                || parsed.Class.Equals("amulet", StringComparison.OrdinalIgnoreCase)))
        {
            int pMin = parsed.HasPower ? parsed.PowerMin : 1;
            int pMax = parsed.HasPower ? Math.Max(parsed.PowerMin, parsed.PowerMax) : 20;
            var rolled = _jewelry.Roll(
                parsed.Class.Equals("ring", StringComparison.OrdinalIgnoreCase),
                parsed.Rarity, pMin, pMax, rng, out var rolledPower);
            if (rolled is not null)
            {
                templateName = rolled;
                chosenPower = rolledPower;
                return true;
            }
        }

        // Build the candidate bucket. Literal classes hit _byClass
        // directly; wildcards fold across the indexed entries.
        IEnumerable<Entry> candidates = parsed.Class.ToLowerInvariant() switch
        {
            "weapon" => _all.Where(e => (e.Group & (Group.Melee | Group.Ranged)) != 0),
            "melee"  => _all.Where(e => (e.Group & Group.Melee) != 0),
            "armor"  => _all.Where(e => (e.Group & Group.Armor) != 0),
            // Phase 24a — "#spell" folds both schools; "#cmagic"/"#nmagic"
            // hit their literal buckets below.
            "spell"  => _all.Where(e => (e.Group & Group.Spell) != 0),
            "*"      => _all,
            _        => _byClass.TryGetValue(parsed.Class, out var b) ? b : Enumerable.Empty<Entry>(),
        };

        // Phase 25-fold A — never resolve an ABSTRACT template. DS1
        // reserves the "base_" prefix for abstract parents (base_glove,
        // base_helm, base_body_armor_cloth, …); they carry a [gui]
        // equip_slot so the armor path indexed them into the sub-buckets,
        // and with defense 0 / no screen_name / no gold_value they slipped
        // through the Normal+allowed filter and showed up as literal
        // "base_glove" junk on every blacksmith's shelf. Real items never
        // use the prefix, so this is a safe, comprehensive gate (covers
        // shop stock and loot alike).
        candidates = candidates.Where(e =>
            !e.Name.StartsWith("base_", StringComparison.OrdinalIgnoreCase));

        // Phase 25a — sub-class filter: "#body,ro" narrows body armor to
        // robes (chain rooted at base_body_armor_cloth). SC-PCONTENT-STANCE
        // — the single-letter subs are STANCE classes per gaspy's
        // parse_template_name (f=Fighter, r=Ranger, m=Mage tokens in the
        // underscore-split name; e.g. bd_ba_f_g_c_avg is fighter cut).
        // The old silent no-op made "#body,f/6-8" roll mage robes onto
        // fighters. Unknown subs keep the no-op behavior.
        if (parsed.Sub.Equals("ro", StringComparison.OrdinalIgnoreCase))
            candidates = candidates.Where(e =>
                _store.TryGet(e.Name, out var t) && t is not null
                && IsDescendantOf(t, "base_body_armor_cloth"));
        else if (parsed.Sub.Length == 1
                 && (parsed.Sub is "f" or "r" or "m" || parsed.Sub is "F" or "R" or "M"))
        {
            var tok = parsed.Sub.ToLowerInvariant();
            candidates = candidates.Where(e =>
            {
                var parts = e.Name.Split('_');
                foreach (var p in parts)
                    if (string.Equals(p, tok, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            });
        }

        // Rarity filter. Without a modifier, only normal-tier items
        // that aren't is_pcontent_allowed=false can roll. With
        // -rare(N) or -unique(N), narrow to that tier — gameplay
        // specs like #*/-unique(2)/175-286 explicitly want named
        // unique drops a normal roll would never pick.
        candidates = parsed.Rarity switch
        {
            Rarity.Rare   => candidates.Where(e => e.Rarity == Rarity.Rare),
            Rarity.Unique => candidates.Where(e => e.Rarity == Rarity.Unique),
            _             => candidates.Where(e => e.Rarity == Rarity.Normal && e.PcontentAllowed),
        };

        // Tier filter. Whole-bucket fallback when the power filter
        // empties — a krug must always drop *something*.
        var pool = candidates.ToList();
        if (pool.Count == 0) return false;
        if (parsed.HasPower)
        {
            var inRange = pool.FindAll(e => e.Power >= parsed.PowerMin && e.Power <= parsed.PowerMax);
            if (inRange.Count > 0) pool = inRange;
        }

        var picked = pool[rng.Next(pool.Count)];
        templateName = picked.Name;
        chosenPower = picked.Power;
        return true;
    }

    /// <summary>Backwards-compatible overload — power is discarded.</summary>
    public bool TryResolve(string spec, Random rng, out string templateName) =>
        TryResolve(spec, rng, out templateName, out _);

    /// <summary>Read-only snapshot of every indexed literal class
    /// bucket — diagnostic CLI uses this to dump tier spreads.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Entry>> ByClass()
    {
        EnsureIndex();
        var view = new Dictionary<string, IReadOnlyList<Entry>>(_byClass.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in _byClass)
            view[k] = v.AsReadOnly();
        return view;
    }

    /// <summary>Read-only flat view of every indexed entry, used by
    /// the CLI to enumerate wildcard buckets the same way the resolver
    /// does at run-time.</summary>
    public IReadOnlyList<Entry> AllEntries()
    {
        EnsureIndex();
        return _all.AsReadOnly();
    }

    public readonly record struct Spec(string Class, string Sub, bool HasPower, int PowerMin, int PowerMax, Rarity Rarity);

    /// <summary>Parses a pcontent spec into class, optional sub-class,
    /// rarity modifier, and power range. Modifier segments
    /// <c>-rare(N)</c> and <c>-unique(N)</c> set the rarity bucket;
    /// other <c>-</c>/<c>+</c> segments are skipped (they're cosmetic
    /// hints that DS1's random roller used to bias odds).</summary>
    public static Spec ParseSpec(string spec)
    {
        if (string.IsNullOrEmpty(spec) || spec[0] != '#') return new Spec("", "", false, 0, 0, Rarity.Normal);
        var body = spec[1..];
        var segments = body.Split('/');
        if (segments.Length == 0 || segments[0].Length == 0) return new Spec("", "", false, 0, 0, Rarity.Normal);

        // Class segment: "weapon,r" -> ("weapon", "r")
        string cls, sub;
        var head = segments[0];
        var commaIx = head.IndexOf(',');
        if (commaIx >= 0) { cls = head[..commaIx]; sub = head[(commaIx + 1)..]; }
        else              { cls = head;            sub = ""; }

        bool hasPower = false;
        int min = 0, max = 0;
        var rarity = Rarity.Normal;
        for (int i = 1; i < segments.Length; i++)
        {
            var s = segments[i];
            if (s.Length == 0) continue;
            if (s[0] == '-' || s[0] == '+')
            {
                // Rarity modifier: read the lowercase prefix up to '('
                // and match against the known tags.
                var paren = s.IndexOf('(');
                var tag = paren > 1 ? s[1..paren] : s[1..];
                if (string.Equals(tag, "rare", StringComparison.OrdinalIgnoreCase))     rarity = Rarity.Rare;
                else if (string.Equals(tag, "unique", StringComparison.OrdinalIgnoreCase)) rarity = Rarity.Unique;
                continue;
            }
            // Power range "N-M" or single "N"
            var dashIx = s.IndexOf('-');
            if (dashIx > 0)
            {
                if (int.TryParse(s[..dashIx], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lo)
                 && int.TryParse(s[(dashIx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hi))
                { min = lo; max = hi; hasPower = true; }
            }
            else if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lo))
            {
                min = lo; max = lo; hasPower = true;
            }
        }
        return new Spec(cls, sub, hasPower, min, max, rarity);
    }

    private void EnsureIndex()
    {
        if (_indexed) return;
        _indexed = true;
        foreach (var tpl in _store.All)
        {
            // SC-PCONTENT-ACTORS — actors are never items. Every monster
            // inherits [attack]attack_class=ac_beastfu from the root
            // actor template, which indexed the whole bestiary into the
            // wildcard pool: a container's #* roll could hand the player
            // walking_corpse_boss_01 ("Ancient Corpse") as loot.
            if (IsDescendantOf(tpl, "actor")) continue;

            // is_pcontent_allowed gates normal-tier rolls. We still
            // index the entry so a rarity-modifier spec
            // (#*/-unique(2)/...) can pick it; bare specs filter it
            // back out at resolve time.
            var allowedAttr = _store.GetAttribute(tpl, "common", "is_pcontent_allowed");
            bool allowed = allowedAttr is null
                || !string.Equals(allowedAttr, "false", StringComparison.OrdinalIgnoreCase);

            var rarity = ClassifyRarity(tpl.Name);

            // Phase 24a/25c — spell path (checked first: cheap name gate).
            // Player-school spells index with power = [magic]
            // required_level — that's the scale shop specs author:
            // Adwana's regular-tier `#spell/1-7` stocks flash
            // (required_level 3), and death_blast (24) is correctly
            // excluded. max_level is the SKILL window, a different axis
            // (zap authors max_level 21 yet is the starter spell).
            //
            // Phase 25-fold B — floor at 1. Starter spells (zap,
            // fireshot, basic heal) author NO required_level → 0, and a
            // band floor of 1 (#spell/1-7) filtered them out entirely, so
            // the two most iconic first spells were unbuyable anywhere. A
            // spell usable from the start IS a tier-1 spell; flooring puts
            // them in the low band while keeping every higher spell in its
            // own tier (death_blast stays 24, out of 1-7 and in of 12-81).
            // Monster-arsenal spells (chain rooted at base_spell_monster)
            // never index.
            if (tpl.Name.StartsWith("spell_", StringComparison.OrdinalIgnoreCase))
            {
                char school = ClassifySpellSchool(tpl);
                if (school != '\0')
                {
                    var reqLevel = Math.Max(1, (int)ParseFloat(_store.GetAttribute(tpl, "magic", "required_level")));
                    var entry = new Entry(tpl.Name, reqLevel, rarity, Group.Spell, allowed);
                    Bucket(school == 'c' ? "cmagic" : "nmagic").Add(entry);
                    _all.Add(entry);
                }
                continue;
            }

            // Weapon path: [attack][attack_class] is set, power = avg
            // of damage_min/damage_max. Weapons must have a hand-grip
            // mesh — without [aspect][model] there's no item to draw.
            var ac = _store.GetAttribute(tpl, "attack", "attack_class");
            if (!string.IsNullOrEmpty(ac))
            {
                if (string.IsNullOrEmpty(_store.GetAttribute(tpl, "aspect", "model"))) continue;
                var dmgMin = ParseFloat(_store.GetAttribute(tpl, "attack", "damage_min"));
                var dmgMax = ParseFloat(_store.GetAttribute(tpl, "attack", "damage_max"));
                var power = (int)Math.Round((dmgMin + dmgMax) / 2.0);
                var key = ac.StartsWith("ac_", StringComparison.OrdinalIgnoreCase) ? ac[3..] : ac;
                var grp = ClassifyWeaponGroup(key);
                var entry = new Entry(tpl.Name, power, rarity, grp, allowed);
                Bucket(key).Add(entry);
                _all.Add(entry);
                continue;
            }

            // Armor path: walks specializes chain to find templates
            // rooted at the shipped <c>armor</c> base; power =
            // [defend][defense] (every shipped armor template lives
            // in a [defend] block — see amr_shield.gas, amr_helm.gas).
            // Armor doesn't need [aspect][model] — DS1 helmets, boots,
            // gloves, and body armor are texture overlays applied to
            // the actor's existing mesh, not standalone props. Only
            // shields and a handful of trinkets have free-standing
            // meshes; the resolver hands a template name to the
            // equipment system either way.
            if (IsDescendantOf(tpl, "armor"))
            {
                var defense = ParseFloat(_store.GetAttribute(tpl, "defend", "defense"));
                var power = (int)Math.Round(defense);
                var entry = new Entry(tpl.Name, power, rarity, Group.Armor, allowed);
                _all.Add(entry);
                // Phase 25a — armor sub-class buckets from the chain's
                // [gui]equip_slot (shop specs address #body/#helm/#boots/
                // #gloves/#shield directly — blacksmith_moik_stourn).
                var slot = _store.GetAttribute(tpl, "gui", "equip_slot")?.Trim();
                string? key = slot switch
                {
                    "es_chest"       => "body",
                    "es_head"        => "helm",
                    "es_feet"        => "boots",
                    "es_hands"       => "gloves",
                    "es_forearms"    => "gloves",
                    "es_shield_hand" => "shield",
                    _                => null,
                };
                if (key is not null) Bucket(key).Add(entry);
            }
        }
        // SC-PCONTENT-JEWELRY — DS1 GENERATES jewelry: #ring/#amulet specs
        // roll a variant + enchant modifiers from the ring_common/
        // amulet_common [pcontent] tables. That modifier roller isn't
        // modeled, and no plain-item path indexes the class (the roots
        // specialize inventory, not armor), so #ring/... used to resolve
        // to NOTHING and the raw spec string leaked into loot piles as a
        // no-icon "#RING/" item. Until the roller lands, the specs
        // resolve to the generic root items — real, equippable rings and
        // amulets with authored icon/slot/value. Group.None keeps them
        // out of #weapon/#armor wildcards; power 1 + the whole-bucket
        // fallback accepts any authored band.
        foreach (var rootName in new[] { "ring", "amulet" })
        {
            if (!_store.TryGet(rootName, out var rootTpl) || rootTpl is null) continue;
            var entry = new Entry(rootName, 1, Rarity.Normal, Group.None, true);
            Bucket(rootName).Add(entry);
            _all.Add(entry);
        }
        foreach (var list in _byClass.Values)
            list.Sort((a, b) => a.Power.CompareTo(b.Power));
        _all.Sort((a, b) => a.Power.CompareTo(b.Power));
    }

    private List<Entry> Bucket(string key)
    {
        if (!_byClass.TryGetValue(key, out var list))
        {
            list = new List<Entry>();
            _byClass[key] = list;
        }
        return list;
    }

    /// <summary>Phase 24a — 'c' for combat-school, 'n' for nature-school,
    /// '\0' for monster/unknown chains (matches SpellTemplate's
    /// chain-derived SpellClass).</summary>
    private static char ClassifySpellSchool(Template template)
    {
        for (var t = template; t is not null; t = t.Specializes)
        {
            switch (t.Name.ToLowerInvariant())
            {
                case "base_spell_dark":
                case "base_scroll_dark":
                case "base_summon_dark":
                    return 'c';
                case "base_spell_good":
                case "base_scroll_good":
                case "base_summon_good":
                    return 'n';
                case "base_spell_monster":
                    return '\0';
            }
        }
        return '\0';
    }

    private bool IsDescendantOf(Template template, string ancestorName)
    {
        for (var t = template; t is not null; t = t.Specializes)
        {
            if (string.Equals(t.Name, ancestorName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>DS1 names items with a rarity tag right after the
    /// type prefix: <c>cb_g_*</c> generic, <c>cb_un_*</c> unique,
    /// <c>bw_ra_*</c> rare. Gaspy's <c>parse_template_name</c> walks
    /// the same path; this reproduces just the rarity slice we need
    /// for tier filtering.</summary>
    public static Rarity ClassifyRarity(string templateName)
    {
        if (string.IsNullOrEmpty(templateName)) return Rarity.Normal;
        var parts = templateName.Split('_');
        // Skip world-level (2w/3w) and dsx prefixes if present.
        int i = 0;
        if (i < parts.Length && (parts[i].Equals("2w", StringComparison.OrdinalIgnoreCase) || parts[i].Equals("3w", StringComparison.OrdinalIgnoreCase))) i++;
        if (i < parts.Length && parts[i].Equals("dsx", StringComparison.OrdinalIgnoreCase)) i++;
        // Skip the type prefix (cb/sd/bw/he/sh/...). Gaspy lists the
        // canonical set; we just skip the first remaining segment if
        // it's not yet a rarity token.
        if (i < parts.Length && !IsRarityTag(parts[i])) i++;
        // Some helmets have two-segment type prefixes (he_fu, he_ca);
        // skip the secondary type token before rarity.
        if (i < parts.Length && !IsRarityTag(parts[i]) && parts[i].Length <= 3) i++;
        if (i < parts.Length)
        {
            if (parts[i].Equals("ra", StringComparison.OrdinalIgnoreCase)) return Rarity.Rare;
            if (parts[i].Equals("un", StringComparison.OrdinalIgnoreCase)) return Rarity.Unique;
        }
        return Rarity.Normal;
    }

    private static bool IsRarityTag(string s) =>
        s.Equals("ra", StringComparison.OrdinalIgnoreCase) || s.Equals("un", StringComparison.OrdinalIgnoreCase);

    private static Group ClassifyWeaponGroup(string attackClass)
    {
        // DS1 ships nine weapon attack_classes plus beastfu (monster
        // intrinsic attacks, not player loot). Bows and miniguns are
        // ranged; the rest are melee. combat_magic is on caster
        // staves used by enemy mages, also melee-shaped from a roll
        // standpoint. beastfu is excluded from #weapon entirely.
        return attackClass.ToLowerInvariant() switch
        {
            "bow" or "minigun" => Group.Ranged,
            "beastfu"          => Group.None,
            _                  => Group.Melee,
        };
    }

    private static double ParseFloat(string? s) =>
        s is not null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : 0.0;
}
