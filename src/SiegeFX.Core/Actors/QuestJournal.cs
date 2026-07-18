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

    /// <summary>SC-QUEST-UI-D — the conversation the player actually heard
    /// when this quest was accepted, one entry per spoken narrative line in
    /// order. Populated once at acceptance (see
    /// <see cref="QuestJournal.RecordDialogue"/>); drives the journal's
    /// "Show Dialogue" chronicle view. Empty for quests activated without a
    /// player-facing conversation (trigger/generator/narrator grants).</summary>
    public List<string> DialogueLog { get; } = new();
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

    /// <summary>SC-QUEST-UI-D — record the story text the player heard when a
    /// quest was accepted, for the journal's Show Dialogue view. First
    /// non-empty recording wins (DS1 logs the conversation as it happened and
    /// never overwrites it on a re-talk); a no-op when the key isn't in the
    /// journal or already carries a log.</summary>
    public void RecordDialogue(string key, IEnumerable<string> lines)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (!_entries.TryGetValue(key, out var entry) || entry.DialogueLog.Count > 0) return;
        foreach (var line in lines)
            if (!string.IsNullOrWhiteSpace(line)) entry.DialogueLog.Add(line.Trim());
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
            // SC-QUEST-OBJ-E — named-boss kills use the underscore-anchored
            // exact match (same rule as talk targets) so "gom" can't credit
            // off any goblin-shaped template; umbrella rows ("krug",
            // "bandit") keep the intentional substring behavior.
            bool killMatch = def.KillTargetExact
                ? IsTalkTargetMatch(deadTemplateName, def.KillTargetTemplate)
                : deadTemplateName.IndexOf(def.KillTargetTemplate,
                                           StringComparison.OrdinalIgnoreCase) >= 0;
            if (!killMatch) continue;

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

    /// <summary>SC-QUEST-TURNIN — true once <paramref name="key"/> is in the
    /// journal in the Completed state. Drives quest-state conversation
    /// selection (NPCs switch to their *_quest_complete lines).</summary>
    public bool IsCompleted(string key)
        => !string.IsNullOrWhiteSpace(key)
           && _entries.TryGetValue(key, out var e)
           && e.State == QuestState.Completed;

    /// <summary>SC-QUEST-TURNIN — authored <c>deactivate_quest*</c>: withdraw
    /// the entry from the journal entirely (DS1 uses it when a quest becomes
    /// moot, e.g. fort_kroth once events pass it by). Completed entries stay —
    /// deactivating history would erase the log the player already earned.</summary>
    public bool Deactivate(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (!_entries.TryGetValue(key, out var e)) return false;
        if (e.State == QuestState.Completed) return false;
        return _entries.Remove(key);
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
    /// <summary>SS-CUSTOM (GAME-4) — merge quest definitions from a map-local
    /// <c>quests/quests.gas</c> (the retail per-map quest file shape: a
    /// <c>[quests]</c> root whose children are quest blocks with
    /// <c>screen_name</c> and ordered <c>[*]</c> description states). Custom
    /// maps authored in SiegeSmith carry their own quests this way; merged
    /// entries overwrite same-key catalog rows so a total conversion can even
    /// re-text a shipped key. Returns the number of quests merged.</summary>
    public static int MergeFromGas(SiegeFX.Core.Assets.GasDocument doc)
    {
        static string Unquote(string s) => s.Trim().Trim('"');
        int merged = 0;
        foreach (var root in doc.Roots)
        {
            if (!root.Header.Equals("quests", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var q in root.Children)
            {
                if (string.IsNullOrWhiteSpace(q.Header)) continue;
                string screen = "", objective = "";
                foreach (var a in q.Attributes)
                    if (a.Name.Equals("screen_name", StringComparison.OrdinalIgnoreCase)) screen = Unquote(a.Value);
                foreach (var state in q.Children)
                {
                    foreach (var a in state.Attributes)
                        if (a.Name.Equals("description", StringComparison.OrdinalIgnoreCase) && objective.Length == 0)
                            objective = Unquote(a.Value);
                    if (objective.Length > 0) break;
                }
                _defs[q.Header] = new QuestDefinition
                {
                    Key = q.Header,
                    ScreenName = screen.Length > 0 ? screen : q.Header,
                    ObjectiveText = objective,
                };
                merged++;
            }
        }
        return merged;
    }

    static readonly Dictionary<string, QuestDefinition> _defs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // ─── Phase 20a stubs (still wired by the existing Norick / Edgaar
            // dialogue trees in fh_r1's conversations.gas). KEPT verbatim so the
            // first-region receipt path doesn't break. The capital-Q Quest_for_Gyorn
            // is the DS1-authored key — Norick activates it from his pickup_speech
            // node — and the audit confirms it COVERED. ─────────────────────────
            // SC-QUEST-CATALOG-POLISH (2026-05-15) — ScreenName +
            // ObjectiveText for every entry below pulled verbatim from
            // DS1's shipped `world/maps/map_world/quests/quests.gas`
            // (extracted via `siegefx tank extract`). Gameplay fields
            // (KillTargetTemplate / TalkTargetTemplate / etc.) are
            // SiegeFX-authored interpretations — they're not in DS1's
            // catalog, which is text+chapter only and relies on the
            // dialogue / trigger graph for activation+credit. Where
            // our gameplay doesn't match the canonical text's intent
            // (e.g. our Quest_for_Gyorn fires a kill goal but DS1's
            // stage-0 description is a TALK objective), the
            // discrepancy is flagged inline.
            //
            // FIXME(Quest_for_Gyorn-gameplay): DS1's stage 0 is "Seek
            // Norick's friend Gyorn in the town of Stonebridge." —
            // a pure TALK objective. The 3-krug-kill beat at the
            // Farmhouse is an unscripted opening fight with no quest
            // entry of its own; we model it as the stage-0 credit
            // here, which works gameplay-wise but diverges from
            // DS1's text-only journal entry. When a future slice
            // splits "implicit opening fight" from "first quest",
            // drop the kill goal and credit on talking to Gyorn.
            ["Quest_for_Gyorn"] = new QuestDefinition
            {
                Key                 = "Quest_for_Gyorn",
                ScreenName          = "Seek Gyorn in Stonebridge",
                KillTargetTemplate  = "krug",
                KillCountGoal       = 3,
                ObjectiveText       = "Seek Norick's friend Gyorn in the town of Stonebridge.",
                NextQuestKey        = "quest_for_gyorn,1",
            },
            ["quest_edgaar_basement"] = new QuestDefinition
            {
                Key                 = "quest_edgaar_basement",
                ScreenName          = "Clear Edgaar's Basement",
                KillTargetTemplate  = "krug",
                KillCountGoal       = 5,
                ObjectiveText       = "Clear the Krug from Edgaar's Basement, and gather supplies for the journey to Stonebridge.",
            },

            // ─── SC-QUEST-OBJ-F-RESYNC — real DS1 keys from `siegefx quests audit`.
            // Sourced from each region's conversations.gas; ordering below follows
            // a rough Ch.I→IX walk through Stonebridge → Glitterdelve → Wesrin →
            // Castle Ehb → Chamber of Stars → Gom. NextQuestKey only set for
            // confirmed staged pairs (key → key,1).

            // Ch.I follow-up (Skartis on path2crypts continues Quest_for_Gyorn).
            // DS1 quests.gas models this as order=1 of `quest_for_gyorn` itself;
            // SiegeFX splits stages into chained keys with `,N` suffix.
            ["quest_for_gyorn,1"] = new QuestDefinition
            {
                Key                 = "quest_for_gyorn,1",
                ScreenName          = "Seek Gyorn in Stonebridge",
                TalkTargetTemplate  = "skartis",
                TalkCountGoal       = 1,
                ObjectiveText       = "Seek Norick's friend Gyorn in the town of Stonebridge by using the old path through the Crypts.",
            },

            // Ch.II Stonebridge North — Gyorn sends the player to find the Overseer
            ["quest_gyorn_seek_overseer"] = new QuestDefinition
            {
                Key                 = "quest_gyorn_seek_overseer",
                ScreenName          = "Deliver Gyorn's Report",
                TalkTargetTemplate  = "overseer",
                TalkCountGoal       = 1,
                ObjectiveText       = "Deliver Gyorn's report to the Overseer in Glacern.",
            },
            // Ch.II Stonebridge — Town Guard sends the player to clear Glitterdelve
            ["quest_open_gate"] = new QuestDefinition
            {
                Key                 = "quest_open_gate",
                ScreenName          = "Clear Glitterdelve Pass",
                TalkTargetTemplate  = "guard3",
                TalkCountGoal       = 1,
                ObjectiveText       = "Clear the way to Glitterdelve for the Stonebridge militia.",
            },
            // Ch.II side — Ella's sister message
            ["quest_sister_message"] = new QuestDefinition
            {
                Key                 = "quest_sister_message",
                ScreenName          = "A Sister's Message",
                TalkTargetTemplate  = "mp_townfolk_female_01",
                TalkCountGoal       = 1,
                ObjectiveText       = "Deliver Ella's message to her sister Ada in Glacern.",
            },
            // Ch.II side — Ordus' Axe (DS1 key is the colorful internal name
            // `quest_drunkard_tower`; ScreenName is the canonical journal title).
            // Recover Ordus' axe from the basement of the Northern guard tower
            // in path2sd; deliver back to Ordus.
            ["quest_drunkard_tower"] = new QuestDefinition
            {
                Key                  = "quest_drunkard_tower",
                ScreenName           = "Ordus' Axe",
                PickupTargetTemplate = "ax_g_o_1h1b_low",
                PickupCountGoal      = 1,
                TalkTargetTemplate   = "ordus",
                TalkCountGoal        = 1,
                DeliverItemTemplate  = "ax_g_o_1h1b_low",
                ObjectiveText        = "Secure Ordus' axe from the Northern guard tower.",
            },
            // Ch.II Glitterdelve aftermath — Torg's beat
            ["quest_torg_seek_overseer"] = new QuestDefinition
            {
                Key                 = "quest_torg_seek_overseer",
                ScreenName          = "Report Torg's Findings",
                TalkTargetTemplate  = "torg",
                TalkCountGoal       = 1,
                ObjectiveText       = "Report Torg's findings to the Overseer in Glacern.",
            },
            // Ch.II side — Free Torg from his captors
            ["quest_free_torg"] = new QuestDefinition
            {
                Key                 = "quest_free_torg",
                ScreenName          = "Rescue Torg",
                TalkTargetTemplate  = "gloern",
                TalkCountGoal       = 1,
                ObjectiveText       = "Rescue Gloern's brother Torg from within the Dwarven mines.",
            },

            // Ch.III Wesrin Cross — Ibsen sends you after Merik
            ["quest_find_merik"] = new QuestDefinition
            {
                Key                 = "quest_find_merik",
                ScreenName          = "Quest for Merik",
                TalkTargetTemplate  = "ibsen",
                TalkCountGoal       = 1,
                ObjectiveText       = "Find Merik the Grand Mage.",
                NextQuestKey        = "quest_find_merik,1",
            },
            // Staged follow-up — Jewlynna narrows the search to the Ice Caves
            // (DS1 quests.gas models this as order=1 inside quest_find_merik).
            ["quest_find_merik,1"] = new QuestDefinition
            {
                Key                 = "quest_find_merik,1",
                ScreenName          = "Quest for Merik",
                TalkTargetTemplate  = "jewlynna",
                TalkCountGoal       = 1,
                ObjectiveText       = "Find Merik the Grand Mage in the Ice Caves north of Glacern.",
            },
            // Ch.III — Reinforce Fortress Kroth (Ibsen's second beat)
            ["quest_fort_kroth"] = new QuestDefinition
            {
                Key                 = "quest_fort_kroth",
                ScreenName          = "Reinforce Fortress Kroth",
                TalkTargetTemplate  = "ibsen",
                TalkCountGoal       = 1,
                ObjectiveText       = "Travel through the Ice Caves to reinforce the Legionnaires at Fortress Kroth.",
            },
            // Ch.V — Fortress Kroth recurs; legionnaire's beat. DS1 keys this as
            // `quest_fort_kroth2` (no comma) chapter-5; SiegeFX uses the
            // `,1`-suffix staged convention for the legionnaire follow-up.
            ["quest_fort_kroth2,1"] = new QuestDefinition
            {
                Key                 = "quest_fort_kroth2,1",
                ScreenName          = "Reinforce Fortress Kroth",
                TalkTargetTemplate  = "guard",
                TalkCountGoal       = 1,
                ObjectiveText       = "Defeat the necromancer besieging Fortress Kroth.",
            },
            // Ch.III side — Book Return (Ardun, the apprentice, sends the
            // player after two volumes of the Fedwyrr's Way trilogy in
            // Glacern). DS1 key kept the dev's "apprentice_books" internal
            // name; canonical ScreenName per quests.gas is "Book Return".
            ["quest_apprentice_books"] = new QuestDefinition
            {
                Key                 = "quest_apprentice_books",
                ScreenName          = "Book Return",
                TalkTargetTemplate  = "apprentice",
                TalkCountGoal       = 1,
                ObjectiveText       = "Find the first two volumes in the Fedwyrr's Way trilogy.",
            },
            // Ch.III side — Homeless Blacksmith (DS1 key `quest_ice_dungeon`
            // is the dev's internal name; canonical journal title per
            // quests.gas is "Homeless Blacksmith" — Orlov's cabin/cellar
            // is overrun, secure it for him).
            ["quest_ice_dungeon"] = new QuestDefinition
            {
                Key                 = "quest_ice_dungeon",
                ScreenName          = "Homeless Blacksmith",
                TalkTargetTemplate  = "orlov",
                TalkCountGoal       = 1,
                ObjectiveText       = "Secure Orlov's cabin and cellar in the wilderness north of Glacern.",
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
                ObjectiveText       = "Confront the Bandit Boss to protect the Traveler's camp.",
            },
            // Ch.IV — Purify the Temple (DS1 stage 0)
            ["quest_purify_temple"] = new QuestDefinition
            {
                Key                 = "quest_purify_temple",
                ScreenName          = "Purify the Temple",
                KillTargetTemplate  = "bandit",
                KillCountGoal       = 4,
                ObjectiveText       = "Destroy the temple Guardian.",
            },
            // Ch.IV — Purify the Temple stage 2 (post-boss; place the holy
            // icon on the temple altar). DS1 quests.gas keys this as
            // `quest_purify_temple_2` (a separate top-level entry, not a
            // ,1 stage of the first) — kept as a distinct catalog row.
            ["quest_purify_temple_2"] = new QuestDefinition
            {
                Key                 = "quest_purify_temple_2",
                ScreenName          = "Purify the Temple",
                TalkTargetTemplate  = "peasant_male_old_02",
                TalkCountGoal       = 1,
                ObjectiveText       = "Place the holy icon on the Temple altar",
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
                //
                // Canonical description in DS1 quests.gas is the
                // (mis-spelled) "Retreive Merik's warding staff." —
                // preserved verbatim for journal fidelity.
                PickupTargetTemplate = "st_un_merik",
                PickupCountGoal      = 1,
                TalkTargetTemplate   = "merik",
                TalkCountGoal        = 1,
                DeliverItemTemplate  = "st_un_merik",
                ObjectiveText        = "Retreive Merik's warding staff.",
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

            // Ch.V side — Missing Treasure Hunters (DS1 key
            // `quest_water_dungeon` is the dev's internal name for the
            // tr_r2 flooded-tower content; the canonical journal title is
            // "Missing Treasure Hunters" per quests.gas).
            ["quest_water_dungeon"] = new QuestDefinition
            {
                Key                 = "quest_water_dungeon",
                ScreenName          = "Missing Treasure Hunters",
                TalkTargetTemplate  = "gregor",
                TalkCountGoal       = 1,
                ObjectiveText       = "Find out what became of Thayne's customers, and report your findings to Gregor.",
            },

            // Ch.VI — Subdue the Droog (parley with Tarish)
            ["quest_subdue_village"] = new QuestDefinition
            {
                Key                 = "quest_subdue_village",
                ScreenName          = "Subdue the Droog",
                TalkTargetTemplate  = "tarish",
                TalkCountGoal       = 1,
                ObjectiveText       = "Subdue the Droog Leadership in their village beyond the desert to the east.",
            },

            // Ch.VII — Journey to Castle Ehb (Nonataya gives this from the parley)
            ["quest_journey_castle"] = new QuestDefinition
            {
                Key                 = "quest_journey_castle",
                ScreenName          = "Journey to Castle Ehb",
                TalkTargetTemplate  = "nonataya",
                TalkCountGoal       = 1,
                ObjectiveText       = "Journey to Castle Ehb to prevent the Seck from capturing the secret chamber.",
            },
            // Ch.VII — Slay the Ancient Dragon of Rathe (Crusader Goquua's quest)
            ["quest_slay_dragon"] = new QuestDefinition
            {
                Key                 = "quest_slay_dragon",
                ScreenName          = "Slay the Ancient Dragon of Rathe",
                TalkTargetTemplate  = "crusader_goquua",
                TalkCountGoal       = 1,
                ObjectiveText       = "Slay the Ancient Dragon before the Seck can free him from Dragon's Rathe.",
            },
            // Ch.VII — Search for the King (Lord Bolingar's quest)
            ["quest_find_king"] = new QuestDefinition
            {
                Key                 = "quest_find_king",
                ScreenName          = "Search for the King",
                TalkTargetTemplate  = "lord_bolingar",
                TalkCountGoal       = 1,
                ObjectiveText       = "Find the King and secure Castle Ehb from the Seck.",
            },

            // Ch.VIII — The Chamber of Stars (King Konreid sends the
            // party to retrieve the artifacts from the Chamber). DS1
            // keys this as `quest_find_artifacts`. No dialogue in shipped
            // World.dsmap fires this via activate_quest=, so audit reports
            // it as catalog-only — DS1 likely activates it via the
            // King's conversation skrit / a presence trigger we haven't
            // surfaced yet. Catalogued for journal-display completeness.
            ["quest_find_artifacts"] = new QuestDefinition
            {
                Key                 = "quest_find_artifacts",
                ScreenName          = "The Chamber of Stars",
                TalkTargetTemplate  = "king",
                TalkCountGoal       = 1,
                ObjectiveText       = "Retrieve artifacts from the Chamber of Stars.",
            },

            // Ch.VIII/IX — Vanquish the Seck (King Konreid sends you
            // after Gom). DS1 stage 0 is "Find and destroy the remaining
            // Seck before they can free Gom." Stage 1 is the Gom
            // confrontation itself.
            ["quest_destroy_gom"] = new QuestDefinition
            {
                Key                 = "quest_destroy_gom",
                ScreenName          = "Vanquish the Seck",
                TalkTargetTemplate  = "king",
                TalkCountGoal       = 1,
                ObjectiveText       = "Find and destroy the remaining Seck before they can free Gom.",
                NextQuestKey        = "quest_destroy_gom2,1",
            },
            // Ch.IX final — second-form Gom confrontation. DS1 keys this
            // as `quest_destroy_gom2` (no comma) chapter-9; SiegeFX uses
            // the `,1`-suffix staged-quest convention.
            ["quest_destroy_gom2,1"] = new QuestDefinition
            {
                Key                 = "quest_destroy_gom2,1",
                ScreenName          = "Vanquish the Seck",
                // SC-ENDGAME — credit = the SECOND-form kill (gom's death
                // spawns gom_super; gom_super's authored template_triggers
                // also complete via change_quest_state, so this kill gate
                // is the belt to that suspenders).
                KillTargetTemplate  = "gom_super",
                KillCountGoal       = 1,
                ObjectiveText       = "Destroy the Seck Leader Gom.",
            },

            // ─── SC-QUEST-OBJ-F-MP — multiplayer-variant keys surfaced by
            // the 2026-05-15 quest audit against World.dsmap. Each `_mp`
            // key is a multiplayer-only fork of an existing SP entry — DS1
            // ships a parallel conversation tree for MP play. SiegeFX is
            // single-player-first, so these mirror their SP counterparts;
            // when MP mode lands we'll branch the dialogue resolver to
            // pick the right key by session mode rather than fork the
            // catalog runtime. Defined here so the audit no longer reports
            // them as MISSING.
            ["quest_gyorn_seek_overseer_mp"] = new QuestDefinition
            {
                Key                 = "quest_gyorn_seek_overseer_mp",
                ScreenName          = "Deliver Gyorn's Report",
                TalkTargetTemplate  = "overseer",
                TalkCountGoal       = 1,
                ObjectiveText       = "Deliver Gyorn's report to the Overseer in Glacern.",
            },
            ["quest_free_torg_mp"] = new QuestDefinition
            {
                Key                 = "quest_free_torg_mp",
                ScreenName          = "Rescue Torg",
                TalkTargetTemplate  = "gloern",
                TalkCountGoal       = 1,
                ObjectiveText       = "Rescue Gloern's brother Torg from within the Dwarven mines.",
            },
            ["quest_merik_staff_mp"] = new QuestDefinition
            {
                Key                  = "quest_merik_staff_mp",
                ScreenName           = "Merik's Staff",
                PickupTargetTemplate = "st_un_merik",
                PickupCountGoal      = 1,
                TalkTargetTemplate   = "merik",
                TalkCountGoal        = 1,
                DeliverItemTemplate  = "st_un_merik",
                ObjectiveText        = "Retreive Merik's warding staff.",
            },
            ["quest_find_king_mp"] = new QuestDefinition
            {
                Key                 = "quest_find_king_mp",
                ScreenName          = "Search for the King",
                TalkTargetTemplate  = "lord_bolingar",
                TalkCountGoal       = 1,
                ObjectiveText       = "Find the King and secure Castle Ehb from the Seck.",
            },
        };

    public static bool TryGet(string key, out QuestDefinition? def)
    {
        if (string.IsNullOrWhiteSpace(key)) { def = null; return false; }
        return _defs.TryGetValue(key, out def);
    }

    public static IReadOnlyDictionary<string, QuestDefinition> All => _defs;

    /// <summary>SC-ENDGAME — resolve an authored change_quest_state key to
    /// catalog keys. DS1 authors the no-comma base form
    /// ("quest_destroy_gom2") while staged catalog entries carry ",N"
    /// suffixes; a verbatim apply cold-created a parallel journal entry
    /// and left the real staged entry active forever.</summary>
    public static IReadOnlyList<string> ResolveKeyAliases(string key)
    {
        if (_defs.ContainsKey(key)) return new[] { key };
        var staged = _defs.Keys
            .Where(k => k.StartsWith(key + ",", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return staged.Count > 0 ? staged : new[] { key };
    }
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

    /// <summary>SC-QUEST-OBJ-E — when true, <see cref="KillTargetTemplate"/>
    /// matches with the underscore-anchored exact rule instead of substring.
    /// Set on named-boss rows ("gom") where substring would collide with
    /// unrelated template families ("goblin_*").</summary>
    public bool KillTargetExact { get; init; }
}
