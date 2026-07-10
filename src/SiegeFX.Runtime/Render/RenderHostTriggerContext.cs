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
        => PartyMemberWithinBox(center, Quaternion.Identity, halfX, halfY, halfZ);

    // ALPHA-2 ORIENTED-BOX — test in the trigger's authored frame. DS1's
    // threshold strips (cr_r1's 2×2×7 cutaway lines) rotate 90°; treating
    // them as world-axis-aligned turned a thin crossing line into a deep
    // dwell zone lying along the corridor.
    public override bool PartyMemberWithinBox(Vector3 center, Quaternion orientation, float halfX, float halfY, float halfZ)
    {
        var inv = Quaternion.Inverse(orientation);
        foreach (var p in _host.PartyMemberPositionsForTriggers())
        {
            var d = Vector3.Transform(p - center, inv);
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

    // ALPHA-2B — the six previously-undispatched authored verbs (oriented
    // variants; the Aabb names route through with identity rotation).
    public override bool AnyActorWithinAabb(Vector3 center, float halfX, float halfY, float halfZ, uint exceptScid)
        => AnyActorWithinBox(center, Quaternion.Identity, halfX, halfY, halfZ, exceptScid);

    public override bool AnyActorWithinBox(Vector3 center, Quaternion orientation, float halfX, float halfY, float halfZ, uint exceptScid)
    {
        var inv = Quaternion.Inverse(orientation);
        foreach (var (scid, pos) in _host.EnumerateActorPositionsForTriggers())
        {
            if (scid == exceptScid) continue;
            var d = Vector3.Transform(pos - center, inv);
            if (MathF.Abs(d.X) <= halfX && MathF.Abs(d.Y) <= halfY && MathF.Abs(d.Z) <= halfZ)
                return true;
        }
        return false;
    }

    public override bool AnyGoWithinAabb(Vector3 center, float halfX, float halfY, float halfZ, uint scidFilter, string templateFilter)
        => AnyGoWithinBox(center, Quaternion.Identity, halfX, halfY, halfZ, scidFilter, templateFilter);

    public override bool AnyGoWithinBox(Vector3 center, Quaternion orientation, float halfX, float halfY, float halfZ, uint scidFilter, string templateFilter)
    {
        return _host.AnyGoWithinBoxForTriggers(center, orientation, halfX, halfY, halfZ, scidFilter, templateFilter);
    }

    public override bool PartyHasItemTemplate(string templateName)
    {
        return _host.PartyHasItemTemplateForTriggers(templateName);
    }

    public override void SetCameraNodeFlag(string verb, uint nodeGuid, bool on)
    {
        _host.OnTriggerSetCameraNodeFlag(verb, nodeGuid, on);
    }

    public override void ChangeActorLife(uint scid, float newLife)
    {
        _host.OnTriggerChangeActorLife(scid, newLife);
    }

    public override void ChangeQuestState(IReadOnlyList<string> args)
    {
        _host.OnTriggerChangeQuestState(args);
    }
}
