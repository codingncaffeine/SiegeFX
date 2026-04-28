using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Actors;

/// <summary>Phase 9-SC-7 / SC-16 (Phase A) — minimum-viable pcontent
/// roller. DS1 authors equipped weapons and drops as specs like
/// <c>#club/2-3</c> ("a club, item-power 2-3") or
/// <c>#weapon/-rare(1)/200-314</c> ("a rare weapon, power 200-314").
/// Authentic resolution honors the power range and rarity modifier;
/// this class ignores both and picks any template whose
/// <c>[attack][attack_class]</c> matches <c>ac_&lt;spec_class&gt;</c>.
/// Wildcard / cross-category specs (<c>#weapon</c>, <c>#*</c>,
/// <c>#armor</c>, <c>#nmagic</c>, <c>#cmagic</c>) currently return
/// false — SC-16 will widen the resolver to honor tiers and broaden
/// the bucket coverage.</summary>
public sealed class PcontentResolver
{
    private readonly TemplateStore _store;
    private readonly Dictionary<string, List<string>> _byClass;
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

    /// <summary>Tries to resolve a pcontent spec to a concrete template
    /// name. Returns false for non-spec strings, unknown classes, and
    /// empty class buckets. The returned name is suitable for
    /// <see cref="TemplateStore.TryGet"/>.</summary>
    public bool TryResolve(string spec, Random rng, out string templateName)
    {
        templateName = "";
        if (!IsSpec(spec)) return false;

        EnsureIndex();

        // Class is the segment after '#' up to the first '/' or '-'
        // (rarity modifier marker like "-rare(1)").
        var afterHash = spec.AsSpan(1);
        var end = afterHash.Length;
        for (var i = 0; i < afterHash.Length; i++)
        {
            if (afterHash[i] == '/' || afterHash[i] == '-')
            {
                end = i;
                break;
            }
        }
        if (end == 0) return false;
        var cls = afterHash[..end].ToString();

        if (!_byClass.TryGetValue(cls, out var bucket) || bucket.Count == 0)
            return false;
        templateName = bucket[rng.Next(bucket.Count)];
        return true;
    }

    private void EnsureIndex()
    {
        if (_indexed) return;
        _indexed = true;
        foreach (var tpl in _store.All)
        {
            var ac = _store.GetAttribute(tpl, "attack", "attack_class");
            if (string.IsNullOrEmpty(ac)) continue;
            // Skip specializes-only stubs that have no renderable model;
            // they'd resolve but TryGetItemMesh would fall through anyway.
            if (string.IsNullOrEmpty(_store.GetAttribute(tpl, "aspect", "model"))) continue;

            // ac_club -> club, ac_sword -> sword, etc.
            var key = ac.StartsWith("ac_", StringComparison.OrdinalIgnoreCase)
                ? ac[3..]
                : ac;
            if (!_byClass.TryGetValue(key, out var list))
            {
                list = new List<string>();
                _byClass[key] = list;
            }
            list.Add(tpl.Name);
        }
    }
}
