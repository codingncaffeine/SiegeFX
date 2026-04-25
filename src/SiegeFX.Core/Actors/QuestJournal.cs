namespace SiegeFX.Core.Actors;

/// <summary>State of a quest in the player's journal. <see cref="Offered"/>
/// is reserved for the future case where the dialogue runtime wants to know
/// "Edgaar already pitched this and I haven't accepted/declined yet"; today
/// we only flip between Active and Completed/Failed, but the enum holds the
/// full DS1 vocabulary so the journal screen doesn't lose information when
/// the offered-but-declined branch eventually lands.</summary>
public enum QuestState
{
    Offered,
    Active,
    Completed,
    Failed,
}

/// <summary>One journal entry. <see cref="Key"/> is the quest's GAS key as
/// authored on the dialogue node's <c>activate_quest</c> attribute (e.g.
/// <c>quest_edgaar_basement</c>, <c>Quest_for_Gyorn</c>) — case is preserved
/// from the source but lookups inside the journal are case-insensitive so a
/// typo in one author's GAS doesn't break dedup.</summary>
public sealed class QuestEntry
{
    public string Key { get; init; } = "";
    public QuestState State { get; set; } = QuestState.Active;
    public DateTime AcceptedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }

    /// <summary>Phase 20c — bound to the catalog at <see cref="QuestJournal.AddActive"/>
    /// time. Null when the journal sees a quest key that no catalog entry covers
    /// (the entry still tracks state, just without a goal counter / target).</summary>
    public QuestDefinition? Definition { get; set; }

    /// <summary>Kills credited toward this quest's <see cref="QuestDefinition.KillCountGoal"/>.
    /// Counter is monotonically non-decreasing within an active entry; flipping
    /// to Completed leaves it at goal so the journal screen still shows "5 / 5".</summary>
    public int KillProgress { get; set; }
}

/// <summary>
/// Phase 20b — player's quest log. Owned by <see cref="PlayerProgression"/>
/// so it shares the player's lifetime and rides through the save file.
/// Ignorance of whether a key matches an authored quest definition is
/// intentional: 20b lands the data shape and journal UI; 20c hooks
/// objectives + completion + a real <c>QuestStore</c>.
/// </summary>
public sealed class QuestJournal
{
    readonly Dictionary<string, QuestEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<QuestEntry> Entries => _entries.Values;

    public IEnumerable<QuestEntry> Active =>
        _entries.Values.Where(e => e.State == QuestState.Active);

    public IEnumerable<QuestEntry> Completed =>
        _entries.Values.Where(e => e.State == QuestState.Completed);

    /// <summary>True when the journal already knows about this quest in any
    /// state. Lets callers distinguish "first acceptance" from "re-pitch".</summary>
    public bool Has(string key) =>
        !string.IsNullOrWhiteSpace(key) && _entries.ContainsKey(key);

    public bool TryGet(string key, out QuestEntry? entry)
    {
        if (string.IsNullOrWhiteSpace(key)) { entry = null; return false; }
        return _entries.TryGetValue(key, out entry);
    }

    /// <summary>Mark a quest active. Idempotent — re-accepting a completed or
    /// active quest is a no-op (DS1 doesn't reset progress on re-talk). Returns
    /// true when this is the first time the key has been seen.</summary>
    public bool AddActive(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (_entries.ContainsKey(key)) return false;
        var entry = new QuestEntry { Key = key, State = QuestState.Active };
        // Bind the catalog definition once at acceptance time so subsequent
        // catalog edits don't retroactively shift in-flight quest goals.
        QuestCatalog.TryGet(key, out var def);
        entry.Definition = def;
        _entries[key] = entry;
        return true;
    }

    /// <summary>Phase 20c — credit a kill against every active entry whose
    /// <see cref="QuestDefinition.KillTargetTemplate"/> matches the dead actor's
    /// template (substring match, case-insensitive — covers grunt / scout /
    /// commander variants under one "krug" umbrella). Auto-promotes the entry
    /// to <see cref="QuestState.Completed"/> the moment the goal is met; that's
    /// our stand-in for a turn-in mechanic until vendor + complete-via-dialogue
    /// land in 20d. Returns the keys of any quests that just completed so the
    /// caller can flash a HUD toast.</summary>
    public IReadOnlyList<string> RegisterKill(string deadTemplateName)
    {
        if (string.IsNullOrWhiteSpace(deadTemplateName)) return Array.Empty<string>();
        List<string>? completed = null;
        foreach (var entry in _entries.Values)
        {
            if (entry.State != QuestState.Active) continue;
            var def = entry.Definition;
            if (def is null || string.IsNullOrEmpty(def.KillTargetTemplate)) continue;
            if (def.KillCountGoal <= 0) continue;
            if (deadTemplateName.IndexOf(def.KillTargetTemplate,
                                         StringComparison.OrdinalIgnoreCase) < 0) continue;

            entry.KillProgress = Math.Min(def.KillCountGoal, entry.KillProgress + 1);
            if (entry.KillProgress >= def.KillCountGoal)
            {
                entry.State = QuestState.Completed;
                entry.ClosedAt = DateTime.UtcNow;
                (completed ??= new List<string>()).Add(entry.Key);
            }
        }
        return (IReadOnlyList<string>?)completed ?? Array.Empty<string>();
    }

    /// <summary>Flip an active quest to completed and stamp the close time.
    /// Adding a completed entry for a key the journal has never seen still
    /// works — the engine sometimes hands out completions cold (cheat console,
    /// debug commands).</summary>
    public bool MarkCompleted(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new QuestEntry { Key = key, State = QuestState.Completed, ClosedAt = DateTime.UtcNow };
            _entries[key] = entry;
            return true;
        }
        if (entry.State == QuestState.Completed) return false;
        entry.State = QuestState.Completed;
        entry.ClosedAt = DateTime.UtcNow;
        return true;
    }

    public bool MarkFailed(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (!_entries.TryGetValue(key, out var entry)) return false;
        if (entry.State == QuestState.Failed) return false;
        entry.State = QuestState.Failed;
        entry.ClosedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>Clear the journal and replay a sequence of (key, state, progress)
    /// tuples. Used by SaveFile load — the in-memory journal needs to be rebuilt
    /// from scratch each time so a stray entry from a different save can't
    /// survive. Definitions get re-bound off the live catalog so a content
    /// patch that shipped between save and load picks up the new goal numbers.</summary>
    public void RestoreFromSave(IEnumerable<(string Key, QuestState State, int KillProgress)> entries)
    {
        _entries.Clear();
        foreach (var (key, state, progress) in entries)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            QuestCatalog.TryGet(key, out var def);
            _entries[key] = new QuestEntry
            {
                Key          = key,
                State        = state,
                Definition   = def,
                KillProgress = progress,
            };
        }
    }
}

/// <summary>
/// Phase 20c — authored quest catalog. DS1 ships quest "definitions" as
/// flag bits set/checked by skrit; we don't run skrit yet, so we substitute
/// a tiny in-engine table that defines the per-quest objective in terms the
/// runtime can actually drive (kill N of template X). Keys are matched
/// case-insensitively against the <c>activate_quest</c> attribute on dialogue
/// nodes; missing entries are tolerated — the journal still tracks the bare
/// state, just without a goal counter or compass marker.
/// </summary>
public static class QuestCatalog
{
    static readonly Dictionary<string, QuestDefinition> _defs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // The Farmhouse opening — Norick asks for help against the krug
            // raid. Any krug template counts (grunt / scout / commander); the
            // shipped fh_r1 spawns are mostly grunts so the goal is reachable
            // without fishing for a specific variant.
            ["Quest_for_Gyorn"] = new QuestDefinition
            {
                Key                 = "Quest_for_Gyorn",
                ScreenName          = "Aid the Farmhouse",
                KillTargetTemplate  = "krug",
                KillCountGoal       = 3,
                ObjectiveText       = "Kill 3 krug raiding the Farmhouse.",
            },
            // Edgaar's basement — same loop with a different label so we can
            // exercise multi-quest tracking when both get accepted.
            ["quest_edgaar_basement"] = new QuestDefinition
            {
                Key                 = "quest_edgaar_basement",
                ScreenName          = "Edgaar's Basement",
                KillTargetTemplate  = "krug",
                KillCountGoal       = 5,
                ObjectiveText       = "Clear 5 krug from Edgaar's basement.",
            },
        };

    public static bool TryGet(string key, out QuestDefinition? def)
    {
        if (string.IsNullOrWhiteSpace(key)) { def = null; return false; }
        return _defs.TryGetValue(key, out def);
    }

    public static IReadOnlyDictionary<string, QuestDefinition> All => _defs;
}

/// <summary>One catalog row. Today every quest is a kill objective; when
/// 20d-and-on add fetch / talk / reach objectives this becomes a discriminated
/// union (oneof KillObjective | TalkObjective | ReachObjective). For now the
/// fields just sit empty when unused.</summary>
public sealed class QuestDefinition
{
    public string Key                { get; init; } = "";
    public string ScreenName         { get; init; } = "";
    public string KillTargetTemplate { get; init; } = "";
    public int    KillCountGoal      { get; init; }
    public string ObjectiveText      { get; init; } = "";
}
