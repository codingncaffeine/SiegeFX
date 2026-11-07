using SiegeFX.Core.Net;

namespace SiegeFX.Runtime;

/// <summary>SC-MP-EOS P3 receipt — headless host↔client round-trip over the
/// loopback transport: JoinRequest → JoinAccept (with a world snapshot),
/// StateDelta application, client Input delivery to the host, and chat
/// relay. Exercises the bounds-checked protocol reader/writer and the
/// MpSession driver end-to-end with no window. Exits 0 on success, 1 with
/// the failing assertion printed — suitable for test-all.bat.</summary>
public static class NetSelfTest
{
    public static bool Run()
    {
        NetLog.Verbose = true;
        bool ok = true;
        void Assert(bool cond, string what)
        {
            if (!cond) { Console.Error.WriteLine($"[selftest-net] FAIL: {what}"); ok = false; }
            else Console.WriteLine($"[selftest-net] ok: {what}");
        }

        var (hostT, clientT) = LoopbackTransport.CreatePair();
        var host = new MpSession(hostT, isHost: true, "HostPlayer");
        var client = new MpSession(clientT, isHost: false, "JoinerPlayer");

        // The "world snapshot" is opaque bytes to the net layer — here a
        // recognizable blob so we can verify the client received it intact.
        var snapshot = System.Text.Encoding.UTF8.GetBytes("WORLD-SNAPSHOT-v13:actors=179,region=fh_r1");
        host.BuildSnapshot = () => snapshot;

        // Host's authoritative actor list (two movers).
        float heroX = 10f;
        host.EnumerateActors = () => new List<MpActorState>
        {
            new(0x01C00001, heroX, 0.5f, 20f, 50),
            new(0x01C00002, 30f, 0.5f, 40f, 999),
        };

        byte[]? clientGotSnapshot = null;
        int clientAssignedId = -1;
        client.ApplySnapshot = s => clientGotSnapshot = s;
        client.OnJoinAccepted = id => clientAssignedId = id;

        uint lastDeltaTick = 0;
        IReadOnlyList<MpActorState>? lastDelta = null;
        client.ApplyDelta = (tick, actors) => { lastDeltaTick = tick; lastDelta = actors; };

        int hostSawInputPeer = -1;
        MpInputCmd hostSawCmd = default;
        float hostSawX = 0, hostSawZ = 0;
        host.ApplyClientInput = (peer, cmd, x, z, scid) =>
        {
            hostSawInputPeer = peer; hostSawCmd = cmd; hostSawX = x; hostSawZ = z;
            // Host authority: move the hero to where the client asked.
            if (cmd == MpInputCmd.Move) heroX = x;
        };

        string? hostChat = null, clientChat = null;
        host.OnChat += m => hostChat = m;
        client.OnChat += m => clientChat = m;

        int joinedNotifications = 0;
        host.OnPlayerJoined = (_, _) => joinedNotifications++;

        // 1) Client joins.
        client.SendJoinRequest();
        Pump(host, client);
        Assert(clientAssignedId == 1, $"client assigned player id (got {clientAssignedId})");
        Assert(clientGotSnapshot is not null && clientGotSnapshot.AsSpan().SequenceEqual(snapshot),
            "client received the world snapshot intact");
        Assert(joinedNotifications == 1, "host fired OnPlayerJoined");

        // 2) Host ticks out a state delta (needs > DeltaInterval to fire).
        host.Tick(0.1); client.Tick(0.0);
        Pump(host, client);
        Assert(lastDelta is not null && lastDelta.Count == 2, $"client applied a 2-actor delta (got {lastDelta?.Count ?? -1})");
        Assert(lastDelta is not null && lastDelta[0].Scid == 0x01C00001 && System.Math.Abs(lastDelta[0].X - 10f) < 0.01f,
            "delta carried the authoritative hero pose");

        // 3) Client sends an input; host applies it authoritatively.
        client.SendInput(MpInputCmd.Move, 25f, 20f);
        Pump(host, client);
        Assert(hostSawInputPeer == 1 && hostSawCmd == MpInputCmd.Move,
            $"host received client Move input (peer {hostSawInputPeer}, cmd {hostSawCmd})");
        Assert(System.Math.Abs(heroX - 25f) < 0.01f, "host authority moved the hero to the input target");

        // 4) Next delta reflects the host's authoritative new pose.
        host.Tick(0.1);
        Pump(host, client);
        Assert(lastDelta is not null && System.Math.Abs(lastDelta[0].X - 25f) < 0.01f,
            "subsequent delta carried the updated authoritative pose");

        // 5) Chat relays both directions.
        client.SendChat("hello host");
        Pump(host, client);
        Assert(hostChat is not null && hostChat.Contains("hello host"), "host received client chat");
        Assert(clientChat is not null && clientChat.Contains("hello host"), "chat relayed back to client");

        // 6) Malformed frame safety — the reader must not throw and must
        //    report Bad; a random 3-byte frame simulates truncation/attack.
        var r = new MpReader(new byte[] { (byte)MpMsg.Input, 0xFF, 0xFF });
        r.U8(); r.U32(); r.U8(); r.F32();
        Assert(r.Bad, "bounds-checked reader flags a truncated frame instead of throwing");

        host.Dispose(); client.Dispose(); hostT.Dispose(); clientT.Dispose();

        // ---- F6: protocol-version handshake gate ----
        // A client on a different build (wrong wire version) must be rejected
        // with a clear reason, never silently accepted onto an incompatible
        // byte layout — the most likely two-machine failure.
        {
            var (ht, ct) = LoopbackTransport.CreatePair();
            var h = new MpSession(ht, isHost: true, "Host");
            var c = new MpSession(ct, isHost: false, "Client");
            string? rejectReason = null; int accepted = -1;
            c.OnJoinRejected = why => rejectReason = why;
            c.OnJoinAccepted = id => accepted = id;
            // Hand-craft a JoinRequest carrying a bogus version (bypassing
            // SendJoinRequest, which always stamps the current version).
            ushort badVer = (ushort)(MpProtocol.Version + 99);
            ct.Send(0, new MpWriter().U8((byte)MpMsg.JoinRequest).U16(badVer).Str("Client").Span);
            Pump(h, c);
            Assert(rejectReason is not null && rejectReason.Contains("Version mismatch"),
                $"mismatched protocol version rejected with a clear reason (got '{rejectReason}')");
            Assert(accepted == -1, "version-mismatched client was NOT accepted into the game");

            // Sanity: a request at the CURRENT version still joins.
            var (ht2, ct2) = LoopbackTransport.CreatePair();
            var h2 = new MpSession(ht2, isHost: true, "Host");
            var c2 = new MpSession(ct2, isHost: false, "Client");
            int ok2 = -1; c2.OnJoinAccepted = id => ok2 = id;
            c2.SendJoinRequest();
            Pump(h2, c2);
            Assert(ok2 == 1, $"matching-version client still joins (got id {ok2})");
            h.Dispose(); c.Dispose(); ht.Dispose(); ct.Dispose();
            h2.Dispose(); c2.Dispose(); ht2.Dispose(); ct2.Dispose();
        }

        // ---- SC-MP-INGAME: player-pose fan-out, GameStart, leave ----
        {
            var (h2, c2) = LoopbackTransport.CreatePair();
            var hs = new MpSession(h2, isHost: true, "Host");
            var cs = new MpSession(c2, isHost: false, "Client");

            // Both report a local pose (movement is client-authoritative). The
            // host is player 0 and moving; the client reports itself standing.
            hs.BuildLocalPlayerState = () => new MpPlayerState(0, 1f, 0f, 2f, 0.5f, 100, (byte)MpPlayerFlags.Moving);
            cs.BuildLocalPlayerState = () => new MpPlayerState(0, 7f, 0f, 8f, 1.2f, 90, 0);

            List<MpPlayerState>? hostSees = null, clientSees = null;
            hs.ApplyPlayerStates = list => hostSees = new List<MpPlayerState>(list);
            cs.ApplyPlayerStates = list => clientSees = new List<MpPlayerState>(list);

            // Host is authoritative over a world actor too — verify the player
            // and world channels coexist without interfering.
            bool clientGotWorldDelta = false;
            cs.ApplyDelta = (_, actors) => clientGotWorldDelta = actors.Count == 1 && actors[0].Scid == 0x01C00009;
            hs.EnumerateActors = () => new List<MpActorState> { new(0x01C00009, 5f, 0f, 6f, 42) };

            string? gsRegion = null; int gsDiff = -1;
            cs.OnGameStart = (region, diff) => { gsRegion = region; gsDiff = diff; };
            int hostSawLeave = -99;
            hs.OnPlayerLeft = id => hostSawLeave = id;

            cs.SendJoinRequest(); Pump(hs, cs);
            Assert(cs.LocalPlayerId == 1, $"in-region: client LocalPlayerId assigned (got {cs.LocalPlayerId})");

            cs.Tick(0.1); Pump(hs, cs);   // client uploads its pose; host stores it
            hs.Tick(0.1); Pump(hs, cs);   // host fans PlayerDelta + StateDelta out

            Assert(clientSees is not null && clientSees.Exists(p => p.Player == 0),
                "in-region: client renders the host avatar (player 0)");
            Assert(clientSees is not null && clientSees.TrueForAll(p => p.Player != cs.LocalPlayerId),
                "in-region: client's own player is filtered out of its render set");
            Assert(hostSees is not null && hostSees.Exists(p => p.Player == 1),
                "in-region: host renders the client avatar (player 1)");
            var hv = clientSees!.Find(p => p.Player == 0);
            Assert(System.Math.Abs(hv.X - 1f) < 0.01f && (hv.Flags & (byte)MpPlayerFlags.Moving) != 0,
                "in-region: host pose + moving flag arrived intact");
            Assert(clientGotWorldDelta, "in-region: world-actor delta coexists with the player-pose channel");

            // F3: a client's rolled hit on a host-owned actor is applied by the
            // host (authoritative), keyed by SCID; junk damage is dropped.
            uint hitScid = 0; float hitDmg = 0f;
            hs.ApplyClientHit = (scid, dmg) => { hitScid = scid; hitDmg = dmg; };
            cs.SendClientHit(0x01C00009, 37.5f); Pump(hs, cs);
            Assert(hitScid == 0x01C00009 && System.Math.Abs(hitDmg - 37.5f) < 0.01f,
                $"in-region: host applied the client's hit by scid (got scid {hitScid:x8} dmg {hitDmg:F1})");
            hitScid = 0; hitDmg = 0f;
            cs.SendClientHit(0x01C00009, 0f); Pump(hs, cs);       // zero damage: no-op
            cs.SendClientHit(0, 10f); Pump(hs, cs);               // no target: no-op
            Assert(hitScid == 0 && hitDmg == 0f,
                "in-region: host ignores a zero-damage / no-target client hit");

            hs.SendGameStart("/world/maps/map_world/regions/fh_r1", 2); Pump(hs, cs);
            Assert(gsRegion == "/world/maps/map_world/regions/fh_r1" && gsDiff == 2,
                "in-region: GameStart delivered region + difficulty to the client");

            c2.Dispose(); // client drops — host must be told player 1 left
            Assert(hostSawLeave == 1, $"in-region: host fired OnPlayerLeft on disconnect (got {hostSawLeave})");
            hs.Dispose(); cs.Dispose(); h2.Dispose();
        }

        // ---- Staging: the host's roster, READY, and who counts as having left ----
        {
            var (h3, c3raw) = LoopbackTransport.CreatePair();
            var c3 = new DropNextSends(c3raw);
            var hs = new MpSession(h3, isHost: true, "Host");
            var cs = new MpSession(c3, isHost: false, "Joiner");
            IReadOnlyList<MpRosterEntry>? hostRoster = null, clientRoster = null;
            hs.OnRoster = r => hostRoster = r;
            cs.OnRoster = r => clientRoster = r;
            cs.SetReady(true);                     // clicked while the join is still unanswered
            cs.SendJoinRequest(); Pump(hs, cs);
            Assert(clientRoster is { Count: 2 } && clientRoster[0] is { Player: 0, Name: "Host", Ready: true }
                   && clientRoster[1].Name == "Joiner",
                "staging: the joiner gets the host's roster, host first");
            Assert(hostRoster is not null && hostRoster.Any(e => e.Name == "Joiner" && e.Ready) && hs.NotReady.Count == 0,
                "staging: a READY clicked while joining reaches the host once the join is answered");
            cs.SetReady(false); Pump(hs, cs);
            Assert(hs.NotReady.SequenceEqual(new[] { "Joiner" }) && clientRoster!.Any(e => e.Name == "Joiner" && !e.Ready),
                "staging: taking READY back reaches the host and comes back in its roster");
            c3.Drop = 1;                           // the next READY is lost on the way
            cs.SetReady(true); Pump(hs, cs);
            Assert(hs.NotReady.Count == 1, "staging: (control) the lost READY did not reach the host");
            cs.Tick(1.1); Pump(hs, cs);
            Assert(hs.NotReady.Count == 0, "staging: a lost READY is sent again until the host's roster shows it");
            string? leftName = null;
            hs.OnPlayerLeft = id => leftName = hostRoster?.FirstOrDefault(e => e.Player == id).Name;
            var before = hostRoster;
            c3.Dispose();
            Assert(hostRoster is { Count: 1 } && before!.Count == 2 && hs.Roster.Count == 1 && leftName == "Joiner",
                $"staging: a player who leaves drops out of the host's roster (left: '{leftName}')");
            hs.Dispose(); cs.Dispose(); h3.Dispose();

            var (h4, c4) = LoopbackTransport.CreatePair();
            var hs4 = new MpSession(h4, isHost: true, "Host");
            int leftCalls = 0;
            hs4.OnPlayerLeft = _ => leftCalls++;
            c4.Dispose();                          // connected, never sent a JoinRequest
            Assert(leftCalls == 0 && hs4.Roster.Count == 1,
                "staging: a connection that closes before it joins is not announced as a player leaving");
            hs4.Dispose(); h4.Dispose();
        }

        // ---- Join in progress + the co-op leash ----
        {
            var (h5, c5) = LoopbackTransport.CreatePair();
            var hs = new MpSession(h5, isHost: true, "Host") { InProgress = ("/world/maps/map_world/regions/fh_r1", 1) };
            hs.BuildLocalPlayerState = () => new MpPlayerState(0, 3f, 0f, 4f, 0f, 100, 0);
            var cs = new MpSession(c5, isHost: false, "Late");
            string? startRegion = null;
            cs.OnGameStart = (region, _) => startRegion = region;
            cs.BuildLocalPlayerState = () => new MpPlayerState(0, 50f, 0f, 60f, 0f, 90, 0);
            cs.SendJoinRequest(); Pump(hs, cs);
            Assert(cs.Joined && startRegion == "/world/maps/map_world/regions/fh_r1",
                $"join in progress: a player who joins a running game is sent into its region (got '{startRegion}')");
            bool outOfView = false;
            hs.IsPlayerOutOfView = _ => outOfView;
            cs.Tick(0.1); hs.Tick(0.1); Pump(hs, cs);
            Assert(!cs.OutOfHostView && cs.HostState is { X: 3f, Z: 4f },
                "leash: a player the host can see is not marked, and knows where the host is");
            outOfView = true;
            cs.Tick(0.1); hs.Tick(0.1); Pump(hs, cs);
            Assert(cs.OutOfHostView, "leash: a player the host cannot see is marked out of view");
            outOfView = false;
            cs.Tick(0.1); hs.Tick(0.1); Pump(hs, cs);
            Assert(!cs.OutOfHostView, "leash: back in view, the mark clears");
            hs.Dispose(); cs.Dispose(); h5.Dispose(); c5.Dispose();
        }

        // ---- v8 world updates: interest, changes only, packing, 10 % loss ----
        {
            var (ht, ct) = LoopbackTransport.CreatePair();
            var net = new LossyLane(ht, lossPct: 10, seed: 5);
            var hs = new MpSession(net, isHost: true, "Host") { StatsInterval = 0 };
            var cs = new MpSession(ct, isHost: false, "Joiner") { StatsInterval = 0 };
            const double dt = 1.0 / 15.0;
            double t = 0;
            bool still = false;
            float cx = 0f;
            hs.BuildLocalPlayerState = () => new MpPlayerState(0, 0f, 8f, 0f, 0f, 100, 0);
            cs.BuildLocalPlayerState = () => new MpPlayerState(0, cx, 8f, 2f, 0f, 90, 0);
            const uint A = 0x01000001, B = 0x01000002, C = 0x01000003, D = 0x01000004;
            MpActorState Walker(uint scid, float x0, double time)
            {
                float a = (float)(time * 1.2);
                return new MpActorState(scid, x0 + 3f * MathF.Cos(a), 8f, 3f * MathF.Sin(a), 50,
                    default, a + MathF.PI / 2, (byte)MpPlayerFlags.Moving);
            }
            List<MpActorState> World(double time) => new()
            {
                Walker(A, 10f, time),                                           // near
                Walker(B, 70f, time),                                           // far band (5 a second)
                new(C, 300f, 8f, 0f, 70, default, 0f, 0),                       // beyond the interest radius
                new(D, 5f, 8f, 5f, 40, new MpChore(3, 7, 9), 1.0f, 0),          // near, standing still
            };
            hs.EnumerateActors = () => World(t);
            var left = new List<uint>();
            cs.OnActorsLeft = l => left.AddRange(l);
            void Step(int n) { for (int i = 0; i < n; i++) { if (!still) t += dt; cs.Tick(dt); hs.Tick(dt); cs.Tick(0); } }
            bool Close(uint scid, MpActorState truth, float cm)
            {
                if (!cs.TryGetActor(scid, out var got)) return false;
                float d = Vector3Distance(got, truth);
                return d <= cm / 100f && got.Life == truth.Life && got.Chore == truth.Chore && got.Flags == truth.Flags
                       && Math.Abs(MpQuant.Yaw(got.Yaw) - MpQuant.Yaw(truth.Yaw)) <= 1;
            }

            cs.SendJoinRequest(); Pump(hs, cs);
            Step(150);
            Assert(net.Lost > 0, $"world updates: (control) the loopback really lost updates ({net.Lost} of {net.LossySent})");
            net.LossPct = 0;
            Step(3);   // a lossless stretch: the far band's turn comes round once
            var now = World(t);
            Assert(Close(A, now[0], 1.5f), "world updates: a near walker matches the host within 1.5 cm after 10 % loss");
            // The far band is brought up to date on every third update: the
            // joiner holds the host's state from one of the last three.
            Assert(Close(B, World(t)[1], 1.5f) || Close(B, World(t - dt)[1], 1.5f) || Close(B, World(t - 2 * dt)[1], 1.5f),
                "world updates: a walker in the far band matches the host's state of one of the last 3 updates within 1.5 cm");
            Assert(Close(D, now[3], 0.1f), "world updates: a standing actor has the host's heading, life and chore");
            Assert(!cs.TryGetActor(C, out _), "world updates: an actor beyond the interest radius is never sent");

            still = true;
            Step(20);                                 // the last moves (the far band's too) are acknowledged
            net.Sizes.Clear();
            Step(MpWorldSender.HeartbeatTicks * 2);
            Assert(net.Sizes.Count == 2 && net.Sizes.TrueForAll(n => n == MpWorldUpdate.HeaderBytes),
                $"world updates: a still world sends only a bare header every {MpWorldSender.HeartbeatTicks} updates (sent {string.Join(",", net.Sizes)} B)");
            net.Sizes.Clear();
            var bStill = World(t)[1];
            still = false;                            // the next step moves A one update's walk; B stays
            hs.EnumerateActors = () => { var w = World(t); w[1] = bStill; return w; };
            Step(1);
            Assert(net.Sizes.Count == 1 && net.Sizes[0] <= MpWorldUpdate.HeaderBytes + 8,
                $"world updates: one walker's step costs one small entry (update {string.Join(",", net.Sizes)} B; v7 sent 21 B per actor every update)");

            hs.EnumerateActors = () => World(t);
            cx = 300f;                                // the joiner walks to C
            Step(6);
            Assert(cs.TryGetActor(C, out var cState) && Math.Abs(cState.X - 300f) < 0.02f,
                "world updates: an actor comes into interest when the player nears it");
            Assert(left.Contains(A) && left.Contains(D) && !cs.TryGetActor(A, out _),
                "world updates: actors left behind are reported gone from interest");

            // A receiver fed junk and cut frames refuses them without throwing.
            var rx = new MpWorldReceiver();
            var junk = new Random(99);
            bool threw = false;
            for (int i = 0; i < 5000; i++)
            {
                var f = new byte[junk.Next(0, 80)];
                junk.NextBytes(f);
                if (f.Length > 0) f[0] = (byte)MpMsg.Snapshot;
                if (f.Length > 7) { f[5] = 0; f[6] = 0; f[7] = 1; }   // whole, no baseline: the entries get parsed
                try { rx.Take(f, out _, out _); }
                catch { threw = true; break; }
            }
            Assert(!threw && rx.Malformed > 0, $"world updates: junk frames are refused without throwing ({rx.Malformed} of 5000 refused)");
            hs.Dispose(); cs.Dispose(); ht.Dispose(); ct.Dispose();
        }

        // ---- v8 world updates under load: 8 players, 360 walking mobs, 1 Mbit/s, 2 % loss, 100 ms ----
        {
            var saved = Console.Out;
            int code = NetLoadTest.Run(new[] { "seconds=60", "max-error-cm=5", "max-missing=0", "max-latency-growth-ms=50", "max-kbit=450" });
            Console.SetOut(saved);
            Assert(code == 0, "world updates: the 8-player load stays within its limits (the [load] lines above)");
        }

        // ---- P5: join rules + spawn-town gates ----
        var vet = new MpSessionInfo("Bob", "Utraea", MpDifficulty.Veteran, 2, 8, false, "Lang", 50, 60);
        Assert(!MpJoinRules.CanJoin(vet, 40, false).Ok, "level-40 char refused from a Veteran game (needs 54+)");
        Assert(MpJoinRules.CanJoin(vet, 60, false).Ok, "level-60 char accepted into a Veteran game");
        var full = vet with { PlayersIn = 8 };
        Assert(!MpJoinRules.CanJoin(full, 99, false).Ok, "full game refuses even a high-level char");
        var newOnly = new MpSessionInfo("Bob", "Ehb", MpDifficulty.Regular, 1, 8, true, "", 1, 1);
        Assert(!MpJoinRules.CanJoin(newOnly, 30, false).Ok, "'new characters only' refuses an existing char");
        Assert(MpJoinRules.CanJoin(newOnly, 1, true).Ok, "'new characters only' accepts a fresh char");
        var towns = MpSpawnTowns.Available(20).ToList();
        Assert(towns.Contains("Meren") && !towns.Contains("Lang"), "level-20 spawn towns include Meren, exclude Lang");
        Assert(!MpSpawnTowns.CanEnter("Grescal", 20) && MpSpawnTowns.CanEnter("Elddim", 0), "town gates: Grescal locked at 20, Elddim open");

        // ---- P6: HMAC tickets + AEAD + fuzz ----
        var key = MpSecurity.NewSessionKey();
        string ticket = MpSecurity.MintTicket(key, 3, "sess-abc", DateTimeOffset.UtcNow.AddMinutes(5));
        Assert(MpSecurity.VerifyTicket(key, ticket, "sess-abc", DateTimeOffset.UtcNow, out int tp) && tp == 3,
            "valid HMAC ticket verifies with the right player id");
        Assert(!MpSecurity.VerifyTicket(key, ticket, "sess-abc", DateTimeOffset.UtcNow.AddMinutes(10), out _),
            "expired ticket rejected");
        Assert(!MpSecurity.VerifyTicket(MpSecurity.NewSessionKey(), ticket, "sess-abc", DateTimeOffset.UtcNow, out _),
            "ticket forged under a different key rejected");
        var sealed_ = MpSecurity.Seal(key, System.Text.Encoding.UTF8.GetBytes("secret payload"));
        var opened = MpSecurity.Open(key, sealed_);
        Assert(opened is not null && System.Text.Encoding.UTF8.GetString(opened) == "secret payload",
            "AEAD seal/open round-trips the payload");
        sealed_[15] ^= 0xFF; // tamper a ciphertext byte
        Assert(MpSecurity.Open(key, sealed_) is null, "AEAD rejects a tampered frame (returns null, no throw)");

        // SecureTransport end-to-end over loopback with a passphrase-derived key
        // (the opt-in AEAD wrap used on the live path when SIEGEFX_MP_PASSPHRASE
        // is set — no lobby server needed to distribute a key).
        Assert(MpSecurity.DeriveKey("dragons").AsSpan().SequenceEqual(MpSecurity.DeriveKey("dragons")),
            "same passphrase derives the same key on both sides");
        {
            var (st1, st2) = LoopbackTransport.CreatePair();
            var sh = new SecureTransport(st1, MpSecurity.DeriveKey("dragons"));
            var sc = new SecureTransport(st2, MpSecurity.DeriveKey("dragons"));
            sh.Send(1, System.Text.Encoding.UTF8.GetBytes("ping"));
            bool rttOk = sc.TryReceive(out _, out var pl) && System.Text.Encoding.UTF8.GetString(pl) == "ping";
            Assert(rttOk, "SecureTransport round-trips an AEAD frame over the transport");
            var (bt1, bt2) = LoopbackTransport.CreatePair();
            var good = new SecureTransport(bt1, MpSecurity.DeriveKey("dragons"));
            var wrong = new SecureTransport(bt2, MpSecurity.DeriveKey("goblins"));
            good.Send(1, System.Text.Encoding.UTF8.GetBytes("secret"));
            Assert(!wrong.TryReceive(out _, out _), "SecureTransport drops a frame sealed under a different passphrase");
            sh.Dispose(); sc.Dispose(); good.Dispose(); wrong.Dispose();
        }
        // Fuzz the protocol reader: 20k random frames, must never throw.
        var rng = new Random(1234);
        bool anyThrow = false;
        for (int i = 0; i < 20000; i++)
        {
            var frame = new byte[rng.Next(0, 64)];
            rng.NextBytes(frame);
            try
            {
                var rr = new MpReader(frame);
                for (int j = 0; j < 8; j++) { rr.U8(); rr.U16(); rr.U32(); rr.F32(); rr.Str(); rr.Str(true); }
            }
            catch { anyThrow = true; break; }
        }
        Assert(!anyThrow, "protocol reader survived 20k random frames without throwing (fuzz-hard)");

        // ---- P7: NAT host election + player-relay ----
        var cands = new List<HostCandidate>
        {
            new(0, "Alice", NatClass.Symmetric, 20),
            new(1, "Bob",   NatClass.FullCone,  40),
            new(2, "Cara",  NatClass.OpenOrForwarded, 60),
        };
        var (elected, relay) = MpHostElection.Elect(cands);
        Assert(elected.Player == 2 && !relay, "most-reachable player (open/forwarded) elected host, no relay needed");
        var symOnly = new List<HostCandidate> { new(0, "A", NatClass.Symmetric, 10), new(1, "B", NatClass.PortRestricted, 30) };
        var (_, relay2) = MpHostElection.Elect(symOnly);
        Assert(relay2, "all-restricted lobby flags relay-likely");
        // Player-relay: client 0 can't reach host 2 directly, but peer 1 can bridge.
        int bridge = MpPlayerRelay.PickRelay(0, 2, cands, (a, b) => !(a == 0 && b == 2));
        Assert(bridge == 1, "player-relay picks peer 1 to bridge the blocked client");

        Console.WriteLine(ok ? "[selftest-net] ALL PASS" : "[selftest-net] FAILURES ABOVE");
        return ok;
    }

    static float Vector3Distance(in MpActorState a, in MpActorState b) =>
        System.Numerics.Vector3.Distance(new(a.X, a.Y, a.Z), new(b.X, b.Y, b.Z));

    static void Pump(MpSession host, MpSession client)
    {
        // Drain both directions a few times (deltas may cascade).
        for (int i = 0; i < 4; i++) { host.Tick(0.0); client.Tick(0.0); }
    }
}

/// <summary>A transport whose lossy lane loses <see cref="LossPct"/> % of its
/// frames (seeded), recording the size of every lossy frame offered.</summary>
sealed class LossyLane : ISessionTransport
{
    readonly ISessionTransport _inner;
    readonly Random _rng;
    public double LossPct;
    public int Lost, LossySent;
    public readonly List<int> Sizes = new();
    public LossyLane(ISessionTransport inner, double lossPct, int seed) { _inner = inner; LossPct = lossPct; _rng = new Random(seed); }
    public string ProviderId => _inner.ProviderId;
    public IReadOnlyList<int> Peers => _inner.Peers;
    public event Action<int>? PeerConnected { add => _inner.PeerConnected += value; remove => _inner.PeerConnected -= value; }
    public event Action<int>? PeerDisconnected { add => _inner.PeerDisconnected += value; remove => _inner.PeerDisconnected -= value; }
    public Task<bool> ListenAsync(int port, CancellationToken ct) => _inner.ListenAsync(port, ct);
    public Task<bool> ConnectAsync(string hostAddress, CancellationToken ct) => _inner.ConnectAsync(hostAddress, ct);
    public void Send(int peerId, ReadOnlySpan<byte> payload) => _inner.Send(peerId, payload);
    public void SendUnreliable(int peerId, ReadOnlySpan<byte> payload)
    {
        LossySent++;
        Sizes.Add(payload.Length);
        if (_rng.NextDouble() * 100 < LossPct) { Lost++; return; }
        _inner.SendUnreliable(peerId, payload);
    }
    public bool TryReceive(out int peerId, out byte[] payload) => _inner.TryReceive(out peerId, out payload);
    public void Dispose() => _inner.Dispose();
}

/// <summary>A transport that loses its next <see cref="Drop"/> sends, for the
/// re-send checks.</summary>
sealed class DropNextSends : ISessionTransport
{
    readonly ISessionTransport _inner;
    public int Drop;
    public DropNextSends(ISessionTransport inner) => _inner = inner;
    public string ProviderId => _inner.ProviderId;
    public IReadOnlyList<int> Peers => _inner.Peers;
    public event Action<int>? PeerConnected { add => _inner.PeerConnected += value; remove => _inner.PeerConnected -= value; }
    public event Action<int>? PeerDisconnected { add => _inner.PeerDisconnected += value; remove => _inner.PeerDisconnected -= value; }
    public Task<bool> ListenAsync(int port, CancellationToken ct) => _inner.ListenAsync(port, ct);
    public Task<bool> ConnectAsync(string hostAddress, CancellationToken ct) => _inner.ConnectAsync(hostAddress, ct);
    public void Send(int peerId, ReadOnlySpan<byte> payload) { if (Drop > 0) { Drop--; return; } _inner.Send(peerId, payload); }
    public bool TryReceive(out int peerId, out byte[] payload) => _inner.TryReceive(out peerId, out payload);
    public void Dispose() => _inner.Dispose();
}
