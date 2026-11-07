using SiegeFX.Core.Net;
using Xunit;

namespace SiegeFX.Tests;

/// <summary>A player's character card survives the wire, head choice
/// included (it also picks the list the hair index points into, so without it
/// other players would see the wrong hair).</summary>
public class MpPlayerInfoTests
{
    [Fact]
    public void CardRoundTripsWithHead()
    {
        var card = new MpPlayerInfo(3, Gender: 1, Face: 7, Style: 2, Head: 4, Shirt: 5, Pants: 6,
            "Wren", "Fighter", Str: 12, Dex: 11, Intel: 10, Melee: 3, Ranged: 1, NMagic: 0, CMagic: 2);
        var w = new MpWriter();
        card.WriteBody(w);
        var r = new MpReader(w.Span);
        var back = MpPlayerInfo.ReadBody(ref r, 3);
        Assert.False(r.Bad);
        Assert.Equal(card, back);
        Assert.Equal(4, back.Head);
    }
}
