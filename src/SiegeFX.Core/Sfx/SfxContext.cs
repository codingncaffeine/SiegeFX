using System.Numerics;

namespace SiegeFX.Core.Sfx;

/// <summary>Phase 21-SC-SPELL-VISUAL-F — bone-name resolver. Caster-side
/// skeleton lookup so DS1 logical bone names (<c>weapon_bone</c>,
/// <c>kill_bone</c>, <c>body_anterior</c>, <c>body_mid</c>,
/// <c>body_posterior</c>, <c>shield_bone</c>) land at the actual bone
/// world position, not at the single WeaponBonePos approximation.
/// Returns null when the bone is unknown so the caller can fall back.
/// Token <c>@</c> prefix is stripped before invocation.</summary>
public delegate Vector3? BoneResolver(string boneName);

/// <summary>Phase 21-SC-SPELL-VFX-2 — caster/target context handed to
/// <see cref="SfxRuntime.Spawn(string, in SfxContext, System.Collections.Generic.IReadOnlyList{string}?)"/>
/// so the VM can resolve DS1's anchor macros (<c>#SOURCE</c>,
/// <c>#SOURCE_POSITION</c>, <c>#TARGET</c>, <c>#TARGET_KB</c>) and
/// honour <c>sfx target $h source</c> / <c>sfx attach_point $h @bone source</c>.
///
/// <para><see cref="WeaponBonePos"/> stays as the legacy fallback (caster
/// shoulder/hand area) when no resolver is supplied or the bone is
/// unknown. <see cref="BoneResolver"/> is Phase 21-SC-SPELL-VISUAL-F's
/// upgrade — wired by the renderer from the live actor skeleton at
/// cast time, so <c>@kill_bone</c> resolves to <c>bip01_spine2</c>'s
/// world matrix translation instead of duplicating the weapon-bone
/// position.</para></summary>
public readonly record struct SfxContext(
    Vector3 SourcePos,
    Vector3 TargetPos,
    Vector3 WeaponBonePos,
    BoneResolver? Resolver = null)
{
    /// <summary>Convenience for legacy (region emitter) callers that only
    /// have a single anchor — both source and target collapse to it.</summary>
    public static SfxContext At(Vector3 origin) => new(origin, origin, origin);

    /// <summary>Resolve a DS1 logical bone name to a caster-skeleton world
    /// position. Strips the leading <c>@</c> if present, calls the
    /// configured <see cref="Resolver"/>, and falls back to the per-bone
    /// approximations or <see cref="WeaponBonePos"/> when the resolver
    /// returns null. Always returns a usable point so callers don't need
    /// a null-fallback dance.</summary>
    public Vector3 ResolveBone(string boneToken)
    {
        var name = boneToken;
        if (name.Length > 0 && name[0] == '@') name = name.Substring(1);
        if (Resolver is not null)
        {
            var p = Resolver(name);
            if (p.HasValue) return p.Value;
        }
        // Approximations when the resolver doesn't know the bone — pick a
        // fallback that reads as close-enough rather than dumping every
        // logical name onto the same WeaponBonePos. SourcePos is feet,
        // WeaponBonePos is hand-height; lerp between them for body bones
        // so the visual lands in the right neighborhood even on a
        // skeleton-less caster (region emitters etc.).
        switch (name.ToLowerInvariant())
        {
            case "weapon_bone":
            case "shield_bone":
                return WeaponBonePos;
            case "body_anterior":
            case "head":
                return SourcePos + new Vector3(0f, 1.6f, 0f);
            case "body_mid":
            case "kill_bone":
            case "kill":
                return SourcePos + new Vector3(0f, 1.0f, 0f);
            case "body_posterior":
                return SourcePos + new Vector3(0f, 0.6f, 0f);
            default:
                return WeaponBonePos;
        }
    }
}
