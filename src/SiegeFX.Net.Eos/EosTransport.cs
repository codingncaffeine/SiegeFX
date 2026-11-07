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
    // The lossy lane: world updates and poses, where only the newest counts.
    // Each is one whole packet; a lost one is never sent again and never
    // holds up the reliable frames on channel 0.
    const byte LossyChannel = 1;

    readonly EosPlatform _plat;
    readonly P2PInterface _p2p;
    readonly ProductUserId _local;
    bool _isHost;
    ulong _connReqNotify;
    ulong _connClosedNotify;
    ulong _connUpNotify;
    // Joiner: Epic re-sends a connection request a few times, so a host that
    // comes online a little later (it is loading the region after START) still
    // gets it; one that was online without listening drops it, and Epic then
    // waits ~45 s before asking again. So a request that is not up after 20 s
    // is withdrawn and made again, every 20 s (a relayed connection sets up in
    // a few seconds). A connection that is up but has
    // never carried a packet from the host in 10 s (the two sides settled on
    // different attempts) is dialed again too.
    const int FirstAskAgainMs = 20_000, SilentHostMs = 10_000;
    bool _hostUp;
    bool _heardOnThisConnection;
    long _askedAtMs, _upAtMs;
    int _askAgainMs = FirstAskAgainMs;

    // Epic carries at most P2PInterface.MAX_PACKET_SIZE (1170) bytes a packet,
    // and a game frame can be several times that (a world update in the region,
    // the snapshot in a JoinAccept). Each packet starts with a tag: a whole
    // frame, or a piece of one (more to come / the last). Everything travels
    // reliable and ordered on one channel, so a peer's pieces arrive in order
    // and are joined again here.
    const byte TagWhole = 0xF0, TagMore = 0xF1, TagLast = 0xF2;
    const int PieceBytes = P2PInterface.MAX_PACKET_SIZE - 1;
    const int MaxFrameBytes = 1 << 20;
    readonly Dictionary<int, MemoryStream> _partial = new();

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
            if (!_isHost)
            {
                _hostUp = false;
                _askedAtMs = Environment.TickCount64;
                _partial.Remove(id);
                // A joiner only ever talks to its host, and the host is a
                // player id, not a connection: the next packet sent asks Epic
                // for a new one. The host may just not be listening yet (it is
                // still loading the region after START), so the address stays.
                NetLog.Info($"eos: connection to the host closed ({info.Reason}); the next packet asks for a new one");
                PeerDisconnected?.Invoke(id);
                return;
            }
            _partial.Remove(id);
            lock (_lock)
            {
                _peers.Remove(id);
                info.RemoteUserId.ToString(out var b);
                if (b?.ToString() is { } key) _puidToId.Remove(key);
            }
            NetLog.Info($"eos: peer {id} connection closed ({info.Reason})");
            PeerDisconnected?.Invoke(id);
        });
        var upOpts = new AddNotifyPeerConnectionEstablishedOptions { LocalUserId = _local, SocketId = MakeSocket() };
        _connUpNotify = _p2p.AddNotifyPeerConnectionEstablished(ref upOpts, null, (ref OnPeerConnectionEstablishedInfo info) =>
        {
            if (!_isHost && _hostPuid is not null && PeerIdFor(info.RemoteUserId) == 0)
            {
                _hostUp = true;
                _heardOnThisConnection = false;
                _upAtMs = Environment.TickCount64;
                _askAgainMs = FirstAskAgainMs;
            }
            NetLog.Info($"eos: connection to {Short(info.RemoteUserId)} up ({info.NetworkType}, {info.ConnectionType})");
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
        // out by the lobby). The first packet sent (the game's JoinRequest,
        // right after this) opens the connection; the host auto-accepts. No
        // empty "hello" goes first: 0.5.2's test builds sent one, and a host
        // that never read it past (see Pump) never read anything after it.
        _hostPuid = ProductUserId.FromString(hostAddress);
        if (_hostPuid == null || !_hostPuid.IsValid())
        { NetLog.Error($"eos: bad host product-user-id '{hostAddress}'"); return Task.FromResult(false); }
        lock (_lock) { _peers[0] = _hostPuid; _puidToId[hostAddress] = 0; }
        _hostUp = false;
        _askedAtMs = Environment.TickCount64;
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
        if (payload.Length <= PieceBytes) { SendPacket(to, TagWhole, payload); return; }
        for (int off = 0; off < payload.Length; off += PieceBytes)
        {
            int n = Math.Min(PieceBytes, payload.Length - off);
            if (!SendPacket(to, off + n >= payload.Length ? TagLast : TagMore, payload.Slice(off, n))) return;
        }
    }

    bool SendPacket(ProductUserId to, byte tag, ReadOnlySpan<byte> body, bool lossy = false)
    {
        var packet = new byte[body.Length + 1];
        packet[0] = tag;
        body.CopyTo(packet.AsSpan(1));
        var opts = new SendPacketOptions
        {
            LocalUserId = _local,
            RemoteUserId = to,
            SocketId = MakeSocket(),
            Channel = lossy ? LossyChannel : Channel,
            Data = new ArraySegment<byte>(packet),
            // Lossy frames are not kept for a connection still being set up:
            // by the time it is, newer ones exist.
            AllowDelayedDelivery = !lossy,
            Reliability = lossy ? PacketReliability.UnreliableUnordered : PacketReliability.ReliableOrdered,
        };
        var r = _p2p.SendPacket(ref opts);
        if (r != Result.Success)
        {
            // A lossy frame with no connection up yet is refused on purpose;
            // say so now and then, not 15 times a second.
            if (!lossy || _lossyRefused++ % 150 == 0)
                NetLog.Warn($"eos: SendPacket {r} ({packet.Length} B{(lossy ? $", lossy lane, {_lossyRefused} refused so far" : "")})");
            return false;
        }
        return true;
    }
    long _lossyRefused;

    public void Send(int peerId, ReadOnlySpan<byte> payload)
    {
        ProductUserId? to;
        lock (_lock) _peers.TryGetValue(peerId, out to);
        if (to == null) { NetLog.Warn($"eos: send to unknown peer {peerId}"); return; }
        SendRaw(to, payload);
    }

    public int MaxUnreliableBytes => PieceBytes;

    public void SendUnreliable(int peerId, ReadOnlySpan<byte> payload)
    {
        ProductUserId? to;
        lock (_lock) _peers.TryGetValue(peerId, out to);
        if (to == null) return;
        if (payload.Length > PieceBytes) { SendRaw(to, payload); return; }   // too big for one packet: the reliable lane
        SendPacket(to, TagWhole, payload, lossy: true);
    }

    /// <summary>Drain all queued P2P packets into the rx queue. Called from
    /// the engine tick (after EosPlatform.Tick).</summary>
    public void Pump()
    {
        if (!_isHost && _hostPuid is not null)
        {
            long now = Environment.TickCount64;
            string? why = !_hostUp
                ? now - _askedAtMs > _askAgainMs ? $"no connection to the host after {_askAgainMs / 1000} s (not listening yet?)" : null
                : !_heardOnThisConnection && now - _upAtMs > SilentHostMs
                    ? $"the connection is up but nothing came from the host in {SilentHostMs / 1000} s" : null;
            if (why is not null)
            {
                var close = new CloseConnectionOptions { LocalUserId = _local, RemoteUserId = _hostPuid, SocketId = MakeSocket() };
                _p2p.CloseConnection(ref close);
                NetLog.Info($"eos: {why}; asking again");
                _hostUp = false;
                _askedAtMs = now;
                // Every 20 s: after a crash, the host keeps the old connection
                // until Epic times it out (~45 s); the next ask then gets in.
                _askAgainMs = FirstAskAgainMs;
            }
        }
        while (true)
        {
            // Success means a packet is waiting, even an EMPTY one (size 0).
            // An empty packet has to be received like any other: left in place
            // it stays at the head of the queue and every packet behind it,
            // JoinRequest and READY included, waits forever.
            var sizeOpts = new GetNextReceivedPacketSizeOptions { LocalUserId = _local, RequestedChannel = Channel };
            if (_p2p.GetNextReceivedPacketSize(ref sizeOpts, out uint size) != Result.Success)
                break;
            var buf = new byte[Math.Max(size, 1u)];
            var recvOpts = new ReceivePacketOptions { LocalUserId = _local, MaxDataSizeBytes = (uint)buf.Length, RequestedChannel = Channel };
            ProductUserId from = default!;
            SocketId sock = default;
            var r = _p2p.ReceivePacket(ref recvOpts, ref from, ref sock, out _, new ArraySegment<byte>(buf), out uint written);
            if (r != Result.Success) { NetLog.Warn($"eos: ReceivePacket {r} (packet of {size} B left in the queue)"); break; }
            int peer = _isHost ? MapPeer(from) : 0;
            if (!_isHost) _heardOnThisConnection = true;
            if (written == 0) continue;   // an empty packet: nothing for the game (0.5.2-test's opening hello)
            var body = buf.AsSpan(1, (int)written - 1);
            switch (buf[0])
            {
                case TagWhole:
                    if (_partial.Remove(peer)) NetLog.Warn($"eos: an unfinished frame from peer {peer} was dropped");
                    Deliver(peer, body.ToArray());
                    break;
                case TagMore:
                case TagLast:
                    if (!_partial.TryGetValue(peer, out var frame)) _partial[peer] = frame = new MemoryStream();
                    if (frame.Length + body.Length > MaxFrameBytes)
                    { _partial.Remove(peer); NetLog.Warn($"eos: a frame from peer {peer} passed {MaxFrameBytes} B — dropped"); break; }
                    frame.Write(body);
                    if (buf[0] == TagLast) { _partial.Remove(peer); Deliver(peer, frame.ToArray()); }
                    break;
                default:
                    // No tag: a build from before the pieces (0.5.2-test). Its
                    // frame goes up whole, so its JoinRequest is answered with
                    // the "different SiegeFX version" refusal.
                    Deliver(peer, buf.AsSpan(0, (int)written).ToArray());
                    break;
            }
        }
        // The lossy lane: each packet is one whole frame.
        while (true)
        {
            var sizeOpts = new GetNextReceivedPacketSizeOptions { LocalUserId = _local, RequestedChannel = LossyChannel };
            if (_p2p.GetNextReceivedPacketSize(ref sizeOpts, out uint size) != Result.Success)
                break;
            var buf = new byte[Math.Max(size, 1u)];
            var recvOpts = new ReceivePacketOptions { LocalUserId = _local, MaxDataSizeBytes = (uint)buf.Length, RequestedChannel = LossyChannel };
            ProductUserId from = default!;
            SocketId sock = default;
            var r = _p2p.ReceivePacket(ref recvOpts, ref from, ref sock, out _, new ArraySegment<byte>(buf), out uint written);
            if (r != Result.Success) { NetLog.Warn($"eos: ReceivePacket {r} on the lossy lane (packet of {size} B left in the queue)"); break; }
            int peer = _isHost ? MapPeer(from) : 0;
            if (!_isHost) _heardOnThisConnection = true;
            if (written >= 2 && buf[0] == TagWhole) Deliver(peer, buf.AsSpan(1, (int)written - 1).ToArray());
        }
    }

    void Deliver(int peer, byte[] frame) { lock (_lock) _rx.Enqueue((peer, frame)); }

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
        if (_connUpNotify != 0) _p2p.RemoveNotifyPeerConnectionEstablished(_connUpNotify);
        var close = new CloseConnectionsOptions { LocalUserId = _local, SocketId = MakeSocket() };
        _p2p.CloseConnections(ref close);
        NetLog.Info("eos: transport closed");
    }
}
