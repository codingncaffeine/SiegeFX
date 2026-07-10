using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Actors;

/// <summary>One equipped-slot or drop-item entry pulled from a template's
/// inventory.pcontent block. DS1 writes equipped items as <c>es_weapon_hand = X</c>
/// / <c>es_shield_hand = Y</c> and drops as <c>il_main = #pattern/range</c>;
/// both land here as the same record, distinguished by <see cref="Slot"/>
/// (empty = drop, non-empty = equipped). The Phase 21-SC-BARREL-D wiring
/// adds a sentinel <c>"gold"</c> slot for <c>[gold*]</c> buckets — Reference
/// holds <c>"min-max"</c> so the roller can pick a count at draw time
/// without re-parsing the template.</summary>
public readonly record struct LootEntry(string Slot, string Reference)
{
    public bool IsEquipped =>
        Slot.Length > 0 && !Slot.Equals("gold", StringComparison.OrdinalIgnoreCase);
    public bool IsGold => Slot.Equals("gold", StringComparison.OrdinalIgnoreCase);

    /// <summary>For <see cref="IsGold"/> entries, parse <see cref="Reference"/>
    /// (formatted "min-max") into the inclusive range. Returns (0,0) on a
    /// malformed value so callers don't have to defensively re-validate.</summary>
    public (int Min, int Max) GoldRange()
    {
        if (!IsGold) return (0, 0);
        var dash = Reference.IndexOf('-');
        if (dash < 0) return (0, 0);
        if (!int.TryParse(Reference.AsSpan(0, dash), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var lo)) return (0, 0);
        if (!int.TryParse(Reference.AsSpan(dash + 1), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var hi)) return (0, 0);
        if (hi < lo) (lo, hi) = (hi, lo);
        return (lo, hi);
    }
}

/// <summary>A <c>[oneof*]</c> bucket inside a template's pcontent tree. The star
/// in <c>oneof*</c> means the bucket may produce nothing; <see cref="Chance"/>
/// is the probability the bucket fires this roll. Leaf buckets (no children)
/// produce one of <see cref="Entries"/>; branch buckets pick one child bucket
/// uniformly at random and recurse. DS1's shipped templates mix both shapes.</summary>
public sealed class LootBucket
{
    /// <summary>Probability the bucket produces anything this roll, in [0,1].
    /// A value of 1 means guaranteed (DS1 writes no <c>chance</c> line for
    /// always-on buckets — typically the equipped-weapon slot).</summary>
    public float Chance { get; }

    /// <summary>Item references that this leaf bucket chooses between. Empty
    /// for branch buckets.</summary>
    public IReadOnlyList<LootEntry> Entries { get; }

    /// <summary>Nested buckets; one is chosen uniformly per roll. Empty for
    /// leaf buckets.</summary>
    public IReadOnlyList<LootBucket> Children { get; }

    public LootBucket(float chance, IReadOnlyList<LootEntry> entries, IReadOnlyList<LootBucket> children)
    {
        Chance = chance;
        Entries = entries;
        Children = children;
    }
}

/// <summary>The complete loot structure for a template: the top-level pcontent
/// block decomposed into its <c>[oneof*]</c> children. Equipped-item buckets
/// (those whose entries are all <c>es_*</c> lines) are kept separate from
/// drop buckets so callers can show what the actor is wearing vs. what it
/// drops on death.</summary>
public sealed class LootTable
{
    public IReadOnlyList<LootBucket> Equipped { get; }
    public IReadOnlyList<LootBucket> Drops { get; }

    public bool IsEmpty => Equipped.Count == 0 && Drops.Count == 0;

    public LootTable(IReadOnlyList<LootBucket> equipped, IReadOnlyList<LootBucket> drops)
    {
        Equipped = equipped;
        Drops = drops;
    }

    /// <summary>Walks the template's specializes chain to the first ancestor that
    /// declares an <c>[inventory][pcontent]</c> block, then builds a structured
    /// <see cref="LootTable"/>. Returns an empty table if no ancestor declares
    /// pcontent — many templates (chickens, props, specializes-only stubs) have
    /// no drops at all, which is expected.</summary>
    public static LootTable FromTemplate(TemplateStore store, Template template) =>
        FromTemplate(store, template, instance: null);

    /// <summary>SC-INSTANCE-OVERRIDES — overload that ALSO folds in the
    /// placement's own <c>[inventory][pcontent]</c> buckets (a chest or mob
    /// authored to carry a specific item on top of whatever its template
    /// rolls). Instance buckets are additive: template buckets still roll.</summary>
    public static LootTable FromTemplate(TemplateStore store, Template template, GasNode? instance)
    {
        var equipped = new List<LootBucket>();
        var drops = new List<LootBucket>();

        void Collect(GasNode pcontent)
        {
            foreach (var bucket in pcontent.Children)
            {
                // Phase 21-SC-BARREL-FOLD — krug.gas + heroes.gas put [gold*]
                // directly under [pcontent] (no enclosing [oneof*]). Without
                // accepting Gold here those gold drops were silently dropped
                // on the floor. [all*] is still pre-existing-unsupported (see
                // splinter SC-LOOT-ALL); accepting it would over-emit since
                // ParseBucket reads it as a oneof — separate slice.
                if (!IsOneof(bucket.Header) && !IsGold(bucket.Header)) continue;
                var parsed = ParseBucket(bucket);
                if (parsed is null) continue;
                if (IsEquippedBucket(parsed)) equipped.Add(parsed);
                else drops.Add(parsed);
            }
        }

        var pcontent = store.GetSection(template, "inventory", "pcontent");
        if (pcontent is not null) Collect(pcontent);

        if (instance is not null
            && TemplateStore.FindChild(instance, "inventory") is { } instInv
            && TemplateStore.FindChild(instInv, "pcontent") is { } instPc)
            Collect(instPc);

        return new LootTable(equipped, drops);
    }

    static bool IsOneof(string header) =>
        header.Equals("oneof", StringComparison.OrdinalIgnoreCase) ||
        header.Equals("oneof*", StringComparison.OrdinalIgnoreCase);

    static bool IsGold(string header) =>
        header.Equals("gold", StringComparison.OrdinalIgnoreCase) ||
        header.Equals("gold*", StringComparison.OrdinalIgnoreCase);

    static LootBucket? ParseBucket(GasNode node)
    {
        float chance = 1f;
        var chanceStr = TemplateStore.FindAttr(node, "chance");
        if (chanceStr is not null &&
            float.TryParse(chanceStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var c))
        {
            chance = Math.Clamp(c, 0f, 1f);
        }

        // Phase 21-SC-BARREL-D — [gold*] / [gold] leaves carry a min/max
        // range (e.g. min=2 max=8 for fh_r1 barrels). Parse into a single
        // synthetic entry so RollBucket can return it as the "drop" without
        // needing a separate gold-aware code path.
        if (IsGold(node.Header))
        {
            int min = 0, max = 0;
            var minStr = TemplateStore.FindAttr(node, "min");
            var maxStr = TemplateStore.FindAttr(node, "max");
            if (minStr is not null) int.TryParse(minStr, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out min);
            if (maxStr is not null) int.TryParse(maxStr, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out max);
            if (max < min) (min, max) = (max, min);
            if (max <= 0) return null;
            return new LootBucket(chance,
                new[] { new LootEntry("gold", $"{min}-{max}") },
                Array.Empty<LootBucket>());
        }

        var entries = new List<LootEntry>();
        foreach (var attr in node.Attributes)
        {
            if (attr.Name.Equals("chance", StringComparison.OrdinalIgnoreCase)) continue;
            if (attr.Name.StartsWith("es_", StringComparison.OrdinalIgnoreCase))
                entries.Add(new LootEntry(attr.Name[3..], attr.Value));
            else if (attr.Name.Equals("il_main", StringComparison.OrdinalIgnoreCase))
                entries.Add(new LootEntry("", attr.Value));
        }

        var children = new List<LootBucket>();
        foreach (var child in node.Children)
        {
            if (!IsOneof(child.Header) && !IsGold(child.Header)) continue;
            var parsed = ParseBucket(child);
            if (parsed is not null) children.Add(parsed);
        }

        if (entries.Count == 0 && children.Count == 0) return null;
        return new LootBucket(chance, entries, children);
    }

    static bool IsEquippedBucket(LootBucket bucket)
    {
        if (bucket.Entries.Count == 0 && bucket.Children.Count == 0) return false;
        foreach (var e in bucket.Entries) if (!e.IsEquipped) return false;
        foreach (var c in bucket.Children) if (!IsEquippedBucket(c)) return false;
        return bucket.Entries.Count > 0 || bucket.Children.Count > 0;
    }
}
