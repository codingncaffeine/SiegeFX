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
    }

    public readonly record struct Entry(string Name, int Power, Rarity Rarity, Group Group, bool PcontentAllowed);

    private readonly TemplateStore _store;
    private readonly Dictionary<string, List<Entry>> _byClass;
    private readonly List<Entry> _all;
    private bool _indexed;

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

        // Build the candidate bucket. Literal classes hit _byClass
        // directly; wildcards fold across the indexed entries.
        IEnumerable<Entry> candidates = parsed.Class.ToLowerInvariant() switch
        {
            "weapon" => _all.Where(e => (e.Group & (Group.Melee | Group.Ranged)) != 0),
            "melee"  => _all.Where(e => (e.Group & Group.Melee) != 0),
            "armor"  => _all.Where(e => (e.Group & Group.Armor) != 0),
            "*"      => _all,
            _        => _byClass.TryGetValue(parsed.Class, out var b) ? b : Enumerable.Empty<Entry>(),
        };

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
            // is_pcontent_allowed gates normal-tier rolls. We still
            // index the entry so a rarity-modifier spec
            // (#*/-unique(2)/...) can pick it; bare specs filter it
            // back out at resolve time.
            var allowedAttr = _store.GetAttribute(tpl, "common", "is_pcontent_allowed");
            bool allowed = allowedAttr is null
                || !string.Equals(allowedAttr, "false", StringComparison.OrdinalIgnoreCase);

            var rarity = ClassifyRarity(tpl.Name);

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
            }
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
