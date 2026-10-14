using System.Numerics;

namespace SiegeFX.Core.Actors;

/// <summary>What the hero does this tick about an order to act on something
/// out of reach (attack, cast, break).</summary>
public enum HeroApproachStep
{
    /// <summary>In reach with a clear line: act now.</summary>
    Act,
    /// <summary>Out of reach: keep walking toward the target. Path to the
    /// target itself (or, for a prop that blocks its own footprint, to a free
    /// spot by it); this check ends the walk as soon as the hero is in reach.</summary>
    WalkIntoReach,
    /// <summary>In reach but the line is blocked: keep closing on the target
    /// until it clears, as a chasing monster does.</summary>
    CloseIn,
    /// <summary>The walk can't get any closer (no path): drop the order.</summary>
    GiveUp,
}

/// <summary>The hero's walk-up to a clicked target, decided again every tick.
/// The order stands until it is carried out, the target is gone, the player
/// gives another order, or the target can't be reached; there is no distance
/// at which the hero gives up, so a long way round is walked like any other
/// path. The reach comes from the data (the weapon's or spell's range).</summary>
public static class HeroApproach
{
    /// <param name="distXZ">Plan-view distance from the hero to the target.</param>
    /// <param name="reach">The weapon's or spell's range.</param>
    /// <param name="lineBlocked">The line to the target is blocked (sight for
    /// a bow or spell, an obstacle for melee).</param>
    /// <param name="unreachable">The hero's last walk order found no path,
    /// not even a partial one.</param>
    public static HeroApproachStep Decide(float distXZ, float reach, bool lineBlocked, bool unreachable)
    {
        if (distXZ <= reach && !lineBlocked) return HeroApproachStep.Act;
        if (unreachable) return HeroApproachStep.GiveUp;
        return distXZ > reach ? HeroApproachStep.WalkIntoReach : HeroApproachStep.CloseIn;
    }

    /// <summary>The point on the straight line from <paramref name="from"/> to
    /// <paramref name="target"/> that stands just inside <paramref name="reach"/>
    /// (95% of it, so the in-reach test passes on arrival); <paramref name="from"/>
    /// when already that close.</summary>
    public static Vector3 PointInReach(Vector3 from, Vector3 target, float reach)
    {
        var d = new Vector3(target.X - from.X, 0f, target.Z - from.Z);
        float len = d.Length();
        if (len < 1e-3f) return from;
        float walk = MathF.Max(0f, len - reach * 0.95f);
        var dir = d / len;
        return new Vector3(from.X + dir.X * walk, target.Y, from.Z + dir.Z * walk);
    }
}
