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

            hs.SendGameStart("/world/maps/map_world/regions/fh_r1", 2); Pump(hs, cs);
            Assert(gsRegion == "/world/maps/map_world/regions/fh_r1" && gsDiff == 2,
                "in-region: GameStart delivered region + difficulty to the client");

            c2.Dispose(); // client drops — host must be told player 1 left
            Assert(hostSawLeave == 1, $"in-region: host fired OnPlayerLeft on disconnect (got {hostSawLeave})");
            hs.Dispose(); cs.Dispose(); h2.Dispose();
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

    static void Pump(MpSession host, MpSession client)
    {
        // Drain both directions a few times (deltas may cascade).
        for (int i = 0; i < 4; i++) { host.Tick(0.0); client.Tick(0.0); }
    }
}
