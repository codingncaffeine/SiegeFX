using System.Numerics;

namespace SiegeFX.Core.Sfx;

/// <summary>Phase 21-SC-SPELL-VFX-2 — caster/target context handed to
/// <see cref="SfxRuntime.Spawn(string, in SfxContext, System.Collections.Generic.IReadOnlyList{string}?)"/>
/// so the VM can resolve DS1's anchor macros (<c>#SOURCE</c>,
/// <c>#SOURCE_POSITION</c>, <c>#TARGET</c>, <c>#TARGET_KB</c>) and
/// honour <c>sfx target $h source</c> / <c>sfx attach_point $h @bone source</c>.
///
/// <para>Real DS1 carries actor GOIDs through the script and looks
/// up live skeletal bone matrices each tick. We approximate: world-space
/// caster + target positions plus a separate "weapon-bone" position
/// (caster shoulder/hand area) so the bolt fires from the player's hand
/// rather than from feet. When we wire real bone resolution later the
/// outer API doesn't change — just the value supplied for
/// <see cref="WeaponBonePos"/>.</para></summary>
public readonly record struct SfxContext(
    Vector3 SourcePos,
    Vector3 TargetPos,
    Vector3 WeaponBonePos)
{
    /// <summary>Convenience for legacy (region emitter) callers that only
    /// have a single anchor — both source and target collapse to it.</summary>
    public static SfxContext At(Vector3 origin) => new(origin, origin, origin);
}
