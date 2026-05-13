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

    /// <summary>SC-QUEST-OBJ-A — talks credited toward
    /// <see cref="QuestDefinition.TalkCountGoal"/>. For simple "speak with
    /// X" quests TalkCountGoal is 1; the field is a counter (not a bool) so
    /// future "talk to N priests" objectives reuse the same plumbing without
    /// another schema bump.</summary>
    public int TalkProgress { get; set; }
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
        ChainFollowUps(completed);
        return (IReadOnlyList<string>?)completed ?? Array.Empty<string>();
    }

    /// <summary>SC-QUEST-OBJ-F — when a quest just auto-completed, activate
    /// any chained <see cref="QuestDefinition.NextQuestKey"/> follow-up.
    /// Walked AFTER the credit loop so we don't iterate _entries while
    /// mutating it. Idempotent: AddActive returns false if the key already
    /// exists, so re-completing the same quest doesn't double-spawn the
    /// follow-up. Cycle-safe by construction — A→B→A would walk justCompleted
    /// once (containing only A's key), AddActive would skip B's already-
    /// queued reopen, and the second activation never gets a chance to fire.
    /// Skipped intentionally on save-restore (the follow-up is already
    /// persisted in the save's quest list).</summary>
    void ChainFollowUps(List<string>? justCompleted)
    {
        if (justCompleted is null || justCompleted.Count == 0) return;
        foreach (var key in justCompleted)
        {
            if (!_entries.TryGetValue(key, out var entry)) continue;
            var def = entry.Definition;
            if (def is null) continue;
            var next = def.NextQuestKey;
            if (string.IsNullOrWhiteSpace(next)) continue;
            if (AddActive(next))
                Console.WriteLine($"[quest] follow-up activated: {next} (from {key})");
        }
    }

    /// <summary>SC-QUEST-OBJ-A — credit a "talk to NPC" objective. Fired by the
    /// dialogue-close edge in RenderHost with the most recently talked-to
    /// actor's template name. Matching is case-insensitive substring (same
    /// shape as kill-target matching) so a TalkTargetTemplate of "gyorn"
    /// catches both "gyorn" and any future variant template. Auto-promotes
    /// to <see cref="QuestState.Completed"/> when the counter reaches the
    /// goal; returns the keys that just flipped so the caller can flash a
    /// HUD toast / play the level-up-style "objective met" cue.</summary>
    public IReadOnlyList<string> RegisterTalk(string npcTemplateName)
    {
        if (string.IsNullOrWhiteSpace(npcTemplateName)) return Array.Empty<string>();
        List<string>? completed = null;
        foreach (var entry in _entries.Values)
        {
            if (entry.State != QuestState.Active) continue;
            var def = entry.Definition;
            if (def is null || string.IsNullOrEmpty(def.TalkTargetTemplate)) continue;
            if (def.TalkCountGoal <= 0) continue;
            if (npcTemplateName.IndexOf(def.TalkTargetTemplate,
                                        StringComparison.OrdinalIgnoreCase) < 0) continue;

            entry.TalkProgress = Math.Min(def.TalkCountGoal, entry.TalkProgress + 1);
            if (entry.TalkProgress >= def.TalkCountGoal)
            {
                entry.State = QuestState.Completed;
                entry.ClosedAt = DateTime.UtcNow;
                (completed ??= new List<string>()).Add(entry.Key);
            }
        }
        ChainFollowUps(completed);
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
    public void RestoreFromSave(IEnumerable<(string Key, QuestState State, int KillProgress, int TalkProgress)> entries)
    {
        _entries.Clear();
        foreach (var (key, state, killProgress, talkProgress) in entries)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            QuestCatalog.TryGet(key, out var def);
            _entries[key] = new QuestEntry
            {
                Key          = key,
                State        = state,
                Definition   = def,
                KillProgress = killProgress,
                TalkProgress = talkProgress,
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
            // ─── Phase 20c originals (kept for backward-compat with the existing
            // Phase 20a Norick dialogue tree that activates Quest_for_Gyorn as a
            // kill-3-krug stub) ──────────────────────────────────────────────────
            ["Quest_for_Gyorn"] = new QuestDefinition
            {
                Key                 = "Quest_for_Gyorn",
                ScreenName          = "Aid the Farmhouse",
                KillTargetTemplate  = "krug",
                KillCountGoal       = 3,
                ObjectiveText       = "Kill 3 krug raiding the Farmhouse.",
            },
            ["quest_edgaar_basement"] = new QuestDefinition
            {
                Key                 = "quest_edgaar_basement",
                ScreenName          = "Edgaar's Basement",
                KillTargetTemplate  = "krug",
                KillCountGoal       = 5,
                ObjectiveText       = "Clear 5 krug from Edgaar's basement.",
            },

            // ─── SC-QUEST-OBJ-F catalog (all 24 Kingdom of Ehb quests from
            // wiki audit, see reference_ds1_ehb_quests.md) ────────────────────
            // Notes:
            //  * Chained main-quest beats use NextQuestKey to auto-activate the
            //    follow-up (DS1 itself splits multi-stage quests into chained
            //    IDs rather than embedding stages).
            //  * Pickup / Reach / Deliver / KillNamed objectives ship as DATA
            //    today; their Register*() runtimes land in slices C/B/D/E. Until
            //    then those entries activate but won't auto-credit — that's the
            //    correct interim state (the data shape is forward-compatible
            //    so future slices auto-pick them up).
            //  * Keys use lowercase_with_underscores. The legacy Quest_for_Gyorn
            //    above is intentionally TitleCase to match the Phase 20a dialogue
            //    tree which can't easily be rekeyed without re-authoring the
            //    conversations.gas.

            // ── Chapter I — Stonebridge ──
            ["quest_seek_gyorn"] = new QuestDefinition
            {
                Key                 = "quest_seek_gyorn",
                ScreenName          = "Seek Gyorn in Stonebridge",
                // SC-QUEST-OBJ-A stub-targets Edgaar so the FH-only receipt
                // works before Stonebridge wiring lands. Switch to "gyorn"
                // once Stonebridge streams (the real DS1 NPC).
                TalkTargetTemplate  = "edgaar",
                TalkCountGoal       = 1,
                ObjectiveText       = "Speak with Gyorn at Stonebridge.",
                NextQuestKey        = "quest_deliver_gyorn_report",
            },

            // ── Chapter II — Journey to the Overseer ──
            ["quest_deliver_gyorn_report"] = new QuestDefinition
            {
                Key                 = "quest_deliver_gyorn_report",
                ScreenName          = "Deliver Gyorn's Report",
                // DELIVER = composite (hold item + talk to receiver). For
                // now we credit on the talk-target alone; SC-QUEST-OBJ-D
                // tightens to "hold report AND talk Hrok".
                TalkTargetTemplate  = "hrok",
                TalkCountGoal       = 1,
                ObjectiveText       = "Carry Gyorn's report to Hrok at the Stonebridge North gate.",
                NextQuestKey        = "quest_clear_glitterdelve",
            },
            ["quest_clear_glitterdelve"] = new QuestDefinition
            {
                Key                 = "quest_clear_glitterdelve",
                ScreenName          = "Clear Glitterdelve Pass",
                KillTargetTemplate  = "krug",
                KillCountGoal       = 6,
                ObjectiveText       = "Clear the krug from Glitterdelve Pass.",
                NextQuestKey        = "quest_report_torg_findings",
            },
            ["quest_report_torg_findings"] = new QuestDefinition
            {
                Key                 = "quest_report_torg_findings",
                ScreenName          = "Report Torg's Findings",
                TalkTargetTemplate  = "torg",
                TalkCountGoal       = 1,
                ObjectiveText       = "Speak with Torg about what he learned in the mines.",
                NextQuestKey        = "quest_for_merik",
            },
            ["quest_ordus_axe"] = new QuestDefinition
            {
                Key                 = "quest_ordus_axe",
                ScreenName          = "Ordus' Axe",
                // PICKUP-based DELIVER; SC-QUEST-OBJ-D fills the composite.
                TalkTargetTemplate  = "ordus",
                TalkCountGoal       = 1,
                ObjectiveText       = "Recover Ordus' lost axe and return it to him.",
            },
            ["quest_sisters_message"] = new QuestDefinition
            {
                Key                 = "quest_sisters_message",
                ScreenName          = "A Sister's Message",
                TalkTargetTemplate  = "sister",
                TalkCountGoal       = 1,
                ObjectiveText       = "Deliver a sister's message to her family.",
            },
            ["quest_rescue_torg"] = new QuestDefinition
            {
                Key                 = "quest_rescue_torg",
                ScreenName          = "Rescue Torg",
                // R + KN + T composite per wiki audit. Land KILL-NAMED in
                // SC-QUEST-OBJ-E; for now this credits on the post-rescue talk.
                TalkTargetTemplate  = "torg",
                TalkCountGoal       = 1,
                ObjectiveText       = "Free Torg from his krug captors.",
            },

            // ── Chapter III — The Search for Merik ──
            ["quest_for_merik"] = new QuestDefinition
            {
                Key                 = "quest_for_merik",
                ScreenName          = "Quest for Merik",
                TalkTargetTemplate  = "merik",
                TalkCountGoal       = 1,
                ObjectiveText       = "Find the wizard Merik in the wilderness beyond Wesrin Cross.",
                NextQuestKey        = "quest_reinforce_fortress_kroth",
            },
            ["quest_book_return"] = new QuestDefinition
            {
                Key                 = "quest_book_return",
                ScreenName          = "Book Return",
                // PICKUP + DELIVER (S/C-QUEST-OBJ-C/D).
                TalkTargetTemplate  = "librarian",
                TalkCountGoal       = 1,
                ObjectiveText       = "Return the borrowed book to its owner.",
            },
            ["quest_reinforce_fortress_kroth"] = new QuestDefinition
            {
                Key                 = "quest_reinforce_fortress_kroth",
                ScreenName          = "Reinforce Fortress Kroth",
                // Reads as DEFEND but DS1 credits on the post-wave NPC dialogue
                // (per audit). Talk-target lands when scripted wave completes.
                TalkTargetTemplate  = "captain_kroth",
                TalkCountGoal       = 1,
                ObjectiveText       = "Hold Fortress Kroth against the goblin assault and report to the captain.",
                NextQuestKey        = "quest_confront_bandit_boss",
            },
            ["quest_homeless_blacksmith"] = new QuestDefinition
            {
                Key                 = "quest_homeless_blacksmith",
                ScreenName          = "Homeless Blacksmith",
                TalkTargetTemplate  = "blacksmith",
                TalkCountGoal       = 1,
                ObjectiveText       = "Help the homeless blacksmith find a new home.",
            },

            // ── Chapter IV — The Warding Staff ──
            ["quest_confront_bandit_boss"] = new QuestDefinition
            {
                Key                 = "quest_confront_bandit_boss",
                ScreenName          = "Confront the Bandit Boss",
                // KN — SC-QUEST-OBJ-E will switch to spawn-id match.
                KillTargetTemplate  = "bandit_boss",
                KillCountGoal       = 1,
                ObjectiveText       = "Defeat the Bandit Boss holding the temple's relic.",
                NextQuestKey        = "quest_meriks_staff",
            },
            ["quest_purify_temple"] = new QuestDefinition
            {
                Key                 = "quest_purify_temple",
                ScreenName          = "Purify the Temple",
                KillTargetTemplate  = "bandit",
                KillCountGoal       = 4,
                ObjectiveText       = "Cleanse the temple of bandit defilement.",
            },
            ["quest_meriks_staff"] = new QuestDefinition
            {
                Key                 = "quest_meriks_staff",
                ScreenName          = "Merik's Staff",
                // P — SC-QUEST-OBJ-C will switch to RegisterPickup match.
                TalkTargetTemplate  = "merik",
                TalkCountGoal       = 1,
                ObjectiveText       = "Recover Merik's Warding Staff and return it to him.",
                NextQuestKey        = "quest_missing_treasure_hunters",
            },

            // ── Chapter V — An Ancient Evil ──
            ["quest_missing_treasure_hunters"] = new QuestDefinition
            {
                Key                 = "quest_missing_treasure_hunters",
                ScreenName          = "Missing Treasure Hunters",
                TalkTargetTemplate  = "treasure_hunter",
                TalkCountGoal       = 1,
                ObjectiveText       = "Find the lost treasure hunters in the depths beyond Fortress Kroth.",
                NextQuestKey        = "quest_subdue_droog",
            },

            // ── Chapter VI — Unwise Alliance ──
            // Sybex guide correction (2026-05-12): wiki classified this as
            // KILL-NAMED but the official strategy guide confirms it credits
            // on dialogue with Nonataya the Droog ambassador, who then
            // becomes a vendor. "Subdue" is a misnomer — it's a parley.
            ["quest_subdue_droog"] = new QuestDefinition
            {
                Key                 = "quest_subdue_droog",
                ScreenName          = "Subdue the Droog",
                TalkTargetTemplate  = "nonataya",
                TalkCountGoal       = 1,
                ObjectiveText       = "Negotiate passage with Nonataya, ambassador of the Droog.",
                NextQuestKey        = "quest_journey_to_castle_ehb",
            },

            // ── Chapter VII — King and Castle ──
            ["quest_journey_to_castle_ehb"] = new QuestDefinition
            {
                Key                 = "quest_journey_to_castle_ehb",
                ScreenName          = "Journey to Castle Ehb",
                // R — SC-QUEST-OBJ-B will switch to RegisterReach match.
                TalkTargetTemplate  = "castle_steward",
                TalkCountGoal       = 1,
                ObjectiveText       = "Reach the gates of Castle Ehb.",
                NextQuestKey        = "quest_slay_dragon_rathe",
            },
            ["quest_slay_dragon_rathe"] = new QuestDefinition
            {
                Key                 = "quest_slay_dragon_rathe",
                ScreenName          = "Slay the Ancient Dragon of Rathe",
                KillTargetTemplate  = "dragon_rathe",
                KillCountGoal       = 1,
                ObjectiveText       = "Slay the Ancient Dragon of Rathe.",
                NextQuestKey        = "quest_search_for_king",
            },
            ["quest_search_for_king"] = new QuestDefinition
            {
                Key                 = "quest_search_for_king",
                ScreenName          = "Search for the King",
                TalkTargetTemplate  = "king_konreid",
                TalkCountGoal       = 1,
                ObjectiveText       = "Find King Konreid in the depths of Castle Ehb.",
                NextQuestKey        = "quest_chamber_of_stars",
            },

            // ── Chapter VIII — The Chamber of Stars ──
            ["quest_chamber_of_stars"] = new QuestDefinition
            {
                Key                 = "quest_chamber_of_stars",
                ScreenName          = "The Chamber of Stars",
                // R + cutscene-trigger. Talk-target is the post-cutscene NPC.
                TalkTargetTemplate  = "advisor",
                TalkCountGoal       = 1,
                ObjectiveText       = "Reach the Chamber of Stars at the heart of Castle Ehb.",
                NextQuestKey        = "quest_vanquish_seck",
            },

            // ── Chapter IX — Dungeon Siege ──
            ["quest_vanquish_seck"] = new QuestDefinition
            {
                Key                 = "quest_vanquish_seck",
                ScreenName          = "Vanquish the Seck",
                // FIXME(SC-QUEST-OBJ-E): substring match on "gom" is broad
                // enough to land on any template containing those 3 letters
                // ("goblin", any "g_om_*" asset). Safe today because this
                // entry only Activates after the entire Ch.I-VIII chain
                // completes (no Active = no kill-credit), but SC-QUEST-OBJ-E
                // should switch this to per-spawn-id match or tighten to the
                // exact endgame template name once verified in Logic.dsres.
                KillTargetTemplate  = "gom",
                KillCountGoal       = 1,
                ObjectiveText       = "Defeat the Seck warlord Gom and end the Resurgence.",
                // No NextQuestKey — endgame.
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

    /// <summary>SC-QUEST-OBJ-A — talk-to-NPC objective. Template-name substring
    /// (case-insensitive) matched against the actor the player most recently
    /// finished a dialogue with. Empty string disables the talk path; both
    /// kill and talk fields can be set on one definition (DS1 "kill 3 krug
    /// then report back" composites collapse to either KILL-and-promote-on-
    /// completion, or TALK with a separate kill-quest as prereq — D / E land
    /// the composite shape).</summary>
    public string TalkTargetTemplate { get; init; } = "";

    /// <summary>How many distinct talk events credit the quest. Defaults
    /// to 0 so a definition that doesn't set TalkTargetTemplate also won't
    /// auto-complete on the first talk-event to anyone. Simple "speak with
    /// X" objectives use 1; "talk to all priests" patterns set N.</summary>
    public int    TalkCountGoal      { get; init; }

    /// <summary>SC-QUEST-OBJ-F — chained-quest follow-up. When this quest
    /// auto-completes (RegisterKill / RegisterTalk hits its goal), the
    /// journal AddActives this key as the next stage. Empty string = single-
    /// stage quest (no follow-up). Matches DS1's own approach to multi-stage
    /// quests like Seek Gyorn → Deliver Gyorn's Report → Clear Glitterdelve,
    /// where each "stage" is a distinct quest-ID rather than a stage-index
    /// inside one entry. Simpler than embedding a Stage[] in the definition
    /// and lets the journal screen show each stage as its own row.</summary>
    public string NextQuestKey      { get; init; } = "";
}
