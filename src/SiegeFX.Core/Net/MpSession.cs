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

    // ── SC-MP-LOOT / give / recruit ─────────────────────────────────────
    /// <summary>Host: a client asks to take pile netId (peer, netId).</summary>
    public Action<int, uint>? OnLootTake;
    /// <summary>Host: a client dropped items at (x,y,z) (peer, x, y, z, entries).</summary>
    public Action<int, float, float, float, List<(string Slot, string Ref)>>? OnLootDrop;
    /// <summary>Client: the host announced an authoritative pile.</summary>
    public Action<uint, float, float, float, List<(string Slot, string Ref)>>? OnLootSpawn;
    /// <summary>Client: a pile resolved (netId, takerPlayer).</summary>
    public Action<uint, int>? OnLootGone;
    /// <summary>Both: an item was handed over (fromPlayer, targetPlayer, slot, ref).
    /// Fires on the machine whose player is the target (host relays).</summary>
    public Action<int, int, string, string>? OnItemGive;
    /// <summary>Host: a client asks to hire NPC scid (peer, scid).</summary>
    public Action<int, uint>? OnRecruitRequest;
    /// <summary>Both: the host granted a hire (scid, owningPlayer).</summary>
    public Action<uint, int>? OnRecruitGrant;
    /// <summary>Client: a host-run enemy hit this machine's player (damage).</summary>
    public Action<float>? OnPlayerHit;
    /// <summary>Both: another machine shattered breakable prop scid.</summary>
    public Action<uint>? OnPropBreak;
    /// <summary>Client: the host spawned an actor at runtime (scid, template, x, y, z).</summary>
    public Action<uint, string, float, float, float>? OnActorSpawn;

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
    // Host: each joined peer's JoinAccept, re-sent when its JoinRequest repeats
    // (the first accept was lost) without counting the player twice.
    readonly Dictionary<int, byte[]> _joinAccepts = new();
    // Client: JoinRequest repeats until the host answers; UDP drops are silent
    // and a client that never hears JoinAccept plays as player 0, the host's id.
    bool _joinPending;
    // Client: until JoinAccept names our player id, LocalPlayerId is 0 (the
    // host's) and our own pose in a PlayerDelta would read as someone else's.
    bool _joined;
    double _joinRetryTimer;
    const double JoinRetryInterval = 1.0;
    /// <summary>Largest compressed snapshot sent in a JoinAccept. The frame is
    /// one UDP datagram (65507 B ceiling, less transport and AEAD headers), and
    /// smaller frames fragment less on the internet. Larger snapshots are left
    /// out: the player id still arrives.</summary>
    public const int MaxSnapshotBytes = 32 * 1024;
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
            if (_joinAccepts.Remove(peer)) _playerCount = Math.Max(1, _playerCount - 1);
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
        _joinPending = true;
        _joinRetryTimer = 0;
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

    static void WriteEntries(MpWriter w, IReadOnlyList<(string Slot, string Ref)> entries)
    {
        int n = Math.Min(entries.Count, byte.MaxValue);
        w.U8((byte)n);
        for (int i = 0; i < n; i++) { w.Str(entries[i].Slot); w.Str(entries[i].Ref); }
    }
    static List<(string Slot, string Ref)> ReadEntries(ref MpReader r)
    {
        int n = r.U8();
        var list = new List<(string, string)>(n);
        for (int i = 0; i < n && !r.Bad; i++) list.Add((r.Str(), r.Str()));
        return list;
    }

    /// <summary>Host: announce an authoritative pile (toPeer -1 = everyone).</summary>
    public void SendLootSpawn(uint netId, float x, float y, float z,
                              IReadOnlyList<(string Slot, string Ref)> entries, int toPeer = -1)
    {
        if (!IsHost) return;
        var w = new MpWriter().U8((byte)MpMsg.LootSpawn).U32(netId).F32(x).F32(y).F32(z);
        WriteEntries(w, entries);
        if (toPeer < 0) Broadcast(w.ToArray()); else _t.Send(toPeer, w.Span);
    }

    /// <summary>Host: resolve a pile — taker adds contents, everyone removes.</summary>
    public void SendLootGone(uint netId, int taker)
    {
        if (!IsHost) return;
        Broadcast(new MpWriter().U8((byte)MpMsg.LootGone).U32(netId).U8((byte)taker).ToArray());
    }

    public void SendLootTake(uint netId)
    {
        if (IsHost) return;
        _t.Send(0, new MpWriter().U8((byte)MpMsg.LootTake).U32(netId).Span);
    }

    public void SendLootDrop(float x, float y, float z, IReadOnlyList<(string Slot, string Ref)> entries)
    {
        if (IsHost) return;
        var w = new MpWriter().U8((byte)MpMsg.LootDrop).F32(x).F32(y).F32(z);
        WriteEntries(w, entries);
        _t.Send(0, w.Span);
    }

    /// <summary>Hand an item to another player. The host applies/relays;
    /// a client sends up for routing.</summary>
    public void SendItemGive(int targetPlayer, string slot, string itemRef)
    {
        var w = new MpWriter().U8((byte)MpMsg.ItemGive)
            .U8((byte)LocalPlayerId).U8((byte)targetPlayer).Str(slot).Str(itemRef, u16Len: true);
        if (IsHost) RouteItemGive(LocalPlayerId, targetPlayer, slot, itemRef);
        else _t.Send(0, w.Span);
    }

    void RouteItemGive(int from, int target, string slot, string itemRef)
    {
        if (target == LocalPlayerId) { OnItemGive?.Invoke(from, target, slot, itemRef); return; }
        var w = new MpWriter().U8((byte)MpMsg.ItemGive)
            .U8((byte)from).U8((byte)target).Str(slot).Str(itemRef, u16Len: true);
        _t.Send(target, w.Span);
    }

    public void SendRecruitRequest(uint scid)
    {
        if (IsHost) return;
        _t.Send(0, new MpWriter().U8((byte)MpMsg.RecruitRequest).U32(scid).Span);
    }

    /// <summary>Host: a host-run enemy hit remote player <paramref name="player"/>;
    /// that player's machine owns its life and applies the damage.</summary>
    public void SendPlayerHit(int player, float damage)
    {
        if (!IsHost || player == LocalPlayerId || damage <= 0f) return;
        _t.Send(player, new MpWriter().U8((byte)MpMsg.PlayerHit).F32(damage).Span);
    }

    /// <summary>Both: this machine shattered breakable prop <paramref name="scid"/>.
    /// The host relays it to the other clients.</summary>
    public void SendPropBreak(uint scid)
    {
        if (scid == 0) return;
        var frame = new MpWriter().U8((byte)MpMsg.PropBreak).U32(scid).ToArray();
        if (IsHost) Broadcast(frame); else _t.Send(0, frame);
    }

    /// <summary>Host: a runtime-spawned actor clients must mirror under the same
    /// scid; to one peer (a late joiner) or, with -1, to everyone.</summary>
    public void SendActorSpawn(uint scid, string template, float x, float y, float z, int toPeer = -1)
    {
        if (!IsHost) return;
        var frame = new MpWriter().U8((byte)MpMsg.ActorSpawn).U32(scid).Str(template).F32(x).F32(y).F32(z).ToArray();
        if (toPeer >= 0) _t.Send(toPeer, frame); else Broadcast(frame);
    }

    /// <summary>Host: announce a granted hire to every machine (self included).</summary>
    public void SendRecruitGrant(uint scid, int owningPlayer)
    {
        if (!IsHost) return;
        OnRecruitGrant?.Invoke(scid, owningPlayer);
        Broadcast(new MpWriter().U8((byte)MpMsg.RecruitGrant).U32(scid).U8((byte)owningPlayer).ToArray());
    }

    /// <summary>Pump: drain received frames and (host) tick out state deltas.
    /// Call once per engine frame with the frame dt.</summary>
    public void Tick(double dt)
    {
        while (_t.TryReceive(out int peer, out var data))
            Handle(peer, data);

        if (!IsHost && _joinPending && _t.Peers.Count > 0)
        {
            _joinRetryTimer += dt;
            if (_joinRetryTimer >= JoinRetryInterval)
            {
                _joinRetryTimer = 0;
                _t.Send(0, new MpWriter().U8((byte)MpMsg.JoinRequest).U16(MpProtocol.Version).Str(_localName).Span);
                NetLog.Warn("session: no JoinAccept yet — JoinRequest re-sent");
            }
        }

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
                if (_joinAccepts.TryGetValue(peer, out var accepted))
                {
                    _t.Send(peer, accepted);
                    NetLog.Info($"session: player {peer} asked again — JoinAccept re-sent");
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
                var packed = MpSnapshot.Pack(snap);
                if (packed.Length > MaxSnapshotBytes)
                {
                    NetLog.Warn($"session: snapshot {snap.Length}B packs to {packed.Length}B > {MaxSnapshotBytes}B — sent without it");
                    packed = Array.Empty<byte>();
                }
                var w = new MpWriter(8 + packed.Length);
                w.U8((byte)MpMsg.JoinAccept).U8((byte)peer).U32((uint)packed.Length);
                foreach (var b in packed) w.U8(b);
                var accept = w.ToArray();
                _joinAccepts[peer] = accept;
                _t.Send(peer, accept);
                OnPlayerJoined?.Invoke(peer, name);
                Broadcast(new MpWriter().U8((byte)MpMsg.PlayerJoined).U8((byte)peer).Str(name).ToArray());
                // Catch the late joiner up on every known character card.
                foreach (var info in _infos.Values)
                    _t.Send(peer, BuildPlayerInfoFrame(info));
                NetLog.Info($"session: player {peer} '{name}' joined, sent snapshot ({snap.Length}B, {packed.Length}B packed), now {_playerCount}/{MaxPlayers}");
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
            case MpMsg.LootTake when IsHost:
            {
                uint id = r.U32();
                if (r.Bad || id == 0) return;
                OnLootTake?.Invoke(peer, id);
                break;
            }
            case MpMsg.LootDrop when IsHost:
            {
                float x = r.F32(), y = r.F32(), z = r.F32();
                var entries = ReadEntries(ref r);
                if (r.Bad) { NetLog.Warn($"session: malformed LootDrop from peer {peer} — dropped"); return; }
                OnLootDrop?.Invoke(peer, x, y, z, entries);
                break;
            }
            case MpMsg.LootSpawn when !IsHost:
            {
                uint id = r.U32();
                float x = r.F32(), y = r.F32(), z = r.F32();
                var entries = ReadEntries(ref r);
                if (r.Bad || id == 0) { NetLog.Warn("session: malformed LootSpawn — dropped"); return; }
                OnLootSpawn?.Invoke(id, x, y, z, entries);
                break;
            }
            case MpMsg.LootGone when !IsHost:
            {
                uint id = r.U32(); int taker = r.U8();
                if (r.Bad || id == 0) return;
                OnLootGone?.Invoke(id, taker);
                break;
            }
            case MpMsg.ItemGive:
            {
                int from = r.U8(), target = r.U8();
                string slot = r.Str(); string itemRef = r.Str(u16Len: true);
                if (r.Bad || string.IsNullOrEmpty(itemRef)) return;
                if (IsHost) RouteItemGive(from, target, slot, itemRef);
                else if (target == LocalPlayerId) OnItemGive?.Invoke(from, target, slot, itemRef);
                break;
            }
            case MpMsg.PlayerHit when !IsHost:
            {
                float damage = r.F32();
                if (r.Bad || !(damage > 0f) || float.IsInfinity(damage)) return;
                OnPlayerHit?.Invoke(damage);
                break;
            }
            case MpMsg.PropBreak:
            {
                uint scid = r.U32();
                if (r.Bad || scid == 0) return;
                OnPropBreak?.Invoke(scid);
                if (IsHost)
                    foreach (var other in _t.Peers)
                        if (other != peer) _t.Send(other, data);
                break;
            }
            case MpMsg.ActorSpawn when !IsHost:
            {
                uint scid = r.U32(); string template = r.Str();
                float x = r.F32(), y = r.F32(), z = r.F32();
                if (r.Bad || scid == 0 || template.Length == 0) return;
                OnActorSpawn?.Invoke(scid, template, x, y, z);
                break;
            }
            case MpMsg.RecruitRequest when IsHost:
            {
                uint scid = r.U32();
                if (r.Bad || scid == 0) return;
                OnRecruitRequest?.Invoke(peer, scid);
                break;
            }
            case MpMsg.RecruitGrant when !IsHost:
            {
                uint scid = r.U32(); int player = r.U8();
                if (r.Bad || scid == 0) return;
                OnRecruitGrant?.Invoke(scid, player);
                break;
            }
            case MpMsg.JoinAccept when !IsHost:
            {
                int assigned = r.U8();
                uint len = r.U32();
                var packed = r.Rest((int)Math.Min(len, (uint)int.MaxValue));
                if (r.Bad) { NetLog.Error("session: malformed JoinAccept — snapshot dropped"); return; }
                if (!_joinPending) return; // a re-sent accept we already applied
                _joinPending = false;
                _joined = true;
                byte[] snap;
                try { snap = MpSnapshot.Unpack(packed); }
                catch (Exception ex) { NetLog.Error($"session: JoinAccept snapshot unreadable ({ex.Message}) — joined without it"); snap = Array.Empty<byte>(); }
                LocalPlayerId = assigned;
                ApplySnapshot?.Invoke(snap);
                OnJoinAccepted?.Invoke(assigned);
                NetLog.Info($"session: join accepted as player {assigned}, snapshot {snap.Length}B applied");
                break;
            }
            case MpMsg.JoinReject when !IsHost:
            {
                string reason = r.Str();
                _joinPending = false;
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
                if (!_joined) return;
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
