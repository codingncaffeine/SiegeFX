using System.Numerics;
using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;

namespace SiegeFX.Runtime.Render;

// ── SC-ANIM-SFX — animation sound notes reach the actor's triggers ──
// DS1's chore skrits watch the blender for ANIMEVENT_SFX_1..4 (the SFX1..SFX4
// notes a clip authors) and post WE_ANIM_SFX(n) to the actor itself; the
// template's [template_triggers] answer with receive_world_message
// ("we_anim_sfx", n) → call_sfx_script(...). The Elddim / Stonebridge /
// Glacern blacksmith's hammering fidget carries SFX1 at 62.5 %, and his row
// plays "blacksmith_hammer" (sparks + s_e_env_hammer_anvil). Our skrit
// bridge's UpdateBlender reports no events, so nothing ever posted the
// message and he hammered in silence. The draw clock owns clip time here, so
// the advance loop reports each crossed note instead.
//
// Local on every machine, like DS1's blender: a co-op host and client each
// animate the actor and each play the sound — nothing goes over the wire.
public sealed partial class RenderHost
{
    private readonly List<int> _animSfxScratch = new(4);

    /// <summary>Post WE_ANIM_SFX(n) to <paramref name="s"/> for every SFX
    /// note its clip passed while its clock went from <paramref name="before"/>
    /// to <paramref name="after"/>. The hero is left out: its swing/cast
    /// sounds run off the attack system's own note handling.</summary>
    private void DispatchAnimSfxNotes(ActorRenderState s, PrsAnimation clip, double before, double after)
    {
        if (s.IsPlayer || clip.Notes.Count == 0 || after <= before) return;
        _animSfxScratch.Clear();
        AnimNotes.SfxCrossed(clip.Notes, clip.AnimLength, before, after, _animSfxScratch);
        if (_animSfxScratch.Count == 0) return;
        uint scid = s.Actor.Instance.Scid;
        foreach (int n in _animSfxScratch)
        {
            _actorBus?.Post("we_anim_sfx", scid, scid, n, 0);
            _triggerRuntime?.PostInboundMessage(scid, "we_anim_sfx", n);
        }
    }

    /// <summary>A trigger's call_sfx_script. When the trigger is a live
    /// actor's own ([template_triggers]: the blacksmith's hammer), the script
    /// runs where the actor stands NOW and in its facing, so offset(x,y,z)
    /// lands in front of / beside it as authored. Region triggers and
    /// emitters (no actor with that SCID) keep their placement position.</summary>
    internal void OnTriggerCallSfxScript(string scriptName, IReadOnlyList<string>? args, Vector3 origin, uint ownerScid)
    {
        if (ownerScid != 0)
            foreach (var s in _actors)
            {
                if (s.IsPlayer || s.Actor.Instance.Scid != ownerScid) continue;
                var xf = s.CurrentTransform;
                // Actors face their local +Z (the authored-facing convention).
                var fwd = Vector3.TransformNormal(Vector3.UnitZ, xf);
                _sfxRuntime?.Spawn(scriptName,
                    SiegeFX.Core.Sfx.SfxContext.At(xf.Translation) with { OwnerForward = fwd }, args);
                return;
            }
        OnTriggerCallSfxScript(scriptName, args, origin);
    }
}
