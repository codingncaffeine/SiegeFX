using System.Numerics;
using SiegeFX.Core.Actors;

namespace SiegeFX.Runtime.Render;

/// <summary>Phase 10-SC-1 — bridges <see cref="TriggerRuntime"/> to live world state.
/// Owns no state of its own; queries the host's <c>_player</c>, <c>_actors</c>, and
/// <c>_actorBus</c> to answer condition checks, and posts world messages back through
/// the same bus that drives skrits. The trigger runtime would happily run with the
/// default no-op base context; this implementation is what makes shipped triggers
/// actually fire when the player walks into a sphere.</summary>
internal sealed class RenderHostTriggerContext : TriggerContext
{
    readonly RenderHost _host;

    public RenderHostTriggerContext(RenderHost host) { _host = host; }

    public override bool AnyActorWithinSphere(Vector3 center, float radius, uint exceptScid)
    {
        var r2 = radius * radius;
        foreach (var (scid, pos) in _host.EnumerateActorPositionsForTriggers())
        {
            if (scid == exceptScid) continue;
            var d = pos - center;
            if (d.LengthSquared() <= r2) return true;
        }
        return false;
    }

    public override bool PartyMemberWithinSphere(Vector3 center, float radius)
    {
        // Phase 26a — ANY party member (leader + recruited followers) inside
        // the volume satisfies the DS1 party_member_within_sphere trigger.
        float r2 = radius * radius;
        foreach (var p in _host.PartyMemberPositionsForTriggers())
        {
            var d = p - center;
            if (d.LengthSquared() <= r2) return true;
        }
        return false;
    }

    public override bool PartyMemberWithinAabb(Vector3 center, float halfX, float halfY, float halfZ)
    {
        foreach (var p in _host.PartyMemberPositionsForTriggers())
        {
            var d = p - center;
            if (MathF.Abs(d.X) <= halfX && MathF.Abs(d.Y) <= halfY && MathF.Abs(d.Z) <= halfZ)
                return true;
        }
        return false;
    }

    public override void PostWorldMessage(string name, uint fromScid, uint toScid)
    {
        // Route back into the same bus that drives skrits, with a zero-arg payload.
        // Triggers that target other triggers (action* sends, condition* receives)
        // ride this same channel — TriggerRuntime.PostInboundMessage stamps the inbox
        // for matrices addressed at the target SCID.
        _host.PostTriggerWorldMessage(name, fromScid, toScid);
    }

    public override void ChangeMood(string moodName)
    {
        _host.OnTriggerMoodChange(moodName);
    }

    public override void CallSfxScript(string scriptName, IReadOnlyList<string>? args, Vector3 origin)
    {
        _host.OnTriggerCallSfxScript(scriptName, args, origin);
    }

    public override void FadeNodes(string verb, IReadOnlyList<string> args)
    {
        _host.OnTriggerFadeNodes(verb, args);
    }

    public override bool PartyMemberWithinNode(uint regionGuid, int nodeSection, int nodeLevel, int nodeObject)
    {
        return _host.PlayerWithinNodeGroup(regionGuid, nodeSection, nodeLevel, nodeObject);
    }

    public override void ChangeQuestState(IReadOnlyList<string> args)
    {
        _host.OnTriggerChangeQuestState(args);
    }
}
