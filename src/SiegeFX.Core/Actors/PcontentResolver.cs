using System.Globalization;
using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Actors;

/// <summary>Phase 9-SC-7 / SC-16 — pcontent roller. DS1 authors equipped
/// weapons and drops as specs like <c>#club/2-3</c> ("a club, item-power
/// 2-3") or <c>#weapon/-rare(1)/200-314</c> ("a rare weapon, power
/// 200-314"). Phase B-1 honors the literal class bucket and the power
/// range so low-tier krug drops can't roll into <c>cb_un_2h_troll_rock</c>.
/// Wildcard / cross-category specs (<c>#weapon</c>, <c>#*</c>,
/// <c>#armor</c>, <c>#nmagic</c>, <c>#cmagic</c>) and rarity modifiers
/// (<c>-rare(N)</c>, <c>-unique(N)</c>) are still Phase B-2 work.</summary>
public sealed class PcontentResolver
{
    public readonly record struct Entry(string Name, int Power);

    private readonly TemplateStore _store;
    private readonly Dictionary<string, List<Entry>> _byClass;
    private bool _indexed;

    public PcontentResolver(TemplateStore store)
    {
        _store = store;
        _byClass = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>True iff <paramref name="spec"/> is a pcontent spec
    /// (starts with <c>#</c>) — callers can short-circuit before
    /// attempting a direct template lookup.</summary>
    public static bool IsSpec(string? spec) =>
        !string.IsNullOrEmpty(spec) && spec[0] == '#';

    /// <summary>Resolves a pcontent spec to a concrete template name.
    /// Returns false for non-spec strings, unknown classes, and empty
    /// class buckets. The chosen template's power is reported through
    /// <paramref name="chosenPower"/> for diagnostic logging.</summary>
    public bool TryResolve(string spec, Random rng, out string templateName, out int chosenPower)
    {
        templateName = "";
        chosenPower = 0;
        if (!IsSpec(spec)) return false;

        EnsureIndex();

        var parsed = ParseSpec(spec);
        if (string.IsNullOrEmpty(parsed.Class)) return false;
        if (!_byClass.TryGetValue(parsed.Class, out var bucket) || bucket.Count == 0)
            return false;

        // Tier filter. When the spec carries a power range, restrict to
        // entries inside [min,max]. If the filter rejects every entry,
        // fall back to the whole bucket — a krug must always drop
        // *something*; a mistuned weapon beats a phantom drop.
        List<Entry> filtered;
        if (parsed.HasPower)
        {
            filtered = bucket.FindAll(e => e.Power >= parsed.PowerMin && e.Power <= parsed.PowerMax);
            if (filtered.Count == 0) filtered = bucket;
        }
        else
        {
            filtered = bucket;
        }

        var picked = filtered[rng.Next(filtered.Count)];
        templateName = picked.Name;
        chosenPower = picked.Power;
        return true;
    }

    /// <summary>Backwards-compatible overload — power is discarded.</summary>
    public bool TryResolve(string spec, Random rng, out string templateName) =>
        TryResolve(spec, rng, out templateName, out _);

    /// <summary>Read-only snapshot of every indexed class bucket. The
    /// diagnostic CLI uses this to dump tier spreads and verify that
    /// real specs (<c>#club/2-3</c>) no longer roll uniques.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Entry>> ByClass()
    {
        EnsureIndex();
        var view = new Dictionary<string, IReadOnlyList<Entry>>(_byClass.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in _byClass)
            view[k] = v.AsReadOnly();
        return view;
    }

    public readonly record struct Spec(string Class, string Sub, bool HasPower, int PowerMin, int PowerMax);

    /// <summary>Parses a pcontent spec into its class, optional
    /// sub-class, and power range. Modifier segments that begin with
    /// <c>-</c> or <c>+</c> are skipped (Phase B-2 will read them); the
    /// power segment is the last bare <c>N</c> or <c>N-M</c>
    /// segment.</summary>
    public static Spec ParseSpec(string spec)
    {
        if (string.IsNullOrEmpty(spec) || spec[0] != '#') return new Spec("", "", false, 0, 0);
        var body = spec[1..];
        var segments = body.Split('/');
        if (segments.Length == 0 || segments[0].Length == 0) return new Spec("", "", false, 0, 0);

        // Class segment: "weapon,r" -> ("weapon", "r")
        string cls, sub;
        var head = segments[0];
        var commaIx = head.IndexOf(',');
        if (commaIx >= 0) { cls = head[..commaIx]; sub = head[(commaIx + 1)..]; }
        else              { cls = head;            sub = ""; }

        bool hasPower = false;
        int min = 0, max = 0;
        for (int i = 1; i < segments.Length; i++)
        {
            var s = segments[i];
            if (s.Length == 0) continue;
            if (s[0] == '-' || s[0] == '+') continue; // modifier — Phase B-2
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
        return new Spec(cls, sub, hasPower, min, max);
    }

    private void EnsureIndex()
    {
        if (_indexed) return;
        _indexed = true;
        foreach (var tpl in _store.All)
        {
            var ac = _store.GetAttribute(tpl, "attack", "attack_class");
            if (string.IsNullOrEmpty(ac)) continue;
            if (string.IsNullOrEmpty(_store.GetAttribute(tpl, "aspect", "model"))) continue;

            // Tier signal: DS1 marks unique-tier templates with
            // [common] is_pcontent_allowed = false so they never roll
            // out of #class/lo-hi specs — uniques are placed by hand.
            var allowed = _store.GetAttribute(tpl, "common", "is_pcontent_allowed");
            if (allowed is not null && string.Equals(allowed, "false", StringComparison.OrdinalIgnoreCase))
                continue;

            // Power = avg(damage_min, damage_max). DS1's PContent.cpp
            // uses a per-template item_power attribute, but the average
            // damage is a well-correlated proxy in shipped content
            // (branch ~1, club ~3, end-game ~200+).
            var dmgMin = ParseFloat(_store.GetAttribute(tpl, "attack", "damage_min"));
            var dmgMax = ParseFloat(_store.GetAttribute(tpl, "attack", "damage_max"));
            var power = (int)Math.Round((dmgMin + dmgMax) / 2.0);

            var key = ac.StartsWith("ac_", StringComparison.OrdinalIgnoreCase)
                ? ac[3..]
                : ac;
            if (!_byClass.TryGetValue(key, out var list))
            {
                list = new List<Entry>();
                _byClass[key] = list;
            }
            list.Add(new Entry(tpl.Name, power));
        }
        foreach (var list in _byClass.Values)
            list.Sort((a, b) => a.Power.CompareTo(b.Power));
    }

    private static double ParseFloat(string? s) =>
        s is not null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : 0.0;
}
