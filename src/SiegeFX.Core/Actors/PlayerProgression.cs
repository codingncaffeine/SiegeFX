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

    public PlayerProgression(Actor player, FormulasStore formulas)
    {
        _player = player;
        _formulas = formulas;
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
        int newLevel = _formulas.LevelForXp(TotalXp);
        if (newLevel == Level) return false;

        int dlvl = newLevel - Level;
        Level = newLevel;
        JustLeveledUp = true;

        // Apply proportional gains × number of levels gained, then recompute the
        // life/mana caps from the new attribute trio. Player template authors max_life=0
        // so the formula path is canonical for the PC.
        var gains = _formulas.ProportionalGains(skill);
        var stats = _player.Stats;
        float newStr = stats.Strength     + gains.Str * dlvl;
        float newDex = stats.Dexterity    + gains.Dex * dlvl;
        float newInt = stats.Intelligence + gains.Int * dlvl;
        float newMaxLife = _formulas.MaxLife(newStr, newDex, newInt);
        float newMaxMana = _formulas.MaxMana(newStr, newDex, newInt);
        var newStats = stats with
        {
            Strength = newStr, Dexterity = newDex, Intelligence = newInt,
            MaxLife = newMaxLife, MaxMana = newMaxMana,
        };
        _player.ResyncStats(newStats);
        return true;
    }

    /// <summary>Phase 19b — set XP + level directly from a save snapshot.
    /// Bypasses <see cref="AwardXp"/>'s level-up math because the auto-grown
    /// stats were saved separately and re-applied on the actor before this
    /// call; running AwardXp here would double-apply the gains. Clears
    /// <see cref="JustLeveledUp"/> so the load doesn't re-fire the level-up
    /// chime / toast for a level the player crossed mid-session.</summary>
    public void RestoreFromSave(long totalXp, int level)
    {
        TotalXp = totalXp;
        Level = level;
        JustLeveledUp = false;
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
