using System.Numerics;
using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;

namespace SiegeFX.Tests;

public class ActorIdleTests
{
    static PrsAnimation.KeyList Track(params (Quaternion r, Vector3 p)[] keys) =>
        new(keys.Select((k, i) => new PrsAnimation.RotKey(i, k.r)).ToArray(),
            keys.Select((k, i) => new PrsAnimation.PosKey(i, k.p)).ToArray());

    static readonly Quaternion Bent = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.6f);

    [Fact]
    public void AOneFrameStanceIsStatic()
    {
        // The blacksmith's a_c_gbn_bs_fs2_ds: one key per track.
        var root = Track((Quaternion.Identity, Vector3.Zero));
        var bones = new[] { Track((Bent, Vector3.UnitY)), null, Track((Quaternion.Identity, Vector3.One)) };
        Assert.True(ActorIdle.IsStaticPose(root, bones));
    }

    [Fact]
    public void RepeatedIdenticalKeysAreStaticAndNegatedQuaternionsAreTheSameRotation()
    {
        var bones = new[] { Track((Bent, Vector3.UnitY), (Quaternion.Negate(Bent), Vector3.UnitY)) };
        Assert.True(ActorIdle.IsStaticPose(null, bones));
    }

    [Fact]
    public void AnArmThatSwingsIsNotStatic()
    {
        // The hammering fidget: the arm rotates across its keys.
        var bones = new[] { Track((Quaternion.Identity, Vector3.Zero), (Bent, Vector3.Zero)) };
        Assert.False(ActorIdle.IsStaticPose(null, bones));
        var moved = new[] { Track((Quaternion.Identity, Vector3.Zero), (Quaternion.Identity, Vector3.UnitZ)) };
        Assert.False(ActorIdle.IsStaticPose(null, moved));
    }

    [Fact]
    public void AFrozenDefaultWithAMovingFidgetIdlesInTheFidget()
    {
        Assert.Equal("chore_fidget",
            ActorIdle.PickIdleChore(null, defaultIsStatic: true, fidgetMoves: true, autoFidgets: true));
    }

    [Theory]
    [InlineData(false, true, true)]   // a moving default stays the idle
    [InlineData(true, false, true)]   // nothing better to show
    [InlineData(true, true, false)]   // actor_auto_fidgets = false
    public void OtherwiseTheDefaultStays(bool defaultIsStatic, bool fidgetMoves, bool autoFidgets)
    {
        Assert.Null(ActorIdle.PickIdleChore(null, defaultIsStatic, fidgetMoves, autoFidgets));
    }

    [Fact]
    public void AnAuthoredInitialChoreWins()
    {
        Assert.Equal("chore_misc",
            ActorIdle.PickIdleChore(" chore_misc ", defaultIsStatic: false, fidgetMoves: false, autoFidgets: false));
    }

    [Fact]
    public void TheIdleClipWinsOverTheDefaultLoopsRequestForClipZero()
    {
        // infinite_loop asks for index 0 as soon as chore_default starts;
        // before this the idle clip only showed until that first dispatch.
        Assert.Equal(4, Actor.ResolveClipIndex(h: 0, clipCount: 6, idleClipIndex: 4,
            buried: false, awakened: false, overrideActive: false));
        Assert.Equal(4, Actor.ResolveClipIndex(h: -1, clipCount: 6, idleClipIndex: 4,
            buried: false, awakened: false, overrideActive: false));
    }

    [Fact]
    public void OverridesAndOtherChoresStillShow()
    {
        // A pinned clip-0 override (a scripted pose) shows clip 0.
        Assert.Equal(0, Actor.ResolveClipIndex(0, 6, 4, buried: false, awakened: false, overrideActive: true));
        // A swing or any other chore is untouched.
        Assert.Equal(2, Actor.ResolveClipIndex(2, 6, 4, buried: false, awakened: false, overrideActive: false));
        // An actor with no idle of its own keeps chore_default.
        Assert.Equal(0, Actor.ResolveClipIndex(0, 6, 0, buried: false, awakened: false, overrideActive: false));
        // A buried skrubb holds its default until it wakes.
        Assert.Equal(0, Actor.ResolveClipIndex(3, 6, 4, buried: true, awakened: false, overrideActive: false));
    }
}
