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
    readonly List<TriggerInstance> _instances = new();
    /// <summary>Replay queue for delayed actions. The 20 Hz tick is fast enough that
    /// "delay(1)" actions don't need a high-precision scheduler — we just stamp the
    /// dispatch time and check on each tick.</summary>
    readonly List<DelayedAction> _delayed = new();
    /// <summary>Wall-clock seconds since this runtime was created — used to schedule
    /// row-level reset_duration cooldowns and per-call <c>delay(N)</c> options.</summary>
    double _now;

    public IReadOnlyList<TriggerInstance> Instances => _instances;
    public double NowSeconds => _now;

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
                    && ctx.PartyMemberWithinAabb(trig.Position, hx, hy, hz);
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

            // single_shot rows latch: once they've fired, they never evaluate again.
            if (state.FiredOnce && row.SingleShot) continue;
            // reset_duration cooldown gate: row stays cold until the cooldown expires.
            if (state.NextEligibleAt > _now) continue;

            // Bucket conditions by group so action grouping can fire only the matching
            // tagged actions. Group 0 is the implicit "ungrouped" pool.
            // TriggerRow rarely exceeds 4 groups in shipped data; a small dict is fine.
            var firedGroups = new HashSet<int>();
            bool anySatisfied = false;
            for (int c = 0; c < row.Conditions.Count; c++)
            {
                var cond = row.Conditions[c];
                if (EvaluateCondition(trig, cond, ctx, ref state))
                {
                    Bump(_conditionHits, cond.Verb);
                    firedGroups.Add(cond.Group);
                    anySatisfied = true;
                }
            }

            // Phase 10-SC-1c — falling edge: row was satisfied last tick but isn't now.
            // Fire when_false actions; they're the only way DS1 author "fade in on leave".
            bool fallingEdge = state.ConditionHeld && !anySatisfied;
            state.ConditionHeld = anySatisfied;

            if (!anySatisfied)
            {
                if (fallingEdge)
                {
                    for (int a = 0; a < row.Actions.Count; a++)
                    {
                        var act = row.Actions[a];
                        if (!act.WhenFalse) continue;
                        ScheduleAction(trig, row, act, ctx);
                    }
                }
                continue;
            }

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

    void ScheduleAction(TriggerInstance trig, TriggerRow row, TriggerCall act, TriggerContext ctx)
    {
        var totalDelay = row.Delay + act.CallDelay;
        if (totalDelay <= 0f) Dispatch(trig, act, ctx, deferred: false);
        else _delayed.Add(new DelayedAction(trig, act, _now + totalDelay));
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
                return ctx.PartyMemberWithinAabb(trig.Position, hx, hy, hz);
            }
            case "party_member_within_node":
                return EvaluateWithinNode(cond, ctx);
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
                return state.ConsumeReceivedMessage(name);
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
                ctx.CallSfxScript(scriptName, scriptArgs, trig.Position);
                return;
            }
        }
    }

    /// <summary>Called by the spawner-side bus bridge whenever a world message is
    /// targeted at a trigger SCID. The runtime looks up the trigger and stamps the
    /// message into every row's pending-message inbox; rows whose
    /// <c>receive_world_message</c> condition matches will consume the stamp on their
    /// next eval.</summary>
    public void PostInboundMessage(uint targetScid, string name)
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            if (_instances[i].Scid != targetScid) continue;
            _instances[i].DepositMessage(name);
        }
    }

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

    public void DepositMessage(string name) => _inbox.Push(name);
    public bool ConsumeReceivedMessage(string name) => _inbox.TryConsume(name);

    struct InboundMessages
    {
        // Inline storage for up to 4 pending messages. Anything beyond that gets
        // dropped — DS1's tightest trigger chain is never that deep, and a runaway
        // would be a content authoring bug we'd rather catch as "missed expected
        // message" than silently grow an unbounded queue.
        string? _m0, _m1, _m2, _m3;

        public void Push(string name)
        {
            if (_m0 is null) { _m0 = name; return; }
            if (_m1 is null) { _m1 = name; return; }
            if (_m2 is null) { _m2 = name; return; }
            if (_m3 is null) { _m3 = name; return; }
            // Inbox full — silently drop. Future SC can add an overflow counter.
        }

        public bool TryConsume(string name)
        {
            if (_m0 is not null && _m0 == name) { Compact(0); return true; }
            if (_m1 is not null && _m1 == name) { Compact(1); return true; }
            if (_m2 is not null && _m2 == name) { Compact(2); return true; }
            if (_m3 is not null && _m3 == name) { Compact(3); return true; }
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

    readonly TriggerRowState[] _rowStates;

    public TriggerInstance(uint scid, uint nodeGuid, Vector3 position, TriggerMatrix matrix, bool startActive)
    {
        Scid = scid;
        NodeGuid = nodeGuid;
        Position = position;
        Matrix = matrix;
        IsActive = startActive;
        _rowStates = new TriggerRowState[matrix.Rows.Count];
    }

    public ref TriggerRowState RowStateAt(int rowIndex) => ref _rowStates[rowIndex];

    public void DepositMessage(string name)
    {
        // Every row of the matrix sees the same inbox stream — DS1 trigger rows are
        // sibling listeners, not exclusive. Each row consumes (or doesn't) on its
        // own evaluation pass.
        for (int i = 0; i < _rowStates.Length; i++) _rowStates[i].DepositMessage(name);
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
    public virtual void CallSfxScript(string scriptName, IReadOnlyList<string>? args, Vector3 origin) { }
}
