using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;

namespace SiegeFX.Tests;

/// <summary>Levels as the player reads them count from 0, as in the
/// original; SiegeFX's level math counts from 1.</summary>
public class ShownLevelTests
{
    [Theory]
    [InlineData(1, 0)]   // a new hero: level 0 in every skill
    [InlineData(2, 1)]   // the first level-up: "advanced to level 1"
    [InlineData(50, 49)]
    [InlineData(0, 0)]   // never below 0
    public void A_level_is_shown_counting_from_zero(int level, int shown) =>
        Assert.Equal(shown, PlayerProgression.Shown(level));

    [Fact]
    public void A_new_hero_keeps_the_starting_title()
    {
        int s = PlayerProgression.Shown(1);
        Assert.Equal("Farmer", ClassTitleResolver.Resolve("Farmer", s, s, s, s));
        Assert.Equal("Apprentice", ClassTitleResolver.Resolve("Farmer", s, s, PlayerProgression.Shown(2), s));
    }
}
