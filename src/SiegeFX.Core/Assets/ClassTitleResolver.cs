namespace SiegeFX.Core.Assets;

/// <summary>
/// Phase 22-INFORAIL-A2 — resolves DS1's dynamic character_class text.
///
/// The starting title comes from the actor template's
/// <c>[actor]screen_class</c> (heroes.gas line 376/410 for farmboy/
/// farmgirl = "Farmer"). Once the player invests skill points the
/// title upgrades along the highest-skill track.
///
/// Source: <c>project_siegefx_class_titles.md</c>, table verified
/// from Dungeon Siege Heaven's classification page and cross-checked
/// against the Fandom wiki + Sybex guide NPC mentions (Gyorn=Squire,
/// Zed=Apprentice, Naidi=Bowyer).
///
/// Selection rule (DS1 vanilla):
/// <list type="bullet">
///   <item>If all four skills are 0 → return the starting title.</item>
///   <item>Otherwise: pick the skill with the highest level; if tied
///     prefer (melee, ranged, nature magic, combat magic) in order.</item>
///   <item>Map that skill+level to the title via the table below.</item>
/// </list>
/// </summary>
public static class ClassTitleResolver
{
    public enum Skill { Melee, Ranged, NatureMagic, CombatMagic }

    /// <summary>Title for a given skill at a given skill level.
    /// Threshold ranges per ds.heavengames.com/gameinfo/classification/.</summary>
    public static string TitleFor(Skill skill, int level)
    {
        // Skill levels in DS1 are 0..180 (formulas.gas max_level=180).
        // Tier brackets: 1-4, 5-10, 11-19, 20-49, 50-99, 100+
        int tier = level switch
        {
            <= 0 => -1,
            <= 4 => 0,
            <= 10 => 1,
            <= 19 => 2,
            <= 49 => 3,
            <= 99 => 4,
            _    => 5,
        };
        if (tier < 0) return "";
        return skill switch
        {
            Skill.Melee       => MeleeTitles[tier],
            Skill.Ranged      => RangedTitles[tier],
            Skill.NatureMagic => NatureMagicTitles[tier],
            Skill.CombatMagic => CombatMagicTitles[tier],
            _ => "",
        };
    }

    /// <summary>Resolve the displayed character_class for a player
    /// given their current skill levels + the template's starting
    /// title. <paramref name="startingTitle"/> is the per-template
    /// <c>[actor]screen_class</c> ("Farmer", "Miner", "Brute", etc.)
    /// — used when no skill has been levelled yet.</summary>
    public static string Resolve(string startingTitle,
                                 int meleeLevel, int rangedLevel,
                                 int natureMagicLevel, int combatMagicLevel)
    {
        int max = System.Math.Max(System.Math.Max(meleeLevel, rangedLevel),
                                  System.Math.Max(natureMagicLevel, combatMagicLevel));
        if (max <= 0) return startingTitle;
        // Tie-break preference: melee, ranged, nature magic, combat magic.
        if (meleeLevel == max)       return TitleFor(Skill.Melee, meleeLevel);
        if (rangedLevel == max)      return TitleFor(Skill.Ranged, rangedLevel);
        if (natureMagicLevel == max) return TitleFor(Skill.NatureMagic, natureMagicLevel);
        return TitleFor(Skill.CombatMagic, combatMagicLevel);
    }

    // Threshold tables indexed by tier (0..5).
    private static readonly string[] MeleeTitles =
        { "Squire", "Soldier", "Warrior", "Knight", "Champion", "Grand Champion" };
    private static readonly string[] RangedTitles =
        { "Bowyer", "Archer", "Marksman", "Sharpshooter", "Master Sharpshooter", "Grandmaster Sharpshooter" };
    private static readonly string[] NatureMagicTitles =
        { "Apprentice", "Theurgist", "Magician", "Grand Mage", "Arch Mage", "Supreme Arch Mage" };
    private static readonly string[] CombatMagicTitles =
        { "Savant", "Hedge Wizard", "Wizard", "Sorcerer", "Grand Sorcerer", "Grand High Sorcerer" };
}
