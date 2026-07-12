namespace SiegeFX.Core.Net;

/// <summary>SC-MP-EOS P5 — retail-verified multiplayer join rules (canon:
/// reference_ds1_mp_progression). No auto-leveling ever; a character joins
/// AS IT IS and mixed levels are handled spatially by per-town gates. These
/// evaluators are pure so they unit-test and drive both the browser (grey a
/// row + reason) and the spawn-town picker.</summary>
public enum MpDifficulty { Regular, Veteran, Elite }

/// <summary>A joinable session's advertised state (from lobby attributes).</summary>
public readonly record struct MpSessionInfo(
    string HostName, string Map, MpDifficulty Difficulty,
    int PlayersIn, int PlayersMax, bool NewCharactersOnly,
    string HostArea, int PartyLevelLow, int PartyLevelHigh);

public static class MpJoinRules
{
    // Retail difficulty-tier character-level minimums.
    public const int VeteranMinLevel = 54;
    public const int EliteMinLevel = 83;

    /// <summary>Can this character join? Returns (ok, reason). Reason is the
    /// greyed-row tooltip when !ok. New characters (level ≤ 1, fresh) satisfy
    /// "new characters only"; existing characters fail it — retail's parity-
    /// by-exclusion, never a boost.</summary>
    public static (bool Ok, string Reason) CanJoin(in MpSessionInfo s, int charLevel, bool charIsFresh)
    {
        if (s.PlayersIn >= s.PlayersMax) return (false, "Game is full.");
        if (s.NewCharactersOnly && !charIsFresh)
            return (false, "Host requires new characters only.");
        int min = s.Difficulty switch
        {
            MpDifficulty.Veteran => VeteranMinLevel,
            MpDifficulty.Elite => EliteMinLevel,
            _ => 0,
        };
        if (charLevel < min)
            return (false, $"{s.Difficulty} requires character level {min}+ (yours is {charLevel}).");
        return (true, "");
    }
}

/// <summary>SC-MP-EOS P5 — Utraean Peninsula per-town level gates (retail
/// Normal-tier minimums from the Sybex guide; the same threshold gates both
/// spawn-town choice and the in-world Displacer). The spawn-town picker
/// offers only towns a character qualifies for; the wilderness is open (the
/// fixed per-zone monsters are the fence).</summary>
public static class MpSpawnTowns
{
    public static readonly (string Town, int MinLevel)[] Peninsula =
    {
        ("Elddim", 0), ("Crystwind", 5), ("Fallraen", 9), ("Meren", 18),
        ("Lang", 25), ("Quillrabe", 42), ("Hiroth", 46), ("Grescal", 50),
    };

    /// <summary>Towns this character may spawn at / teleport to.</summary>
    public static IEnumerable<string> Available(int charLevel) =>
        Peninsula.Where(t => charLevel >= t.MinLevel).Select(t => t.Town);

    public static bool CanEnter(string town, int charLevel)
    {
        foreach (var (t, min) in Peninsula)
            if (string.Equals(t, town, StringComparison.OrdinalIgnoreCase))
                return charLevel >= min;
        return true; // unknown town / Ehb campaign = ungated
    }
}
