using System.Text;
using SiegeFX.Core.Save;

namespace SiegeFX.Tests;

public class HeroOrdersSaveTests
{
    [Fact]
    public void The_heros_standing_orders_round_trip()
    {
        var save = new SaveFile { Player = new PlayerSnapshot { MoveOrder = 2, AtkOrder = 0, TgtOrder = 1 } };

        var back = SaveStore.Deserialize(SaveStore.Serialize(save));

        Assert.NotNull(back?.Player);
        Assert.Equal((2, 0, 1), (back.Player.MoveOrder, back.Player.AtkOrder, back.Player.TgtOrder));
    }

    [Fact]
    public void A_save_written_before_hero_orders_keeps_the_defaults()
    {
        var json = "{\"SchemaVersion\": " + SaveFile.CurrentSchemaVersion + ", \"Player\": {\"Scid\": 1, \"Level\": 3}}";

        var back = SaveStore.Deserialize(Encoding.UTF8.GetBytes(json));

        Assert.NotNull(back?.Player);
        Assert.Equal((-1, -1, -1), (back.Player.MoveOrder, back.Player.AtkOrder, back.Player.TgtOrder));
    }
}
