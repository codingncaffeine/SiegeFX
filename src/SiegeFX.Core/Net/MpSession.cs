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
    /// <summary>Host: apply a client's rolled damage to a host-owned actor by
    /// SCID (scid, damage). The host runs the resulting death/loot locally; its
    /// life delta syncs the new life (and any kill) back to every client.</summary>
    public Action<uint, float>? ApplyClientHit;
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

    // Player-pose sync (movement is client-authoritative; the host fans the
    // whole set back out so every machine can render the other avatars).
    /// <summary>Both roles: read the LOCAL player's pose for broadcast.</summary>
    public Func<MpPlayerState>? BuildLocalPlayerState;
    /// <summary>Both roles: apply the OTHER players' poses (remote avatars). The
    /// local player (<see cref="LocalPlayerId"/>) is filtered out first.</summary>
    public Action<IReadOnlyList<MpPlayerState>>? ApplyPlayerStates;
    /// <summary>Client: host pressed START — leave staging, relaunch into the
    /// region and reconnect. (regionPath, difficulty).</summary>
    public Action<string, int>? OnGameStart;

    public event Action<string>? OnChat;
    /// <summary>Structured chat (player id, text) — the in-game overlay
    /// resolves the id to a name; the legacy OnChat string feed stays for
    /// the staging screen.</summary>
    public event Action<int, string>? OnChatFrom;
    /// <summary>Both roles: a player's character card arrived (appearance,
    /// name/class, stats). Fired for every player including echoes of the
    /// local one; consumers filter by <see cref="LocalPlayerId"/>.</summary>
    public Action<MpPlayerInfo>? OnPlayerInfo;
    readonly Dictionary<int, MpPlayerInfo> _infos = new(); // host: relay store for late joiners

    /// <summary>This machine's player id (host = 0; client = the id the host
    /// assigned in JoinAccept). Remote-avatar rendering filters this out.</summary>
    public int LocalPlayerId { get; private set; }

    readonly string _localName;
    uint _tick;
    double _deltaTimer;
    double _clientSendTimer;
    const double DeltaInterval = 1.0 / 15.0; // 15 Hz authoritative deltas
    public int MaxPlayers = 8;
    int _playerCount = 1; // host counts as 1
    readonly Dictionary<int, MpPlayerState> _players = new(); // host: player id -> latest pose

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
            _players.Remove(peer);
            OnPlayerLeft?.Invoke(peer);
            Broadcast(new MpWriter().U8((byte)MpMsg.PlayerLeft).U8((byte)peer).ToArray());
            NetLog.Info($"session: player {peer} left ({_playerCount}/{MaxPlayers})");
        }
    }

    /// <summary>Client entry: request to join once the transport connected.</summary>
    public void SendJoinRequest()
    {
        if (IsHost) return;
        _t.Send(0, new MpWriter().U8((byte)MpMsg.JoinRequest).U16(MpProtocol.Version).Str(_localName).Span);
        NetLog.Info($"session: sent JoinRequest as '{_localName}' (protocol v{MpProtocol.Version})");
    }

    public void SendInput(MpInputCmd cmd, float x, float z, uint targetScid = 0)
    {
        if (IsHost) return;
        _t.Send(0, new MpWriter().U8((byte)MpMsg.Input)
            .U32(_tick).U8((byte)cmd).F32(x).F32(z).U32(targetScid).Span);
    }

    /// <summary>Client: report a hit its player rolled on a host-owned actor.
    /// The host applies the damage authoritatively (its life delta then syncs
    /// the result back). Friend-trust: the client owns its own damage roll.</summary>
    public void SendClientHit(uint targetScid, float damage)
    {
        if (IsHost || targetScid == 0 || damage <= 0f) return;
        _t.Send(0, new MpWriter().U8((byte)MpMsg.ClientHit).U32(targetScid).F32(damage).Span);
    }

    public void SendChat(string text)
    {
        var w = new MpWriter().U8((byte)MpMsg.Chat).Str(text, u16Len: true);
        if (IsHost) { RelayChat(0, text); } else _t.Send(0, w.Span);
    }

    /// <summary>Both roles: publish this machine's character card. The host
    /// stores + fans it out (stamped id 0); a client sends it up for the host
    /// to stamp and relay. Call after joining and whenever the card changes
    /// (level-up, rename) — receivers treat it as a full replace.</summary>
    public void SendClientInfo(in MpPlayerInfo info)
    {
        if (IsHost)
        {
            var stamped = info with { Player = (byte)LocalPlayerId };
            _infos[LocalPlayerId] = stamped;
            OnPlayerInfo?.Invoke(stamped);
            Broadcast(BuildPlayerInfoFrame(stamped));
        }
        else
        {
            var w = new MpWriter().U8((byte)MpMsg.ClientInfo);
            info.WriteBody(w);
            _t.Send(0, w.Span);
        }
    }

    static byte[] BuildPlayerInfoFrame(in MpPlayerInfo info)
    {
        var w = new MpWriter().U8((byte)MpMsg.PlayerInfo).U8(info.Player);
        info.WriteBody(w);
        return w.ToArray();
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

                // Players: refresh our own pose (id 0), fan the whole set out to
                // clients, and hand the host its own view of the remote avatars.
                if (BuildLocalPlayerState is not null)
                    _players[LocalPlayerId] = BuildLocalPlayerState() with { Player = (byte)LocalPlayerId };
                if (_players.Count > 0)
                {
                    if (_t.Peers.Count > 0) Broadcast(BuildPlayerDelta());
                    EmitRemotePlayers();
                }

                // World actors (enemies) — host-authoritative, keyed by SCID.
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
        else
        {
            // Client: movement is client-owned, so report our own pose upstream.
            _clientSendTimer += dt;
            if (_clientSendTimer >= DeltaInterval)
            {
                _clientSendTimer = 0;
                if (BuildLocalPlayerState is not null)
                    SendClientState(BuildLocalPlayerState() with { Player = (byte)LocalPlayerId });
            }
        }
    }

    byte[] BuildPlayerDelta()
    {
        var w = new MpWriter(8 + _players.Count * 20);
        w.U8((byte)MpMsg.PlayerDelta).U32(_tick).U8((byte)Math.Min(_players.Count, byte.MaxValue));
        int n = 0;
        foreach (var s in _players.Values)
        {
            if (n++ >= byte.MaxValue) break;
            w.U8(s.Player).F32(s.X).F32(s.Y).F32(s.Z).F32(s.Yaw).U16(s.Life).U8(s.Flags);
        }
        return w.ToArray();
    }

    // Host renders the OTHER players (everyone but itself) from its own table.
    void EmitRemotePlayers()
    {
        if (ApplyPlayerStates is null) return;
        var list = new List<MpPlayerState>(_players.Count);
        foreach (var s in _players.Values) if (s.Player != LocalPlayerId) list.Add(s);
        if (list.Count > 0) ApplyPlayerStates(list);
    }

    void SendClientState(MpPlayerState s) =>
        _t.Send(0, new MpWriter().U8((byte)MpMsg.ClientState)
            .F32(s.X).F32(s.Y).F32(s.Z).F32(s.Yaw).U16(s.Life).U8(s.Flags).Span);

    /// <summary>Host: broadcast the START signal so every client leaves staging,
    /// relaunches into the region and reconnects in-world.</summary>
    public void SendGameStart(string regionPath, int difficulty)
    {
        if (!IsHost) return;
        Broadcast(new MpWriter().U8((byte)MpMsg.GameStart).Str(regionPath).U8((byte)difficulty).ToArray());
        NetLog.Info($"session: host broadcast GameStart region='{regionPath}' diff={difficulty} to {_t.Peers.Count} peer(s)");
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
                ushort ver = r.U16();
                string name = r.Str();
                if (r.Bad) { NetLog.Warn($"session: malformed JoinRequest from peer {peer} — dropped"); return; }
                if (ver != MpProtocol.Version)
                {
                    // Different build/protocol — reject cleanly rather than let
                    // the two desync on incompatible byte layouts. The friend
                    // test's single most likely failure; make it unmistakable.
                    string why = $"Version mismatch — host is protocol v{MpProtocol.Version}, you sent v{ver}. Both players need the same SiegeFX build.";
                    _t.Send(peer, new MpWriter().U8((byte)MpMsg.JoinReject).Str(why).Span);
                    NetLog.Warn($"session: rejected peer {peer} — protocol v{ver} != host v{MpProtocol.Version}");
                    return;
                }
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
                // Catch the late joiner up on every known character card.
                foreach (var info in _infos.Values)
                    _t.Send(peer, BuildPlayerInfoFrame(info));
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
            case MpMsg.ClientHit when IsHost:
            {
                uint scid = r.U32();
                float dmg = r.F32();
                if (r.Bad) { NetLog.Warn($"session: malformed ClientHit from peer {peer} — dropped"); return; }
                if (scid != 0 && dmg > 0f && !float.IsNaN(dmg) && !float.IsInfinity(dmg))
                    ApplyClientHit?.Invoke(scid, dmg);
                break;
            }
            case MpMsg.ClientState when IsHost:
            {
                float x = r.F32(), y = r.F32(), z = r.F32(), yaw = r.F32();
                ushort life = r.U16(); byte flags = r.U8();
                if (r.Bad) { NetLog.Warn($"session: malformed ClientState from peer {peer} — dropped"); return; }
                _players[peer] = new MpPlayerState((byte)peer, x, y, z, yaw, life, flags);
                break;
            }
            case MpMsg.Chat when IsHost:
            {
                string text = r.Str(u16Len: true);
                if (!r.Bad) RelayChat(peer, text);
                break;
            }
            case MpMsg.ClientInfo when IsHost:
            {
                var info = MpPlayerInfo.ReadBody(ref r, (byte)peer);
                if (r.Bad) { NetLog.Warn($"session: malformed ClientInfo from peer {peer} — dropped"); return; }
                _infos[peer] = info;
                OnPlayerInfo?.Invoke(info);
                Broadcast(BuildPlayerInfoFrame(info));
                break;
            }
            case MpMsg.PlayerInfo when !IsHost:
            {
                byte pid = r.U8();
                var info = MpPlayerInfo.ReadBody(ref r, pid);
                if (r.Bad) { NetLog.Warn("session: malformed PlayerInfo — dropped"); return; }
                OnPlayerInfo?.Invoke(info);
                break;
            }
            case MpMsg.JoinAccept when !IsHost:
            {
                int assigned = r.U8();
                uint len = r.U32();
                var snap = r.Rest((int)Math.Min(len, (uint)int.MaxValue));
                if (r.Bad) { NetLog.Error("session: malformed JoinAccept — snapshot dropped"); return; }
                LocalPlayerId = assigned;
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
            case MpMsg.PlayerDelta when !IsHost:
            {
                uint tick = r.U32();
                int count = r.U8();
                var list = new List<MpPlayerState>(count);
                for (int i = 0; i < count && !r.Bad; i++)
                {
                    byte pid = r.U8();
                    float x = r.F32(), y = r.F32(), z = r.F32(), yaw = r.F32();
                    ushort life = r.U16(); byte flags = r.U8();
                    if (pid != LocalPlayerId) list.Add(new MpPlayerState(pid, x, y, z, yaw, life, flags));
                }
                if (r.Bad) { NetLog.Warn("session: truncated PlayerDelta — dropped"); return; }
                if (list.Count > 0) ApplyPlayerStates?.Invoke(list);
                break;
            }
            case MpMsg.GameStart when !IsHost:
            {
                string region = r.Str();
                int diff = r.U8();
                if (r.Bad) { NetLog.Error("session: malformed GameStart — ignored"); return; }
                NetLog.Info($"session: received GameStart region='{region}' diff={diff}");
                OnGameStart?.Invoke(region, diff);
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
                if (!r.Bad) { OnChat?.Invoke($"[{p}] {text}"); OnChatFrom?.Invoke(p, text); }
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
        OnChatFrom?.Invoke(fromPlayer, text);
        Broadcast(new MpWriter().U8((byte)MpMsg.ChatRelay).U8((byte)fromPlayer).Str(text, u16Len: true).ToArray());
    }

    public void Dispose()
    {
        _t.PeerDisconnected -= OnPeerGone;
    }
}
