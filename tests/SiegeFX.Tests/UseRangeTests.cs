using System.Numerics;
using SiegeFX.Core.Geometry;

namespace SiegeFX.Tests;

public class UseRangeTests
{
    // A DS1 wall door: mesh authored Z-up, hinge at the local origin, the
    // leaf running 1.6u along -X, 0.2u thick (Y), 3u tall (Z). The placement
    // bakes the Z-up→Y-up standup, a yaw, and the world position.
    static readonly Vector3 Min = new(-1.6f, -0.1f, 0f);
    static readonly Vector3 Max = new(0f, 0.1f, 3f);
    static readonly Vector3 Hinge = new(100f, 5f, -40f);
    static readonly Matrix4x4 World =
        Matrix4x4.CreateRotationX(-MathF.PI / 2f)
        * Matrix4x4.CreateRotationY(0.7f)
        * Matrix4x4.CreateTranslation(Hinge);

    [Fact]
    public void FootprintIsTheLeafLength() =>
        Assert.Equal(1.6f, UseRange.FootprintRadius(World, Min, Max), 3);

    [Fact]
    public void StandingInFrontOfTheLeafIsInRange()
    {
        // The leaf's middle, stepped 1.2u off its face (the closed door
        // blocks the doorway, so this is about as close as a hero gets).
        var mid = Vector3.Transform(new Vector3(-0.8f, 0f, 0f), World);
        var face = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, World));
        Assert.True(UseRange.DoorInRange(mid + face * 1.2f, World, Min, Max, UseRange.DefaultDoorUseRange));
    }

    [Fact]
    public void FarAwayIsOutOfRange()
    {
        Assert.False(UseRange.DoorInRange(Hinge + new Vector3(8f, 0f, 0f), World, Min, Max, UseRange.DefaultDoorUseRange));
        // Either side of footprint (1.6) + use_range (1.5).
        Assert.False(UseRange.DoorInRange(Hinge + new Vector3(0f, 0f, 3.2f), World, Min, Max, UseRange.DefaultDoorUseRange));
        Assert.True(UseRange.DoorInRange(Hinge + new Vector3(0f, 0f, 3.0f), World, Min, Max, UseRange.DefaultDoorUseRange));
    }

    [Fact]
    public void AnotherFloorIsOutOfRange() =>
        Assert.False(UseRange.DoorInRange(Hinge + new Vector3(0.5f, -4f, 0f), World, Min, Max, UseRange.DefaultDoorUseRange));

    [Fact]
    public void AuthoredUseRangeWidensTheReach()
    {
        var p = Hinge + new Vector3(4f, 0f, 0f);
        Assert.False(UseRange.DoorInRange(p, World, Min, Max, 1.5f));
        Assert.True(UseRange.DoorInRange(p, World, Min, Max, 3f));
    }
}
