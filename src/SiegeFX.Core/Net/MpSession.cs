namespace SiegeFX.Core.Net;

/// <summary>SC-MP-EOS P3 — host-authoritative session driver. Sits above
/// <see cref="ISessionTransport"/> and speaks <see cref="MpMsg"/>. The host
/// owns the sim; clients send input up and receive world snapshots + state
/// deltas down. The engine supplies four callbacks (snapshot in/out, delta
/// apply, input apply) so this stays engine-agnostic and unit-testable.
/// Retail-faithful: character state is client-owned, world state is the
/// host's and disposable.</summary>
public sealed class MpSession : IDisposable
{
    public bool IsHost { get; }
    readonly ISessionTransport _t;

    // Host callbacks
    /// <summary>Host: produce the world snapshot bytes for a late joiner
    /// (the SaveFile capture path in the engine).</summary>
    public Func<byte[]>? BuildSnapshot;
    /// <summary>Host: enumerate the authoritative actor poses for a delta.</summary>
    public Func<IReadOnlyList<MpActorState>>? EnumerateActors;
    /// <summary>Host: apply a client's input to the sim.</summary>
    public Action<int, MpInputCmd, float, float, uint>? ApplyClientInput;
    /// <summary>Host: a player joined (id, name) — for the staging roster.</summary>
    public Action<int, string>? OnPlayerJoined;
    public Action<int>? OnPlayerLeft;

    // Client callbacks
    /// <summary>Client: consume the host's world snapshot on join.</summary>
    public Action<byte[]>? ApplySnapshot;
    /// <summary>Client: apply an authoritative state delta.</summary>
    public Action<uint, IReadOnlyList<MpActorState>>? ApplyDelta;
    /// <summary>Client: join accepted (assigned player id) / rejected.</summary>
    public Action<int>? OnJoinAccepted;
    public Action<string>? OnJoinRejected;

    public event Action<string>? OnChat;

    readonly string _localName;
    uint _tick;
    double _deltaTimer;
    const double DeltaInterval = 1.0 / 15.0; // 15 Hz authoritative deltas
    public int MaxPlayers = 8;
    int _playerCount = 1; // host counts as 1

    public MpSession(ISessionTransport transport, bool isHost, string localName)
    {
        _t = transport; IsHost = isHost; _localName = localName ?? "Player";
        _t.PeerDisconnected += OnPeerGone;
    }

    void OnPeerGone(int peer)
    {
        if (IsHost)
        {
            _playerCount = Math.Max(1, _playerCount - 1);
            OnPlayerLeft?.Invoke(peer);
            Broadcast(new MpWriter().U8((byte)MpMsg.PlayerLeft).U8((byte)peer).ToArray());
            NetLog.Info($"session: player {peer} left ({_playerCount}/{MaxPlayers})");
        }
    }

    /// <summary>Client entry: request to join once the transport connected.</summary>
    public void SendJoinRequest()
    {
        if (IsHost) return;
        _t.Send(0, new MpWriter().U8((byte)MpMsg.JoinRequest).Str(_localName).Span);
        NetLog.Info($"session: sent JoinRequest as '{_localName}'");
    }

    public void SendInput(MpInputCmd cmd, float x, float z, uint targetScid = 0)
    {
        if (IsHost) return;
        _t.Send(0, new MpWriter().U8((byte)MpMsg.Input)
            .U32(_tick).U8((byte)cmd).F32(x).F32(z).U32(targetScid).Span);
    }

    public void SendChat(string text)
    {
        var w = new MpWriter().U8((byte)MpMsg.Chat).Str(text, u16Len: true);
        if (IsHost) { RelayChat(0, text); } else _t.Send(0, w.Span);
    }

    /// <summary>Pump: drain received frames and (host) tick out state deltas.
    /// Call once per engine frame with the frame dt.</summary>
    public void Tick(double dt)
    {
        while (_t.TryReceive(out int peer, out var data))
            Handle(peer, data);

        if (IsHost)
        {
            _deltaTimer += dt;
            if (_deltaTimer >= DeltaInterval)
            {
                _deltaTimer = 0;
                _tick++;
                var actors = EnumerateActors?.Invoke();
                if (actors is { Count: > 0 } && _t.Peers.Count > 0)
                {
                    var w = new MpWriter(16 + actors.Count * 18);
                    w.U8((byte)MpMsg.StateDelta).U32(_tick).U16((ushort)Math.Min(actors.Count, ushort.MaxValue));
                    int n = Math.Min(actors.Count, ushort.MaxValue);
                    for (int i = 0; i < n; i++)
                    {
                        var a = actors[i];
                        w.U32(a.Scid).F32(a.X).F32(a.Y).F32(a.Z).U16(a.Life);
                    }
                    Broadcast(w.ToArray());
                }
            }
        }
    }

    void Broadcast(byte[] frame)
    {
        foreach (var peer in _t.Peers) _t.Send(peer, frame);
    }

    void Handle(int peer, byte[] data)
    {
        var r = new MpReader(data);
        var type = (MpMsg)r.U8();
        switch (type)
        {
            case MpMsg.JoinRequest when IsHost:
            {
                string name = r.Str();
                if (r.Bad) { NetLog.Warn($"session: malformed JoinRequest from peer {peer} — dropped"); return; }
                if (_playerCount >= MaxPlayers)
                {
                    _t.Send(peer, new MpWriter().U8((byte)MpMsg.JoinReject).Str("Game is full.").Span);
                    NetLog.Info($"session: rejected peer {peer} — full");
                    return;
                }
                _playerCount++;
                var snap = BuildSnapshot?.Invoke() ?? Array.Empty<byte>();
                var w = new MpWriter(8 + snap.Length);
                w.U8((byte)MpMsg.JoinAccept).U8((byte)peer).U32((uint)snap.Length);
                foreach (var b in snap) w.U8(b);
                _t.Send(peer, w.Span);
                OnPlayerJoined?.Invoke(peer, name);
                Broadcast(new MpWriter().U8((byte)MpMsg.PlayerJoined).U8((byte)peer).Str(name).ToArray());
                NetLog.Info($"session: player {peer} '{name}' joined, sent snapshot ({snap.Length}B), now {_playerCount}/{MaxPlayers}");
                break;
            }
            case MpMsg.Input when IsHost:
            {
                uint tick = r.U32();
                var cmd = (MpInputCmd)r.U8();
                float x = r.F32(), z = r.F32();
                uint scid = r.U32();
                if (r.Bad) { NetLog.Warn($"session: malformed Input from peer {peer} — dropped"); return; }
                ApplyClientInput?.Invoke(peer, cmd, x, z, scid);
                break;
            }
            case MpMsg.Chat when IsHost:
            {
                string text = r.Str(u16Len: true);
                if (!r.Bad) RelayChat(peer, text);
                break;
            }
            case MpMsg.JoinAccept when !IsHost:
            {
                int assigned = r.U8();
                uint len = r.U32();
                var snap = r.Rest((int)Math.Min(len, (uint)int.MaxValue));
                if (r.Bad) { NetLog.Error("session: malformed JoinAccept — snapshot dropped"); return; }
                ApplySnapshot?.Invoke(snap);
                OnJoinAccepted?.Invoke(assigned);
                NetLog.Info($"session: join accepted as player {assigned}, snapshot {snap.Length}B applied");
                break;
            }
            case MpMsg.JoinReject when !IsHost:
            {
                string reason = r.Str();
                OnJoinRejected?.Invoke(r.Bad ? "rejected" : reason);
                NetLog.Warn($"session: join rejected: {reason}");
                break;
            }
            case MpMsg.StateDelta when !IsHost:
            {
                uint tick = r.U32();
                int count = r.U16();
                var list = new List<MpActorState>(count);
                for (int i = 0; i < count && !r.Bad; i++)
                    list.Add(new MpActorState(r.U32(), r.F32(), r.F32(), r.F32(), r.U16()));
                if (r.Bad) { NetLog.Warn("session: truncated StateDelta — dropped"); return; }
                ApplyDelta?.Invoke(tick, list);
                break;
            }
            case MpMsg.PlayerJoined:
            {
                int p = r.U8(); string name = r.Str();
                if (!r.Bad) OnPlayerJoined?.Invoke(p, name);
                break;
            }
            case MpMsg.PlayerLeft:
            {
                int p = r.U8();
                if (!r.Bad) OnPlayerLeft?.Invoke(p);
                break;
            }
            case MpMsg.ChatRelay:
            {
                int p = r.U8(); string text = r.Str(u16Len: true);
                if (!r.Bad) OnChat?.Invoke($"[{p}] {text}");
                break;
            }
            default:
                NetLog.Warn($"session: unexpected msg {type} from peer {peer} (host={IsHost}) — dropped");
                break;
        }
    }

    void RelayChat(int fromPlayer, string text)
    {
        OnChat?.Invoke($"[{fromPlayer}] {text}");
        Broadcast(new MpWriter().U8((byte)MpMsg.ChatRelay).U8((byte)fromPlayer).Str(text, u16Len: true).ToArray());
    }

    public void Dispose()
    {
        _t.PeerDisconnected -= OnPeerGone;
    }
}
