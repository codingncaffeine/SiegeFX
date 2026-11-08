using System.Text;
using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeFX.Tests;

/// <summary>A co-op game loads the original's multiplayer world: the
/// multiplayer twins instead of the single-player placements (no hireable
/// companions), the multiplayer trigger rows instead of the single-player
/// ones, and monsters scaled by [mp_scaling] for the number of players.</summary>
public class MpWorldTests
{
    // fh_r1's two Noricks at one bridge: the single-player one (who can be
    // talked into the NIS) and the multiplayer one (who only talks).
    const string Actors = """
        [t:norick,n:0x00000001]
        {
            [common] { b is_multi_player = false; }
            [placement] { p position = 1,0,1,0x000000AA; }
        }
        [t:norick,n:0x00000002]
        {
            [common] { b is_single_player = false; }
            [placement] { p position = 1,0,1,0x000000AA; }
        }
        [t:farmgirl,n:0x00000003]
        {
            [placement] { p position = 2,0,2,0x000000AA; }
        }
        """;

    [Fact]
    public void EachGameLoadsItsOwnTwinAndTheSharedPlacements()
    {
        var writer = new TankWriter { Priority = TankPriority.Factory };
        writer.Add("/world/maps/map_world/regions/fh_r1/objects/actor.gas", Encoding.UTF8.GetBytes(Actors));
        var path = Path.Combine(Path.GetTempPath(), $"siegefx-mptwins-{Guid.NewGuid():N}.dsmap");
        try
        {
            writer.Write(path, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            using var tank = TankFile.Open(path);
            var reader = new TankReader(tank);
            var sp = RegionObjects.LoadActors(reader, "/world/maps/map_world/regions/fh_r1", multiplayer: false).Actors;
            var mp = RegionObjects.LoadActors(reader, "/world/maps/map_world/regions/fh_r1", multiplayer: true).Actors;
            Assert.Equal(new uint[] { 1, 3 }, sp.Select(a => a.Scid));
            Assert.Equal(new uint[] { 2, 3 }, mp.Select(a => a.Scid));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TriggerRowsCarryBothModeFlags()
    {
        var doc = GasDocument.Load(Encoding.UTF8.GetBytes("""
            [sp_only] { b multi_player = false; b single_player = true; }
            [mp_only] { b multi_player = true; b single_player = false; }
            [both] { }
            """));
        var rows = doc.Roots.Select(n => TriggerRow.Parse(n, "test", null)).ToList();
        Assert.Equal(new[] { (true, false), (false, true), (true, true) }, rows.Select(r => (r.SinglePlayer, r.MultiPlayer)));
    }

    const string Scaling = """
        [mp_scaling]
        {
            life	= [[ .35, .5, .8, 1, 2.15, 3.1, 4.17, 5 ]];
            xp		= [[ .35, .59, .8, 1, 1.25, 1.5, 1.75, 2 ]];
        }
        """;

    [Fact]
    public void MonstersFollowTheOriginalsPlayerCountTable()
    {
        var f = FormulasStore.Load(Encoding.UTF8.GetBytes(Scaling));
        Assert.Equal((0.5f, 0.59f), f.MpScaling(2));
        Assert.Equal((1f, 1f), f.MpScaling(4));
        Assert.Equal((5f, 2f), f.MpScaling(8));
        Assert.Equal((0.35f, 0.35f), f.MpScaling(0));    // clamped to the table
        Assert.Equal((5f, 2f), f.MpScaling(12));
        // No table, or a broken one, scales nothing.
        Assert.Equal((1f, 1f), FormulasStore.Load(Encoding.UTF8.GetBytes("[mp_scaling] { life = [[ 2, x ]]; }")).MpScaling(2));
    }

    [Fact]
    public void ARescaledMonsterKeepsItsShareOfLife()
    {
        var stats = new ActorStats(MaxLife: 100f, MaxMana: 0f, DamageMin: 1f, DamageMax: 2f, Defense: 0f,
            AttackRange: 1f, WalkSpeed: 1f, ExperienceValue: 50, Strength: 0f, Dexterity: 0f, Intelligence: 0f);
        var c = new ActorCombatState(stats);
        c.ApplyDamage(40f);
        c.RescaleLife(stats with { MaxLife = 50f });
        Assert.Equal(30f, c.CurrentLife, 3);
        c.RescaleLife(stats with { MaxLife = 215f });
        Assert.Equal(129f, c.CurrentLife, 3);
        c.ApplyDamage(500f);
        c.RescaleLife(stats with { MaxLife = 100f });
        Assert.True(c.IsDead);                               // the dead stay dead
    }
}
