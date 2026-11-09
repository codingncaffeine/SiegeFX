using System.Numerics;
using SiegeFX.Core.Sfx;

namespace SiegeFX.Tests;

public class SfxOwnerFacingTests
{
    static readonly Vector3 Smith = new(8.7f, 0.4f, -13.8f);

    [Fact]
    public void AnActorsOwnScriptOffsetsAlongItsFacing()
    {
        // The blacksmith's hammer sparks: offset(0,0,.8) is 0.8 m in front of him.
        var ctx = SfxContext.At(Smith) with { OwnerForward = -Vector3.UnitX };
        var off = SfxRuntime.CasterFrameOffset(ctx, new(0, 0, 0.8f));
        Assert.Equal(-0.8f, off.X, 4);
        Assert.Equal(0f, off.Z, 4);
    }

    [Fact]
    public void WithoutAnOwnerFacingTheWorldZFrameStays()
    {
        var off = SfxRuntime.CasterFrameOffset(SfxContext.At(Smith), new(0, 0, 0.8f));
        Assert.Equal(0.8f, off.Z, 4);
    }

    [Fact]
    public void ACasterToTargetFrameStillWins()
    {
        var ctx = new SfxContext(Smith, Smith + new Vector3(0, 0, -5), Smith) { OwnerForward = Vector3.UnitX };
        var off = SfxRuntime.CasterFrameOffset(ctx, new(0, 0, 1f));
        Assert.Equal(-1f, off.Z, 4);
    }
}
