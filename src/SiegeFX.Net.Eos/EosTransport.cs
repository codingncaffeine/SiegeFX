using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using SiegeFX.Core.Net;

namespace SiegeFX.Net.Eos;

/// <summary>SC-MP-EOS P4 — ISessionTransport over EOS P2P. Peers are
/// ProductUserIds (opaque, relayed through Epic when direct punch fails —
/// the relay is why EOS is the insurance tier). We map them to small int
/// peer ids for the game layer. Auto-accepts incoming connections on our
/// socket; ReceivePacket is drained each Tick.</summary>
public sealed class EosTransport : ISessionTransport
{
    const string SocketName = "SIEGEFX";
    const byte Channel = 0;

    readonly EosPlatform _plat;
    readonly P2PInterface _p2p;
    readonly ProductUserId _local;
    bool _isHost;
    ulong _connReqNotify;
    ulong _connClosedNotify;

    readonly object _lock = new();
    readonly Dictionary<int, ProductUserId> _peers = new();      // gameId -> puid
    readonly Dictionary<string, int> _puidToId = new();          // puid str -> gameId
    readonly Queue<(int Peer, byte[] Data)> _rx = new();
    int _nextPeerId = 1;
    ProductUserId? _hostPuid;

    public string ProviderId => "eos";
    public IReadOnlyList<int> Peers { get { lock (_lock) return _peers.Keys.ToList(); } }
    public event Action<int>? PeerConnected;
    public event Action<int>? PeerDisconnected;

    public EosTransport(EosPlatform plat)
    {
        _plat = plat;
        _p2p = plat.Platform!.GetP2PInterface();
        _local = plat.LocalUser!;
        // Fire PeerDisconnected when EOS reports a P2P connection closed (peer
        // quit / lost / relay dropped) so the session reacts exactly as it does
        // on a LAN timeout — the diagnostics + roster cull stay provider-agnostic.
        var closedOpts = new AddNotifyPeerConnectionClosedOptions { LocalUserId = _local, SocketId = MakeSocket() };
        _connClosedNotify = _p2p.AddNotifyPeerConnectionClosed(ref closedOpts, null, (ref OnRemoteConnectionClosedInfo info) =>
        {
            int id = PeerIdFor(info.RemoteUserId);
            if (id < 0) return;
            lock (_lock)
            {
                _peers.Remove(id);
                info.RemoteUserId.ToString(out var b);
                if (b?.ToString() is { } key) _puidToId.Remove(key);
            }
            NetLog.Info($"eos: peer {id} connection closed ({info.Reason})");
            PeerDisconnected?.Invoke(id);
        });
    }

    int PeerIdFor(ProductUserId puid)
    {
        puid.ToString(out var buf);
        string key = buf?.ToString() ?? puid.ToString();
        lock (_lock) return _puidToId.TryGetValue(key, out var id) ? id : -1;
    }

    static SocketId MakeSocket() => new() { SocketName = SocketName };

    public Task<bool> ListenAsync(int port, CancellationToken ct)
    {
        _isHost = true;
        var sock = MakeSocket();
        var opts = new AddNotifyPeerConnectionRequestOptions { LocalUserId = _local, SocketId = sock };
        _connReqNotify = _p2p.AddNotifyPeerConnectionRequest(ref opts, null, (ref OnIncomingConnectionRequestInfo info) =>
        {
            var acc = new AcceptConnectionOptions { LocalUserId = _local, RemoteUserId = info.RemoteUserId, SocketId = MakeSocket() };
            var r = _p2p.AcceptConnection(ref acc);
            if (r == Result.Success)
            {
                int id = MapPeer(info.RemoteUserId);
                NetLog.Info($"eos: accepted peer {id} ({Short(info.RemoteUserId)})");
                PeerConnected?.Invoke(id);
            }
            else NetLog.Warn($"eos: AcceptConnection {r}");
        });
        NetLog.Info("eos: hosting — accepting P2P connections on socket " + SocketName);
        return Task.FromResult(true);
    }

    public Task<bool> ConnectAsync(string hostAddress, CancellationToken ct)
    {
        // For EOS the "address" is the host's ProductUserId string (handed
        // out by the lobby). Sending the first packet opens the connection;
        // the host auto-accepts.
        _hostPuid = ProductUserId.FromString(hostAddress);
        if (_hostPuid == null || !_hostPuid.IsValid())
        { NetLog.Error($"eos: bad host product-user-id '{hostAddress}'"); return Task.FromResult(false); }
        lock (_lock) { _peers[0] = _hostPuid; _puidToId[hostAddress] = 0; }
        // A tiny hello nudges the connection open (the game's JoinRequest
        // follows immediately via Send).
        SendRaw(_hostPuid, Array.Empty<byte>());
        NetLog.Info($"eos: connecting to host {Short(_hostPuid)}");
        return Task.FromResult(true);
    }

    int MapPeer(ProductUserId puid)
    {
        puid.ToString(out var buf);
        string key = buf?.ToString() ?? puid.ToString();
        lock (_lock)
        {
            if (_puidToId.TryGetValue(key, out var existing)) return existing;
            int id = _isHost ? _nextPeerId++ : 0;
            _peers[id] = puid; _puidToId[key] = id;
            return id;
        }
    }

    static string Short(ProductUserId p) { p.ToString(out var b); var s = b?.ToString() ?? ""; return s.Length > 8 ? s[..8] + "…" : s; }

    void SendRaw(ProductUserId to, ReadOnlySpan<byte> payload)
    {
        var opts = new SendPacketOptions
        {
            LocalUserId = _local,
            RemoteUserId = to,
            SocketId = MakeSocket(),
            Channel = Channel,
            Data = new ArraySegment<byte>(payload.ToArray()),
            AllowDelayedDelivery = true,
            Reliability = PacketReliability.ReliableOrdered,
        };
        var r = _p2p.SendPacket(ref opts);
        if (r != Result.Success) NetLog.Warn($"eos: SendPacket {r}");
    }

    public void Send(int peerId, ReadOnlySpan<byte> payload)
    {
        ProductUserId? to;
        lock (_lock) _peers.TryGetValue(peerId, out to);
        if (to == null) { NetLog.Warn($"eos: send to unknown peer {peerId}"); return; }
        SendRaw(to, payload);
    }

    /// <summary>Drain all queued P2P packets into the rx queue. Called from
    /// the engine tick (after EosPlatform.Tick).</summary>
    public void Pump()
    {
        while (true)
        {
            var sizeOpts = new GetNextReceivedPacketSizeOptions { LocalUserId = _local, RequestedChannel = Channel };
            if (_p2p.GetNextReceivedPacketSize(ref sizeOpts, out uint size) != Result.Success || size == 0)
                break;
            var buf = new byte[size];
            var recvOpts = new ReceivePacketOptions { LocalUserId = _local, MaxDataSizeBytes = size, RequestedChannel = Channel };
            ProductUserId from = default!;
            SocketId sock = default;
            var r = _p2p.ReceivePacket(ref recvOpts, ref from, ref sock, out _, new ArraySegment<byte>(buf), out uint written);
            if (r != Result.Success) break;
            int peer = _isHost ? MapPeer(from) : 0;
            if (written == 0) continue; // connection-open nudge
            var data = written == buf.Length ? buf : buf.AsSpan(0, (int)written).ToArray();
            lock (_lock) _rx.Enqueue((peer, data));
        }
    }

    public bool TryReceive(out int peerId, out byte[] payload)
    {
        lock (_lock)
        {
            if (_rx.Count == 0) { peerId = -1; payload = Array.Empty<byte>(); return false; }
            (peerId, payload) = _rx.Dequeue();
            return true;
        }
    }

    public void Dispose()
    {
        if (_connReqNotify != 0) _p2p.RemoveNotifyPeerConnectionRequest(_connReqNotify);
        if (_connClosedNotify != 0) _p2p.RemoveNotifyPeerConnectionClosed(_connClosedNotify);
        var close = new CloseConnectionsOptions { LocalUserId = _local, SocketId = MakeSocket() };
        _p2p.CloseConnections(ref close);
        NetLog.Info("eos: transport closed");
    }
}
