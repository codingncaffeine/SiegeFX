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
        _entries[key] = new QuestEntry { Key = key, State = QuestState.Active };
        return true;
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

    /// <summary>Clear the journal and replay a sequence of (key, state) pairs.
    /// Used by SaveFile load — the in-memory journal needs to be rebuilt from
    /// scratch each time so a stray entry from a different save can't survive.</summary>
    public void RestoreFromSave(IEnumerable<(string Key, QuestState State)> entries)
    {
        _entries.Clear();
        foreach (var (key, state) in entries)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            _entries[key] = new QuestEntry { Key = key, State = state };
        }
    }
}
