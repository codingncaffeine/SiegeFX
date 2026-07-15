using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Actors;

/// <summary>
/// Player-character XP + level state. Awarded each time the PC's swing connects
/// (per-damage XP) plus the victim's <see cref="ActorStats.ExperienceValue"/> on
/// the killing blow. Crossing an XP threshold from <see cref="FormulasStore.XpTable"/>
/// triggers a level-up: STR/DEX/INT auto-grow proportional to the skill kind that
/// earned the level (<see cref="FormulasStore.ProportionalGains"/>), and MaxLife /
/// MaxMana are recomputed via the player formula and republished onto the actor.
///
/// Per-skill bookkeeping is deferred — Phase 16d tracks one running pool and one
/// running level, attributing all gains to the skill kind specified at award time.
/// When Phase 17+ adds the four-skill spread (Melee / Ranged / Nature / Combat
/// magic) that DS1 ships, this class becomes a holder of four parallel pools and
/// the level-up math runs per-pool. The shape stays the same.
///
/// Auto-grow uses fractional attributes — STR moves by 0.64 per Melee level, not
/// a rounded 1, matching DS1's internal-float / display-rounded model. Over many
/// levels the float drift is what makes a Melee character actually scale into a
/// pure tank. Display code rounds for readout.
/// </summary>
public sealed class PlayerProgression
{
    readonly Actor _player;
    readonly FormulasStore _formulas;

    /// <summary>Cumulative XP earned this character. Single pool in 16d.</summary>
    public long TotalXp { get; private set; }

    /// <summary>Current level (1-based). Walks up in lockstep with <see cref="TotalXp"/>
    /// crossing each <see cref="FormulasStore.XpTable"/> threshold; never decreases.</summary>
    public int Level { get; private set; } = 1;

    /// <summary>True for one query after each level-up; consume via <see cref="ConsumeJustLeveledUp"/>.
    /// Lets the HUD flash a "Level Up!" toast or play a chime exactly once.</summary>
    public bool JustLeveledUp { get; private set; }

    /// <summary>Phase 20b — quest journal. Owned here because progression is the
    /// player-lifetime state bag and the journal is per-PC (it rides through the
    /// save with the rest of the player snapshot).</summary>
    public QuestJournal Journal { get; } = new();

    /// <summary>Phase 20d — gold purse. Drops from kills credit here, vendor
    /// transactions debit/credit here. Long because DS1's late-game prices run
    /// six digits and we don't want to shave cap headroom for no reason.</summary>
    public long Gold { get; private set; }

    /// <summary>Returns true if the debit succeeded (i.e. funds were available).
    /// Failed debits leave the purse untouched so the caller can show a
    /// "not enough gold" toast without rolling back state.</summary>
    public bool TryDebitGold(long amount)
    {
        if (amount <= 0) return true;
        if (Gold < amount) return false;
        Gold -= amount;
        return true;
    }

    /// <summary>Add gold to the purse. Negative or zero amounts are no-ops so
    /// kill-drop callsites don't have to guard their own zero rolls.</summary>
    public void CreditGold(long amount)
    {
        if (amount <= 0) return;
        Gold += amount;
    }

    /// <summary>Phase 20d — set the player gold from a save snapshot, bypassing
    /// the debit/credit guards (those are for in-session txns, not load).</summary>
    public void RestoreGoldFromSave(long gold) => Gold = Math.Max(0, gold);

    public long XpForCurrentLevel => _formulas.XpForLevel(Level);
    public long XpForNextLevel    => _formulas.XpForLevel(Level + 1);
    public long XpIntoCurrentLevel => TotalXp - XpForCurrentLevel;
    public long XpToNextLevel      => XpForNextLevel - TotalXp;

    // Phase 21-SC-INV-A2 (round 6) — per-skill XP pools. The four skill kinds
    // each accumulate independently from <see cref="AwardXp"/>'s tagged amount;
    // the per-skill level walks the same XP table as the global level. Only
    // the global pool drives stat auto-grow; the per-skill pool exists so the
    // HUD's ability cells can show "progress toward next M/R/Q/W rank" — the
    // single most-asked-for player feedback the always-on bar can carry.
    private readonly long[] _skillXp = new long[4];
    public long SkillXp(SkillKind k) => _skillXp[(int)k];
    /// <summary>Per-skill XP pools in <see cref="SkillKind"/> order, for the
    /// save snapshot. Round-trips via <see cref="RestoreFromSave"/> so attribute
    /// growth resumes from the right per-skill levels on load.</summary>
    public long[] SkillXpSnapshot() => (long[])_skillXp.Clone();
    public int  SkillLevel(SkillKind k) => _formulas.LevelForXp(_skillXp[(int)k]);
    public long SkillXpForCurrentLevel(SkillKind k) => _formulas.XpForLevel(SkillLevel(k));
    public long SkillXpForNextLevel(SkillKind k)    => _formulas.XpForLevel(SkillLevel(k) + 1);
    public long SkillXpIntoCurrentLevel(SkillKind k) => _skillXp[(int)k] - SkillXpForCurrentLevel(k);
    public float SkillProgressFraction(SkillKind k)
    {
        long span = SkillXpForNextLevel(k) - SkillXpForCurrentLevel(k);
        if (span <= 0) return 0f;
        long into = _skillXp[(int)k] - SkillXpForCurrentLevel(k);
        if (into <= 0) return 0f;
        if (into >= span) return 1f;
        return (float)into / span;
    }

    // SC-ATTR-XP — DS1's authored attribute growth (formulas.gas): STR/DEX/
    // INT are themselves skills ([skill*] Strength/Dexterity/Intelligence,
    // max_level 180) fed by REDISTRIBUTED experience — every award to a
    // combat skill also adds award × that skill's str/dex/int_influence to
    // the attribute's own XP pool, and the attribute's value advances along
    // the SAME experience table. Because the table is convex this is NOT the
    // same as "+influence per skill level": attributes hold still for the
    // first few skill levels then climb ~1 per level, tracking the skill at
    // a roughly constant offset (a pure fighter's STR runs ~2 levels behind
    // Melee — at Melee 50 DS1 STR ≈ 58, where the old linear model gave 42).
    // Pools are pure linear combinations of the per-skill XP pools and START
    // AT ZERO — the attribute VALUE is authored base + (pool level − 1).
    // Calibration check against retail outcomes: a pure archer at Ranged 10
    // has DEX ≈ 10 + (LevelForXp(0.62·xp(10)) − 1) ≈ 17-18, matching DS1
    // character tables. (The first cut seeded the pool at XpForLevel(base),
    // which on the convex table priced DEX 10→11 at Ranged ~10 — attributes
    // never visibly moved in the early game: "my int hasn't gone up once".)
    // Saves need no new fields — RestoreFromSave re-derives from skill pools.
    readonly int[] _attrLevelApplied = new int[3];  // str/dex/int pool levels

    long AttrXpPool(int attr)
    {
        double pool = 0;
        for (int s = 0; s < _skillXp.Length; s++)
        {
            var g = _formulas.ProportionalGains((SkillKind)s);
            float w = attr == 0 ? g.Str : attr == 1 ? g.Dex : g.Int;
            pool += _skillXp[s] * (double)w;
        }
        return (long)pool;
    }

    /// <summary>True when the most recent <see cref="AwardXp"/> moved any
    /// attribute — the render layer re-syncs the worn-gear enchant layer on
    /// this edge (attribute crossings don't always coincide with skill
    /// crossings under the redistribution model).</summary>
    public bool AttributesChangedLastAward { get; private set; }

    /// <summary>SC-ATTR-XP — which attributes crossed a level on the most
    /// recent award, and to what value: (0=Strength, 1=Dexterity,
    /// 2=Intelligence, newLevel). DS1 announces attribute advances on the
    /// message strip just like skills; the render layer reads this edge.</summary>
    public IReadOnlyList<(int Attr, int NewLevel)> LastAttrLevelUps => _lastAttrLevelUps;
    readonly List<(int Attr, int NewLevel)> _lastAttrLevelUps = new();

    /// <summary>SC-ATTR-XP — progress toward the NEXT attribute level (0..1),
    /// attr 0=STR 1=DEX 2=INT. The character sheet's attribute bars read this
    /// (they used to mirror the SKILL fractions, so a bar could reset without
    /// the attribute number moving).</summary>
    public float AttrProgressFraction(int attr)
    {
        long pool = AttrXpPool(attr);
        int lvl = _formulas.LevelForXp(pool);
        long floor = _formulas.XpForLevel(lvl);
        long next = _formulas.XpForLevel(lvl + 1);
        if (next <= floor) return 0f;
        return Math.Clamp((float)(pool - floor) / (next - floor), 0f, 1f);
    }

    // SC-ATTR-XP — the hero's AUTHORED base attributes, captured at spawn
    // (before any award or enchant). The attribute's natural value is the
    // deterministic function base + (pool level − 1); the restore path
    // reconciles saved stats against it.
    readonly float[] _attrBase = new float[3];

    public PlayerProgression(Actor player, FormulasStore formulas)
    {
        _player = player;
        _formulas = formulas;
        var st = player.Stats;
        _attrBase[0] = st.Strength;
        _attrBase[1] = st.Dexterity;
        _attrBase[2] = st.Intelligence;
        // Pools start at zero: applied level = LevelForXp(0) = 1 per attribute
        // (the authored base carries the rest of the value).
        for (int a = 0; a < 3; a++)
            _attrLevelApplied[a] = _formulas.LevelForXp(0);
    }

    // Fractional XP carry — the authentic per-damage model produces non-integer
    // awards (damage × XP/MaxLife); the fraction accumulates here so nothing is
    // lost to rounding across many small hits.
    double _xpCarry;

    /// <summary>DS1's authentic per-damage XP award: a hit that removes
    /// <paramref name="lifeRemoved"/> HP from a victim worth
    /// <paramref name="victimXpValue"/> XP grants
    /// <c>lifeRemoved × (victimXpValue / victimMaxLife)</c> — killing a monster
    /// yields exactly its authored <c>experience_value</c> in total across
    /// however many hits it took, with NO separate kill bonus. Each award is
    /// then capped by <c>[experience_limiting_factors]</c> (10% of the next
    /// level's XP delta at level 1, 2.5% afterwards) so one huge hit on a
    /// high-value monster can't vault multiple levels. Fractions carry.</summary>
    public bool AwardDamageXp(float lifeRemoved, float victimXpValue, float victimMaxLife, SkillKind skill)
    {
        if (lifeRemoved <= 0f || victimXpValue <= 0f) return false;
        double xp = lifeRemoved * (double)victimXpValue / Math.Max(1f, victimMaxLife);

        double factor = Level <= 1 ? _formulas.XpFirstLevelFactor : _formulas.XpLaterLevelsFactor;
        double levelSpan = _formulas.XpForLevel(Level + 1) - _formulas.XpForLevel(Level);
        if (levelSpan > 0) xp = Math.Min(xp, factor * levelSpan);

        xp += _xpCarry;
        long whole = (long)xp;
        _xpCarry = xp - whole;
        return AwardXp(whole, skill);
    }

    /// <summary>Add XP and apply level-ups. <paramref name="amount"/> is the raw
    /// number from the combat resolver — typically the damage dealt, plus the
    /// dying actor's <see cref="ActorStats.ExperienceValue"/> on the killing
    /// blow. <paramref name="skill"/> selects which proportional-gains row the
    /// auto-grow uses; melee swings always use <see cref="SkillKind.Melee"/>
    /// for now, with spell paths choosing Nature/Combat when those land.</summary>
    public bool AwardXp(long amount, SkillKind skill)
    {
        if (amount <= 0) return false;
        TotalXp += amount;

        // DS1 grows STR/DEX/INT off SKILL level-ups, not the aggregate character
        // level: each skill's [str/dex/int]_influence share applies per level of
        // THAT skill. Detect this skill's level crossing and apply its gains
        // immediately, so the character sheet reflects the change the instant
        // the skill ticks up — instead of waiting for total XP to cross a
        // character-level threshold (which used to credit whichever skill
        // happened to land the killing XP, and left skill level-ups that didn't
        // coincide with a character level-up granting nothing at all).
        int idx = (int)skill;
        int oldSkillLevel = _formulas.LevelForXp(_skillXp[idx]);
        _skillXp[idx] += amount;
        int newSkillLevel = _formulas.LevelForXp(_skillXp[idx]);

        // Character level is the total-XP aggregate: a display value plus the
        // milestone toast. It grants no attributes on its own — that's the
        // per-skill path below — matching DS1's model.
        int newCharLevel = _formulas.LevelForXp(TotalXp);
        bool charLeveled = newCharLevel > Level;
        Level = newCharLevel;

        // SC-ATTR-XP — advance the attribute pools along the experience
        // table (DS1's redistribution model, see the field comment). Each
        // attribute crossing adds WHOLE levels to the live stat — applied as
        // a DELTA so any worn-gear enchant layer riding on the stats
        // survives — and life/mana caps recompute when anything moved.
        // Attribute crossings are independent of skill crossings.
        AttributesChangedLastAward = false;
        _lastAttrLevelUps.Clear();
        var stats = _player.Stats;
        float newStr = stats.Strength, newDex = stats.Dexterity, newInt = stats.Intelligence;
        for (int a = 0; a < 3; a++)
        {
            int lvl = _formulas.LevelForXp(AttrXpPool(a));
            int dAttr = lvl - _attrLevelApplied[a];
            if (dAttr == 0) continue;
            _attrLevelApplied[a] = lvl;
            AttributesChangedLastAward = true;
            if (dAttr > 0) _lastAttrLevelUps.Add((a, lvl));
            if (a == 0) newStr += dAttr;
            else if (a == 1) newDex += dAttr;
            else newInt += dAttr;
        }
        if (AttributesChangedLastAward)
        {
            var newStats = stats with
            {
                Strength = newStr, Dexterity = newDex, Intelligence = newInt,
                MaxLife = _formulas.MaxLife(newStr, newDex, newInt),
                MaxMana = _formulas.MaxMana(newStr, newDex, newInt),
            };
            _player.ResyncStats(newStats);
            // SC-ATTR-XP diag — prove the applied value sticks on the live
            // actor (chasing "bar moves, number doesn't"). The actor hash
            // pairs with the [sheet] diag: mismatched hashes = the panel
            // reads a DIFFERENT Actor object than progression writes.
            Console.WriteLine(
                $"[attr] crossing applied: str={newStr:F1} dex={newDex:F1} int={newInt:F1} " +
                $"(pool lvls {_attrLevelApplied[0]}/{_attrLevelApplied[1]}/{_attrLevelApplied[2]}) " +
                $"live-after-resync int={_player.Stats.Intelligence:F1} " +
                $"actor#{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_player):X8}");
        }

        if (newSkillLevel <= oldSkillLevel)
        {
            if (charLeveled) JustLeveledUp = true;
            return charLeveled || AttributesChangedLastAward;
        }

        JustLeveledUp = true;
        return true;
    }

    /// <summary>SC-COMPANION-PROGRESSION — initialize pools from a template's
    /// authored [actor][skills] levels (pm_*.gas authors fractional levels:
    /// ulora uber=1.24 with all class skills at 1, boryev combat_magic=34).
    /// The authored ATTRIBUTES already bake these skill levels' growth, so the
    /// applied-level markers sync to the seeded pools and growth resumes only
    /// PAST the authored levels — the same "already baked in" rule as the
    /// pre-persistence save restore below. Templates that author no uber get
    /// a character level derived from their highest class skill. Call once,
    /// right after construction, before any award.</summary>
    public void SeedAuthoredLevels(float uber, float melee, float ranged, float natureMagic, float combatMagic)
    {
        _skillXp[(int)SkillKind.Melee]       = XpForFractionalLevel(melee);
        _skillXp[(int)SkillKind.Ranged]      = XpForFractionalLevel(ranged);
        _skillXp[(int)SkillKind.NatureMagic] = XpForFractionalLevel(natureMagic);
        _skillXp[(int)SkillKind.CombatMagic] = XpForFractionalLevel(combatMagic);
        long maxSkillXp = Math.Max(
            Math.Max(_skillXp[(int)SkillKind.Melee], _skillXp[(int)SkillKind.Ranged]),
            Math.Max(_skillXp[(int)SkillKind.NatureMagic], _skillXp[(int)SkillKind.CombatMagic]));
        TotalXp = Math.Max(XpForFractionalLevel(uber), maxSkillXp);
        Level = _formulas.LevelForXp(TotalXp);
        JustLeveledUp = false;
        for (int a = 0; a < 3; a++)
            _attrLevelApplied[a] = _formulas.LevelForXp(AttrXpPool(a));
    }

    long XpForFractionalLevel(float level)
    {
        if (level <= 0f) return 0;
        int floor = Math.Max(1, (int)level);
        long lo = _formulas.XpForLevel(floor);
        long hi = _formulas.XpForLevel(floor + 1);
        float frac = Math.Clamp(level - floor, 0f, 1f);
        return lo + (long)((hi - lo) * frac);
    }

    /// <summary>Phase 19b — set XP + level directly from a save snapshot.
    /// Bypasses <see cref="AwardXp"/>'s level-up math because the auto-grown
    /// stats were saved separately and re-applied on the actor before this
    /// call; running AwardXp here would double-apply the gains. Clears
    /// <see cref="JustLeveledUp"/> so the load doesn't re-fire the level-up
    /// chime / toast for a level the player crossed mid-session.</summary>
    public void RestoreFromSave(long totalXp, int level, IReadOnlyList<long>? skillXp = null)
    {
        TotalXp = totalXp;
        Level = level;
        JustLeveledUp = false;
        if (skillXp is { Count: > 0 })
        {
            // New save: restore the real per-skill split. The saved attributes
            // already bake in these skill levels' gains, so a resumed session
            // only grants for levels earned beyond them.
            for (int i = 0; i < _skillXp.Length; i++)
                _skillXp[i] = i < skillXp.Count ? skillXp[i] : 0;
        }
        else
        {
            // Pre-persistence save: the per-skill split wasn't stored, but the
            // grown attributes WERE restored directly. Seed every skill to the
            // character level so growth resumes only when a skill advances PAST
            // it — never re-granting attributes already baked into the save.
            long floor = _formulas.XpForLevel(level);
            for (int i = 0; i < _skillXp.Length; i++) _skillXp[i] = floor;
        }
        // SC-ATTR-XP — re-derive each attribute's applied level from the
        // restored skill pools (the pools are linear combinations of them, so
        // no new save fields are needed) — then RECONCILE the stats upward.
        // The natural attribute value is a deterministic function:
        // base + (pool level − 1). A save written BEFORE a crossing carries
        // the older value; the old sync-only restore silently FORFEITED the
        // level earned between save and load ("int bar moving but the value
        // isn't changing" — INT hit 11 in-session, the next load restored 10
        // and marked level 2 already-granted). Never reconciles DOWN.
        var st = _player.Stats;
        float rs = st.Strength, rd = st.Dexterity, ri = st.Intelligence;
        bool reconciled = false;
        for (int a = 0; a < 3; a++)
        {
            _attrLevelApplied[a] = _formulas.LevelForXp(AttrXpPool(a));
            float expected = _attrBase[a] + (_attrLevelApplied[a] - 1);
            ref float cur = ref (a == 0 ? ref rs : ref (a == 1 ? ref rd : ref ri));
            if (cur < expected) { cur = expected; reconciled = true; }
        }
        if (reconciled)
        {
            _player.ResyncStats(st with
            {
                Strength = rs, Dexterity = rd, Intelligence = ri,
                MaxLife = _formulas.MaxLife(rs, rd, ri),
                MaxMana = _formulas.MaxMana(rs, rd, ri),
            });
        }
    }

    /// <summary>One-shot edge consumer. Returns true exactly once after a level
    /// gain — meant for a HUD toast / chime hookup. Subsequent reads return false
    /// until the next AwardXp crosses another threshold.</summary>
    public bool ConsumeJustLeveledUp()
    {
        if (!JustLeveledUp) return false;
        JustLeveledUp = false;
        return true;
    }
}
