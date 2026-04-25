namespace SiegeFX.Core.Assets;

/// <summary>
/// Index of all instant-hit spells parsed out of the world template store.
/// One <see cref="SpellTemplate"/> per template under
/// <c>/world/contentdb/templates/regular/interactive/spl_spell.gas</c> whose
/// <c>[magic]</c> block has a usable <c>cast_range</c> and damage formula.
///
/// Phase 17a is read-only — load once at world start, query by name. Spell
/// books, learned-spell tracking, and slotting are higher-level concerns
/// handled by <see cref="Actors.PlayerSpellbook"/>.
/// </summary>
public sealed class SpellCatalog
{
    readonly Dictionary<string, SpellTemplate> _byName = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _byName.Count;
    public IEnumerable<SpellTemplate> All => _byName.Values;

    public bool TryGet(string name, out SpellTemplate spell)
    {
        if (_byName.TryGetValue(name, out var s)) { spell = s; return true; }
        spell = null!;
        return false;
    }

    /// <summary>Build by walking every template in <paramref name="store"/>
    /// and trying <see cref="SpellTemplate.FromTemplate"/>. Templates that
    /// don't qualify (missing magic block, non-offensive intent) are skipped
    /// silently — the store is the source of truth for "is this a template",
    /// the catalog just filters the offensive subset.</summary>
    public static SpellCatalog Build(TemplateStore store)
    {
        var cat = new SpellCatalog();
        foreach (var t in store.All)
        {
            if (!t.Name.StartsWith("spell_", StringComparison.OrdinalIgnoreCase)) continue;
            var st = SpellTemplate.FromTemplate(t, store);
            if (st is null) continue;
            cat._byName[t.Name] = st;
        }
        return cat;
    }
}
