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

        Console.WriteLine(ok ? "[selftest-net] ALL PASS" : "[selftest-net] FAILURES ABOVE");
        return ok;
    }

    static void Pump(MpSession host, MpSession client)
    {
        // Drain both directions a few times (deltas may cascade).
        for (int i = 0; i < 4; i++) { host.Tick(0.0); client.Tick(0.0); }
    }
}
