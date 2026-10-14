using System.Numerics;
using SiegeFX.Core.Actors;

namespace SiegeFX.Tests;

public class HeroApproachTests
{
    [Theory]
    [InlineData(20f)]
    [InlineData(40f)]
    [InlineData(150f)]
    public void A_far_target_is_walked_up_to_however_far_it_is(float dist)
    {
        Assert.Equal(HeroApproachStep.WalkIntoReach, HeroApproach.Decide(dist, reach: 10f, lineBlocked: false, unreachable: false));
    }

    [Fact]
    public void In_reach_with_a_clear_line_the_hero_acts()
    {
        Assert.Equal(HeroApproachStep.Act, HeroApproach.Decide(9.5f, 10f, lineBlocked: false, unreachable: false));
        // Acting needs no walk, so a stale failed walk does not cancel it.
        Assert.Equal(HeroApproachStep.Act, HeroApproach.Decide(9.5f, 10f, lineBlocked: false, unreachable: true));
    }

    [Fact]
    public void In_reach_behind_a_wall_the_hero_closes_in()
    {
        Assert.Equal(HeroApproachStep.CloseIn, HeroApproach.Decide(6f, 10f, lineBlocked: true, unreachable: false));
    }

    [Fact]
    public void The_order_is_dropped_only_when_no_walk_gets_closer()
    {
        Assert.Equal(HeroApproachStep.GiveUp, HeroApproach.Decide(25f, 10f, lineBlocked: false, unreachable: true));
        Assert.Equal(HeroApproachStep.GiveUp, HeroApproach.Decide(6f, 10f, lineBlocked: true, unreachable: true));
    }

    [Fact]
    public void The_walk_stops_just_inside_reach_on_the_line_to_the_target()
    {
        var p = HeroApproach.PointInReach(Vector3.Zero, new Vector3(30f, 2f, 0f), 10f);
        Assert.Equal(20.5f, p.X, 3);
        Assert.Equal(0f, p.Z, 3);
        Assert.Equal(2f, p.Y, 3);
        Assert.True(HeroApproach.Decide(30f - p.X, 10f, false, false) == HeroApproachStep.Act);
    }

    [Fact]
    public void Already_close_enough_means_no_walk()
    {
        var from = new Vector3(1f, 0f, 1f);
        Assert.Equal(from, HeroApproach.PointInReach(from, new Vector3(3f, 0f, 1f), 10f));
    }
}
