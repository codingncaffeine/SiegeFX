using System.Globalization;
using System.Numerics;

namespace SiegeFX.Core.Actors;

/// <summary>Phase 10-SC-1 — drives every trigger placement in a region. One runtime
/// per region; a region's trigger placements register their <see cref="TriggerInstance"/>
/// here at spawn, the runtime walks them on each 20 Hz tick, evaluates their conditions
/// against a caller-supplied <see cref="TriggerContext"/>, and dispatches matched
/// actions back through the same context.
///
/// The runtime owns no scene-graph state of its own. It is a pure dispatcher: it
/// reads world position from the context and writes side effects through it. That
/// keeps the trigger system testable headless (the audit CLI plugs a fake context
/// with a simulated player position to drive coverage counts) and keeps the
/// renderer from needing to know about trigger semantics.</summary>
public sealed class TriggerRuntime
{
    /// <summary>TRUE in a multiplayer game — gates authored
    /// single_player=false rows (MP-only duplicates skip in SP) and
    /// multi_player=false rows (SP-only rows skip in MP).</summary>
    public bool IsMultiplayerSession { get; set; }

    readonly List<TriggerInstance> _instances = new();
    /// <summary>Replay queue for delayed actions. The 20 Hz tick is fast enough that
    /// "delay(1)" actions don't need a high-precision scheduler — we just stamp the
    /// dispatch time and check on each tick.</summary>
    readonly List<DelayedAction> _delayed = new();
    /// <summary>Wall-clock seconds since this runtime was created — used to schedule
    /// row-level reset_duration cooldowns and per-call <c>delay(N)</c> options.</summary>
    double _now;

    /// <summary>ALPHA-2 SEAM FOLD — how far a held volume condition's bounds
    /// grow so a party member straddling a box seam doesn't flap the row
    /// (enter → falling-edge reversal → enter …) on follower micro-steps.
    /// Mirrors the camera_fade enter/stay hysteresis precedent.</summary>
    const float VolumeHysteresisMargin = 0.35f;

    public IReadOnlyList<TriggerInstance> Instances => _instances;
    public double NowSeconds => _now;

    /// <summary>SC-WORLD-SCRIPT-PERSIST — capture every instance's mutable
    /// state (activation, fired one-shots, held edges, pending delayed
    /// actions with remaining time) for the save file.</summary>
    public List<Save.TriggerStateSnapshot> SnapshotStates()
    {
        var list = new List<Save.TriggerStateSnapshot>(_instances.Count);
        var byScid = new Dictionary<uint, Save.TriggerStateSnapshot>(_instances.Count);
        foreach (var t in _instances)
        {
            var s = new Save.TriggerStateSnapshot { Scid = t.Scid, IsActive = t.IsActive };
            for (int r = 0; r < t.Matrix.Rows.Count; r++)
            {
                ref var rs = ref t.RowStateAt(r);
                if (rs.FiredOnce) s.FiredRows.Add(r);
                if (rs.ConditionHeld) s.HeldRows.Add(r);
            }
            list.Add(s);
            byScid[t.Scid] = s;
        }
        foreach (var d in _delayed)
        {
            if (!byScid.TryGetValue(d.Trigger.Scid, out var owner)) continue;
            int flat = FlatActionIndex(d.Trigger, d.Action);
            if (flat >= 0)
                owner.Delayed.Add(new Save.DelayedActionSnapshot
                { FlatIndex = flat, RemainingSec = Math.Max(0, d.FireAt - _now) });
        }
        return list;
    }

    /// <summary>SC-WORLD-SCRIPT-PERSIST — apply a saved trigger-state set
    /// onto the (freshly booted) instances: matched by scid; instances the
    /// save doesn't mention keep their boot defaults. Pending delayed
    /// actions re-schedule with their remaining time; stale pendings for
    /// restored instances are dropped first. Returns the rows whose trigger
    /// isn't registered yet (region not streamed) so the caller can retry
    /// after each stream-in and merge them into the next capture — without
    /// this, saving far from a completed one-shot permanently erased it.</summary>
    public List<Save.TriggerStateSnapshot> RestoreStates(IReadOnlyList<Save.TriggerStateSnapshot> snaps)
    {
        var leftovers = new List<Save.TriggerStateSnapshot>();
        if (snaps.Count == 0) return leftovers;
        var byScid = new Dictionary<uint, TriggerInstance>(_instances.Count);
        foreach (var t in _instances) byScid[t.Scid] = t;
        var covered = new HashSet<uint>();
        foreach (var s in snaps) covered.Add(s.Scid);
        _delayed.RemoveAll(d => covered.Contains(d.Trigger.Scid));
        int applied = 0;
        foreach (var s in snaps)
        {
            if (!byScid.TryGetValue(s.Scid, out var t)) { leftovers.Add(s); continue; }
            t.IsActive = s.IsActive;
            // The save is authoritative BOTH directions: rows fired/held
            // AFTER the save must unlatch on an in-session load, or a
            // one-shot that fired post-save (pressure plate, gate opener)
            // stays FiredOnce in the restored world and can never fire
            // again — a progression softlock. Reset every row to defaults
            // first, then re-apply the snapshot's latches.
            for (int r = 0; r < t.Matrix.Rows.Count; r++)
            {
                ref var rs = ref t.RowStateAt(r);
                rs.FiredOnce = false;
                rs.ConditionHeld = false;
                rs.NextEligibleAt = 0;
            }
            foreach (var r in s.FiredRows)
                if (r >= 0 && r < t.Matrix.Rows.Count) t.RowStateAt(r).FiredOnce = true;
            foreach (var r in s.HeldRows)
            {
                if (r < 0 || r >= t.Matrix.Rows.Count) continue;
                // Fade-verb rows deliberately DON'T restore their held
                // edge: the fade refcounts they drive rebuild from scratch
                // at load, so the row must re-fire its rising edge for a
                // player standing inside the volume — a restored Held
                // suppressed it and left the cutaway in its boot state.
                bool fadeRow = t.Matrix.Rows[r].Actions.Any(a =>
                    a.Verb.StartsWith("fade_", StringComparison.OrdinalIgnoreCase)
                    || a.Verb.Equals("set_camera_fade_node", StringComparison.OrdinalIgnoreCase));
                if (fadeRow) continue;
                t.RowStateAt(r).ConditionHeld = true;
            }
            foreach (var d in s.Delayed)
            {
                var act = ActionAtFlatIndex(t, d.FlatIndex);
                if (act is not null)
                    _delayed.Add(new DelayedAction(t, act, _now + Math.Max(0, d.RemainingSec)));
            }
            applied++;
        }
        Console.WriteLine($"  [triggers] restored state for {applied}/{snaps.Count} saved instance(s)" +
                          (leftovers.Count > 0 ? $" ({leftovers.Count} pending un-streamed)" : ""));
        return leftovers;
    }

    static int FlatActionIndex(TriggerInstance t, TriggerCall action)
    {
        int i = 0;
        foreach (var row in t.Matrix.Rows)
            foreach (var a in row.Actions)
            {
                if (ReferenceEquals(a, action)) return i;
                i++;
            }
        return -1;
    }

    static TriggerCall? ActionAtFlatIndex(TriggerInstance t, int flat)
    {
        int i = 0;
        foreach (var row in t.Matrix.Rows)
            foreach (var a in row.Actions)
            {
                if (i == flat) return a;
                i++;
            }
        return null;
    }

    /// <summary>Phase 10-SC-1b — occupancy of named trigger_groups this tick.
    /// A row that authors <c>occupants_group = NAME</c> with a satisfied volume condition
    /// (party_member_within_sphere / _bounding_box / _node) marks NAME occupied; consumer
    /// rows (<c>party_member_entered/left_trigger_group(NAME, ...)</c>) compare current vs
    /// previous to fire on transitions.</summary>
    public IReadOnlyDictionary<string, bool> Occupants => _occupiedNow;
    Dictionary<string, bool> _occupiedNow = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, bool> _occupiedPrev = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Counts of every action verb fired since runtime start. Exposed so the
    /// audit CLI and runtime smoke tests can confirm dispatch coverage matches the
    /// authored <c>action*</c> mix.</summary>
    public IReadOnlyDictionary<string, int> ActionFireCounts => _actionFireCounts;
    readonly Dictionary<string, int> _actionFireCounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Counts of every condition verb that was satisfied at least once. Useful
    /// for the same audit CLI to report which verbs are actually getting hit by gameplay
    /// vs. which are wired-but-cold.</summary>
    public IReadOnlyDictionary<string, int> ConditionHitCounts => _conditionHits;
    readonly Dictionary<string, int> _conditionHits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Subset of <see cref="ActionFireCounts"/> that came from <c>when_false</c>
    /// dispatches. Lets the audit CLI demonstrate falling-edge wiring independently of
    /// the regular action fire count.</summary>
    public int WhenFalseFireCount => _whenFalseFires;
    int _whenFalseFires;

    public void Register(TriggerInstance trigger) => _instances.Add(trigger);

    /// <summary>Drain pending delayed actions, then evaluate every active row in every
    /// trigger. <paramref name="dt"/> advances the wall clock; <paramref name="ctx"/>
    /// answers world-state queries (player position, message bus) and receives action
    /// side effects.</summary>
    public void Tick(double dt, TriggerContext ctx)
    {
        _now += dt;

        // Fire any delayed actions whose time has come — keep the rest in place.
        for (int i = _delayed.Count - 1; i >= 0; i--)
        {
            if (_delayed[i].FireAt <= _now)
            {
                Dispatch(_delayed[i].Trigger, _delayed[i].Action, ctx, deferred: true);
                _delayed.RemoveAt(i);
            }
        }

        // Phase 10-SC-1b producer pass — recompute occupants_group membership before
        // any consumer row evaluates entered/left. Two-pass keeps the producer's volume
        // check authoritative for this tick: rotate now → prev (cheap, dicts are tiny),
        // then refill _occupiedNow from active producer rows.
        (_occupiedPrev, _occupiedNow) = (_occupiedNow, _occupiedPrev);
        _occupiedNow.Clear();
        for (int i = 0; i < _instances.Count; i++)
        {
            var trig = _instances[i];
            if (!trig.IsActive) continue;
            UpdateOccupantsForInstance(trig, ctx);
        }

        for (int i = 0; i < _instances.Count; i++)
        {
            var trig = _instances[i];
            if (!trig.IsActive) continue;
            EvaluateInstance(trig, ctx);
        }
    }

    void UpdateOccupantsForInstance(TriggerInstance trig, TriggerContext ctx)
    {
        var matrix = trig.Matrix;
        for (int r = 0; r < matrix.Rows.Count; r++)
        {
            var row = matrix.Rows[r];
            if (row.OccupantsGroup.Length == 0) continue;
            for (int c = 0; c < row.Conditions.Count; c++)
            {
                if (EvaluateOccupancyVolume(trig, row.Conditions[c], ctx))
                {
                    _occupiedNow[row.OccupantsGroup] = true;
                    break;
                }
            }
        }
    }

    /// <summary>Stateless variant of <see cref="EvaluateCondition"/> that handles only the
    /// volume verbs used to populate occupants_group. Side-effect free so the producer
    /// pass can run before condition-hit counters get bumped by the main eval loop.</summary>
    static bool EvaluateOccupancyVolume(TriggerInstance trig, TriggerCall cond, TriggerContext ctx)
    {
        switch (cond.Verb.ToLowerInvariant())
        {
            case "party_member_within_sphere":
                return TryFloatArg(cond, 0, out var radius)
                    && ctx.PartyMemberWithinSphere(trig.Position, radius);
            case "party_member_within_bounding_box":
                return TryFloatArg(cond, 0, out var hx)
                    && TryFloatArg(cond, 1, out var hy)
                    && TryFloatArg(cond, 2, out var hz)
                    && ctx.PartyMemberWithinBox(trig.Position, trig.Orientation, hx, hy, hz);
            case "party_member_within_node":
                return EvaluateWithinNode(cond, ctx);
            default:
                return false;
        }
    }

    /// <summary>DS1 signature: party_member_within_node(regionGuid, nodesection,
    /// nodelevel, nodeobject, boundaryMode). -1 wildcards match any value; the
    /// canonical all-wildcards form means "party member anywhere in the region"
    /// (the farmhouse cellar's group-producer trigger 0x01c0026b). The trailing
    /// boundary-mode string is ignored like the other volume verbs.</summary>
    static bool EvaluateWithinNode(TriggerCall cond, TriggerContext ctx)
    {
        if (cond.Args.Count == 0) return false;
        uint regionGuid = ParseScid(cond.Args[0]);
        if (regionGuid == 0) return false;
        int section = TryIntArg(cond, 1, out var s) ? s : -1;
        int level   = TryIntArg(cond, 2, out var l) ? l : -1;
        int obj     = TryIntArg(cond, 3, out var o) ? o : -1;
        return ctx.PartyMemberWithinNode(regionGuid, section, level, obj);
    }

    void EvaluateInstance(TriggerInstance trig, TriggerContext ctx)
    {
        var matrix = trig.Matrix;
        for (int r = 0; r < matrix.Rows.Count; r++)
        {
            var row = matrix.Rows[r];
            ref var state = ref trig.RowStateAt(r);

            // Authored single_player=false rows are MP-only (mood/fade/
            // quest duplicates for the MP flow); a single-player session
            // never evaluates them.
            if (!row.SinglePlayer && !IsMultiplayerSession) continue;
            if (!row.MultiPlayer && IsMultiplayerSession) continue;
            // single_shot rows latch: once they've fired, they never evaluate again.
            if (state.FiredOnce && row.SingleShot) continue;
            // reset_duration cooldown gate: row stays cold until the cooldown expires.
            if (state.NextEligibleAt > _now) continue;

            // Bucket conditions by group so action grouping can fire only the matching
            // tagged actions. Group 0 is the implicit "ungrouped" pool.
            // TriggerRow rarely exceeds 4 groups in shipped data; a small dict is fine.
            var firedGroups = new HashSet<int>();
            bool anySatisfied = false;
            bool gated = RowHasMessageGate(row);
            for (int c = 0; c < row.Conditions.Count; c++)
            {
                var cond = row.Conditions[c];
                // A "wait_for_every_message" volume condition is no trigger
                // of its own: it is checked when the row's message arrives.
                if (gated && IsMessageGate(cond)) continue;
                if (EvaluateCondition(trig, cond, ctx, ref state))
                {
                    if (gated && IsMessageCondition(cond) && !MessageGatesHold(trig, row, ctx, ref state))
                        continue;   // message consumed with the gate shut — DS1 drops it

                    Bump(_conditionHits, cond.Verb);
                    firedGroups.Add(cond.Group);
                    anySatisfied = true;
                }
            }

            // Phase 10-SC-1c — falling edge: row was satisfied last tick but isn't now.
            // Fire when_false actions; they're the only way DS1 author "fade in on leave".
            bool fallingEdge = state.ConditionHeld && !anySatisfied;
            // Review fold (2026-07-03) — RISING-edge dispatch. DS1's boundary
            // modes ("on_every_enter") fire once per entry, not continuously
            // while the volume holds. The old every-tick re-dispatch forced
            // downstream debounces (mood) and would strobe opposing fade rows
            // whose volumes overlap (fh_r1's hide-cellar box vs the doorway
            // reveal boxes). Level conditions now dispatch on the false→true
            // transition only; leaving and re-entering re-fires, matching
            // "on_every_enter". Caveat: a row mixing a held level condition
            // with a receive_world_message condition would consume messages
            // without dispatching while held — no shipped row does this.
            bool risingEdge = !state.ConditionHeld && anySatisfied;
            state.ConditionHeld = anySatisfied;

            if (!anySatisfied)
            {
                if (fallingEdge)
                {
                    for (int a = 0; a < row.Actions.Count; a++)
                    {
                        var act = row.Actions[a];
                        if (act.WhenFalse)
                        {
                            ScheduleAction(trig, row, act, ctx);
                            continue;
                        }
                        // SC-FADE-BOX-REVERSE — box-family fade templates
                        // (trigger_fade_nodes_box / trigger_fade_node_box /
                        // trigger_change_mood_box) hold their "out" fades only
                        // while a party member is inside the volume; DS1 ships
                        // a separate *_offonly_box variant for one-way fades,
                        // which is the tell. fh_r1's hide-the-cellar boxes
                        // restore hc_r1's sections on exit this way — nothing
                        // else ever reveals sections 3/4. "in" actions stay
                        // sticky (staged reveals never re-hide).
                        if (trig.AutoReverseFades &&
                            TryReverseFadeAction(act, out var reversedArgs))
                        {
                            Bump(_actionFireCounts, act.Verb);
                            ctx.FadeNodes(act.Verb, reversedArgs);
                        }
                    }
                }
                continue;
            }
            if (!risingEdge) continue;

            // Actions: untagged actions (group 0) fire whenever any condition
            // satisfied. Tagged actions only fire when their group matches a
            // satisfied condition's group. when_false actions sit out the
            // any-true frame — they only fire on the falling edge above.
            for (int a = 0; a < row.Actions.Count; a++)
            {
                var act = row.Actions[a];
                if (act.WhenFalse) continue;
                if (act.Group != 0 && !firedGroups.Contains(act.Group)) continue;
                ScheduleAction(trig, row, act, ctx);
            }

            state.FiredOnce = true;
            // reset_duration acts as a per-row cooldown after firing.
            // flip_flop is intentionally NOT toggling instance.IsActive: shipped DS1
            // rows pair `flip_flop = true` with explicit `when_false` actions whose
            // author intent is "enter activates, leave deactivates" — toggling the
            // instance off after enter would silence the falling-edge dispatch and
            // leave the leave-side action stranded. The actual alternation between
            // sides is already expressed by `when_false`; we treat flip_flop as a
            // documented-but-inert flag pending direct repro from a row that needs it.
            if (row.ResetDuration > 0f)
                state.NextEligibleAt = _now + row.ResetDuration;
        }
    }

    static bool IsMessageCondition(TriggerCall cond) =>
        cond.Verb.Equals("receive_world_message", StringComparison.OrdinalIgnoreCase);

    /// <summary>Boundary mode <c>wait_for_every_message</c> /
    /// <c>wait_for_first_message</c> (the last argument of the volume verbs):
    /// the condition gates the row's receive_world_message instead of firing
    /// it. The multiplayer_world teleporter buttons ship exactly this —
    /// receive_world_message("we_req_activate") + party_member_within_node(...,
    /// "wait_for_every_message") = "the button was pressed AND someone is on
    /// the platform" — and evaluated as two independent sources, stepping onto
    /// the platform launched it with no press.</summary>
    static bool IsMessageGate(TriggerCall cond)
    {
        if (IsMessageCondition(cond) || cond.Args.Count == 0) return false;
        var mode = cond.Args[^1].Trim().Trim('"');
        return mode.Equals("wait_for_every_message", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("wait_for_first_message", StringComparison.OrdinalIgnoreCase);
    }

    static bool RowHasMessageGate(TriggerRow row)
    {
        bool msg = false, gate = false;
        foreach (var c in row.Conditions)
        {
            if (IsMessageCondition(c)) msg = true;
            else if (IsMessageGate(c)) gate = true;
        }
        return msg && gate;
    }

    bool MessageGatesHold(TriggerInstance trig, TriggerRow row, TriggerContext ctx, ref TriggerRowState state)
    {
        foreach (var c in row.Conditions)
            if (IsMessageGate(c) && !EvaluateCondition(trig, c, ctx, ref state)) return false;
        return true;
    }

    void ScheduleAction(TriggerInstance trig, TriggerRow row, TriggerCall act, TriggerContext ctx)
    {
        var totalDelay = row.Delay + act.CallDelay;
        if (totalDelay <= 0f) Dispatch(trig, act, ctx, deferred: false);
        else _delayed.Add(new DelayedAction(trig, act, _now + totalDelay));
    }

    /// <summary>Dispatch every pending delayed action NOW, in scheduled order, and
    /// clear the queue. Skipping a cinematic means jumping to its outcome: the fh_r1
    /// intro trigger schedules its fade-to-black mood chain behind delay(36.6..42.5),
    /// and leaving that armed after an Esc-skip plays a full-screen blackout over
    /// live gameplay half a minute later. Scheduled order matters — the last
    /// mood_change in the timeline is the restore the world must settle on.</summary>
    public void FastForwardDelayed(TriggerContext ctx)
    {
        if (_delayed.Count == 0) return;
        var pending = _delayed.ToArray();
        _delayed.Clear();
        Array.Sort(pending, (a, b) => a.FireAt.CompareTo(b.FireAt));
        foreach (var d in pending)
            Dispatch(d.Trigger, d.Action, ctx, deferred: true);
    }

    bool EvaluateCondition(TriggerInstance trig, TriggerCall cond, TriggerContext ctx, ref TriggerRowState state)
    {
        switch (cond.Verb.ToLowerInvariant())
        {
            case "actor_within_sphere":
            {
                if (!TryFloatArg(cond, 0, out var radius)) return false;
                return ctx.AnyActorWithinSphere(trig.Position, radius, exceptScid: trig.Scid);
            }
            case "party_member_within_sphere":
            {
                if (!TryFloatArg(cond, 0, out var radius)) return false;
                // ALPHA-2 SEAM FOLD — sticky volumes: once a row is held,
                // its volume grows by a small margin so follower
                // micro-steps on a box seam can't flap enter/exit every
                // tick. The path2crypts stair boxes were flapping
                // hide → auto-reverse → hide per step, which yanked nav
                // triangles out from under the player (crawl/wedge) and
                // left the lower level half-revealed.
                if (state.ConditionHeld) radius += VolumeHysteresisMargin;
                return ctx.PartyMemberWithinSphere(trig.Position, radius);
            }
            case "go_within_sphere":
            {
                if (!TryFloatArg(cond, 0, out var radius)) return false;
                return ctx.AnyActorWithinSphere(trig.Position, radius, exceptScid: trig.Scid);
            }
            case "party_member_within_bounding_box":
            {
                if (!TryFloatArg(cond, 0, out var hx)) return false;
                if (!TryFloatArg(cond, 1, out var hy)) return false;
                if (!TryFloatArg(cond, 2, out var hz)) return false;
                if (state.ConditionHeld)
                {
                    hx += VolumeHysteresisMargin;
                    hy += VolumeHysteresisMargin;
                    hz += VolumeHysteresisMargin;
                }
                return ctx.PartyMemberWithinBox(trig.Position, trig.Orientation, hx, hy, hz);
            }
            case "party_member_within_node":
                return EvaluateWithinNode(cond, ctx);
            // ALPHA-2B — AABB variant of actor_within_sphere (dm_r8 uses it
            // to sense a scripted actor at a spot).
            case "actor_within_bounding_box":
            {
                if (!TryFloatArg(cond, 0, out var bx)) return false;
                if (!TryFloatArg(cond, 1, out var by)) return false;
                if (!TryFloatArg(cond, 2, out var bz)) return false;
                return ctx.AnyActorWithinBox(trig.Position, trig.Orientation, bx, by, bz, exceptScid: trig.Scid);
            }
            // ALPHA-2B — any game object in the box, optionally filtered by
            // scid (arg 3, 0 = any) and template name (arg 4, "" = any).
            // cr_r1's pressure-plate volumes ship (box, 0x0, "", mode).
            case "go_within_bounding_box":
            {
                if (!TryFloatArg(cond, 0, out var gx)) return false;
                if (!TryFloatArg(cond, 1, out var gy)) return false;
                if (!TryFloatArg(cond, 2, out var gz)) return false;
                uint scidFilter = cond.Args.Count > 3 ? ParseScid(cond.Args[3]) : 0;
                string nameFilter = cond.Args.Count > 4 ? cond.Args[4].Trim('"') : "";
                return ctx.AnyGoWithinBox(trig.Position, trig.Orientation, gx, gy, gz, scidFilter, nameFilter);
            }
            // ALPHA-2B — item-gated trigger: has_go_in_inventory("any", scid,
            // template). Shipped form checks the whole party for a quest item
            // (ds_r1's azunite_scholar_artifact_01).
            case "has_go_in_inventory":
            {
                if (cond.Args.Count < 3) return false;
                return ctx.PartyHasItemTemplate(cond.Args[2].Trim('"'));
            }
            case "party_member_entered_trigger_group":
            {
                if (cond.Args.Count == 0) return false;
                var name = cond.Args[0];
                bool now  = _occupiedNow.TryGetValue(name, out var n) && n;
                bool prev = _occupiedPrev.TryGetValue(name, out var p) && p;
                return now && !prev;
            }
            case "party_member_left_trigger_group":
            {
                if (cond.Args.Count == 0) return false;
                var name = cond.Args[0];
                bool now  = _occupiedNow.TryGetValue(name, out var n) && n;
                bool prev = _occupiedPrev.TryGetValue(name, out var p) && p;
                return prev && !now;
            }
            case "receive_world_message":
            {
                // Drained against the per-row inbox the runtime fills from message-bus
                // posts targeted at trig.Scid. Match is by message name (arg 0).
                if (cond.Args.Count == 0) return false;
                var name = cond.Args[0];
                return state.ConsumeReceivedMessage(name, MessageQualifier(cond));
            }
            default:
                return false;
        }
    }

    void Dispatch(TriggerInstance trig, TriggerCall act, TriggerContext ctx, bool deferred)
    {
        Bump(_actionFireCounts, act.Verb);
        if (act.WhenFalse) _whenFalseFires++;
        switch (act.Verb.ToLowerInvariant())
        {
            case "send_world_message":
            {
                if (act.Args.Count == 0) return;
                var name = act.Args[0];
                uint target = act.Args.Count > 1 ? ParseScid(act.Args[1]) : 0u;
                ctx.PostWorldMessage(name, trig.Scid, target);
                return;
            }
            case "mood_change":
                if (act.Args.Count > 0) ctx.ChangeMood(act.Args[0]);
                return;
            case "set_interest_radius":
                if (act.Args.Count > 0 && float.TryParse(act.Args[0].TrimEnd('f', 'F'),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
                    ctx.SetInterestRadius(r);
                return;
            case "fade_node":
            case "fade_nodes":
            case "fade_nodes_global":
                ctx.FadeNodes(act.Verb, act.Args);
                return;
            case "change_quest_state":
                // SC-QUEST-OBJ-B — DS1's REACH mechanism (Siege University
                // 209): region triggers flip quest state when the party
                // arrives (quest name + activate/deactivate/complete + step).
                ctx.ChangeQuestState(act.Args);
                return;
            case "call_sfx_script":
            {
                // action* = call_sfx_script("smoke_emitter", "color0(1,1,1)…").
                // Args[0] is the script name; any remaining args are the
                // caller-supplied [N] payload referenced from the script body.
                if (act.Args.Count == 0) return;
                var scriptName = act.Args[0];
                IReadOnlyList<string>? scriptArgs =
                    act.Args.Count > 1 ? act.Args.Skip(1).ToList() : null;
                ctx.CallSfxScript(scriptName, scriptArgs, trig.Position, trig.Scid);
                return;
            }
            // ALPHA-2B — runtime camera-flag flips on a specific snode:
            // set_camera_fade_node(guid, 0|1) / set_bounds_camera_node(guid, 0|1).
            // sd_r1 authors 121 of the former for its building cutaways.
            case "set_camera_fade_node":
            case "set_bounds_camera_node":
            {
                if (act.Args.Count < 2) return;
                uint node = ParseScid(act.Args[0]);
                bool on = act.Args[1].Trim().TrimEnd('f', 'F') != "0";
                if (node != 0) ctx.SetCameraNodeFlag(act.Verb, node, on);
                return;
            }
            // ALPHA-2B — change_actor_life(newLife, targetScid). Shipped
            // uses are scripted kills (0f).
            case "change_actor_life":
            {
                if (act.Args.Count < 2) return;
                if (!TryFloatArg(act, 0, out var newLife)) return;
                ctx.ChangeActorLife(ParseScid(act.Args[1]), newLife);
                return;
            }
            // SC-ENDGAME — the campaign's single victory verb (gom_super,
            // on we_exploded). Without it the game had no reachable end
            // state at all.
            case "victory_condition_met":
                ctx.VictoryConditionMet(act.Args.Count > 0 ? act.Args[0] : "");
                return;
        }
    }

    /// <summary>Called by the spawner-side bus bridge whenever a world message is
    /// targeted at a trigger SCID. The runtime looks up the trigger and stamps the
    /// message into every row's pending-message inbox; rows whose
    /// <c>receive_world_message</c> condition matches will consume the stamp on their
    /// next eval.</summary>
    /// <param name="data">SC-ANIM-SFX — the message's qualifier, matched
    /// against a <c>receive_world_message(name, N)</c> condition's N
    /// (WE_ANIM_SFX carries its SFX note number). Null = none.</param>
    public void PostInboundMessage(uint targetScid, string name, int? data = null)
    {
        // SC-TRIGGER-ARM — we_trigger_activate/_deactivate are ACTIVATION
        // messages, not inbox messages: they flip the instance's IsActive.
        // DS1 stages whole sequences behind them — the intro's "start
        // norick part" trigger ships start_active=false and is armed 85s
        // in; without the flip it never evaluates at all.
        bool arm = name.Equals("we_trigger_activate", StringComparison.OrdinalIgnoreCase);
        bool disarm = name.Equals("we_trigger_deactivate", StringComparison.OrdinalIgnoreCase);
        for (int i = 0; i < _instances.Count; i++)
        {
            if (_instances[i].Scid != targetScid) continue;
            if (arm) _instances[i].IsActive = true;
            else if (disarm) _instances[i].IsActive = false;
            else _instances[i].DepositMessage(name, data);
        }
    }

    /// <summary>The integer second argument of a receive_world_message
    /// condition, or null when it authors none.</summary>
    internal static int? MessageQualifier(TriggerCall cond) =>
        TryIntArg(cond, 1, out var q) ? q : null;

    static bool TryFloatArg(TriggerCall call, int idx, out float v)
    {
        v = 0;
        if (idx >= call.Args.Count) return false;
        var s = call.Args[idx].TrimEnd('f', 'F');
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    static bool TryIntArg(TriggerCall call, int idx, out int v)
    {
        v = 0;
        if (idx >= call.Args.Count) return false;
        return int.TryParse(call.Args[idx].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    }

    /// <summary>SC-FADE-BOX-REVERSE — build the exit-restore counterpart of a
    /// held fade action: same verb/target keys with the "out"/"out:black"
    /// mode token replaced by "in". Returns false for non-fade verbs and for
    /// actions that were already reveals (those stay sticky).</summary>
    static bool TryReverseFadeAction(TriggerCall act, out string[] reversedArgs)
    {
        reversedArgs = Array.Empty<string>();
        var verb = act.Verb.ToLowerInvariant();
        if (verb is not ("fade_node" or "fade_nodes" or "fade_nodes_global")) return false;
        for (int i = act.Args.Count - 1; i >= 1; i--)
        {
            var mode = act.Args[i].Trim().Trim('"').ToLowerInvariant();
            if (mode is "in" or "fade_in" or "instant") return false;
            if (mode is "out" or "out:black" or "fade_out")
            {
                reversedArgs = new string[act.Args.Count];
                for (int j = 0; j < act.Args.Count; j++) reversedArgs[j] = act.Args[j];
                reversedArgs[i] = "in";
                return true;
            }
        }
        return false;
    }

    static uint ParseScid(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0u;
    }

    static void Bump(Dictionary<string, int> dict, string key)
    {
        dict.TryGetValue(key, out var n);
        dict[key] = n + 1;
    }

    readonly record struct DelayedAction(TriggerInstance Trigger, TriggerCall Action, double FireAt);
}

/// <summary>Per-row mutable state. Lives on the <see cref="TriggerInstance"/> alongside
/// the (immutable, possibly shared) <see cref="TriggerMatrix"/>. Visible internal so the
/// runtime can mutate it directly via <c>ref</c> without an indirection per row.</summary>
public struct TriggerRowState
{
    public bool FiredOnce;
    public double NextEligibleAt;
    /// <summary>Phase 10-SC-1c — last tick's "any condition satisfied" answer for this
    /// row, used to detect the true→false falling edge that drives <c>when_false</c>
    /// actions.</summary>
    public bool ConditionHeld;
    /// <summary>Pending world-message names that landed on this row's owning trigger
    /// since the last evaluation. Each receive_world_message condition consumes its
    /// match. We cap to a tiny ring buffer because shipped DS1 trigger inboxes never
    /// run more than a couple deep between ticks.</summary>
    InboundMessages _inbox;

    /// <param name="data">SC-ANIM-SFX — the message's qualifier (WE_ANIM_SFX
    /// carries its note number 1-4); null when the poster sends none.</param>
    public void DepositMessage(string name, int? data = null) => _inbox.Push(new(name, data));
    /// <param name="qualifier">The condition's second argument
    /// (<c>receive_world_message("we_anim_sfx",1)</c>). A message and a
    /// condition that both carry one must agree; either side without one
    /// matches by name alone (the pre-qualifier behavior).</param>
    public bool ConsumeReceivedMessage(string name, int? qualifier = null) => _inbox.TryConsume(name, qualifier);

    readonly record struct Msg(string Name, int? Data)
    {
        public bool Matches(string name, int? qualifier) =>
            Name == name && (qualifier is null || Data is null || Data == qualifier);
    }

    struct InboundMessages
    {
        // Inline storage for up to 4 pending messages. Anything beyond that gets
        // dropped — DS1's tightest trigger chain is never that deep, and a runaway
        // would be a content authoring bug we'd rather catch as "missed expected
        // message" than silently grow an unbounded queue.
        Msg? _m0, _m1, _m2, _m3;

        public void Push(Msg m)
        {
            if (_m0 is null) { _m0 = m; return; }
            if (_m1 is null) { _m1 = m; return; }
            if (_m2 is null) { _m2 = m; return; }
            if (_m3 is null) { _m3 = m; return; }
            // Inbox full — silently drop. Future SC can add an overflow counter.
        }

        public bool TryConsume(string name, int? qualifier)
        {
            if (_m0 is { } a && a.Matches(name, qualifier)) { Compact(0); return true; }
            if (_m1 is { } b && b.Matches(name, qualifier)) { Compact(1); return true; }
            if (_m2 is { } c && c.Matches(name, qualifier)) { Compact(2); return true; }
            if (_m3 is { } d && d.Matches(name, qualifier)) { Compact(3); return true; }
            return false;
        }

        void Compact(int slot)
        {
            // Shift survivors down so unused slots stay null and Push fills front-first.
            switch (slot)
            {
                case 0: _m0 = _m1; _m1 = _m2; _m2 = _m3; _m3 = null; break;
                case 1: _m1 = _m2; _m2 = _m3; _m3 = null; break;
                case 2: _m2 = _m3; _m3 = null; break;
                case 3: _m3 = null; break;
            }
        }
    }
}

/// <summary>One live trigger placement: the SCID lifted from special.gas, the world
/// position composed from the placement record, the matrix shared with its template,
/// and per-row mutable state. Created by <see cref="ActorSpawner.SpawnTriggers"/>.</summary>
public sealed class TriggerInstance
{
    public uint Scid { get; }
    public uint NodeGuid { get; }
    public Vector3 Position { get; }
    public TriggerMatrix Matrix { get; }
    public bool IsActive;

    /// <summary>ALPHA-2 ORIENTED-BOX — the trigger's WORLD orientation (node
    /// rotation composed with the placement quaternion). Bounding-box
    /// conditions test in this frame: DS1 authors thin threshold strips
    /// rotated 90° (cr_r1's 2×2×7 cutaway lines), and testing them as
    /// world-axis-aligned boxes turned a 2u-deep crossing line into a
    /// 14u-deep dwell zone lying along the corridor.</summary>
    public Quaternion Orientation { get; }

    /// <summary>SC-FADE-BOX-REVERSE — the placement's template name; box-family
    /// fade templates get their held "out" fades restored on row falling edge.</summary>
    public string TemplateName { get; }
    public bool AutoReverseFades { get; }

    readonly TriggerRowState[] _rowStates;

    public TriggerInstance(uint scid, uint nodeGuid, Vector3 position, TriggerMatrix matrix, bool startActive,
                           string templateName = "", Quaternion? orientation = null)
    {
        Scid = scid;
        NodeGuid = nodeGuid;
        Position = position;
        Matrix = matrix;
        IsActive = startActive;
        Orientation = orientation ?? Quaternion.Identity;
        TemplateName = templateName;
        var tn = templateName.ToLowerInvariant();
        // The GLOBAL fade family is excluded: fade_nodes_global rows are one-way
        // world-state choreography, not held-volume cutaways. cr_r1 authors 48
        // "out:black" global rows (progressive crypt de-roofing as the party
        // descends) against a single authored "in" — nothing ever restores the
        // caps. Synthesized exit-reversal put the crypt roofs BACK each time the
        // player left a threshold box, so from a doorway the chase camera stared
        // at unlit cap exteriors in black fog — the "next room is pitch black /
        // not loaded" report (test 107). sd_r1 ships the same pattern (38:0).
        // The local box families keep the measured hold-while-inside reversal
        // (fh_r1 surface / hc_r1 basement, user-confirmed correct).
        AutoReverseFades = tn.Contains("box") &&
                           (tn.Contains("fade_node") || tn.Contains("change_mood")) &&
                           !tn.Contains("offonly") &&
                           !tn.Contains("global");
        _rowStates = new TriggerRowState[matrix.Rows.Count];
    }

    public ref TriggerRowState RowStateAt(int rowIndex) => ref _rowStates[rowIndex];

    public void DepositMessage(string name, int? data = null)
    {
        // Every row of the matrix sees the same inbox stream — DS1 trigger rows are
        // sibling listeners, not exclusive. Each row consumes (or doesn't) on its
        // own evaluation pass. SC-ANIM-SFX — only rows that listen for this message
        // get it: a row never consumes what it doesn't listen for, so a looping
        // anim's WE_ANIM_SFX(2) used to pile up in the SFX1 row's 4-slot inbox and,
        // once full, the inbox dropped the messages that row did want.
        for (int i = 0; i < _rowStates.Length; i++)
            if (Listens(Matrix.Rows[i], name, data)) _rowStates[i].DepositMessage(name, data);
    }

    static bool Listens(TriggerRow row, string name, int? data)
    {
        foreach (var c in row.Conditions)
        {
            if (!c.Verb.Equals("receive_world_message", StringComparison.OrdinalIgnoreCase)) continue;
            if (c.Args.Count == 0 || c.Args[0] != name) continue;
            var q = TriggerRuntime.MessageQualifier(c);
            if (q is null || data is null || q == data) return true;
        }
        return false;
    }
}

/// <summary>Inversion-of-control surface the trigger runtime uses to query world
/// state and post side effects. The runtime calls into this; concrete implementations
/// (the live RenderHost, or a fake for the audit CLI) override the methods that
/// matter. The base class itself is instantiable: every method is a no-op that
/// reports "nothing satisfies", which is the right answer for the audit CLI's
/// headless dry-tick and for any partially-wired host.</summary>
public class TriggerContext
{
    public virtual bool AnyActorWithinSphere(Vector3 center, float radius, uint exceptScid) => false;
    public virtual bool PartyMemberWithinSphere(Vector3 center, float radius) => false;
    public virtual bool PartyMemberWithinAabb(Vector3 center, float halfX, float halfY, float halfZ) => false;
    // ALPHA-2B — non-party volume checks + item gate + camera-flag / life actions.
    public virtual bool AnyActorWithinAabb(Vector3 center, float halfX, float halfY, float halfZ, uint exceptScid) => false;
    public virtual bool AnyGoWithinAabb(Vector3 center, float halfX, float halfY, float halfZ, uint scidFilter, string templateFilter) => false;
    // ALPHA-2 ORIENTED-BOX — box conditions carry the trigger's world
    // orientation. Defaults fall through to the axis-aligned variants so
    // headless/audit contexts that only override those keep functioning;
    // live hosts override these for correct rotated thresholds.
    public virtual bool PartyMemberWithinBox(Vector3 center, Quaternion orientation, float halfX, float halfY, float halfZ)
        => PartyMemberWithinAabb(center, halfX, halfY, halfZ);
    public virtual bool AnyActorWithinBox(Vector3 center, Quaternion orientation, float halfX, float halfY, float halfZ, uint exceptScid)
        => AnyActorWithinAabb(center, halfX, halfY, halfZ, exceptScid);
    public virtual bool AnyGoWithinBox(Vector3 center, Quaternion orientation, float halfX, float halfY, float halfZ, uint scidFilter, string templateFilter)
        => AnyGoWithinAabb(center, halfX, halfY, halfZ, scidFilter, templateFilter);
    public virtual bool PartyHasItemTemplate(string templateName) => false;
    public virtual void SetCameraNodeFlag(string verb, uint nodeGuid, bool on) { }
    public virtual void ChangeActorLife(uint scid, float newLife) { }
    /// <summary>SC-ENDGAME — gom_super authors
    /// <c>victory_condition_met("end_game")</c> on we_exploded; the live
    /// host rolls the ending. Headless contexts no-op.</summary>
    public virtual void VictoryConditionMet(string condition) { }
    /// <summary>DS1 semantics: is any party member currently standing in a
    /// terrain node of the region identified by <paramref name="regionGuid"/>
    /// whose fade-group keys match (with -1 wildcards)?</summary>
    public virtual bool PartyMemberWithinNode(uint regionGuid, int nodeSection, int nodeLevel, int nodeObject) => false;
    public virtual void PostWorldMessage(string name, uint fromScid, uint toScid) { }
    public virtual void ChangeMood(string moodName) { }
    public virtual void SetInterestRadius(float radius) { }
    /// <summary>Fade action dispatch. <paramref name="verb"/> is fade_node
    /// (single snode guid) or fade_nodes / fade_nodes_global
    /// (regionGuid, nodesection, nodelevel, nodeobject group addressing);
    /// the trailing arg is the fade mode ("out:black", "out", "in").</summary>
    public virtual void FadeNodes(string verb, IReadOnlyList<string> args) { }
    /// <summary>SC-QUEST-OBJ-B — change_quest_state trigger action
    /// (quest key + activate/deactivate/complete + optional step).</summary>
    public virtual void ChangeQuestState(IReadOnlyList<string> args) { }
    /// <summary>Phase 17-SC-G — emitter / spell trigger gateway. The default
    /// implementation is a no-op so the audit CLI's headless context can still
    /// drive trigger fan-out without dragging the Runtime project in. Live
    /// hosts override to dispatch into <c>SfxRuntime</c>.</summary>
    /// <param name="ownerScid">The trigger's SCID. For an actor's own
    /// [template_triggers] (the blacksmith's hammer sparks) the host plays the
    /// script where that actor stands now, not where it spawned.</param>
    public virtual void CallSfxScript(string scriptName, IReadOnlyList<string>? args, Vector3 origin, uint ownerScid) { }
}
