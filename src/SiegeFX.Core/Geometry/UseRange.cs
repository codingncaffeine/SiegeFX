using System.Numerics;

namespace SiegeFX.Core.Geometry;

/// <summary>Walk-up-and-use reach for placed world objects. DS1 measures an
/// object's <c>[aspect] use_range</c> from the edge of its footprint, not
/// from its origin: a door's origin is its hinge, and its closed leaf is a
/// path blocker about one leaf-length wide, so the nearest spot a hero can
/// stand is already that far from the hinge.</summary>
public static class UseRange
{
    /// <summary>Default <c>use_range</c> when a door template authors none.</summary>
    public const float DefaultDoorUseRange = 1.5f;

    /// <summary>Height band (world units) inside which a use counts as on the
    /// same floor as the object.</summary>
    public const float DoorMaxDy = 3f;

    /// <summary>XZ footprint radius of a placed prop: the farthest corner of
    /// its local AABB (Y collapsed to the middle) from the placement origin
    /// in world XZ. Same measure the nav-obstacle stamping uses, so "in use
    /// range" agrees with where the blocker lets the hero stand.</summary>
    public static float FootprintRadius(Matrix4x4 world, Vector3 localMin, Vector3 localMax)
    {
        float midY = (localMin.Y + localMax.Y) * 0.5f;
        var origin = world.Translation;
        float maxR2 = 0f;
        Span<Vector3> corners = stackalloc Vector3[4]
        {
            new(localMin.X, midY, localMin.Z),
            new(localMax.X, midY, localMin.Z),
            new(localMax.X, midY, localMax.Z),
            new(localMin.X, midY, localMax.Z),
        };
        foreach (var c in corners)
        {
            var w = Vector3.Transform(c, world);
            float dx = w.X - origin.X, dz = w.Z - origin.Z;
            maxR2 = MathF.Max(maxR2, dx * dx + dz * dz);
        }
        return MathF.Sqrt(maxR2);
    }

    /// <summary>True when <paramref name="actor"/> is close enough to use a
    /// door placed at <paramref name="world"/>: XZ distance from the hinge no
    /// more than the leaf footprint plus <paramref name="useRange"/>, and on
    /// the same floor.</summary>
    public static bool DoorInRange(Vector3 actor, Matrix4x4 world, Vector3 localMin, Vector3 localMax, float useRange)
    {
        var o = world.Translation;
        if (MathF.Abs(actor.Y - o.Y) > DoorMaxDy) return false;
        float reach = FootprintRadius(world, localMin, localMax) + MathF.Max(0f, useRange);
        float dx = actor.X - o.X, dz = actor.Z - o.Z;
        return dx * dx + dz * dz <= reach * reach;
    }
}
