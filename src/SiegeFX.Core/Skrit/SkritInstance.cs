using System;
using System.Collections.Generic;

namespace SiegeFX.Core.Skrit;

/// <summary>One running Skrit script attached to one host object (an actor, a GO, a
/// "bot"). Owns the per-instance globals via its <see cref="SkritVm"/>, the current state
/// name, and the list of live scheduled chores. The host drives the instance through
/// <see cref="Tick"/> each logic frame and <see cref="Dispatch"/> when events fire.
///
/// Phase 8d ships a bounded slice: scheduled chores (<c>at ( N frames|seconds )</c>)
/// and event handlers. Declarative transitions with guards / <c>__if__</c> and the full
/// world-message trigger matrix are stubbed — the runtime treats triggers like named
/// events for now.</summary>
public sealed class SkritInstance
{
    public SkritProgram Program { get; }
    public SkritVm Vm { get; }
    public IHostBridge Host { get; }

    /// <summary>Logic tick rate used to resolve <c>frames</c> units in <c>at ( N frames )</c>
    /// — DS1 ran its scripted systems at 20 fps. Real time keeps ticking at whatever the
    /// host calls <see cref="Tick"/> with; we just use this to convert frame counts into
    /// seconds when a chore is registered.</summary>
    public const double FramesPerSecond = 20.0;

    public string? CurrentState { get; private set; }
    string? _pendingState; // captured from SetState inside a handler; applied between handlers

    readonly List<ScheduledChore> _chores = new();
    readonly Dictionary<string, SkritStateDecl> _stateDecls = new();

    public IReadOnlyList<ScheduledChore> Chores => _chores;

    public SkritInstance(SkritProgram program, IHostBridge host)
    {
        Program = program;
        Host = host;
        Vm = new SkritVm(program, host); // runs @__init__ on ctor

        // Index state decls once so EnterState doesn't linear-scan TopLevels at 20 Hz × N actors.
        foreach (var top in program.Script.TopLevels)
            if (top is SkritStateDecl s) _stateDecls[s.Name] = s;
    }

    /// <summary>Activate the startup state (first <c>startup state X$</c> in the script)
    /// and fire any header trigger body attached to it. Call once after construction.</summary>
    public void Start()
    {
        foreach (var top in Program.Script.TopLevels)
        {
            if (top is SkritStateDecl s && s.IsStartup)
            {
                EnterState(s.Name);
                ApplyPendingState();
                return;
            }
        }
    }

    /// <summary>Fire a named event on this instance. Resolution order: current state's
    /// event, current state's trigger, top-level <c>@event/Name</c>, top-level
    /// <c>@trigger/Name</c>. Returns <c>true</c> if a handler actually ran.</summary>
    public bool Dispatch(string eventName, params SkritValue[] args)
    {
        string? key = null;
        if (CurrentState is not null)
        {
            var stateKey = $"{CurrentState}/event/{eventName}";
            if (Program.ChunksByName.ContainsKey(stateKey)) key = stateKey;
            if (key is null)
            {
                var trigKey = $"{CurrentState}/trigger/{eventName}";
                if (Program.ChunksByName.ContainsKey(trigKey)) key = trigKey;
            }
        }
        if (key is null)
        {
            var topKey = $"@event/{eventName}";
            if (Program.ChunksByName.ContainsKey(topKey)) key = topKey;
        }
        if (key is null)
        {
            var topTrig = $"@trigger/{eventName}";
            if (Program.ChunksByName.ContainsKey(topTrig)) key = topTrig;
        }
        if (key is null) return false;

        Vm.Run(key, args);
        ApplyPendingState();
        return true;
    }

    /// <summary>Advance the scheduler by <paramref name="dt"/> seconds; fire any chores
    /// whose delays have elapsed. Chores are one-shot by default (DS1 semantics — a
    /// re-entering state restarts them). <c>OnUpdate$</c> is also dispatched here if the
    /// current state or the script defines it.</summary>
    public void Tick(double dt)
    {
        // OnUpdate$ first. If it SetStates, EnterState has already cleared + repopulated
        // _chores, so the chore pass below would decrement Remaining on brand-new chores
        // that were meant to get their first tick *next* frame. Snapshot state and skip
        // the chore pass when the dispatch switched us.
        var stateBefore = CurrentState;
        Dispatch("OnUpdate$", SkritValue.FromFloat((float)dt));
        if (CurrentState != stateBefore) return;

        for (int i = 0; i < _chores.Count; i++)
        {
            var c = _chores[i];
            c.Remaining -= dt;
            _chores[i] = c;
        }
        // Fire all expired chores in insertion order, then compact.
        for (int i = 0; i < _chores.Count; )
        {
            if (_chores[i].Remaining <= 0)
            {
                var fire = _chores[i];
                _chores.RemoveAt(i);
                Vm.Run(fire.ChunkKey);
                ApplyPendingState();
                if (CurrentState != fire.OwningState) break; // state switched mid-tick; queue was rebuilt
            }
            else i++;
        }
    }

    /// <summary>Host bridges call this from inside a handler to request a state change.
    /// The actual swap happens between handler invocations so the currently-running
    /// bytecode doesn't have its scope/chore list yanked out from under it.</summary>
    public void RequestSetState(string stateName) => _pendingState = stateName;

    // ApplyPendingState is the single drainer. EnterState never recurses into it — it just
    // populates _pendingState via its own Dispatch calls, and this loop picks them up.
    void ApplyPendingState()
    {
        while (_pendingState is not null)
        {
            var next = _pendingState;
            _pendingState = null;
            EnterState(next);
        }
    }

    void EnterState(string stateName)
    {
        if (!_stateDecls.TryGetValue(stateName, out var state)) return; // unknown target — binder diagnosed it
        CurrentState = stateName;
        _chores.Clear();

        // Register one-shot chores. `at ( N frames )` converts via FramesPerSecond; any
        // other unit string (seconds, the default) is interpreted as seconds.
        foreach (var m in state.Body)
        {
            if (m is SkritScheduledBlock sb)
            {
                double delay = EvalConstCount(sb.CountExpr);
                if (string.Equals(sb.Unit, "frames", StringComparison.OrdinalIgnoreCase))
                    delay /= FramesPerSecond;
                _chores.Add(new ScheduledChore(
                    ChunkKey: $"{stateName}/sched/{sb.Name}",
                    Remaining: delay,
                    OwningState: stateName));
            }
        }

        // Run any header-trigger body (inline `startup state X$ trigger OnY$ { ... }`).
        // Nested SetStates land in _pendingState and are drained by the caller of EnterState.
        var headerKey = $"{stateName}/header";
        if (Program.ChunksByName.ContainsKey(headerKey)) Vm.Run(headerKey);

        // Run OnEnterState$ if the state or script defines it.
        var enterKey = $"{stateName}/event/OnEnterState$";
        if (Program.ChunksByName.ContainsKey(enterKey)) Vm.Run(enterKey);
    }

    /// <summary>Best-effort constant-fold for <c>at ( N ... )</c> counts. Most shipped
    /// skrits use literal ints or floats; anything more complex bails to 0 and logs.</summary>
    static double EvalConstCount(SkritExpr e) => e switch
    {
        SkritIntLit i => i.Value,
        SkritFloatLit f => f.Value,
        SkritUnaryExpr { Op: "-" } u => -EvalConstCount(u.Operand),
        _ => 0.0,
    };

    public struct ScheduledChore
    {
        public string ChunkKey;
        public double Remaining;
        public string OwningState;
        public ScheduledChore(string ChunkKey, double Remaining, string OwningState)
        { this.ChunkKey = ChunkKey; this.Remaining = Remaining; this.OwningState = OwningState; }
    }
}
