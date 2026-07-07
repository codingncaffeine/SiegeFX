using System;
using System.Collections.Generic;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeSmith.Services;

/// <summary>A placeable object template discovered in the install's ContentDB — a name plus the
/// aspect model it resolves to (present ⇒ the engine renders it as a prop).</summary>
public sealed record PropTemplate(string Name, string Model)
{
    public string Label => Name;
}

/// <summary>Catalogue of placeable object templates from a Dungeon Siege install, the object-placement
/// analogue of <see cref="SnoCatalog"/>. Reuses the engine's <see cref="TemplateStore"/> to walk
/// <c>/world/contentdb/templates/**/*.gas</c> in each resource tank (Logic/Objects) and resolve the
/// <c>specializes</c> chain, then classifies each template: those resolving <c>[aspect]model</c> are
/// renderable props the World Builder can place. Owns the tanks it opens.</summary>
public sealed class TemplateCatalog : IDisposable
{
    private readonly List<TankFile> _tanks = new();

    /// <summary>Placeable prop templates (resolve an aspect model), sorted by name.</summary>
    public IReadOnlyList<PropTemplate> Props { get; private set; } = Array.Empty<PropTemplate>();

    public static TemplateCatalog Build(IEnumerable<string> tankPaths)
    {
        var cat = new TemplateCatalog();
        cat.Load(tankPaths);
        return cat;
    }

    private void Load(IEnumerable<string> tankPaths)
    {
        // First name wins; DS1 leaf props resolve their model within one tank, so a per-tank store
        // (which can't see a parent that lives in another tank) is accurate for the common case.
        var props = new SortedDictionary<string, PropTemplate>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in tankPaths)
        {
            TankFile file;
            try { file = TankFile.Open(path); }
            catch { continue; }
            TankReader reader;
            try { reader = new TankReader(file); }
            catch { file.Dispose(); continue; }
            _tanks.Add(file);

            TemplateStore store;
            try { (store, _) = TemplateStore.LoadFromTank(reader); }
            catch { continue; }

            foreach (var t in store.All)
            {
                if (!t.TypeTag.Equals("template", StringComparison.OrdinalIgnoreCase)) continue;
                var model = store.GetAttribute(t, "aspect", "model");
                if (string.IsNullOrEmpty(model)) continue;
                props.TryAdd(t.Name, new PropTemplate(t.Name, model!));
            }
        }

        var list = new List<PropTemplate>(props.Count);
        foreach (var p in props.Values) list.Add(p);
        Props = list;
    }

    public void Dispose()
    {
        foreach (var f in _tanks) { try { f.Dispose(); } catch { } }
        _tanks.Clear();
    }
}
