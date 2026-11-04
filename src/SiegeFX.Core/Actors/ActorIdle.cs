using System.Numerics;
using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Actors;

/// <summary>SC-IDLE-FIDGET — which chore an undirected actor idles in.
///
/// In DS1 an idle actor whose mind has <c>actor_auto_fidgets</c> (the actor
/// base template sets it true) runs job_fidget / the talk jobs'
/// <c>StartFidgetIfRequired$</c>: chore_fidget is re-queued on every
/// WE_ANIM_DONE, so the fidget loop IS the idle. chore_default only shows
/// between chores. We idle on chore_default because for nearly every actor
/// it is a breathing loop, but some templates author it as a single frozen
/// frame and put the real idle in chore_fidget: the blacksmith's
/// <c>a_c_gbn_bs_fs2_ds</c> is one 0.08 s frame, his <c>fs2_dsf</c> is the
/// hammering loop (its note fires the "blacksmith_hammer" sfx trigger).
/// Idling on clip 0 left him standing rigid at the anvil.</summary>
public static class ActorIdle
{
    /// <summary>The chore name to idle in, or null for chore_default.
    /// An authored <c>initial_chore</c> wins; otherwise chore_fidget when
    /// chore_default is a static pose, the fidget clip actually moves, and the
    /// mind doesn't turn auto-fidgeting off.</summary>
    public static string? PickIdleChore(string? initialChore, PrsAnimation? defaultClip,
                                        PrsAnimation? fidgetClip, bool autoFidgets) =>
        PickIdleChore(initialChore,
            defaultIsStatic: defaultClip is not null && IsStaticPose(defaultClip),
            fidgetMoves: fidgetClip is not null && !IsStaticPose(fidgetClip),
            autoFidgets);

    /// <summary>The same decision over precomputed clip facts.</summary>
    public static string? PickIdleChore(string? initialChore, bool defaultIsStatic,
                                        bool fidgetMoves, bool autoFidgets)
    {
        if (!string.IsNullOrWhiteSpace(initialChore)) return initialChore.Trim();
        return autoFidgets && defaultIsStatic && fidgetMoves ? "chore_fidget" : null;
    }

    public static bool IsStaticPose(PrsAnimation clip) => IsStaticPose(clip.RootKeys, clip.BoneKeys);

    /// <summary>True when no track in the clip changes: every rotation and
    /// position key of the root and of each bone equals that track's first.</summary>
    public static bool IsStaticPose(PrsAnimation.KeyList? root, IReadOnlyList<PrsAnimation.KeyList?> bones)
    {
        if (!IsStaticTrack(root)) return false;
        foreach (var b in bones)
            if (!IsStaticTrack(b)) return false;
        return true;
    }

    static bool IsStaticTrack(PrsAnimation.KeyList? keys)
    {
        if (keys is null) return true;
        const float eps = 1e-4f;
        for (int i = 1; i < keys.RotKeys.Count; i++)
        {
            // q and -q are the same rotation.
            float dot = MathF.Abs(Quaternion.Dot(keys.RotKeys[0].Rotation, keys.RotKeys[i].Rotation));
            if (dot < 1f - eps) return false;
        }
        for (int i = 1; i < keys.PosKeys.Count; i++)
            if (Vector3.DistanceSquared(keys.PosKeys[0].Position, keys.PosKeys[i].Position) > eps * eps)
                return false;
        return true;
    }
}
