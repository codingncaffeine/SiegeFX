using SiegeFX.Core.Actors;
using SiegeFX.Core.Net;

namespace SiegeFX.Tests;

/// <summary>Items that travel between players (a dropped pile, the host's
/// pile, an item handed over) arrive whole: a part-drunk potion keeps its
/// fill and a spell book its spells.</summary>
public class MpLootTests
{
    static readonly LootEntry Potion = new("", "potion_health_large", 0.35f);
    static readonly LootEntry Book = new("", "book_glb_magic_01", 1f, "spell_zap,spell_healing_hands");
    static readonly LootEntry Sword = new("es_weapon_hand", "sword_01");

    static (MpSession Host, List<MpSession> Clients) Joined(int clients)
    {
        var hub = new LoopbackHub(clients);
        var host = new MpSession(hub.Host, isHost: true, "Host");
        var list = new List<MpSession>();
        for (int i = 0; i < clients; i++)
        {
            var c = new MpSession(hub.Clients[i], isHost: false, $"P{i + 1}");
            list.Add(c);
            c.SendJoinRequest();
            Pump(host, list);
            Assert.True(c.Joined);
        }
        return (host, list);
    }

    static void Pump(MpSession host, IEnumerable<MpSession> clients, int rounds = 4)
    {
        for (int i = 0; i < rounds; i++)
        {
            host.Tick(0);
            foreach (var c in clients) c.Tick(0);
        }
    }

    [Fact]
    public void ADroppedPileReachesTheHostAndComesBackWhole()
    {
        var (host, clients) = Joined(1);
        List<LootEntry>? dropped = null, spawned = null;
        host.OnLootDrop = (_, _, _, _, e) => dropped = e;
        clients[0].OnLootSpawn = (_, _, _, _, e) => spawned = e;

        clients[0].SendLootDrop(1, 2, 3, new[] { Potion, Book, Sword });
        Pump(host, clients);
        Assert.Equal(new[] { Potion, Book, Sword }, dropped);

        host.SendLootSpawn(7, 1, 2, 3, dropped!);
        Pump(host, clients);
        Assert.Equal(new[] { Potion, Book, Sword }, spawned);
    }

    [Fact]
    public void AnItemHandedToAnotherPlayerKeepsItsFillAndSpells()
    {
        var (host, clients) = Joined(2);
        LootEntry? toP2 = null, toHost = null;
        clients[1].OnItemGive = (_, _, e) => toP2 = e;
        host.OnItemGive = (_, _, e) => toHost = e;

        clients[0].SendItemGive(clients[1].LocalPlayerId, Book);   // routed through the host
        clients[1].SendItemGive(host.LocalPlayerId, Potion);
        Pump(host, clients);
        Assert.Equal(Book, toP2);
        Assert.Equal(Potion, toHost);
    }

    [Fact]
    public void AFillFromTheWireStaysInRange()
    {
        var (host, clients) = Joined(1);
        List<LootEntry>? dropped = null;
        host.OnLootDrop = (_, _, _, _, e) => dropped = e;
        clients[0].SendLootDrop(0, 0, 0, new[] { Potion with { Fill = float.NaN }, Potion with { Fill = 5f }, Potion with { Fill = -1f } });
        Pump(host, clients);
        Assert.Equal(new[] { 1f, 1f, 0f }, dropped!.Select(e => e.Fill));
    }
}
