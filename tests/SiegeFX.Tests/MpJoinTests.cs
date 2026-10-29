using SiegeFX.Core.Net;

namespace SiegeFX.Tests;

public class MpJoinTests
{
    // Loopback with a real datagram's limits: frames past the UDP ceiling are
    // refused, and chosen frames can be lost on the way.
    sealed class Lossy(ISessionTransport inner) : ISessionTransport
    {
        public const int UdpMax = 65507;
        public Func<byte[], bool> Drop = _ => false;
        public Task<bool> ListenAsync(int port, CancellationToken ct) => inner.ListenAsync(port, ct);
        public Task<bool> ConnectAsync(string hostAddress, CancellationToken ct) => inner.ConnectAsync(hostAddress, ct);
        public void Send(int peerId, ReadOnlySpan<byte> payload)
        {
            if (payload.Length > UdpMax || Drop(payload.ToArray())) return;
            inner.Send(peerId, payload);
        }
        public bool TryReceive(out int peerId, out byte[] payload) => inner.TryReceive(out peerId, out payload);
        public IReadOnlyList<int> Peers => inner.Peers;
        public event Action<int>? PeerConnected { add => inner.PeerConnected += value; remove => inner.PeerConnected -= value; }
        public event Action<int>? PeerDisconnected { add => inner.PeerDisconnected += value; remove => inner.PeerDisconnected -= value; }
        public string ProviderId => inner.ProviderId;
        public void Dispose() => inner.Dispose();
    }

    static (MpSession Host, MpSession Client, Lossy HostLink) Pair(byte[] snapshot)
    {
        var (h, c) = LoopbackTransport.CreatePair();
        var hostLink = new Lossy(h);
        var host = new MpSession(hostLink, isHost: true, "Host") { BuildSnapshot = () => snapshot };
        var client = new MpSession(new Lossy(c), isHost: false, "Client");
        return (host, client, hostLink);
    }

    static void Pump(MpSession host, MpSession client, double dt = 0)
    {
        for (int i = 0; i < 4; i++) { host.Tick(dt); client.Tick(dt); }
    }

    // The save JSON of a fresh farm game: indented, ~130 kB.
    static byte[] FarmSizedSave()
    {
        var sb = new System.Text.StringBuilder("{\n  \"actors\": [\n");
        for (int i = 0; i < 1200; i++)
            sb.Append("    {\n      \"scid\": ").Append(0x01C00000 + i).Append(",\n      \"life\": 25.0,\n      \"template\": \"krug_scout\"\n    },\n");
        return System.Text.Encoding.UTF8.GetBytes(sb.Append("  ]\n}\n").ToString());
    }

    [Fact]
    public void ASnapshotPastTheDatagramCeilingStillJoinsAsTheAssignedPlayer()
    {
        var save = FarmSizedSave();
        Assert.True(save.Length > Lossy.UdpMax);
        var (host, client, _) = Pair(save);
        byte[]? applied = null;
        int joinedAs = -1;
        client.ApplySnapshot = b => applied = b;
        client.OnJoinAccepted = id => joinedAs = id;

        client.SendJoinRequest();
        Pump(host, client);

        Assert.Equal(1, joinedAs);
        Assert.Equal(1, client.LocalPlayerId);
        Assert.Equal(save, applied);
    }

    [Fact]
    public void ALostJoinAcceptIsAskedForAgain()
    {
        var (host, client, hostLink) = Pair([1, 2, 3]);
        int accepts = 0;
        hostLink.Drop = f => f[0] == 10 && accepts++ == 0;   // the first JoinAccept
        int joins = 0, joinedAs = -1;
        host.OnPlayerJoined = (_, _) => joins++;
        client.OnJoinAccepted = id => joinedAs = id;

        client.SendJoinRequest();
        Pump(host, client);
        Assert.Equal(-1, joinedAs);

        Pump(host, client, dt: 1.1);
        Assert.Equal(1, joinedAs);
        Assert.Equal(1, joins);   // the repeat is not a second player
    }

    [Fact]
    public void AnUnpackableSnapshotIsLeftOutButThePlayerIdArrives()
    {
        var noise = new byte[MpSession.MaxSnapshotBytes * 2];
        new Random(7).NextBytes(noise);
        var (host, client, _) = Pair(noise);
        byte[]? applied = null;
        client.ApplySnapshot = b => applied = b;

        client.SendJoinRequest();
        Pump(host, client);

        Assert.Equal(1, client.LocalPlayerId);
        Assert.Equal(Array.Empty<byte>(), applied);
    }

    [Fact]
    public void CoopEventsReachTheRightMachine()
    {
        var (host, client, _) = Pair([]);
        client.SendJoinRequest();
        Pump(host, client);

        uint hostSawBreak = 0, clientSawBreak = 0;
        host.OnPropBreak = s => hostSawBreak = s;
        client.OnPropBreak = s => clientSawBreak = s;
        client.SendPropBreak(0x01C00DA3);
        Pump(host, client);
        Assert.Equal(0x01C00DA3u, hostSawBreak);
        Assert.Equal(0u, clientSawBreak);          // not echoed to its sender

        host.SendPropBreak(0x01C00DA4);
        Pump(host, client);
        Assert.Equal(0x01C00DA4u, clientSawBreak);

        float hit = 0;
        client.OnPlayerHit = d => hit = d;
        host.SendPlayerHit(client.LocalPlayerId, 7.5f);
        Pump(host, client);
        Assert.Equal(7.5f, hit);

        (uint Scid, string Tpl, float X)? spawn = null;
        client.OnActorSpawn = (scid, tpl, x, _, _) => spawn = (scid, tpl, x);
        host.SendActorSpawn(0xFE000001, "krug_scout", 3f, 0f, -5f);
        Pump(host, client);
        Assert.Equal((0xFE000001u, "krug_scout", 3f), spawn);
    }

    [Fact]
    public void ChoresRideThePoseFrames()
    {
        var (host, client, _) = Pair([]);
        client.SendJoinRequest();
        Pump(host, client);

        var swing = new MpChore(Seq: 7, Clip: 3, Duration: 24);
        client.BuildLocalPlayerState = () => new MpPlayerState(0, 1f, 0f, 2f, 0f, 30, 0, swing);
        MpPlayerState? seenByHost = null;
        host.ApplyPlayerStates = list => seenByHost = list[0];
        host.BuildLocalPlayerState = () => new MpPlayerState(0, 0f, 0f, 0f, 0f, 30, 0, new MpChore(2, 5, 10));
        MpPlayerState? seenByClient = null;
        client.ApplyPlayerStates = list => seenByClient = list[0];
        host.EnumerateActors = () => [new MpActorState(0x01C005CA, 1f, 2f, 3f, 12, new MpChore(9, 4, 20))];
        MpActorState? mob = null;
        client.ApplyDelta = (_, list) => mob = list[0];

        Pump(host, client, dt: 0.1);
        Pump(host, client, dt: 0.1);

        Assert.Equal(swing, seenByHost?.Chore);
        Assert.Equal(new MpChore(2, 5, 10), seenByClient?.Chore);
        Assert.Equal(new MpChore(9, 4, 20), mob?.Chore);
    }
}
