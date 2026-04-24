using System.Collections.Generic;

namespace SiegeFX.Core.Skrit;

/// <summary>The host surface the Skrit VM talks to for everything it can't handle locally:
/// external constants (WS_INTRO, MSG_ONBUTTONPRESS, ...), external free-calls (PostWorldMessage),
/// member access on host objects (worldState.CurrentState), script-to-script invocation, and
/// declarative state transitions. A no-op implementation is provided for tests and the fuzz
/// harness — Phase 8d will plug in the actor-backed bridge.</summary>
public interface IHostBridge
{
    SkritValue GetExtern(string name);
    void SetExtern(string name, SkritValue value);
    SkritValue CallExtern(string name, SkritValue[] args);
    SkritValue GetMember(SkritValue receiver, string member);
    void SetMember(SkritValue receiver, string member, SkritValue value);
    SkritValue CallMember(SkritValue receiver, string member, SkritValue[] args);
    void SetState(string stateName);
}

/// <summary>Default bridge that returns null for every lookup and silently accepts writes.
/// Useful for testing the VM's own semantics without needing a real host.</summary>
public sealed class NullHostBridge : IHostBridge
{
    public SkritValue GetExtern(string name) => SkritValue.Null;
    public void SetExtern(string name, SkritValue value) { }
    public SkritValue CallExtern(string name, SkritValue[] args) => SkritValue.Null;
    public SkritValue GetMember(SkritValue receiver, string member) => SkritValue.Null;
    public void SetMember(SkritValue receiver, string member, SkritValue value) { }
    public SkritValue CallMember(SkritValue receiver, string member, SkritValue[] args) => SkritValue.Null;
    public void SetState(string stateName) { }
}
