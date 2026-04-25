namespace SiegeFX.Core.Actors;

/// <summary>
/// Phase 20d — authored vendor table. DS1 ships vendor inventories on
/// the actor instance's <c>inventory</c> block (one entry per stocked
/// item plus a price multiplier on the template). Until the inventory
/// runtime grows enough to honor that, we substitute a small in-engine
/// table keyed by NPC template name. Substring match is intentional: a
/// vendor template like <c>npc_norick</c> matches the looser key
/// <c>norick</c> so authored variants don't have to be enumerated here.
/// </summary>
public static class VendorCatalog
{
    static readonly Dictionary<string, VendorDefinition> _defs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["norick"] = new VendorDefinition
            {
                NameMatch  = "norick",
                ScreenName = "Norick",
                Stock      = new[]
                {
                    new VendorStockItem
                    {
                        ItemReference = "_2hsword_iron",
                        ScreenName    = "Iron Two-Handed Sword",
                        Price         = 50,
                        Slot          = "weapon_hand",
                    },
                    new VendorStockItem
                    {
                        ItemReference = "_potion_health_minor",
                        ScreenName    = "Minor Health Potion",
                        Price         = 15,
                        Slot          = "consumable",
                    },
                },
            },
        };

    /// <summary>All catalog rows (snapshot view). Used by the vendor panel to
    /// resolve sell prices off any vendor's list, not just the open one.</summary>
    public static IEnumerable<VendorDefinition> AllDefinitions => _defs.Values;

    /// <summary>Try to look up a vendor for the given actor template name.
    /// Returns the first catalog entry whose <see cref="VendorDefinition.NameMatch"/>
    /// is a case-insensitive substring of the template name, or null when
    /// no row matches.</summary>
    public static VendorDefinition? Find(string templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName)) return null;
        foreach (var def in _defs.Values)
        {
            if (templateName.IndexOf(def.NameMatch, StringComparison.OrdinalIgnoreCase) >= 0)
                return def;
        }
        return null;
    }
}

/// <summary>One vendor row. <see cref="NameMatch"/> is the substring tested
/// against actor template names; <see cref="ScreenName"/> is the title shown
/// in the trade panel.</summary>
public sealed class VendorDefinition
{
    public string NameMatch  { get; init; } = "";
    public string ScreenName { get; init; } = "";
    public IReadOnlyList<VendorStockItem> Stock { get; init; } = Array.Empty<VendorStockItem>();
}

/// <summary>One item the vendor offers for sale. <see cref="ItemReference"/>
/// matches the same template-ref convention loot piles use, so a successful
/// purchase appends a <c>LootEntry(Slot, ItemReference)</c> to the player's
/// inventory and the existing equip path picks it up unchanged.</summary>
public sealed class VendorStockItem
{
    public string ItemReference { get; init; } = "";
    public string ScreenName    { get; init; } = "";
    public long   Price         { get; init; }
    public string Slot          { get; init; } = "";
}
