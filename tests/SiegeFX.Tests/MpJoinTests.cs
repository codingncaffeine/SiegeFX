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
}
