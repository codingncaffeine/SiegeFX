using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Actors;

/// <summary>Phase 25a — DS1 shop authoring, per SU 208a + the shipped
/// 1W_shopkeep templates (canonical example: blacksmith_moik_stourn).
/// Stock lives in <c>[inventory][store_pcontent]</c> under tab blocks
/// ([armor]/[weapons]/[shields]/[magic]/[misc]) holding <c>[all*]</c>
/// bags (every entry rolls, count in min..max) and <c>[oneof*]</c> bags
/// (chance-gated single pick). Prices come from <c>[store]</c>'s
/// <c>item_markup</c> over the item's gold value; <c>can_sell_self</c>
/// marks a hireable rather than a shop.</summary>
public sealed class StoreTable
{
    public sealed record Bag(IReadOnlyList<string> Specs, int Min, int Max, float Chance, bool OneOf);
    public sealed record Tab(string Name, IReadOnlyList<Bag> Bags);

    public IReadOnlyList<Tab> Tabs { get; }
    /// <summary>[store] item_markup — buy price multiplier over the
    /// item's gold value (moik authors 2). Default 1.</summary>
    public float ItemMarkup { get; }
    /// <summary>[store] can_sell_self — hireable NPC, not a shop.</summary>
    public bool CanSellSelf { get; }
    public float FullRatio { get; }

    public bool IsShop => Tabs.Count > 0 && !CanSellSelf;

    StoreTable(IReadOnlyList<Tab> tabs, float markup, bool canSellSelf, float fullRatio)
    {
        Tabs = tabs;
        ItemMarkup = markup;
        CanSellSelf = canSellSelf;
        FullRatio = fullRatio;
    }

    /// <summary>Null when the template chain has neither a [store] block
    /// nor store_pcontent — i.e. not a merchant or hireable at all.</summary>
    public static StoreTable? FromTemplate(TemplateStore store, Template template)
    {
        var storeBlock = store.GetSection(template, "store");
        var pcontent   = store.GetSection(template, "inventory", "store_pcontent");
        if (storeBlock is null && pcontent is null) return null;

        float markup = 1f;
        var markupStr = store.GetAttribute(template, "store", "item_markup");
        if (!string.IsNullOrEmpty(markupStr)
            && float.TryParse(markupStr, System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out var m))
            markup = m;
        bool sellsSelf = string.Equals(
            store.GetAttribute(template, "store", "can_sell_self")?.Trim(),
            "true", StringComparison.OrdinalIgnoreCase);

        float fullRatio = 0f;
        var tabs = new List<Tab>();
        if (pcontent is not null)
        {
            var frStr = TemplateStore.FindAttr(pcontent, "full_ratio");
            if (!string.IsNullOrEmpty(frStr))
                float.TryParse(frStr, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out fullRatio);

            foreach (var tabNode in pcontent.Children)
            {
                var bags = new List<Bag>();
                foreach (var bagNode in tabNode.Children)
                {
                    bool oneOf = bagNode.Header.StartsWith("oneof", StringComparison.OrdinalIgnoreCase);
                    bool all   = bagNode.Header.StartsWith("all", StringComparison.OrdinalIgnoreCase)
                                 || bagNode.Header == "*";
                    if (!oneOf && !all) continue;

                    var specs = new List<string>();
                    int min = 1, max = 1;
                    float chance = 1f;
                    foreach (var attr in bagNode.Attributes)
                    {
                        var name = attr.Name.TrimEnd('*');
                        if (name.StartsWith("il_", StringComparison.OrdinalIgnoreCase))
                            specs.Add(attr.Value.Trim().Trim('"', ';'));
                        else if (name.Equals("min", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(attr.Value.Trim(), out min);
                        else if (name.Equals("max", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(attr.Value.Trim(), out max);
                        else if (name.Equals("chance", StringComparison.OrdinalIgnoreCase))
                            float.TryParse(attr.Value.Trim(), System.Globalization.NumberStyles.Float,
                                           System.Globalization.CultureInfo.InvariantCulture, out chance);
                    }
                    if (specs.Count > 0)
                        bags.Add(new Bag(specs, Math.Min(min, max), Math.Max(min, max), chance, oneOf));
                }
                if (bags.Count > 0)
                    tabs.Add(new Tab(tabNode.Header.ToLowerInvariant(), bags));
            }
        }
        return new StoreTable(tabs, markup, sellsSelf, fullRatio);
    }

    public readonly record struct StockItem(string Tab, string TemplateName, int Power, string Spec);

    /// <summary>Roll the shop's stock: every [all*] bag contributes each
    /// of its specs at a count rolled in min..max; [oneof*] bags pass a
    /// chance gate then contribute ONE spec. Duplicate template rolls
    /// collapse into a single row (DS1 shelves show one of each).</summary>
    public List<StockItem> GenerateStock(PcontentResolver resolver, Random rng)
    {
        var outList = new List<StockItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tab in Tabs)
        {
            foreach (var bag in tab.Bags)
            {
                IEnumerable<string> chosen;
                if (bag.OneOf)
                {
                    if (rng.NextDouble() > bag.Chance) continue;
                    chosen = new[] { bag.Specs[rng.Next(bag.Specs.Count)] };
                }
                else chosen = bag.Specs;

                foreach (var spec in chosen)
                {
                    int count = rng.Next(bag.Min, bag.Max + 1);
                    for (int i = 0; i < count; i++)
                    {
                        string name;
                        int power = 0;
                        if (PcontentResolver.IsSpec(spec))
                        {
                            if (!resolver.TryResolve(spec, rng, out name, out power)) continue;
                        }
                        else name = spec; // literal template stock

                        if (seen.Add(name))
                            outList.Add(new StockItem(tab.Name, name, power, spec));
                    }
                }
            }
        }
        return outList;
    }
}
