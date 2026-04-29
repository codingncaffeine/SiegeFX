namespace SiegeFX.Core.Actors;

/// <summary>Stateless loot generator. Walks a <see cref="LootTable"/> and produces
/// a concrete drop list for one kill. Deterministic if <paramref name="rng"/> is;
/// for reproducibility seed from the target actor's scid so a given kill always
/// drops the same thing in CLI sims.
///
/// Interpretation of DS1 semantics:
/// <list type="bullet">
///   <item>Every top-level bucket rolls independently once per kill — a goblin
///         grunt has an "equipped weapon" bucket AND a "rare drop" outer bucket,
///         and both can fire on the same kill.</item>
///   <item>Inside a bucket, <c>chance</c> gates whether the bucket produces
///         anything. A bucket with no <c>chance</c> line is always-on.</item>
///   <item>A leaf bucket (<see cref="LootBucket.Entries"/> populated, no children)
///         picks one entry uniformly at random.</item>
///   <item>A branch bucket (<see cref="LootBucket.Children"/> populated) picks
///         one child uniformly and recurses — this models the nested "drop-rarity"
///         layering DS1 uses (common → rare → unique buckets sit as peers under
///         an outer wrapper).</item>
/// </list>
/// The "pick one uniformly" branch is an educated guess — DS1's exact arbitration
/// for a mixed peer-bucket set (all with different chances) isn't public. A single
/// uniform pick-one keeps total-drop counts roughly in line with the shipped data
/// (a goblin grunt drops ~1 item per kill once fired) and is easy to swap for a
/// weighted variant later.</summary>
public static class LootRoller
{
    /// <summary>Phase 21-SC-LOOT-FIX — fraction of kills on which an enemy's
    /// worn (Equipped) gear lands on the corpse. Skipping Equipped entirely
    /// (the Phase 12-SC-3 fix to stop 100%-drop weapons) overshot: most DS1
    /// mob templates author their worn weapon in es_weapon_hand and ship no
    /// separate il_main drop, so kills produced zero loot. Tunable knob;
    /// pick something low enough that a goblin grunt isn't a guaranteed
    /// weapon vending machine but high enough that mid-fight gear upgrades
    /// actually happen. 0.35 lines up with the "decent but not constant"
    /// drop cadence DS1 shipped.</summary>
    const double EquippedDropChance = 0.35;

    public static List<LootEntry> Roll(LootTable table, Random rng)
    {
        var results = new List<LootEntry>();
        // Drops buckets — `il_main = #pattern/range` style, chance-gated by
        // their own oneof*. Always roll these.
        foreach (var bucket in table.Drops) RollBucket(bucket, rng, results);
        // Equipped buckets — `es_weapon_hand = X` etc. Worn gear; gate at
        // the bucket level with EquippedDropChance so they drop sometimes.
        foreach (var bucket in table.Equipped)
        {
            if (rng.NextDouble() < EquippedDropChance)
                RollBucket(bucket, rng, results);
        }
        return results;
    }

    static void RollBucket(LootBucket bucket, Random rng, List<LootEntry> results)
    {
        if (bucket.Chance < 1f && rng.NextDouble() >= bucket.Chance) return;

        if (bucket.Children.Count > 0)
        {
            var child = bucket.Children[rng.Next(bucket.Children.Count)];
            RollBucket(child, rng, results);
            return;
        }

        if (bucket.Entries.Count > 0)
        {
            results.Add(bucket.Entries[rng.Next(bucket.Entries.Count)]);
        }
    }
}
