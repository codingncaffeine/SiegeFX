using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>Indexes DS1 templates by name so actor spawn can resolve archetype refs.
/// Templates are scattered across a forest of .gas files under
/// <c>/world/contentdb/templates/</c>; this class walks that tree, parses each file,
/// catalogues every <c>[t:template,n:NAME]</c> node, and resolves each template's
/// <c>specializes</c> pointer once the full set is known so lookups can walk the chain
/// without additional IO.</summary>
public sealed class TemplateStore
{
    readonly Dictionary<string, Template> _byName = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _byName.Count;
    public IEnumerable<Template> All => _byName.Values;

    public bool TryGet(string name, out Template template)
    {
        if (_byName.TryGetValue(name, out var t)) { template = t; return true; }
        template = null!;
        return false;
    }

    /// <summary>Loads every template under <paramref name="rootPath"/> inside the given
    /// tank. Default root matches DS1's shipped layout. Returns the count of diagnostics
    /// encountered (malformed files, unresolvable parents) — nonzero is expected against
    /// shipped data; callers should surface them but not abort.</summary>
    public static (TemplateStore Store, IReadOnlyList<string> Diagnostics) LoadFromTank(
        TankReader tank, string rootPath = "/world/contentdb/templates")
    {
        var store = new TemplateStore();
        var diags = new List<string>();
        var rootNorm = rootPath.TrimEnd('/') + "/";

        foreach (var path in tank.ListFiles())
        {
            if (!path.StartsWith(rootNorm, StringComparison.OrdinalIgnoreCase)) continue;
            if (!path.EndsWith(".gas", StringComparison.OrdinalIgnoreCase)) continue;

            byte[] bytes;
            try { bytes = tank.ExtractToMemory(path); }
            catch (Exception ex) { diags.Add($"{path}: extract failed: {ex.Message}"); continue; }

            GasDocument doc;
            try { doc = GasDocument.Load(bytes); }
            catch (Exception ex) { diags.Add($"{path}: parse failed: {ex.Message}"); continue; }

            foreach (var node in doc.Roots)
            {
                if (!TryParseHeader(node.Header, out var typeTag, out var name)) continue;
                // A template store catalogues everything that looks like a template; actor
                // spawn filters on typeTag=="template" while other callers (categories, inst
                // archetypes) may want siblings. Name collisions across files are a DS1 asset
                // bug — record them as diagnostics and keep the first seen.
                if (store._byName.ContainsKey(name))
                {
                    diags.Add($"{path}: duplicate template name '{name}' (first seen at '{store._byName[name].SourcePath}')");
                    continue;
                }
                var specializes = FindAttr(node, "specializes");
                store._byName[name] = new Template(name, typeTag, specializes, node, path);
            }
        }

        // Wire specializes links. Missing parents are informational: DS1 sometimes references
        // templates that live in different tanks (Objects.dsres vs Logic.dsres) and the caller
        // can layer stores.
        foreach (var t in store._byName.Values)
        {
            if (t.SpecializesName is null) continue;
            if (store._byName.TryGetValue(t.SpecializesName, out var parent))
                t.Specializes = parent;
            else
                diags.Add($"{t.Name}: parent '{t.SpecializesName}' not found in store");
        }

        return (store, diags);
    }

    /// <summary>Parses <c>t:template,n:NAME</c>-shaped headers into their type tag and
    /// name. Returns false for any header that doesn't carry both <c>t:</c> and <c>n:</c>
    /// so non-template roots (e.g. naked section blocks if any slipped in) are ignored.</summary>
    public static bool TryParseHeader(string header, out string typeTag, out string name)
    {
        typeTag = ""; name = "";
        string? t = null, n = null;
        foreach (var raw in header.Split(','))
        {
            var pair = raw.Trim();
            var colon = pair.IndexOf(':');
            if (colon <= 0) continue;
            var key = pair[..colon].Trim();
            var val = pair[(colon + 1)..].Trim();
            if (string.Equals(key, "t", StringComparison.OrdinalIgnoreCase)) t = val;
            else if (string.Equals(key, "n", StringComparison.OrdinalIgnoreCase)) n = val;
        }
        if (t is null || n is null) return false;
        typeTag = t; name = n;
        return true;
    }

    /// <summary>Finds the first attribute named <paramref name="attrName"/> on
    /// <paramref name="node"/>. Case-insensitive. Does not descend into children.</summary>
    public static string? FindAttr(GasNode node, string attrName)
    {
        for (int i = 0; i < node.Attributes.Count; i++)
            if (string.Equals(node.Attributes[i].Name, attrName, StringComparison.OrdinalIgnoreCase))
                return node.Attributes[i].Value;
        return null;
    }

    /// <summary>Finds the first child block on <paramref name="node"/> whose header
    /// matches <paramref name="blockName"/> (case-insensitive). Template section headers
    /// are bare names like <c>aspect</c>, <c>body</c>, <c>mind</c> — no <c>t:</c>/<c>n:</c>
    /// pairs.</summary>
    public static GasNode? FindChild(GasNode node, string blockName)
    {
        for (int i = 0; i < node.Children.Count; i++)
            if (string.Equals(node.Children[i].Header, blockName, StringComparison.OrdinalIgnoreCase))
                return node.Children[i];
        return null;
    }

    /// <summary>Walks the specializes chain looking up an attribute by a dotted path.
    /// For <c>GetAttribute(t, "aspect", "model")</c>: descend <c>aspect</c> in t's node,
    /// then read <c>model</c>; if absent, repeat on t.Specializes. Returns null if no
    /// ancestor defines the field.
    ///
    /// DS1 also accepts a colon-shorthand for nested attributes — <c>aspect:model = X;</c>
    /// at the template root is equivalent to <c>[aspect] { model = X; }</c>. Foliage
    /// templates (cornstalk_grs_*, planter_*, fire_charred_template lookups, etc.) use
    /// the shorthand exclusively, so we check the flat form on each chain link before
    /// descending — otherwise an inherited <c>[aspect]</c> block on a base template
    /// (e.g. <c>base_burnable</c>'s life-only aspect) would shadow the leaf's real
    /// model and the prop would silently fail to render.</summary>
    public string? GetAttribute(Template template, params string[] path)
    {
        if (path.Length == 0) return null;
        var flatName = path.Length >= 2 ? string.Join(':', path) : null;
        for (var t = template; t is not null; t = t.Specializes)
        {
            if (flatName is not null)
            {
                var flat = FindAttr(t.Node, flatName);
                if (flat is not null) return flat;
            }
            var node = t.Node;
            for (int i = 0; i < path.Length - 1; i++)
            {
                var next = FindChild(node, path[i]);
                if (next is null) { node = null!; break; }
                node = next;
            }
            if (node is null) continue;
            var v = FindAttr(node, path[^1]);
            if (v is not null) return v;
        }
        return null;
    }

    /// <summary>Walks the specializes chain looking up a section by dotted path. Returns
    /// the first match along the chain. Use for grabbing whole subtrees like
    /// <c>body.chore_dictionary</c> that callers want to iterate child-by-child.</summary>
    public GasNode? GetSection(Template template, params string[] path)
    {
        if (path.Length == 0) return template.Node;
        for (var t = template; t is not null; t = t.Specializes)
        {
            var node = t.Node;
            for (int i = 0; i < path.Length; i++)
            {
                var next = FindChild(node, path[i]);
                if (next is null) { node = null!; break; }
                node = next;
            }
            if (node is not null) return node;
        }
        return null;
    }
}
