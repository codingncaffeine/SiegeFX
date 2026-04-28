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

        for (int i = 0; i < _instances.Count; i++)
        {
            var trig = _instances[i];
            if (!trig.IsActive) continue;
            EvaluateInstance(trig, ctx);
        }
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

            if (!anySatisfied) continue;

            // Actions: untagged actions (group 0) fire whenever any condition
            // satisfied. Tagged actions only fire when their group matches a
            // satisfied condition's group.
            for (int a = 0; a < row.Actions.Count; a++)
            {
                var act = row.Actions[a];
                if (act.Group != 0 && !firedGroups.Contains(act.Group)) continue;
                ScheduleAction(trig, row, act, ctx);
            }

            state.FiredOnce = true;
            // reset_duration acts as a per-row cooldown after firing. flip_flop swaps
            // active state; we record both effects here so the next Tick sees them.
            if (row.ResetDuration > 0f)
                state.NextEligibleAt = _now + row.ResetDuration;
            if (row.FlipFlop)
                trig.IsActive = !trig.IsActive;
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
                return ctx.PartyMemberWithinNode(trig.NodeGuid);
            case "party_member_entered_trigger_group":
            case "party_member_left_trigger_group":
                // These need region-occupancy bookkeeping (an "occupants set" per group).
                // Phase 10-SC-1 surfaces them as parsed but cold; the audit CLI flags the
                // gap so we can splinter the occupants tracker out as its own SC.
                return false;
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
                ctx.FadeNodes(act.Args);
                return;
            // when_false is a row modifier in DS1 — fires the next action only when the
            // condition is false. We surface it for parser coverage but treat it as a
            // no-op until SC-1b wires the negated-evaluation pass.
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
    public virtual bool PartyMemberWithinNode(uint nodeGuid) => false;
    public virtual void PostWorldMessage(string name, uint fromScid, uint toScid) { }
    public virtual void ChangeMood(string moodName) { }
    public virtual void SetInterestRadius(float radius) { }
    public virtual void FadeNodes(IReadOnlyList<string> args) { }
}
