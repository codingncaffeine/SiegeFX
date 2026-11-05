using SiegeFX.Core.Actors;

namespace SiegeFX.Tests;

/// <summary>God mode's damage guard.</summary>
public class InvulnerableTests
{
    static ActorCombatState Hero() => new(new ActorStats(50, 20, 0, 0, 0, 0, 1, 0, 10, 10, 10));

    [Fact]
    public void An_invulnerable_actor_takes_no_damage_even_from_a_lethal_hit()
    {
        var c = Hero();
        c.Invulnerable = true;
        Assert.Equal(0f, c.ApplyDamage(500f));
        Assert.Equal((50f, false, false), (c.CurrentLife, c.IsDead, c.JustDied));
    }

    [Fact]
    public void Switched_off_the_actor_takes_damage_again()
    {
        var c = Hero();
        c.Invulnerable = true;
        c.ApplyDamage(10f);
        c.Invulnerable = false;
        Assert.Equal(10f, c.ApplyDamage(10f));
        Assert.Equal(40f, c.CurrentLife);
    }
}
