using SiegeFX.Core.Net;

namespace SiegeFX.Tests;

/// <summary>The host's game rules (the original's Game Settings and Map
/// Settings) and the join, team, pause and player-vs-player rules they drive.</summary>
public class MpSettingsTests
{
    static (MpSession Host, List<MpSession> Clients) Game(int clients, Action<MpSession>? hostSetup = null, params string[] names)
    {
        var hub = new LoopbackHub(clients);
        var host = new MpSession(hub.Host, isHost: true, "Host");
        hostSetup?.Invoke(host);
        var list = new List<MpSession>();
        for (int i = 0; i < clients; i++)
            list.Add(new MpSession(hub.Clients[i], isHost: false, i < names.Length ? names[i] : $"P{i + 1}"));
        return (host, list);
    }

    static void Pump(MpSession host, IEnumerable<MpSession> clients, int rounds = 4, double dt = 0)
    {
        for (int i = 0; i < rounds; i++)
        {
            host.Tick(dt);
            foreach (var c in clients) c.Tick(dt);
        }
    }

    static MpGameSettings Sample() => new()
    {
        PlayerLimit = 5, Difficulty = 2, TimeLimitMinutes = 90, DropOnDeath = MpDropOnDeath.Inventory,
        TeamPlay = true, PlayerVsPlayer = true, AllowStartChoice = true, NewCharactersOnly = true,
        AllowPause = true, AllowJoinInProgress = false, HasPassword = true,
        Map = "map_world", MapName = "Kingdom of Ehb", World = "normal", WorldName = "Regular",
        StartGroup = "stonebridge", StartName = "Stonebridge", StartLevel = 3,
    };

    [Fact]
    public void TheRulesSurviveTheWireTheRelaunchAndThePrefsFile()
    {
        var s = Sample();
        var w = new MpWriter();
        s.Write(w);
        var r = new MpReader(w.Span);
        Assert.Equal(s, MpGameSettings.Read(ref r));
        Assert.Equal(s, MpGameSettings.FromEnv(s.ToEnv()));
        var back = MpGameSettings.FromPrefs(s.ToPrefs());
        // The prefs keep the host's choices, not the map, start or password.
        Assert.Equal(s with { HasPassword = false, StartGroup = "", StartName = "", StartLevel = 0 }, back);
    }

    [Fact]
    public void OutOfRangeRulesAreClampedToTheOriginalsChoices()
    {
        var s = new MpGameSettings { PlayerLimit = 200, Difficulty = 9, TimeLimitMinutes = 17, DropOnDeath = (MpDropOnDeath)7 }.Clamped();
        Assert.Equal(8, s.PlayerLimit);
        Assert.Equal(2, s.Difficulty);
        Assert.Equal(0, s.TimeLimitMinutes);
        Assert.Equal(MpDropOnDeath.All, s.DropOnDeath);
        Assert.Null(MpGameSettings.FromEnv("not hex"));
        Assert.Null(MpGameSettings.FromEnv("0102"));
    }

    [Fact]
    public void ChangesAreAnnouncedInTheOriginalsWords()
    {
        var before = new MpGameSettings();
        var lines = MpGameSettings.Describe(before, before with { PlayerLimit = 4, DropOnDeath = MpDropOnDeath.EquippedItems, AllowPause = true, TimeLimitMinutes = 120 });
        Assert.Equal(new[]
        {
            "Host set the player limit to 4 players.",
            "Host set the time limit to 2 hours.",
            "Host set \"Drop on Death\" to Equipped Items only.",
            "Host has allowed all players to be able to pause the game.",
        }, lines);
        Assert.Empty(MpGameSettings.Describe(before, before));
        Assert.Equal("15 minutes", MpGameSettings.TimeLimitName(15));
        Assert.Equal("None", MpGameSettings.TimeLimitName(0));
    }

    [Fact]
    public void ClientsReceiveTheHostsRulesButNeverItsPassword()
    {
        var (host, clients) = Game(1);
        MpGameSettings? seen = null;
        clients[0].OnSettings = (_, after) => seen = after;
        clients[0].JoinPassword = "swordfish";
        host.SetSettings(Sample() with { HasPassword = false }, password: "swordfish");
        clients[0].SendJoinRequest();
        Pump(host, clients);

        Assert.True(clients[0].Joined);
        Assert.NotNull(seen);
        Assert.True(seen!.HasPassword);
        Assert.Equal(host.Settings, clients[0].Settings);
        Assert.DoesNotContain("swordfish", clients[0].Settings.ToString());
    }

    [Fact]
    public void AWrongPasswordIsTurnedAway()
    {
        var (host, clients) = Game(1);
        host.SetSettings(new MpGameSettings(), password: "swordfish");
        string? why = null;
        clients[0].OnJoinRejected = r => why = r;
        clients[0].JoinPassword = "trout";
        clients[0].SendJoinRequest();
        Pump(host, clients);
        Assert.Equal("The password you entered was invalid.", why);
        Assert.False(clients[0].Joined);
        Assert.Equal(1, host.PlayerCount);
    }

    [Fact]
    public void ThePlayerLimitTurnsAwayTheNextPlayerAndCannotDropBelowThePlayersIn()
    {
        var (host, clients) = Game(3);
        host.SetSettings(new MpGameSettings { PlayerLimit = 2 });
        string? why = null;
        clients[1].OnJoinRejected = r => why = r;
        clients[0].SendJoinRequest();
        Pump(host, clients);
        clients[1].SendJoinRequest();
        Pump(host, clients);
        Assert.True(clients[0].Joined);
        Assert.False(clients[1].Joined);
        Assert.Equal("The game is full.", why);

        host.SetSettings(host.Settings with { PlayerLimit = 3 });
        clients[2].SendJoinRequest();
        Pump(host, clients);
        Assert.True(clients[2].Joined);
        host.SetSettings(host.Settings with { PlayerLimit = 2 });   // three are in
        Assert.Equal(3, host.Settings.PlayerLimit);
    }

    [Fact]
    public void WithoutJoinInProgressOnlyTheStartingPartyGetsIn()
    {
        var (host, clients) = Game(2, h =>
        {
            h.SetSettings(new MpGameSettings { AllowJoinInProgress = false });
            h.InProgress = ("/world/maps/map_world/regions/fh_r1", 1);
            h.StartingParty.Add("Ada");
        }, "Ada", "Bram");
        string? why = null;
        clients[1].OnJoinRejected = r => why = r;
        clients[0].SendJoinRequest();
        clients[1].SendJoinRequest();
        Pump(host, clients);
        Assert.True(clients[0].Joined);
        Assert.False(clients[1].Joined);
        Assert.Equal("Host has prevented new players from joining the game while in progress.", why);

        // Allowed again: the same player gets in.
        host.SetSettings(host.Settings with { AllowJoinInProgress = true });
        clients[1].SendJoinRequest();
        Pump(host, clients);
        Assert.True(clients[1].Joined);
    }

    [Fact]
    public void ATeamPickReachesEveryRosterAndARepeatIsHarmless()
    {
        var (host, clients) = Game(2);
        foreach (var c in clients) c.SendJoinRequest();
        Pump(host, clients);
        clients[0].SetTeam(5);
        host.SetTeam(5);
        Pump(host, clients);
        clients[0].SetTeam(5);
        Pump(host, clients);

        foreach (var view in new[] { host.Roster, clients[0].Roster, clients[1].Roster })
        {
            Assert.Equal(5, view.Single(e => e.Player == 0).Team);
            Assert.Equal(5, view.Single(e => e.Player == clients[0].LocalPlayerId).Team);
            Assert.Equal(0, view.Single(e => e.Player == clients[1].LocalPlayerId).Team);
        }
    }

    [Fact]
    public void ALostTeamPickIsSentAgain()
    {
        var (h, c) = LoopbackTransport.CreatePair();
        var drop = new DropFirst(c, (byte)MpMsg.TeamPick);
        var host = new MpSession(h, isHost: true, "Host");
        var client = new MpSession(drop, isHost: false, "Guest");
        client.SendJoinRequest();
        for (int i = 0; i < 4; i++) { host.Tick(0); client.Tick(0); }
        client.SetTeam(3);
        for (int i = 0; i < 4; i++) { host.Tick(0); client.Tick(0); }
        Assert.Equal(0, host.Roster.Single(e => e.Player == 1).Team);   // the first pick was lost
        for (int i = 0; i < 10; i++) { host.Tick(0.3); client.Tick(0.3); }
        Assert.Equal(3, host.Roster.Single(e => e.Player == 1).Team);
        Assert.Equal(1, drop.Dropped);
    }

    sealed class DropFirst(ISessionTransport inner, byte type) : ISessionTransport
    {
        public int Dropped;
        public bool Armed;   // drop the next one too
        public Task<bool> ListenAsync(int port, CancellationToken ct) => inner.ListenAsync(port, ct);
        public Task<bool> ConnectAsync(string hostAddress, CancellationToken ct) => inner.ConnectAsync(hostAddress, ct);
        public void Send(int peerId, ReadOnlySpan<byte> payload)
        {
            if (payload.Length > 0 && payload[0] == type && (Dropped == 0 || Armed)) { Dropped++; Armed = false; return; }
            inner.Send(peerId, payload);
        }
        public bool TryReceive(out int peerId, out byte[] payload) => inner.TryReceive(out peerId, out payload);
        public IReadOnlyList<int> Peers => inner.Peers;
        public event Action<int>? PeerConnected { add => inner.PeerConnected += value; remove => inner.PeerConnected -= value; }
        public event Action<int>? PeerDisconnected { add => inner.PeerDisconnected += value; remove => inner.PeerDisconnected -= value; }
        public string ProviderId => inner.ProviderId;
        public void Dispose() => inner.Dispose();
    }

    [Fact]
    public void PausingIsEveryonesOnlyWhenTheHostAllowsIt()
    {
        var (host, clients) = Game(2);
        foreach (var c in clients) c.SendJoinRequest();
        Pump(host, clients);
        var seen = new List<(string Who, bool Paused, int By)>();
        host.OnPause = (p, by) => seen.Add(("host", p, by));
        clients[1].OnPause = (p, by) => seen.Add(("c2", p, by));

        Assert.False(clients[0].RequestPause(true));   // not allowed
        Pump(host, clients);
        Assert.False(host.Paused);
        Assert.Empty(seen);

        host.SetSettings(new MpGameSettings { AllowPause = true });
        Pump(host, clients);
        Assert.True(clients[0].RequestPause(true));
        Pump(host, clients);
        Assert.True(host.Paused && clients[0].Paused && clients[1].Paused);
        int by = clients[0].LocalPlayerId;
        Assert.Contains(("host", true, by), seen);
        Assert.Contains(("c2", true, by), seen);

        host.RequestPause(false);
        Pump(host, clients);
        Assert.False(host.Paused || clients[0].Paused || clients[1].Paused);
    }

    [Fact]
    public void PlayerVsPlayerHitsReachTheStruckPlayerOnlyWhenEnabled()
    {
        var (host, clients) = Game(2);
        foreach (var c in clients) c.SendJoinRequest();
        Pump(host, clients);
        var hits = new List<(string Who, float Dmg, int By)>();
        host.OnPlayerHit = (d, by) => hits.Add(("host", d, by));
        clients[0].OnPlayerHit = (d, by) => hits.Add(("c1", d, by));
        clients[1].OnPlayerHit = (d, by) => hits.Add(("c2", d, by));
        int p1 = clients[0].LocalPlayerId, p2 = clients[1].LocalPlayerId;

        clients[0].SendPvpHit(p2, 12f);
        Pump(host, clients);
        Assert.Empty(hits);   // player-vs-player is off

        host.SetSettings(new MpGameSettings { PlayerVsPlayer = true });
        Pump(host, clients);
        clients[0].SendPvpHit(p2, 12f);
        clients[1].SendPvpHit(0, 7f);
        host.SendPvpHit(p1, 3f);
        Pump(host, clients);
        Assert.Equal(new[] { ("c1", 3f, 0), ("c2", 12f, p1), ("host", 7f, p2) }, hits.OrderBy(h => h.Who).ToArray());
    }

    // A client that sends a frame its own rules would not: the host's rules decide.
    static (MpSession Host, ISessionTransport Raw) RawGuest()
    {
        var (h, c) = LoopbackTransport.CreatePair();
        var host = new MpSession(h, isHost: true, "Host");
        c.Send(0, new MpWriter().U8((byte)MpMsg.JoinRequest).U16(MpProtocol.Version).Str("Rogue").Str("").Span);
        host.Tick(0);
        while (c.TryReceive(out _, out _)) { }
        return (host, c);
    }

    [Fact]
    public void TheHostIgnoresAPauseItDoesNotAllow()
    {
        var (host, raw) = RawGuest();
        int pauses = 0;
        host.OnPause = (_, _) => pauses++;
        raw.Send(0, new MpWriter().U8((byte)MpMsg.Pause).U8(1).Span);
        host.Tick(0);
        Assert.False(host.Paused);
        Assert.Equal(0, pauses);
        host.SetSettings(new MpGameSettings { AllowPause = true });
        raw.Send(0, new MpWriter().U8((byte)MpMsg.Pause).U8(1).Span);
        host.Tick(0);
        Assert.True(host.Paused);
        Assert.Equal(1, pauses);
    }

    [Fact]
    public void TheHostDropsAPlayerVsPlayerHitItDoesNotAllow()
    {
        var (host, raw) = RawGuest();
        var hits = new List<float>();
        host.OnPlayerHit = (d, _) => hits.Add(d);
        raw.Send(0, new MpWriter().U8((byte)MpMsg.PvpHit).U8(0).F32(9f).Span);
        host.Tick(0);
        Assert.Empty(hits);
        host.SetSettings(new MpGameSettings { PlayerVsPlayer = true });
        raw.Send(0, new MpWriter().U8((byte)MpMsg.PvpHit).U8(0).F32(9f).Span);
        host.Tick(0);
        Assert.Equal(new[] { 9f }, hits);
    }

    [Fact]
    public void ALostSettingsFrameIsMadeUpByTheNextCopy()
    {
        var (h, c) = LoopbackTransport.CreatePair();
        var drop = new DropFirst(h, (byte)MpMsg.Settings);
        var host = new MpSession(drop, isHost: true, "Host");
        var client = new MpSession(c, isHost: false, "Guest");
        client.SendJoinRequest();
        for (int i = 0; i < 4; i++) { host.Tick(0); client.Tick(0); }
        Assert.Equal(1, drop.Dropped);                           // the copy sent with the JoinAccept was lost
        drop.Armed = true;
        host.SetSettings(new MpGameSettings { DropOnDeath = MpDropOnDeath.All });
        for (int i = 0; i < 4; i++) { host.Tick(0); client.Tick(0); }
        Assert.Equal(2, drop.Dropped);                           // and so was the change
        Assert.Equal(MpDropOnDeath.Nothing, client.Settings.DropOnDeath);
        for (int i = 0; i < 10; i++) { host.Tick(0.3); client.Tick(0.3); }
        Assert.Equal(MpDropOnDeath.All, client.Settings.DropOnDeath);
    }

    [Fact]
    public void TheGameClockIsTheHostsAndStopsWhilePaused()
    {
        var (host, clients) = Game(1);
        clients[0].SendJoinRequest();
        Pump(host, clients);
        host.Tick(30);                                  // the staging area: no game yet
        Assert.Equal(0, host.GameSeconds);
        host.InProgress = ("/world/maps/map_world/regions/fh_r1", 1);
        host.SetSettings(new MpGameSettings { AllowPause = true, TimeLimitMinutes = 15 });
        host.Tick(100);
        Pump(host, clients, rounds: 2, dt: 1.5);        // a periodic copy carries the clock
        Assert.InRange(clients[0].GameSeconds, 100, 110);
        host.RequestPause(true);
        Pump(host, clients);
        double h = host.GameSeconds, c = clients[0].GameSeconds;
        Pump(host, clients, rounds: 5, dt: 10);
        Assert.Equal(h, host.GameSeconds);
        Assert.Equal(c, clients[0].GameSeconds);
    }

    [Fact]
    public void TeamChatReachesTheSendersTeamOnly()
    {
        var (host, clients) = Game(3);
        foreach (var c in clients) c.SendJoinRequest();
        Pump(host, clients);
        host.SetSettings(new MpGameSettings { TeamPlay = true });
        host.SetTeam(2); clients[0].SetTeam(2); clients[1].SetTeam(2); clients[2].SetTeam(7);
        Pump(host, clients);
        var heard = new List<(string Who, int From, string Text)>();
        host.OnTeamChat = (f, t) => heard.Add(("host", f, t));
        for (int i = 0; i < 3; i++) { int k = i; clients[i].OnTeamChat = (f, t) => heard.Add(($"c{k + 1}", f, t)); }

        Assert.True(clients[0].SendTeamChat("left flank"));
        Pump(host, clients);
        int p1 = clients[0].LocalPlayerId;
        Assert.Equal(new[] { ("c2", p1, "left flank"), ("host", p1, "left flank") }, heard.OrderBy(h => h.Who).ToArray());

        heard.Clear();
        host.SetSettings(host.Settings with { TeamPlay = false });
        Pump(host, clients);
        Assert.False(clients[0].SendTeamChat("anyone?"));
        Pump(host, clients);
        Assert.Empty(heard);
    }

    [Fact]
    public void TheGameEndCarriesItsReason()
    {
        var (host, clients) = Game(1);
        clients[0].SendJoinRequest();
        Pump(host, clients);
        MpGameEndReason? why = null;
        clients[0].OnGameEnded = r => why = r;
        host.EndGame(MpGameEndReason.TimeLimit);
        Pump(host, clients);
        Assert.Equal(MpGameEndReason.TimeLimit, why);
    }
}
