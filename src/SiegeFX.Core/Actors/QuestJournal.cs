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

    /// <summary>SC-QUEST-OBJ-C — items picked up that match the quest's
    /// <see cref="QuestDefinition.PickupTargetTemplate"/>. Most pickup
    /// objectives are PickupCountGoal=1 (recover one named artifact) but
    /// the field is a counter for future "collect N relics" patterns.</summary>
    public int PickupProgress { get; set; }
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

    /// <summary>SC-QUEST-OBJ-A-EXACT post-RESYNC matcher. Exact equality on
    /// the actor's full template name OR the catalog target followed by an
    /// underscore (so "merik" lights up "merik" and "merik_nis" but not
    /// "merikalive"). Case-insensitive in both arms.</summary>
    static bool IsTalkTargetMatch(string actorTemplate, string catalogTarget)
    {
        if (string.Equals(actorTemplate, catalogTarget, StringComparison.OrdinalIgnoreCase))
            return true;
        return actorTemplate.Length > catalogTarget.Length
            && actorTemplate.StartsWith(catalogTarget, StringComparison.OrdinalIgnoreCase)
            && actorTemplate[catalogTarget.Length] == '_';
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
        => RegisterTalk(npcTemplateName, playerInventory: null);

    /// <summary>SC-QUEST-OBJ-D overload — same as RegisterTalk but checks
    /// every active quest's <see cref="QuestDefinition.DeliverItemTemplate"/>
    /// against the player's inventory before crediting. Deliver objectives
    /// without the item held silently no-op (the talk just doesn't count
    /// toward the quest until the item is in hand). Talk-only objectives
    /// (empty DeliverItemTemplate) work unchanged. Pass null inventory to
    /// fall back to talk-only matching — the dialogue path uses this when
    /// it doesn't have an inventory reference at hand.</summary>
    public IReadOnlyList<string> RegisterTalk(string npcTemplateName,
        IReadOnlyList<SiegeFX.Core.Actors.LootEntry>? playerInventory)
    {
        if (string.IsNullOrWhiteSpace(npcTemplateName)) return Array.Empty<string>();
        List<string>? completed = null;
        foreach (var entry in _entries.Values)
        {
            if (entry.State != QuestState.Active) continue;
            var def = entry.Definition;
            if (def is null || string.IsNullOrEmpty(def.TalkTargetTemplate)) continue;
            if (def.TalkCountGoal <= 0) continue;
            // SC-QUEST-OBJ-D — deliver gate. When DeliverItemTemplate is set,
            // the talk only credits if the player holds a matching item. No
            // inventory passed = treat as talk-only (legacy callers).
            if (!string.IsNullOrEmpty(def.DeliverItemTemplate))
            {
                if (playerInventory is null) continue;
                bool holdsItem = false;
                foreach (var it in playerInventory)
                {
                    if (it.Reference.IndexOf(def.DeliverItemTemplate,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    { holdsItem = true; break; }
                }
                if (!holdsItem) continue;
            }
            // SC-QUEST-OBJ-A-EXACT (post-RESYNC fold 2026-05-13): match on
            // exact NPC template OR on TalkTargetTemplate + "_" prefix. DS1
            // mixes naming conventions — some NPCs are bare-name templates
            // ("torg", "skartis", "gloern") while others ship with suffixes
            // ("merik_nis", "lord_bolingar_join", "king_konreid"). Exact-
            // match alone would silently no-op on every suffixed variant
            // when the catalog stores the bare name; pure substring would
            // re-introduce false-positive collisions ("torg" matching
            // "torgmaster", etc.). Underscore-anchored prefix splits the
            // difference: "merik" matches "merik" and "merik_nis" but NOT
            // "merikalive" or other non-namespace neighbors. The previous
            // torg double-credit case (rescue+report) is gone post-RESYNC
            // (the two quests now target distinct NPCs).
            if (!IsTalkTargetMatch(npcTemplateName, def.TalkTargetTemplate)) continue;

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

    /// <summary>SC-QUEST-OBJ-C — credit "pickup item X" objectives against
    /// the just-acquired item's template. Fired from the loot-pickup edge
    /// in RenderHost (after the LootPile resolves into the inventory grid).
    /// Substring match (case-insensitive) against
    /// <see cref="QuestDefinition.PickupTargetTemplate"/>; auto-promotes to
    /// Completed when the counter reaches the goal. Returns the keys that
    /// just flipped so the caller can flash a toast.</summary>
    public IReadOnlyList<string> RegisterPickup(string itemTemplateName)
    {
        if (string.IsNullOrWhiteSpace(itemTemplateName)) return Array.Empty<string>();
        List<string>? completed = null;
        foreach (var entry in _entries.Values)
        {
            if (entry.State != QuestState.Active) continue;
            var def = entry.Definition;
            if (def is null || string.IsNullOrEmpty(def.PickupTargetTemplate)) continue;
            if (def.PickupCountGoal <= 0) continue;
            if (itemTemplateName.IndexOf(def.PickupTargetTemplate,
                                         StringComparison.OrdinalIgnoreCase) < 0) continue;

            entry.PickupProgress = Math.Min(def.PickupCountGoal, entry.PickupProgress + 1);
            if (entry.PickupProgress >= def.PickupCountGoal)
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
    public void RestoreFromSave(IEnumerable<(string Key, QuestState State, int KillProgress, int TalkProgress, int PickupProgress)> entries)
    {
        _entries.Clear();
        foreach (var (key, state, killProgress, talkProgress, pickupProgress) in entries)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            QuestCatalog.TryGet(key, out var def);
            _entries[key] = new QuestEntry
            {
                Key            = key,
                State          = state,
                Definition     = def,
                KillProgress   = killProgress,
                TalkProgress   = talkProgress,
                PickupProgress = pickupProgress,
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
    // SC-QUEST-OBJ-F-RESYNC (2026-05-13) — catalog rebuilt against the real
    // DS1 quest-key roster `siegefx quests audit` mined from each region's
    // conversations.gas. Replaces the wiki-guessed Ch.I-IX entries from the
    // first F slice; only 3 of those guesses (`Quest_for_Gyorn`,
    // `quest_edgaar_basement`, `quest_purify_temple`) matched real keys.
    //
    // Design decisions baked in here:
    //  - One catalog row per AUTHORED activate_quest string. The `key,N`
    //    staged-suffix syntax DS1 ships (`quest_destroy_gom2,1`,
    //    `quest_for_gyorn,1`, `quest_find_merik,1`, `quest_fort_kroth2,1`)
    //    is preserved verbatim — the runtime doesn't try to model `,N`
    //    as a separate stage index, it just treats each suffix variant
    //    as its own quest entry. Catalog stays a flat dict; the journal
    //    UI shows each beat as a row of its own.
    //  - `_mp` multiplayer twins are intentionally NOT included. Phase 21
    //    targets SP; multiplayer quest variants land in a later phase.
    //  - TalkTargetTemplate is set to the conversation-key-derived NPC
    //    template name (the F-AUDIT output's "conversation_<NPC>" line is
    //    the source). Where the conversation key doesn't obviously map to
    //    an NPC template (e.g. `conversation_tent2_a` — a tent location
    //    rather than a person), TalkTargetTemplate is left empty until
    //    SC-QUEST-OBJ-C/D wire pickup/reach which is the real credit path.
    //  - NextQuestKey chaining is set only for confirmed staged pairs
    //    (`key` -> `key,1`). Main-quest chapter chaining (Ch.I -> Ch.II
    //    -> ...) is NOT wired here — that ordering ships as activate_quest
    //    on each subsequent NPC's dialogue tree, so DS1 dialogue authors
    //    drive the chain naturally without us hard-coding chapter order.
    static readonly Dictionary<string, QuestDefinition> _defs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // ─── Phase 20a stubs (still wired by the existing Norick / Edgaar
            // dialogue trees in fh_r1's conversations.gas). KEPT verbatim so the
            // first-region receipt path doesn't break. The capital-Q Quest_for_Gyorn
            // is the DS1-authored key — Norick activates it from his pickup_speech
            // node — and the audit confirms it COVERED. ─────────────────────────
            ["Quest_for_Gyorn"] = new QuestDefinition
            {
                Key                 = "Quest_for_Gyorn",
                ScreenName          = "Aid the Farmhouse",
                KillTargetTemplate  = "krug",
                KillCountGoal       = 3,
                ObjectiveText       = "Kill 3 krug raiding the Farmhouse.",
                NextQuestKey        = "quest_for_gyorn,1",
            },
            ["quest_edgaar_basement"] = new QuestDefinition
            {
                Key                 = "quest_edgaar_basement",
                ScreenName          = "Edgaar's Basement",
                KillTargetTemplate  = "krug",
                KillCountGoal       = 5,
                ObjectiveText       = "Clear 5 krug from Edgaar's basement.",
            },

            // ─── SC-QUEST-OBJ-F-RESYNC — real DS1 keys from `siegefx quests audit`.
            // Sourced from each region's conversations.gas; ordering below follows
            // a rough Ch.I→IX walk through Stonebridge → Glitterdelve → Wesrin →
            // Castle Ehb → Chamber of Stars → Gom. NextQuestKey only set for
            // confirmed staged pairs (key → key,1).

            // Ch.I follow-up (Skartis on path2crypts continues Quest_for_Gyorn)
            ["quest_for_gyorn,1"] = new QuestDefinition
            {
                Key                 = "quest_for_gyorn,1",
                ScreenName          = "Aid the Farmhouse — Continued",
                TalkTargetTemplate  = "skartis",
                TalkCountGoal       = 1,
                ObjectiveText       = "Continue toward Stonebridge; speak with Skartis at the crypts path.",
            },

            // Ch.II Stonebridge North — Gyorn sends the player to find the Overseer
            ["quest_gyorn_seek_overseer"] = new QuestDefinition
            {
                Key                 = "quest_gyorn_seek_overseer",
                ScreenName          = "Seek the Overseer",
                TalkTargetTemplate  = "overseer",
                TalkCountGoal       = 1,
                ObjectiveText       = "Speak with the Overseer about the krug raids.",
            },
            // Ch.II Stonebridge — open the north gate
            ["quest_open_gate"] = new QuestDefinition
            {
                Key                 = "quest_open_gate",
                ScreenName          = "Open the North Gate",
                TalkTargetTemplate  = "guard3",
                TalkCountGoal       = 1,
                ObjectiveText       = "Persuade the guard to open the North Gate.",
            },
            // Ch.II side — Ella's sister message
            ["quest_sister_message"] = new QuestDefinition
            {
                Key                 = "quest_sister_message",
                ScreenName          = "A Sister's Message",
                TalkTargetTemplate  = "ella",
                TalkCountGoal       = 1,
                ObjectiveText       = "Deliver Ella's sister's message.",
            },
            // Ch.II Glitterdelve aftermath — Torg's beat
            ["quest_torg_seek_overseer"] = new QuestDefinition
            {
                Key                 = "quest_torg_seek_overseer",
                ScreenName          = "Carry Torg's Findings",
                TalkTargetTemplate  = "torg",
                TalkCountGoal       = 1,
                ObjectiveText       = "Speak with Torg in the depths of the Dwarven Mines.",
            },
            // Ch.II side — Free Torg from his captors
            ["quest_free_torg"] = new QuestDefinition
            {
                Key                 = "quest_free_torg",
                ScreenName          = "Free Torg",
                TalkTargetTemplate  = "gloern",
                TalkCountGoal       = 1,
                ObjectiveText       = "Gloern reports Torg's been captured — find and free him.",
            },

            // Ch.III Wesrin Cross — Ibsen sends you after Merik
            ["quest_find_merik"] = new QuestDefinition
            {
                Key                 = "quest_find_merik",
                ScreenName          = "Find Merik",
                TalkTargetTemplate  = "ibsen",
                TalkCountGoal       = 1,
                ObjectiveText       = "Find the wizard Merik beyond Wesrin Cross.",
                NextQuestKey        = "quest_find_merik,1",
            },
            // Staged follow-up — Jewlynna continues the search
            ["quest_find_merik,1"] = new QuestDefinition
            {
                Key                 = "quest_find_merik,1",
                ScreenName          = "Find Merik — Continued",
                TalkTargetTemplate  = "jewlynna",
                TalkCountGoal       = 1,
                ObjectiveText       = "Speak with Jewlynna; she's seen Merik recently.",
            },
            // Ch.III — Reinforce Fortress Kroth (Ibsen's second beat)
            ["quest_fort_kroth"] = new QuestDefinition
            {
                Key                 = "quest_fort_kroth",
                ScreenName          = "Reinforce Fortress Kroth",
                TalkTargetTemplate  = "ibsen",
                TalkCountGoal       = 1,
                ObjectiveText       = "Hold Fortress Kroth against the goblin assault.",
            },
            // Ch.V — Fortress Kroth recurs; legionnaire's beat
            ["quest_fort_kroth2,1"] = new QuestDefinition
            {
                Key                 = "quest_fort_kroth2,1",
                ScreenName          = "Reinforce Fortress Kroth — Phase II",
                TalkTargetTemplate  = "legionnaire1",
                TalkCountGoal       = 1,
                ObjectiveText       = "Speak with the legionnaire at Fortress Kroth.",
            },
            // Ch.III side — apprentice books
            ["quest_apprentice_books"] = new QuestDefinition
            {
                Key                 = "quest_apprentice_books",
                ScreenName          = "Apprentice's Books",
                TalkTargetTemplate  = "apprentice",
                TalkCountGoal       = 1,
                ObjectiveText       = "Help the apprentice recover the missing books.",
            },
            // Ch.III side — Orlov's ice dungeon (Homeless Blacksmith parallel)
            ["quest_ice_dungeon"] = new QuestDefinition
            {
                Key                 = "quest_ice_dungeon",
                ScreenName          = "Orlov's Ice Cellar",
                TalkTargetTemplate  = "orlov",
                TalkCountGoal       = 1,
                ObjectiveText       = "Help Orlov clear the frost beasts from his cellar.",
            },

            // Ch.IV — Confront the bandit boss
            ["quest_kill_bandits"] = new QuestDefinition
            {
                Key                 = "quest_kill_bandits",
                ScreenName          = "Confront the Bandit Boss",
                // FIXME(SC-QUEST-OBJ-E): conversation source is `tent2_a` (a tent
                // location, not an NPC template). Real credit is the bandit-boss
                // kill — switch to per-spawn-id KILL-NAMED when E lands and the
                // boss's authored template name is verified.
                KillTargetTemplate  = "bandit",
                KillCountGoal       = 4,
                ObjectiveText       = "Defeat the bandits holding the temple.",
            },
            // Ch.IV — Purify the Temple (this key was the only pre-RESYNC catalog
            // row that already matched DS1's authored key, so kept as-is)
            ["quest_purify_temple"] = new QuestDefinition
            {
                Key                 = "quest_purify_temple",
                ScreenName          = "Purify the Temple",
                KillTargetTemplate  = "bandit",
                KillCountGoal       = 4,
                ObjectiveText       = "Cleanse the temple of bandit defilement.",
            },
            // Ch.IV — Purify the Temple cleanup leg (post-boss talk)
            ["quest_purify_temple_2"] = new QuestDefinition
            {
                Key                 = "quest_purify_temple_2",
                ScreenName          = "Purify the Temple — Aftermath",
                TalkTargetTemplate  = "azunite_scholar",
                TalkCountGoal       = 1,
                ObjectiveText       = "Speak with the Azunite scholar after clearing the temple.",
            },
            // Ch.IV — Recover Merik's Staff (Merik NIS sequence in Lost Cathedral)
            ["quest_merik_staff"] = new QuestDefinition
            {
                Key                 = "quest_merik_staff",
                ScreenName          = "Merik's Staff",
                // SC-QUEST-OBJ-C + D composite: pickup the staff fires a
                // progress toast on grab (PickupCountGoal=1 auto-completes
                // the pickup leg), and the talk-with-deliver gate then
                // requires the staff to still be in inventory when the
                // player turns it in to Merik. NOTE: because PickupCountGoal
                // is 1 the pickup leg auto-completes the entry, so the talk
                // leg today is a redundant secondary path - DS1's intended
                // shape is "grab AND return," not "grab OR return." Real
                // multi-stage gating lands when the catalog grows a stage
                // model (SC-QUEST-OBJ-F-RESYNC's deferred decision). For
                // now the entry exercises both Register* hooks.
                PickupTargetTemplate = "merik_staff",
                PickupCountGoal      = 1,
                TalkTargetTemplate   = "merik",
                TalkCountGoal        = 1,
                DeliverItemTemplate  = "merik_staff",
                ObjectiveText        = "Recover Merik's Warding Staff and return it to him.",
            },
            // SC-QUEST-OBJ-C smoke-test entry — single pickup gate against a
            // template that actually exists in fh_r1's inventory.gas, so the
            // receipt path can fire in the FH-only test region. The real DS1
            // pickup quests (Merik's Staff above, Ordus' Axe, Book Return)
            // target items in regions the player has to travel to first.
            ["quest_grab_fireshot"] = new QuestDefinition
            {
                Key                  = "quest_grab_fireshot",
                ScreenName           = "Test Pickup — Fireshot",
                PickupTargetTemplate = "spell_fireshot",
                PickupCountGoal      = 1,
                ObjectiveText        = "Pick up the Fireshot scroll in the basement.",
            },

            // Ch.V side — Tower of Refuge / water dungeon (Gregor)
            ["quest_water_dungeon"] = new QuestDefinition
            {
                Key                 = "quest_water_dungeon",
                ScreenName          = "The Flooded Dungeon",
                TalkTargetTemplate  = "gregor",
                TalkCountGoal       = 1,
                ObjectiveText       = "Help Gregor clear the flooded dungeon.",
            },

            // Ch.VI — Subdue the village (the Droog parley, Tarish)
            ["quest_subdue_village"] = new QuestDefinition
            {
                Key                 = "quest_subdue_village",
                ScreenName          = "Subdue the Village",
                TalkTargetTemplate  = "tarish",
                TalkCountGoal       = 1,
                ObjectiveText       = "Negotiate passage with Tarish.",
            },

            // Ch.VII — Journey to Castle Ehb (Nonataya gives this from the parley)
            ["quest_journey_castle"] = new QuestDefinition
            {
                Key                 = "quest_journey_castle",
                ScreenName          = "Journey to Castle Ehb",
                TalkTargetTemplate  = "nonataya",
                TalkCountGoal       = 1,
                ObjectiveText       = "Travel to Castle Ehb.",
            },
            // Ch.VII — Slay the dragon (Goquua's quest)
            ["quest_slay_dragon"] = new QuestDefinition
            {
                Key                 = "quest_slay_dragon",
                ScreenName          = "Slay the Ancient Dragon",
                TalkTargetTemplate  = "goquua",
                TalkCountGoal       = 1,
                ObjectiveText       = "Defeat the Ancient Dragon of Rathe.",
            },
            // Ch.VII — Find the King (Lord Bolingar)
            ["quest_find_king"] = new QuestDefinition
            {
                Key                 = "quest_find_king",
                ScreenName          = "Find the King",
                TalkTargetTemplate  = "lord_bolingar",
                TalkCountGoal       = 1,
                ObjectiveText       = "Find King Konreid in the depths of Castle Ehb.",
            },

            // Ch.VIII/IX — King Konreid sends you to destroy Gom
            ["quest_destroy_gom"] = new QuestDefinition
            {
                Key                 = "quest_destroy_gom",
                ScreenName          = "Destroy Gom",
                TalkTargetTemplate  = "king",
                TalkCountGoal       = 1,
                ObjectiveText       = "Confront Gom in the Chamber of Stars.",
                NextQuestKey        = "quest_destroy_gom2,1",
            },
            // Ch.IX final — second-form Gom confrontation
            ["quest_destroy_gom2,1"] = new QuestDefinition
            {
                Key                 = "quest_destroy_gom2,1",
                ScreenName          = "Vanquish the Seck",
                // FIXME(SC-QUEST-OBJ-E + SC-GOM-TWO-PHASE): the actual credit
                // path is Gom's death-script spawning Super Gom (HP 8710 →
                // 11800). Until E + GOM-TWO-PHASE land, this is a TALK gate.
                TalkTargetTemplate  = "gom",
                TalkCountGoal       = 1,
                ObjectiveText       = "Defeat Gom and end the Seck Resurgence.",
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

    /// <summary>SC-QUEST-OBJ-C — pickup objective. Template-name substring
    /// (case-insensitive) matched against the just-picked-up item's
    /// resolved template name. Empty disables the pickup path; multiple
    /// objective types can coexist on one definition (e.g. a quest with
    /// both a pickup gate and a follow-up talk credit). Most pickup
    /// objectives are name-exact uniques like <c>spell_fireshot</c> or
    /// <c>merik_staff</c> — substring is a safety net for templates that
    /// ship with suffixes (e.g. quest item promoted across regions).</summary>
    public string PickupTargetTemplate { get; init; } = "";

    /// <summary>How many distinct pickup events credit the quest. Defaults
    /// to 0 so a definition that doesn't set PickupTargetTemplate also
    /// won't credit. Simple "recover X" objectives use 1; collect-N
    /// patterns set N.</summary>
    public int    PickupCountGoal      { get; init; }

    /// <summary>SC-QUEST-OBJ-D — deliver objective. Composite of pickup +
    /// talk: the quest's <see cref="TalkTargetTemplate"/> is the
    /// receiving NPC, and DeliverItemTemplate names the item that must
    /// be in the player's inventory at talk time for the credit to fire.
    /// When set, the talk path gates on the item being held; when empty,
    /// talk credits unconditionally (the plain TALK path). Substring
    /// match same as PickupTargetTemplate.</summary>
    public string DeliverItemTemplate  { get; init; } = "";
}
