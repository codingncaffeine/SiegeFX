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
        // SC-CARRIED-INVENTORY — EXCEPT rare/unique-tier gear (gaspy's
        // name-segment rule: cb_un_2h_horrid, bw_ra_*). Bosses author their
        // signature weapon as their equipped item — Horrid's Club, the Krug
        // Commander's Cutter — and the guide promises those drops; a 35%
        // roll on a one-time boss would eat the drop most playthroughs.
        foreach (var bucket in table.Equipped)
        {
            if (ContainsRareOrUnique(bucket) || rng.NextDouble() < EquippedDropChance)
                RollBucket(bucket, rng, results);
        }
        return results;
    }

    static bool ContainsRareOrUnique(LootBucket bucket)
    {
        foreach (var e in bucket.Entries)
            if (PcontentResolver.ClassifyRarity(e.Reference) != PcontentResolver.Rarity.Normal)
                return true;
        foreach (var c in bucket.Children)
            if (ContainsRareOrUnique(c)) return true;
        return false;
    }

    static void RollBucket(LootBucket bucket, Random rng, List<LootEntry> results)
    {
        if (!RollChance(bucket.Chance, rng)) return;
        _ = EmitFromBucket(bucket, rng, results);
    }

    static bool RollChance(float chance, Random rng) =>
        chance >= 1f || rng.NextDouble() < chance;

    /// <summary>Phase 21-SC-BARREL-D — replaces the original "pick one
    /// child uniformly" branch with a sequential per-child chance roll.
    /// DS1's <c>[oneof*]</c> with chance-gated peer buckets gives a fixed
    /// distribution across (this peer / that peer / nothing) when read as
    /// "walk peers in order, first one whose chance passes wins". The
    /// regional barrel pcontent lines (e.g. <c>barrel_glb_fh_r1</c> with
    /// gold@0.35 + potion@0.05) drop ~35% gold, ~3% potions, ~62% empty
    /// under this reading, matching the community-observed barrel
    /// distribution (60-70% empty / 20-25% gold / 5-10% potions).
    ///
    /// SC-CARRIED-INVENTORY fold — "wins" now means the peer actually
    /// EMITTED something, not merely passed its own chance line. The krug
    /// mob templates wrap their gold in a chance-1 <c>[all*]</c> whose
    /// 0.15 sits on the INNER [gold*]; under pass-wins that wrapper
    /// swallowed every roll and the sibling branch holding the authored
    /// <c>#spell/0-2</c> / bow / melee / body drops was unreachable —
    /// which is why no krug ever dropped a spell. Flat peer sets (the
    /// barrel shape) produce identical distributions either way.
    /// Returns true when at least one entry landed in
    /// <paramref name="results"/>.</summary>
    static bool EmitFromBucket(LootBucket bucket, Random rng, List<LootEntry> results)
    {
        // SC-LOOT-ALL — [all*] buckets emit EVERYTHING: every entry plus
        // every child (each child still gated by its own chance). DS1 uses
        // this shape for guaranteed boss drops and multi-item payloads.
        if (bucket.EmitAll)
        {
            bool any = bucket.Entries.Count > 0;
            foreach (var e in bucket.Entries) results.Add(e);
            foreach (var child in bucket.Children)
                if (RollChance(child.Chance, rng))
                    any |= EmitFromBucket(child, rng, results);
            return any;
        }
        if (bucket.Children.Count > 0)
        {
            foreach (var child in bucket.Children)
            {
                if (!RollChance(child.Chance, rng)) continue;
                // A peer that passed its chance but emitted nothing (a
                // wrapper whose inner rolls all failed) doesn't win —
                // keep walking so its siblings still get their shot.
                if (EmitFromBucket(child, rng, results)) return true;
            }
            return false;
        }
        if (bucket.Entries.Count > 0)
        {
            results.Add(bucket.Entries[rng.Next(bucket.Entries.Count)]);
            return true;
        }
        return false;
    }
}
