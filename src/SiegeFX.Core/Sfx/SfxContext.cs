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
    /// <summary>SC-SPELL-AUDIT-2 — the spell's evaluated effect_duration in
    /// seconds (0 = none). Persistent plume emitters created WITHOUT an
    /// authored dur() default to this instead of the 0.35s one-shot default,
    /// so sustained clouds (acid gas) live as long as their gameplay effect.</summary>
    public float DefaultEmitterDuration { get; init; }

    /// <summary>SC-SPELL-CAST-WINDOW — the caster's remaining cast-clip
    /// window in seconds at the FIRE moment (0 = unknown). Bolt/line
    /// effects clamp their re-strike life to it: retail's zap exists only
    /// while the arm is extended, but the authored bolt_life (zap: 1s)
    /// outlived the hand-drop and read as fake. Region/ambient spawns
    /// leave it 0 and keep the authored duration cap.</summary>
    public float CastWindowSec { get; init; }

    /// <summary>SC-TARGET-BONES — bone resolver for the spell's TARGET
    /// skeleton (the caster-side <see cref="Resolver"/> counterpart), wired
    /// at cast time from the live target actor. Null = the per-bone height
    /// approximation table stays in effect.</summary>
    public BoneResolver? TargetResolver { get; init; }

    /// <summary>Resolve a bone on the TARGET: live skeleton when the
    /// resolver is wired, else <see cref="TargetPos"/> + the same
    /// approximation offsets <see cref="ResolveBone"/> uses.</summary>
    public Vector3 ResolveTargetBone(string boneToken)
    {
        var name = boneToken;
        if (name.Length > 0 && name[0] == '@') name = name.Substring(1);
        if (TargetResolver is not null)
        {
            var p = TargetResolver(name);
            if (p.HasValue) return p.Value;
        }
        float dy = name.ToLowerInvariant() switch
        {
            "body_anterior" or "head" => 1.6f,
            "body_mid" or "kill_bone" or "kill" => 1.0f,
            "body_posterior" => 0.6f,
            "weapon_bone" or "shield_bone" => 0.95f,
            _ => 0.9f,
        };
        return TargetPos + new Vector3(0f, dy, 0f);
    }

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
