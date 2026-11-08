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
    /// <summary>Host: a player joined (id, name).</summary>
    public Action<int, string>? OnPlayerJoined;
    /// <summary>A player who had joined left (id). Connections that close before
    /// their JoinRequest is answered were never players and do not fire it.</summary>
    public Action<int>? OnPlayerLeft;
    /// <summary>Both roles: everyone in the game changed: a join, a leave or a
    /// ready mark. The host keeps the roster (host first, always ready) and sends
    /// it to every client, so all machines show the same names and marks.</summary>
    public Action<IReadOnlyList<MpRosterEntry>>? OnRoster;

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
    /// <summary>Client: the game is over: the host left or the time limit ran
    /// out (<see cref="MpMsg.GameEnd"/>).</summary>
    public Action<MpGameEndReason>? OnGameEnded;

    // ── The host's game rules (staging_area_game_settings.gas) ──────────
    /// <summary>The rules this game plays by: the host's own, or the copy it
    /// sent (a client's starts at the defaults until the first one arrives).</summary>
    public MpGameSettings Settings { get; private set; } = new();
    /// <summary>Both roles: the rules changed (before, after). The host's
    /// fires on its own change too.</summary>
    public Action<MpGameSettings, MpGameSettings>? OnSettings;
    /// <summary>Host: the game password ("" = none). It never leaves the host.</summary>
    public string Password { get; private set; } = "";
    /// <summary>Client: the password this player typed to join ("" = none).</summary>
    public string JoinPassword = "";

    /// <summary>Client: the rules this player already knows (carried from the
    /// staging area into the region), taken quietly; the host's own copy
    /// replaces them when it arrives.</summary>
    public void AdoptSettings(MpGameSettings known)
    {
        if (!IsHost) Settings = known.Clamped();
    }

    /// <summary>Host: new rules for everyone. The player limit cannot drop
    /// below the players already in the game.</summary>
    public void SetSettings(MpGameSettings next, string? password = null)
    {
        if (!IsHost) return;
        if (password is not null) Password = password;
        next = (next with { HasPassword = Password.Length > 0 }).Clamped();
        if (next.PlayerLimit < _playerCount) next = next with { PlayerLimit = Math.Max(_playerCount, MpGameSettings.MinPlayers) };
        var before = Settings;
        Settings = next;
        if (before == next) return;
        OnSettings?.Invoke(before, next);
        Broadcast(BuildSettingsFrame());
        NetLog.Info($"session: game settings: {string.Join(", ", next.Summary(_playerCount))}");
    }

    byte[] BuildSettingsFrame()
    {
        var w = new MpWriter(96).U8((byte)MpMsg.Settings);
        Settings.Write(w);
        w.U32((uint)Math.Max(0, GameSeconds));
        return w.ToArray();
    }

    /// <summary>Seconds the game has been played (paused time not counted): the
    /// host's clock, which every Settings frame carries, kept running between
    /// frames on the joiners. It drives the time limit.</summary>
    public double GameSeconds { get; private set; }

    /// <summary>Host, in the game: the players who were in the staging area when
    /// it started. Every machine relaunches into the region and joins again, so
    /// they are let in even when the host does not allow joining in progress.</summary>
    public HashSet<string> StartingParty { get; } = new(StringComparer.Ordinal);

    /// <summary>How many players are in the game (the host counts).</summary>
    public int PlayerCount => IsHost ? _playerCount : Math.Max(1, _roster.Count);

    // ── Teams (staging_area_select_team.gas) ────────────────────────────
    /// <summary>This player's team in the staging area (0 = none, 1..16 a team
    /// sign). Re-sent until the host's roster shows it, like the ready mark.</summary>
    public void SetTeam(int team)
    {
        team = Math.Clamp(team, 0, MpTeams.Count);
        _wantTeam = team;
        if (IsHost)
        {
            if (_roster.TryGetValue(LocalPlayerId, out var me) && me.Team != team) { _roster[LocalPlayerId] = me with { Team = team }; RosterChanged(); }
            return;
        }
        _teamRetryTimer = 0;
        if (_joined) SendTeam();
    }
    int _wantTeam;
    double _teamRetryTimer;
    void SendTeam() => Out(0, new MpWriter().U8((byte)MpMsg.TeamPick).U8((byte)_wantTeam).Span);

    // ── Pausing ("Allow players to pause the game") ─────────────────────
    /// <summary>The whole game is paused (every machine stops its world).</summary>
    public bool Paused { get; private set; }
    /// <summary>Both roles: the game was paused or resumed (paused, by which player).</summary>
    public Action<bool, int>? OnPause;

    /// <summary>Pause or resume the game for everyone. Only when the host allows
    /// pausing; a client asks the host, which answers everyone.</summary>
    public bool RequestPause(bool paused)
    {
        if (!Settings.AllowPause) return false;
        if (IsHost) { ApplyPause(paused, LocalPlayerId); return true; }
        if (!_joined) return false;
        Out(0, new MpWriter().U8((byte)MpMsg.Pause).U8(paused ? (byte)1 : (byte)0).Span);
        return true;
    }

    void ApplyPause(bool paused, int by)
    {
        if (Paused == paused) return;
        Paused = paused;
        OnPause?.Invoke(paused, by);
        Broadcast(new MpWriter().U8((byte)MpMsg.Pause).U8(paused ? (byte)1 : (byte)0).U8((byte)by).ToArray());
        NetLog.Info($"session: player {by} {(paused ? "paused" : "resumed")} the game");
    }

    // ── Team chat ("Chat Window (Send to Team)", Shift+Enter) ──────────
    /// <summary>Both roles: a teammate's line (player, text).</summary>
    public Action<int, string>? OnTeamChat;

    /// <summary>Send a line to this player's team (team play on and a team
    /// chosen). False when there is no team to send to.</summary>
    public bool SendTeamChat(string text)
    {
        if (!Settings.TeamPlay || string.IsNullOrWhiteSpace(text)) return false;
        if (IsHost) { RouteTeamChat(LocalPlayerId, text); return TeamOf(LocalPlayerId) > 0; }
        if (!_joined) return false;
        Out(0, new MpWriter().U8((byte)MpMsg.TeamChat).Str(text, u16Len: true).Span);
        return true;
    }

    int TeamOf(int player) => _roster.TryGetValue(player, out var e) ? e.Team : 0;

    void RouteTeamChat(int from, string text)
    {
        int team = TeamOf(from);
        if (team == 0 || !Settings.TeamPlay) return;
        var frame = new MpWriter().U8((byte)MpMsg.TeamChat).U8((byte)from).Str(text, u16Len: true).ToArray();
        foreach (var e in _roster.Values)
        {
            if (e.Player == from || e.Team != team) continue;
            if (e.Player == LocalPlayerId) OnTeamChat?.Invoke(from, text);
            else Out(e.Player, frame);
        }
    }

    // ── Player-vs-player combat ─────────────────────────────────────────
    /// <summary>This player struck player <paramref name="target"/> (only when
    /// the host enables player-vs-player combat). The struck player's machine
    /// owns its life and applies the damage.</summary>
    public void SendPvpHit(int target, float damage)
    {
        if (!Settings.PlayerVsPlayer || target == LocalPlayerId || !(damage > 0f) || float.IsInfinity(damage)) return;
        if (IsHost) SendPlayerHit(target, damage, LocalPlayerId);
        else Out(0, new MpWriter().U8((byte)MpMsg.PvpHit).U8((byte)target).F32(damage).Span);
    }
    /// <summary>Client: seconds since the last frame from the host. The host
    /// sends at least a bare world update twice a second, so a long silence
    /// means the host is gone even when no <see cref="MpMsg.GameEnd"/> came.</summary>
    public double SecondsSinceHost { get; private set; }
    /// <summary>Host: is this remote player outside the host's view? Marks the
    /// player in the pose frames (the co-op leash).</summary>
    public Func<int, bool>? IsPlayerOutOfView;
    /// <summary>Client: the host cannot see this player (from the last pose frame).</summary>
    public bool OutOfHostView { get; private set; }
    /// <summary>Client: the host's pose from the last pose frame (null until one arrives).</summary>
    public MpPlayerState? HostState { get; private set; }

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
    /// <summary>This machine's player was hit by a host-run enemy or, in
    /// player-vs-player combat, by another player (damage, attacker player id;
    /// 255 = a monster).</summary>
    public Action<float, int>? OnPlayerHit;
    public const int MonsterAttacker = 255;
    /// <summary>Both: another machine shattered breakable prop scid.</summary>
    public Action<uint>? OnPropBreak;
    /// <summary>Client: the host spawned an actor at runtime (scid, template, x, y, z).</summary>
    public Action<uint, string, float, float, float>? OnActorSpawn;

    /// <summary>This machine's player id (host = 0; client = the id the host
    /// assigned in JoinAccept). Remote-avatar rendering filters this out.</summary>
    public int LocalPlayerId { get; private set; }

    readonly string _localName;
    uint _tick;
    /// <summary>Host: the number of the last world update sent (15 a second).</summary>
    public uint CurrentTick => _tick;
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
    // host's) and our own pose would be sent as the host's.
    bool _joined;
    double _joinRetryTimer;
    const double JoinRetryInterval = 1.0;
    /// <summary>Largest compressed snapshot sent in a JoinAccept. The frame is
    /// one UDP datagram (65507 B ceiling, less transport and AEAD headers), and
    /// smaller frames fragment less on the internet. Larger snapshots are left
    /// out: the player id still arrives.</summary>
    public const int MaxSnapshotBytes = 32 * 1024;
    readonly Dictionary<int, MpPlayerState> _players = new(); // host: player id -> latest pose
    // The staging roster: the host's own (the truth), or a client's latest copy.
    readonly SortedDictionary<int, MpRosterEntry> _roster = new();
    double _rosterTimer;                     // host: re-sent now and then (a LAN frame can drop)
    const double RosterInterval = 2.0;
    bool _wantReady;                         // client: the mark this player last chose
    double _readyRetryTimer;                 // client: re-sent until the host's roster shows it

    public MpSession(ISessionTransport transport, bool isHost, string localName)
    {
        _t = transport; IsHost = isHost; _localName = localName ?? "Player";
        _t.PeerDisconnected += OnPeerGone;
        if (IsHost) _roster[0] = new MpRosterEntry(0, _localName, true, 0);
    }

    /// <summary>Everyone in the game as this machine knows it, host first.</summary>
    public IReadOnlyList<MpRosterEntry> Roster => _roster.Values.ToList();

    /// <summary>Client: the host answered the JoinRequest. The host is always joined.</summary>
    public bool Joined => IsHost || _joined;

    /// <summary>Host: the names of the players who are not ready yet.</summary>
    public IReadOnlyList<string> NotReady =>
        _roster.Values.Where(e => !e.Ready).Select(e => e.Name).ToList();

    /// <summary>Client: mark this player ready (or not) in the staging area. The
    /// mark is re-sent until the host's roster shows it, so a dropped frame
    /// cannot leave the host showing the old one. The host is always ready.</summary>
    public void SetReady(bool ready)
    {
        if (IsHost) return;
        _wantReady = ready;
        _readyRetryTimer = 0;
        if (_joined) SendReady();
    }

    void SendReady() =>
        Out(0, new MpWriter().U8((byte)MpMsg.Ready).U8(_wantReady ? (byte)1 : (byte)0).Span);

    byte[] BuildRosterFrame()
    {
        var w = new MpWriter(4 + _roster.Count * 20);
        int n = Math.Min(_roster.Count, byte.MaxValue);
        w.U8((byte)MpMsg.Roster).U8((byte)n);
        foreach (var e in _roster.Values.Take(n))
            w.U8((byte)e.Player).U8(e.Ready ? (byte)1 : (byte)0).U8((byte)e.Team).Str(e.Name);
        return w.ToArray();
    }

    // Host: the roster changed; show it here and send it to everyone.
    void RosterChanged()
    {
        _rosterTimer = 0;
        OnRoster?.Invoke(Roster);
        Broadcast(BuildRosterFrame());
    }

    void OnPeerGone(int peer)
    {
        if (IsHost)
        {
            _players.Remove(peer);
            _senders.Remove(peer);
            _clientSeq.Remove(peer);
            if (!_joinAccepts.Remove(peer))
            {
                // A connection that closed before its JoinRequest was answered
                // (or before one arrived) was never a player.
                NetLog.Info($"session: connection {peer} closed before it joined");
                return;
            }
            _playerCount = Math.Max(1, _playerCount - 1);
            _roster.Remove(peer, out var gone);
            OnPlayerLeft?.Invoke(peer);
            Broadcast(new MpWriter().U8((byte)MpMsg.PlayerLeft).U8((byte)peer).ToArray());
            NetLog.Info($"session: player {peer} '{gone.Name}' left ({_playerCount}/{MaxPlayers})");
            RosterChanged();
        }
        else if (_joinPending)
            NetLog.Info("session: the connection closed before the host answered; the JoinRequest keeps going");
    }

    /// <summary>Client entry: request to join once the transport connected.</summary>
    public void SendJoinRequest()
    {
        if (IsHost) return;
        _joinPending = true;
        _joinRetryTimer = 0;
        Out(0, BuildJoinRequest());
        NetLog.Info($"session: sent JoinRequest as '{_localName}' (protocol v{MpProtocol.Version}{(JoinPassword.Length > 0 ? ", with a password" : "")})");
    }

    byte[] BuildJoinRequest() =>
        new MpWriter().U8((byte)MpMsg.JoinRequest).U16(MpProtocol.Version).Str(_localName).Str(JoinPassword).ToArray();

    public void SendInput(MpInputCmd cmd, float x, float z, uint targetScid = 0)
    {
        if (IsHost) return;
        Out(0, new MpWriter().U8((byte)MpMsg.Input)
            .U32(_tick).U8((byte)cmd).F32(x).F32(z).U32(targetScid).Span);
    }

    /// <summary>Client: report a hit its player rolled on a host-owned actor.
    /// The host applies the damage authoritatively (its life delta then syncs
    /// the result back). Friend-trust: the client owns its own damage roll.</summary>
    public void SendClientHit(uint targetScid, float damage)
    {
        if (IsHost || targetScid == 0 || damage <= 0f) return;
        Out(0, new MpWriter().U8((byte)MpMsg.ClientHit).U32(targetScid).F32(damage).Span);
        NetLog.Info($"client hit -> host: scid {targetScid:x8} dmg {damage:F1}");
    }

    public void SendChat(string text)
    {
        var w = new MpWriter().U8((byte)MpMsg.Chat).Str(text, u16Len: true);
        if (IsHost) { RelayChat(0, text); } else Out(0, w.Span);
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
            Out(0, w.Span);
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
        if (toPeer < 0) Broadcast(w.ToArray()); else Out(toPeer, w.Span);
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
        Out(0, new MpWriter().U8((byte)MpMsg.LootTake).U32(netId).Span);
    }

    public void SendLootDrop(float x, float y, float z, IReadOnlyList<(string Slot, string Ref)> entries)
    {
        if (IsHost) return;
        var w = new MpWriter().U8((byte)MpMsg.LootDrop).F32(x).F32(y).F32(z);
        WriteEntries(w, entries);
        Out(0, w.Span);
    }

    /// <summary>Hand an item to another player. The host applies/relays;
    /// a client sends up for routing.</summary>
    public void SendItemGive(int targetPlayer, string slot, string itemRef)
    {
        var w = new MpWriter().U8((byte)MpMsg.ItemGive)
            .U8((byte)LocalPlayerId).U8((byte)targetPlayer).Str(slot).Str(itemRef, u16Len: true);
        if (IsHost) RouteItemGive(LocalPlayerId, targetPlayer, slot, itemRef);
        else Out(0, w.Span);
    }

    void RouteItemGive(int from, int target, string slot, string itemRef)
    {
        if (target == LocalPlayerId) { OnItemGive?.Invoke(from, target, slot, itemRef); return; }
        var w = new MpWriter().U8((byte)MpMsg.ItemGive)
            .U8((byte)from).U8((byte)target).Str(slot).Str(itemRef, u16Len: true);
        Out(target, w.Span);
    }

    public void SendRecruitRequest(uint scid)
    {
        if (IsHost) return;
        Out(0, new MpWriter().U8((byte)MpMsg.RecruitRequest).U32(scid).Span);
    }

    /// <summary>Host: a host-run enemy hit remote player <paramref name="player"/>;
    /// that player's machine owns its life and applies the damage.</summary>
    public void SendPlayerHit(int player, float damage, int attacker = MonsterAttacker)
    {
        if (!IsHost || damage <= 0f) return;
        if (player == LocalPlayerId) { if (attacker != MonsterAttacker) OnPlayerHit?.Invoke(damage, attacker); return; }
        Out(player, new MpWriter().U8((byte)MpMsg.PlayerHit).F32(damage).U8((byte)attacker).Span);
    }

    /// <summary>Both: this machine shattered breakable prop <paramref name="scid"/>.
    /// The host relays it to the other clients.</summary>
    public void SendPropBreak(uint scid)
    {
        if (scid == 0) return;
        var frame = new MpWriter().U8((byte)MpMsg.PropBreak).U32(scid).ToArray();
        if (IsHost) Broadcast(frame); else Out(0, frame);
    }

    /// <summary>Host: a runtime-spawned actor clients must mirror under the same
    /// scid; to one peer (a late joiner) or, with -1, to everyone.</summary>
    public void SendActorSpawn(uint scid, string template, float x, float y, float z, int toPeer = -1)
    {
        if (!IsHost) return;
        var frame = new MpWriter().U8((byte)MpMsg.ActorSpawn).U32(scid).Str(template).F32(x).F32(y).F32(z).ToArray();
        if (toPeer >= 0) Out(toPeer, frame); else Broadcast(frame);
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
        SecondsSinceHost += dt;
        // The game clock runs while a game is under way and not paused.
        if (!Paused && (IsHost ? InProgress is not null : _joined)) GameSeconds += dt;
        while (_t.TryReceive(out int peer, out var data))
        {
            Stats.Received(peer, data);
            if (!IsHost) SecondsSinceHost = 0;
            Handle(peer, data);
        }

        if (StatsInterval > 0 && _t.Peers.Count > 0)
        {
            _statsTimer += dt;
            if (_statsTimer >= StatsInterval)
            {
                NetLog.Info($"stats {_statsTimer:0} s{(IsHost ? $", update {_tick}" : $", newest update {_world.Newest}")}: {Stats.Report(_statsTimer)}");
                _statsTimer = 0;
            }
        }

        if (!IsHost && _joinPending && _t.Peers.Count > 0)
        {
            _joinRetryTimer += dt;
            if (_joinRetryTimer >= JoinRetryInterval)
            {
                _joinRetryTimer = 0;
                Out(0, BuildJoinRequest());
                NetLog.Warn("session: no JoinAccept yet — JoinRequest re-sent");
            }
        }

        if (!IsHost && _joined)
        {
            bool shown = _roster.TryGetValue(LocalPlayerId, out var me) && me.Ready;
            if (shown != _wantReady)
            {
                _readyRetryTimer += dt;
                if (_readyRetryTimer >= JoinRetryInterval) { _readyRetryTimer = 0; SendReady(); }
            }
            int shownTeam = _roster.TryGetValue(LocalPlayerId, out var meTeam) ? meTeam.Team : 0;
            if (shownTeam != _wantTeam)
            {
                _teamRetryTimer += dt;
                if (_teamRetryTimer >= JoinRetryInterval) { _teamRetryTimer = 0; SendTeam(); }
            }
        }

        if (IsHost && _t.Peers.Count > 0)
        {
            _rosterTimer += dt;
            if (_rosterTimer >= RosterInterval)
            {
                _rosterTimer = 0;
                Broadcast(BuildRosterFrame());
                Broadcast(BuildSettingsFrame());
                foreach (var peer in _startRepeats.Keys.ToList())
                {
                    if (!_t.Peers.Contains(peer) || --_startRepeats[peer] < 0) { _startRepeats.Remove(peer); continue; }
                    SendInProgressStart(peer);
                }
            }
        }

        if (IsHost)
        {
            // 15 updates a second on average whatever the frame rate: the
            // remainder carries over (a long hitch does not cause a burst).
            _deltaTimer += dt;
            if (_deltaTimer >= DeltaInterval)
            {
                _deltaTimer -= DeltaInterval;
                if (_deltaTimer >= DeltaInterval) _deltaTimer = 0;
                _tick++;

                // Players: refresh our own pose (id 0) and hand the host its
                // own view of the remote avatars.
                if (BuildLocalPlayerState is not null)
                    _players[LocalPlayerId] = BuildLocalPlayerState() with { Player = (byte)LocalPlayerId };
                if (_players.Count > 0) EmitRemotePlayers();

                // The world update for each joiner: players + the world actors
                // near it, against what it last acknowledged.
                var actors = EnumerateActors?.Invoke();
                if (_t.Peers.Count > 0) SendWorldUpdates(actors);
            }
        }
        else
        {
            // Client: movement is client-owned, so report our own pose upstream,
            // with the newest world update held (the host writes against it).
            _clientSendTimer += dt;
            if (_clientSendTimer >= DeltaInterval)
            {
                _clientSendTimer -= DeltaInterval;
                if (_clientSendTimer >= DeltaInterval) _clientSendTimer = 0;
                if (BuildLocalPlayerState is not null && _joined)
                    SendClientState(BuildLocalPlayerState() with { Player = (byte)LocalPlayerId });
            }
        }
    }

    // ── World updates (protocol v8) ─────────────────────────────────────
    /// <summary>Host: how far from each joiner's player the world actors are kept up to date.</summary>
    public MpInterest Interest = MpInterest.Default;
    /// <summary>Client: world actors that left this player's surroundings (by scid);
    /// the host stops updating them until they come near again.</summary>
    public Action<IReadOnlyList<uint>>? OnActorsLeft;
    /// <summary>Client: a world update was used (its tick), after its callbacks ran.</summary>
    public Action<uint>? OnWorldUpdate;

    readonly Dictionary<int, MpWorldSender> _senders = new();   // host: per joiner
    readonly Dictionary<int, ushort> _clientSeq = new();         // host: newest ClientState per joiner
    readonly Dictionary<uint, uint> _netKeys = new();            // host: scid -> key
    uint _nextNetKey = MpQuant.FirstActorKey;
    readonly List<MpEnt> _all = new();
    readonly HashSet<uint> _dupScids = new();
    readonly MpWorldReceiver _world = new();                     // client
    readonly Dictionary<uint, MpActorState> _actorsByScid = new(); // client: the newest state of each followed actor
    ushort _seq;                                                 // client: ClientState counter

    /// <summary>Host: the update writer for one joiner (made on first use).</summary>
    public MpWorldSender SenderFor(int peer)
    {
        if (!_senders.TryGetValue(peer, out var s)) _senders[peer] = s = new MpWorldSender();
        return s;
    }

    /// <summary>Client: the receiving side of the world updates.</summary>
    public MpWorldReceiver World => _world;

    /// <summary>Client: the newest state the host sent for world actor
    /// <paramref name="scid"/>, for an actor this machine only now has (a
    /// streamed-in neighbor region, a mirrored spawn).</summary>
    public bool TryGetActor(uint scid, out MpActorState state) => _actorsByScid.TryGetValue(scid, out state);

    void SendWorldUpdates(IReadOnlyList<MpActorState>? actors)
    {
        _all.Clear();
        foreach (var p in _players.Values)
            _all.Add(new MpEnt { Key = p.Player, Scid = 0, S = MpQuant.Of(p) });
        if (actors is not null)
        {
            _dupScids.Clear();
            foreach (var a in actors)
            {
                // Two actors on one scid would give one key twice, and the
                // receiver refuses an update whose keys do not rise.
                if (!_dupScids.Add(a.Scid)) continue;
                if (!_netKeys.TryGetValue(a.Scid, out var key)) _netKeys[a.Scid] = key = _nextNetKey++;
                _all.Add(new MpEnt { Key = key, Scid = a.Scid, S = MpQuant.Of(a) });
            }
        }
        if (_all.Count == 0) return;   // nothing in the world to tell anyone (the staging area)
        _all.Sort((a, b) => a.Key.CompareTo(b.Key));
        (int, int, int) fallback = _players.TryGetValue(LocalPlayerId, out var me)
            ? (MpQuant.Q(me.X), MpQuant.Q(me.Y), MpQuant.Q(me.Z)) : (0, 0, 0);
        int max = _t.MaxUnreliableBytes;
        foreach (var peer in _t.Peers)
        {
            // Only players: a connection that has not joined drops world updates.
            if (!_joinAccepts.ContainsKey(peer)) continue;
            var center = _players.TryGetValue(peer, out var them)
                ? (MpQuant.Q(them.X), MpQuant.Q(them.Y), MpQuant.Q(them.Z)) : fallback;
            bool outOfView = IsPlayerOutOfView?.Invoke(peer) == true;
            foreach (var f in SenderFor(peer).Write(_tick, _all, peer, center, outOfView, Interest, max))
                OutLossy(peer, f);
        }
    }

    // Client: use a complete world update newer than the last: what changed
    // goes to the game, actors that left are named, players are refreshed.
    void UseWorldUpdate(MpEnt[] now, MpEnt[] before)
    {
        var changed = new List<MpActorState>();
        var left = new List<uint>();
        var players = new List<MpPlayerState>();
        int i = 0, j = 0;
        while (i < now.Length || j < before.Length)
        {
            uint kn = i < now.Length ? now[i].Key : uint.MaxValue;
            uint kb = j < before.Length ? before[j].Key : uint.MaxValue;
            if (kn <= kb)
            {
                var e = now[i++];
                bool same = kn == kb && before[j].S == e.S;
                if (kn == kb) j++;
                if (e.Key < MpQuant.FirstActorKey)
                {
                    var ps = MpQuant.Player((byte)e.Key, e.S);
                    players.Add(ps);
                    if (e.Key == 0) HostState = ps;
                }
                else if (!same)
                {
                    var st = MpQuant.Actor(e.Scid, e.S);
                    changed.Add(st);
                    _actorsByScid[e.Scid] = st;
                }
            }
            else
            {
                var e = before[j++];
                if (e.Key >= MpQuant.FirstActorKey) { left.Add(e.Scid); _actorsByScid.Remove(e.Scid); }
            }
        }
        OutOfHostView = (_world.Flags & 1) != 0;
        if (changed.Count > 0) ApplyDelta?.Invoke(_world.Newest, changed);
        if (left.Count > 0) OnActorsLeft?.Invoke(left);
        if (players.Count > 0) ApplyPlayerStates?.Invoke(players);
        OnWorldUpdate?.Invoke(_world.Newest);
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
        OutLossy(0, new MpWriter().U8((byte)MpMsg.ClientState).U16(++_seq).U32(_world.Newest)
            .F32(s.X).F32(s.Y).F32(s.Z).F32(s.Yaw).U16(s.Life).U8(s.Flags)
            .U8(s.Chore.Seq).U8(s.Chore.Clip).U8(s.Chore.Duration).Span);

    /// <summary>Host: broadcast the START signal so every client leaves staging,
    /// relaunches into the region and reconnects in-world.</summary>
    public void SendGameStart(string regionPath, int difficulty)
    {
        if (!IsHost) return;
        Broadcast(BuildGameStart(regionPath, difficulty));
        NetLog.Info($"session: host broadcast GameStart region='{regionPath}' diff={difficulty} to {_t.Peers.Count} peer(s)");
    }

    static byte[] BuildGameStart(string regionPath, int difficulty) =>
        new MpWriter().U8((byte)MpMsg.GameStart).Str(regionPath).U8((byte)difficulty).ToArray();

    /// <summary>Host: the game is under way (the host's region and difficulty).
    /// A player who joins from the menus then gets a GameStart right after
    /// the JoinAccept and goes straight into the game (join in progress);
    /// players already in the region ignore it. Null in the staging area.</summary>
    public (string Region, int Difficulty)? InProgress { get; set; }
    // Host: GameStart repeats still owed to a peer that just joined (a LAN
    // frame can drop; Epic's are reliable, the repeats are harmless there).
    readonly Dictionary<int, int> _startRepeats = new();

    void SendInProgressStart(int peer)
    {
        if (InProgress is not { } ip) return;
        Out(peer, BuildGameStart(ip.Region, ip.Difficulty));
    }

    /// <summary>What this session sent and received, per peer and message type.</summary>
    public MpNetStats Stats { get; } = new();
    /// <summary>How often the session logs its <see cref="Stats"/> while peers are connected (s; 0 = never).</summary>
    public double StatsInterval = 10;
    double _statsTimer;

    void Out(int peer, ReadOnlySpan<byte> frame)
    {
        Stats.Sent(peer, frame);
        _t.Send(peer, frame);
    }

    void OutLossy(int peer, ReadOnlySpan<byte> frame)
    {
        Stats.Sent(peer, frame);
        _t.SendUnreliable(peer, frame);
    }

    void Broadcast(byte[] frame)
    {
        foreach (var peer in _t.Peers) Out(peer, frame);
    }

    /// <summary>Host: tell everyone the game is over (the host is leaving).
    /// Three copies: a LAN datagram can be lost and the host does not stay to
    /// resend, and a repeat is harmless. The caller keeps the transport pumping
    /// a moment so they go out before the connections close.</summary>
    public void EndGame(MpGameEndReason reason = MpGameEndReason.HostLeft)
    {
        if (!IsHost) return;
        for (int i = 0; i < 3; i++) Broadcast(new[] { (byte)MpMsg.GameEnd, (byte)reason });
        NetLog.Info($"session: told {_t.Peers.Count} peer(s) the game is over");
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
                // The password follows the name from v9 on; an older build's
                // request ends at the name and is turned away by its version.
                string password = ver == MpProtocol.Version ? r.Str() : "";
                if (ver != MpProtocol.Version)
                {
                    // Different build/protocol — reject cleanly rather than let
                    // the two desync on incompatible byte layouts. The friend
                    // test's single most likely failure; make it unmistakable.
                    string why = $"Version mismatch — host is protocol v{MpProtocol.Version}, you sent v{ver}. Both players need the same SiegeFX build.";
                    Out(peer, new MpWriter().U8((byte)MpMsg.JoinReject).Str(why).Span);
                    NetLog.Warn($"session: rejected peer {peer} — protocol v{ver} != host v{MpProtocol.Version}");
                    return;
                }
                if (_joinAccepts.TryGetValue(peer, out var accepted))
                {
                    Out(peer, accepted);
                    SendInProgressStart(peer);
                    NetLog.Info($"session: player {peer} asked again — JoinAccept re-sent");
                    return;
                }
                string? refuse = null;
                if (r.Bad) refuse = "Could not join the game.";
                else if (_playerCount >= Math.Min(MaxPlayers, Settings.PlayerLimit)) refuse = "The game is full.";
                else if (Password.Length > 0 && password != Password) refuse = "The password you entered was invalid.";
                else if (InProgress is not null && !Settings.AllowJoinInProgress && !StartingParty.Contains(name)) refuse = "Host has prevented new players from joining the game while in progress.";
                if (refuse is not null)
                {
                    Out(peer, new MpWriter().U8((byte)MpMsg.JoinReject).Str(refuse).Span);
                    NetLog.Info($"session: rejected peer {peer} '{name}' — {refuse}");
                    return;
                }
                _playerCount++;
                // A new player on this connection starts from a full world update.
                _senders.Remove(peer);
                _clientSeq.Remove(peer);
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
                Out(peer, accept);
                Out(peer, BuildSettingsFrame());
                if (Paused) Out(peer, new MpWriter().U8((byte)MpMsg.Pause).U8(1).U8(0).Span);
                OnPlayerJoined?.Invoke(peer, name);
                Broadcast(new MpWriter().U8((byte)MpMsg.PlayerJoined).U8((byte)peer).Str(name).ToArray());
                _roster[peer] = new MpRosterEntry(peer, name, false, 0);
                RosterChanged();
                if (InProgress is { } ip)
                {
                    SendInProgressStart(peer);
                    _startRepeats[peer] = 3;
                    NetLog.Info($"session: player {peer} joined a game in progress — sent GameStart '{ip.Region}'");
                }
                // Catch the late joiner up on every known character card.
                foreach (var info in _infos.Values)
                    Out(peer, BuildPlayerInfoFrame(info));
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
                ushort seq = r.U16();
                uint ack = r.U32();
                float x = r.F32(), y = r.F32(), z = r.F32(), yaw = r.F32();
                ushort life = r.U16(); byte flags = r.U8();
                var chore = new MpChore(r.U8(), r.U8(), r.U8());
                if (r.Bad) { NetLog.Warn($"session: malformed ClientState from peer {peer} — dropped"); return; }
                // The lossy lane can deliver an older one after a newer one.
                if (_clientSeq.TryGetValue(peer, out var lastSeq) && (short)(seq - lastSeq) <= 0) return;
                _clientSeq[peer] = seq;
                SenderFor(peer).Ack(ack);
                _players[peer] = new MpPlayerState((byte)peer, x, y, z, yaw, life, flags, chore);
                break;
            }
            case MpMsg.Ready when IsHost:
            {
                bool ready = r.U8() != 0;
                if (r.Bad) { NetLog.Warn($"session: malformed Ready from peer {peer} — dropped"); return; }
                if (!_roster.TryGetValue(peer, out var e))
                { NetLog.Warn($"session: Ready from peer {peer}, which has not joined — dropped"); return; }
                if (e.Ready == ready) { Out(peer, BuildRosterFrame()); return; }  // asked again: its roster was lost
                _roster[peer] = e with { Ready = ready };
                NetLog.Info($"session: player {peer} '{e.Name}' is {(ready ? "ready" : "not ready")}");
                RosterChanged();
                break;
            }
            case MpMsg.Roster when !IsHost:
            {
                int n = r.U8();
                var list = new List<MpRosterEntry>(n);
                for (int i = 0; i < n && !r.Bad; i++)
                {
                    int p = r.U8(); bool ready = r.U8() != 0; int team = r.U8(); string name = r.Str();
                    list.Add(new MpRosterEntry(p, name, ready, Math.Min(team, MpTeams.Count)));
                }
                if (r.Bad) { NetLog.Warn("session: truncated Roster — dropped"); return; }
                if (list.Count == _roster.Count && list.TrueForAll(e => _roster.TryGetValue(e.Player, out var o) && o == e))
                    return;   // the periodic copy of what we already show
                _roster.Clear();
                foreach (var e in list) _roster[e.Player] = e;
                OnRoster?.Invoke(list);
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
                int attacker = r.U8();
                if (r.Bad || !(damage > 0f) || float.IsInfinity(damage)) return;
                OnPlayerHit?.Invoke(damage, attacker);
                break;
            }
            case MpMsg.PropBreak:
            {
                uint scid = r.U32();
                if (r.Bad || scid == 0) return;
                OnPropBreak?.Invoke(scid);
                if (IsHost)
                    foreach (var other in _t.Peers)
                        if (other != peer) Out(other, data);
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
                _world.Reset();
                _actorsByScid.Clear();
                byte[] snap;
                try { snap = MpSnapshot.Unpack(packed); }
                catch (Exception ex) { NetLog.Error($"session: JoinAccept snapshot unreadable ({ex.Message}) — joined without it"); snap = Array.Empty<byte>(); }
                LocalPlayerId = assigned;
                ApplySnapshot?.Invoke(snap);
                OnJoinAccepted?.Invoke(assigned);
                NetLog.Info($"session: join accepted as player {assigned}, snapshot {snap.Length}B applied");
                if (_wantReady) { _readyRetryTimer = 0; SendReady(); }   // READY was clicked while joining
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
            case MpMsg.Snapshot when !IsHost:
            {
                if (!_joined) return;
                if (_world.Take(data, out var now, out var before)) UseWorldUpdate(now, before);
                break;
            }
            case MpMsg.GameEnd when !IsHost:
            {
                var reason = (MpGameEndReason)r.U8();
                if (r.Bad || reason > MpGameEndReason.TimeLimit) reason = MpGameEndReason.HostLeft;
                NetLog.Info($"session: the host ended the game ({reason})");
                OnGameEnded?.Invoke(reason);
                break;
            }
            case MpMsg.Settings when !IsHost:
            {
                var next = MpGameSettings.Read(ref r);
                if (next is null) { NetLog.Warn("session: malformed Settings — dropped"); return; }
                uint clock = r.U32();
                if (!r.Bad) GameSeconds = clock;
                if (next == Settings) return;   // the periodic copy
                var before = Settings;
                Settings = next;
                OnSettings?.Invoke(before, next);
                break;
            }
            case MpMsg.TeamPick when IsHost:
            {
                int team = r.U8();
                if (r.Bad || team > MpTeams.Count) { NetLog.Warn($"session: malformed TeamPick from peer {peer} — dropped"); return; }
                if (!_roster.TryGetValue(peer, out var e)) return;
                if (e.Team == team) { Out(peer, BuildRosterFrame()); return; }
                _roster[peer] = e with { Team = team };
                NetLog.Info($"session: player {peer} '{e.Name}' joined team {team}");
                RosterChanged();
                break;
            }
            case MpMsg.Pause:
            {
                bool paused = r.U8() != 0;
                if (IsHost)
                {
                    if (r.Bad || !_roster.ContainsKey(peer)) return;
                    if (!Settings.AllowPause) { Out(peer, new MpWriter().U8((byte)MpMsg.Pause).U8(Paused ? (byte)1 : (byte)0).U8(0).Span); return; }
                    ApplyPause(paused, peer);
                }
                else
                {
                    int by = r.U8();
                    if (r.Bad || Paused == paused) return;
                    Paused = paused;
                    OnPause?.Invoke(paused, by);
                }
                break;
            }
            case MpMsg.TeamChat:
            {
                if (IsHost)
                {
                    string text = r.Str(u16Len: true);
                    if (!r.Bad && _roster.ContainsKey(peer)) RouteTeamChat(peer, text);
                }
                else
                {
                    int from = r.U8(); string text = r.Str(u16Len: true);
                    if (!r.Bad) OnTeamChat?.Invoke(from, text);
                }
                break;
            }
            case MpMsg.PvpHit when IsHost:
            {
                int target = r.U8();
                float damage = r.F32();
                if (r.Bad || !(damage > 0f) || float.IsInfinity(damage) || !Settings.PlayerVsPlayer) return;
                if (target == peer || !_roster.ContainsKey(target)) return;
                SendPlayerHit(target, damage, peer);
                break;
            }
            case MpMsg.GameStart when !IsHost:
            {
                string region = r.Str();
                int diff = r.U8();
                if (r.Bad) { NetLog.Error("session: malformed GameStart — ignored"); return; }
                if (OnGameStart is null) return;   // already in the game: a join-in-progress repeat
                NetLog.Info($"session: received GameStart region='{region}' diff={diff}");
                OnGameStart.Invoke(region, diff);
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

/// <summary>One row of the staging roster: the player id (the host is 0), the
/// name the player joined with, and whether the player is ready.</summary>
/// <summary>One player in the staging roster. <see cref="Team"/>: 0 = none,
/// 1..16 one of the original's team signs (team_signs.gas).</summary>
public readonly record struct MpRosterEntry(int Player, string Name, bool Ready, int Team = 0);

/// <summary>The original's sixteen team signs (/ui/config/multiplayer/team_signs.gas).</summary>
public static class MpTeams
{
    public const int Count = 16;
    public static string Sign(int team) => $"b_gui_ig_i_ic_coa_{Math.Clamp(team, 1, Count):000}";
}
