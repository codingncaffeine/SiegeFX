using System.Numerics;
using SiegeFX.Core.Nav;

namespace SiegeFX.Tests;

public class PropFootprintTests
{
    // An invented crate: 2 wide (X), 1 tall (Y), 3 deep (Z), authored Y-up.
    static readonly Vector3 Min = new(-1f, 0f, -1.5f), Max = new(1f, 1f, 1.5f);

    [Fact]
    public void YUpBoxSpansXByZAndStandsOnItsBase()
    {
        var f = PropFootprint.FromBounds(Min, Max, Matrix4x4.CreateTranslation(10f, 2f, 20f));
        Assert.Equal(2f, f.LenU, 3);
        Assert.Equal(3f, f.LenV, 3);
        Assert.Equal(2f, f.BaseY, 3);
        Assert.Equal(3f, f.TopY, 3);
        Assert.Equal(10f, f.Center.X, 3);
        Assert.Equal(20f, f.Center.Y, 3);
    }

    [Fact]
    public void ZUpBoxStoodUpByItsBindPoseUsesItsZAsHeight()
    {
        // Authored Z-up: 2 wide (X), 3 long (Y), 1 tall (Z). The root bind
        // pose turns mesh Z to world up.
        var zMin = new Vector3(-1f, -1.5f, 0f);
        var zMax = new Vector3(1f, 1.5f, 1f);
        var standUp = Matrix4x4.CreateRotationX(-MathF.PI / 2f);
        var f = PropFootprint.FromBounds(zMin, zMax, standUp * Matrix4x4.CreateTranslation(5f, 0f, 5f));
        Assert.Equal(6f, f.LenU * f.LenV, 3);
        Assert.Equal(3f, MathF.Max(f.LenU, f.LenV), 3);
        Assert.Equal(0f, f.BaseY, 3);
        Assert.Equal(1f, f.TopY, 3);
        Assert.Equal(5f, f.Center.X, 3);
        Assert.Equal(5f, f.Center.Y, 3);
    }

    [Fact]
    public void YawAndScaleMoveAndSizeTheRectangle()
    {
        var world = Matrix4x4.CreateScale(0.5f) * Matrix4x4.CreateRotationY(MathF.PI / 4f)
                  * Matrix4x4.CreateTranslation(-3f, 0f, 4f);
        var f = PropFootprint.FromBounds(Min, Max, world);
        Assert.Equal(1f, f.LenU, 3);
        Assert.Equal(1.5f, f.LenV, 3);
        Assert.Equal(0.5f, f.TopY, 3);
        Assert.Equal(-3f, f.Center.X, 3);
        Assert.Equal(4f, f.Center.Y, 3);
        // The rectangle's sides stay perpendicular after the yaw.
        Assert.Equal(0f, Vector3.Dot(f.AxisU, f.AxisV), 3);
    }
}
