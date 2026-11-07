using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using SiegeFX.Core.Net;

namespace SiegeFX.Runtime;

/// <summary>Internet play, end to end, for the build it runs in: loads the
/// optional EOS module beside the program the way the multiplayer menu does,
/// logs in with the game's credentials (eos_config.txt in the save folder, or
/// the one shipped beside the program), creates a lobby, finds it through
/// Epic's lobby search and leaves it, and checks that the login tells Epic the
/// player's name and the platform, nothing about the computer. Then a second
/// copy of the program, with a test identity of its own, joins this one over
/// Epic's P2P the way a friend joins from the Internet list: the join is
/// answered, both copies show the same roster, the joiner's READY reaches the
/// host, the host's START reaches the joiner, and both sides connect again on
/// new connections the way the relaunch into the region does. Needs the
/// network. Epic's cache lives in SIEGEFX_EOS_CACHE when set, else in a
/// temporary folder. Epic keeps the anonymous device identity per user
/// profile: on Linux the joiner gets a data folder of its own (its identity
/// and session log), elsewhere the two copies come out as one player and the
/// join steps are reported as skipped. Exits 0 only when every step that ran
/// works.</summary>
public static class EosSelfTest
{
    const string PlayerName = "SiegeFX selftest";
    const string PeerName = "SiegeFX selftest peer";
    public const string PeerArg = "--selftest-eos-peer";
    /// <summary>The joiner's exit code when it signed in as the host's own player.</summary>
    public const int SameIdentity = 2;
    const string StartRegion = "/world/maps/map_world/regions/fh_r1";

    public static bool Run()
    {
        var (platform, ok, assert, cache, cfg) = Start("selftest-eos", PlayerName, null, null);
        if (platform is null) return false;
        var pumpUntil = PumpLoop(platform, null, null);

        try
        {
            // What the login told Epic: the player's own name, and the platform
            // rather than the computer's name.
            var sentName = platform.GetType().GetProperty("SentDisplayName")?.GetValue(platform) as string;
            assert(sentName == PlayerName && sentName != Environment.UserName,
                   $"Epic gets the player's name, not the computer's login name (sent '{sentName}')");
            var sentModel = platform.GetType().GetProperty("SentDeviceModel")?.GetValue(platform) as string;
            assert(sentModel is { Length: > 0 } && !sentModel.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase),
                   $"the device id names the platform, not the computer (sent '{sentModel}')");
            if (!ok()) return false;
            var (lobby, transport) = MpProviderFactory.Create("eos");
            using var lobbyService = lobby;
            using var sessionTransport = transport;
            assert(lobby.ProviderId == "eos", $"the multiplayer factory hands out EOS (got '{lobby.ProviderId}')");
            if (!ok()) return false;

            string name = $"selftest {Environment.ProcessId}";
            var attributes = new Dictionary<string, string> { ["map"] = "map_world", ["difficulty"] = "normal" };
            var create = lobby.CreateAsync(name, attributes, CancellationToken.None);
            pumpUntil(() => create.IsCompleted, 30);
            var created = create.IsCompletedSuccessfully ? create.Result : null;
            assert(created is not null, $"created lobby '{name}'");
            if (created is null) return false;

            // A fresh lobby can take a moment to show up in the search.
            IReadOnlyList<LobbyInfo> found = Array.Empty<LobbyInfo>();
            for (int attempt = 0; attempt < 6 && !found.Any(l => l.HostAddress == created.HostAddress); attempt++)
            {
                if (attempt > 0) pumpUntil(() => false, 2);
                var list = lobby.ListAsync(CancellationToken.None);
                pumpUntil(() => list.IsCompleted, 30);
                found = list.IsCompletedSuccessfully ? list.Result : Array.Empty<LobbyInfo>();
            }
            // A listed lobby is known by its host's player id (what a joiner connects to).
            assert(found.Any(l => l.HostAddress == created.HostAddress), $"Epic's lobby search finds it ({found.Count} SiegeFX lobbies listed)");

            if (ok()) HostTheJoiner(platform, created.HostAddress, cache, cfg, assert);

            var leave = lobby.LeaveAsync();
            pumpUntil(() => leave.IsCompleted, 15);
            assert(leave.IsCompletedSuccessfully, "left the lobby");
        }
        finally
        {
            (platform as IDisposable)?.Dispose();
        }
        Console.WriteLine(ok() ? "[selftest-eos] internet play works" : "[selftest-eos] FAILED");
        return ok();
    }

    /// <summary>The host's half of the join: a second copy of the program joins
    /// over P2P and readies up, START goes out the way the staging screen sends
    /// it, then both sides start over on new connections (the relaunch into the
    /// region) and the joiner joins again, then leaves.</summary>
    static void HostTheJoiner(object platform, string hostId, string cache, string cfg, Action<bool, string> assert)
    {
        var peerOut = new ConcurrentQueue<string>();
        using var peer = StartPeer(hostId, cache + "-peer", cfg, peerOut);
        if (peer is null) { assert(false, "started a second copy of the program to join"); return; }
        try
        {
            var (lobby1, t1) = MpProviderFactory.Create("eos");
            using (lobby1)
            using (t1)
            {
                t1.ListenAsync(0, CancellationToken.None);
                var host = new MpSession(t1, isHost: true, PlayerName);
                host.BuildSnapshot = SnapshotBytes;
                var pump = PumpLoop(platform, t1, host, peerOut);
                bool Joined() => host.Roster.Any(e => e.Player != 0 && e.Name == PeerName);
                pump(() => Joined() || peer.HasExited, 60);
                if (!Joined() && peer.HasExited && peer.ExitCode == SameIdentity)
                {
                    Console.WriteLine("[selftest-eos] skipped: the join steps (this system keeps one Epic identity " +
                                      "per user account, so a second copy here is the same player; they run on Linux)");
                    return;
                }
                assert(Joined(), "a second copy of the game joins over Epic's P2P (its JoinRequest answered)");
                if (!Joined()) return;
                assert(pump(() => host.NotReady.Count == 0, 20), "the joiner's READY shows on the host");
                // START: re-sent for 0.6 s, then the connection drops, as in the game.
                for (int i = 0; i < 6; i++) { host.SendGameStart(StartRegion, 1); pump(() => false, 0.1); }
                host.Dispose();
            }
            // The host loads its region slower than the joiner here: the joiner
            // asks before anyone is listening and has to keep asking.
            PumpLoop(platform, null, null, peerOut)(() => false, 8);

            var (lobby2, t2) = MpProviderFactory.Create("eos");
            using (lobby2)
            using (t2)
            {
                t2.ListenAsync(0, CancellationToken.None);
                var host2 = new MpSession(t2, isHost: true, PlayerName);
                string? left = null;
                host2.OnPlayerLeft = id => left = host2.Roster.Any(e => e.Player == id) ? "still listed" : "gone";
                var pump = PumpLoop(platform, t2, host2, peerOut);
                bool Back() => host2.Roster.Any(e => e.Name == PeerName);
                assert(pump(Back, 60), "the joiner connects again after START (the relaunch into the region)");
                if (Back()) assert(pump(() => left is not null, 30) && left == "gone", $"the host sees the joiner leave ({left ?? "no word"})");
                pump(() => peer.HasExited, 30);
                host2.Dispose();
            }
        }
        finally
        {
            if (!peer.HasExited) { try { peer.Kill(true); } catch { } }
            peer.WaitForExit(5000);
            while (peerOut.TryDequeue(out var line)) Console.WriteLine(line);
        }
        assert(peer.ExitCode == 0, $"the joining copy finished its steps (exit {peer.ExitCode})");
    }

    static Process? StartPeer(string hostId, string cache, string cfg, ConcurrentQueue<string> output)
    {
        string? exe = Environment.ProcessPath;
        if (exe is null) return null;
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        // A dev build may run as "dotnet SiegeFX.dll"; the shipped builds are the program itself.
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            psi.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
        psi.ArgumentList.Add(PeerArg);
        psi.ArgumentList.Add(hostId);
        psi.ArgumentList.Add(cfg);
        psi.Environment["SIEGEFX_EOS_CACHE"] = cache;
        // Epic's device identity follows the user profile; on Linux that is the
        // data folder, so the joiner gets one of its own and is a second player.
        if (OperatingSystem.IsLinux())
        {
            string data = cache + "-data";
            Directory.CreateDirectory(data);
            psi.Environment["XDG_DATA_HOME"] = data;
        }
        var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 } l && l.Contains("[selftest-eos peer]")) output.Enqueue("  " + l); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } l) output.Enqueue("  [selftest-eos peer] stderr: " + l); };
        try { p.Start(); } catch (Exception ex) { Console.WriteLine($"[selftest-eos] starting the joiner: {ex.Message}"); p.Dispose(); return null; }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    /// <summary>The joiner's half, run as a second copy of the program:
    /// <c>--selftest-eos-peer &lt;host product user id&gt; &lt;eos_config.txt&gt;</c>.
    /// Returns the exit code: 0 when every step works, <see cref="SameIdentity"/>
    /// when this copy signed in as the host's own player.</summary>
    public static int RunPeer(string hostId, string cfg)
    {
        var (platform, ok, assert, _, _) = Start("selftest-eos peer", PeerName, hostId, cfg);
        if (platform is null) return 1;
        try
        {
            string me0 = platform.GetType().GetProperty("LocalUserIdText")?.GetValue(platform) as string ?? "";
            if (me0 == hostId)
            {
                Console.WriteLine("[selftest-eos peer] signed in as the host's own player (one Epic identity per user account here)");
                return SameIdentity;
            }
            var (lobby, transport) = MpProviderFactory.Create("eos");
            using (lobby)
            using (transport)
            {
                transport.ConnectAsync(hostId, CancellationToken.None);
                var me = new MpSession(transport, isHost: false, PeerName);
                string? startRegion = null;
                byte[]? snapshot = null;
                me.OnGameStart = (region, _) => startRegion = region;
                me.ApplySnapshot = b => snapshot = b;
                var pump = PumpLoop(platform, transport, me);
                me.SendJoinRequest();
                assert(pump(() => me.Joined, 60), "the host answered the JoinRequest");
                if (!ok()) return 1;
                assert(snapshot is not null && snapshot.AsSpan().SequenceEqual(SnapshotBytes()),
                       $"the world snapshot arrived whole ({snapshot?.Length ?? 0} B: several Epic packets)");
                me.SetReady(true);
                assert(pump(() => me.Roster.Any(e => e.Player == me.LocalPlayerId && e.Name == PeerName && e.Ready)
                                  && me.Roster.Any(e => e.Player == 0 && e.Name == PlayerName), 20),
                       "the host's roster names both players and shows this one ready");
                assert(pump(() => startRegion is not null, 30), $"START arrived (region '{startRegion}')");
                me.Dispose();
            }
            if (!ok()) return 1;

            // The relaunch: a new connection to the same host, joined again,
            // after a moment of region loading (the host takes longer).
            PumpLoop(platform, null, null)(() => false, 3);
            var (lobby2, t2) = MpProviderFactory.Create("eos");
            using (lobby2)
            using (t2)
            {
                t2.ConnectAsync(hostId, CancellationToken.None);
                var again = new MpSession(t2, isHost: false, PeerName);
                var pump = PumpLoop(platform, t2, again);
                again.SendJoinRequest();
                assert(pump(() => again.Joined, 60), "joined again on a new connection");
                pump(() => false, 1);
                again.Dispose();
            }
        }
        finally
        {
            (platform as IDisposable)?.Dispose();
        }
        Console.WriteLine(ok() ? "[selftest-eos peer] done" : "[selftest-eos peer] FAILED");
        return ok() ? 0 : 1;
    }

    public const string LoadPeerArg = "--selftest-mp-peer";

    /// <summary>A headless player for network load runs against a real host
    /// over Epic: <c>--selftest-mp-peer &lt;host product user id&gt;
    /// &lt;eos_config.txt&gt; &lt;seconds&gt; &lt;index&gt;</c>. It joins, then
    /// walks a circle around the host's player (radius 10 + 6 × index m, 3 m/s,
    /// at the host's height), so the host's mobs see and chase it, and logs
    /// every 10 s what the world updates brought: updates used a second, the
    /// download, how many actors it follows and how many of them walk, and how
    /// much later than the steady 15 a second the updates arrive (growth = a
    /// backlog). Exits 0 when it joined and updates kept coming.</summary>
    public static int RunLoadPeer(string hostId, string cfg, double seconds, int index)
    {
        string name = $"Load peer {index}";
        var (platform, ok, assert, _, _) = Start("mp-peer", name, hostId, cfg);
        if (platform is null) return 1;
        try
        {
            var (lobby, transport) = MpProviderFactory.Create("eos");
            using (lobby)
            using (transport)
            {
                transport.ConnectAsync(hostId, CancellationToken.None);
                var me = new MpSession(transport, isHost: false, name) { StatsInterval = 0 };
                var clock = Stopwatch.StartNew();
                double radius = 10 + 6 * index, phase = index * 0.9;
                me.BuildLocalPlayerState = () =>
                {
                    var h = me.HostState ?? default;
                    double a = phase + clock.Elapsed.TotalSeconds * 3.0 / radius;
                    return new MpPlayerState(0, h.X + (float)(radius * Math.Cos(a)), h.Y, h.Z + (float)(radius * Math.Sin(a)),
                        (float)(a + Math.PI), 200, (byte)MpPlayerFlags.Moving);
                };
                double t0 = -1; uint tick0 = 0;
                var lateness = new List<double>();
                long updates = 0;
                me.OnWorldUpdate = tick =>
                {
                    double now = clock.Elapsed.TotalSeconds;
                    if (t0 < 0) { t0 = now; tick0 = tick; }
                    lateness.Add((now - t0) - (tick - tick0) / 15.0);
                    updates++;
                };
                var pump = PumpLoop(platform, transport, me);
                me.SendJoinRequest();
                assert(pump(() => me.Joined, 90), "joined the host");
                if (!ok()) return 1;
                double end = clock.Elapsed.TotalSeconds + seconds, lastReport = clock.Elapsed.TotalSeconds;
                long totalUpdates = 0;
                double firstP95 = double.NaN, lastP95 = double.NaN;
                while (clock.Elapsed.TotalSeconds < end)
                {
                    pump(() => clock.Elapsed.TotalSeconds - lastReport >= 10, 11);
                    double span = clock.Elapsed.TotalSeconds - lastReport;
                    lastReport = clock.Elapsed.TotalSeconds;
                    lateness.Sort();
                    double p95 = lateness.Count > 0 ? lateness[Math.Max(0, (int)Math.Ceiling(0.95 * lateness.Count) - 1)] : double.NaN;
                    if (double.IsNaN(firstP95) && lateness.Count > 0) firstP95 = p95;
                    if (lateness.Count > 0) lastP95 = p95;
                    var w = me.World;
                    int followed = w.Current.Count(e => e.Key >= MpQuant.FirstActorKey);
                    int walking = w.Current.Count(e => e.Key >= MpQuant.FirstActorKey && (e.S.Flags & (byte)MpPlayerFlags.Moving) != 0);
                    Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"[mp-peer {index}] {updates / span:0.0} updates/s, lateness p95 {p95 * 1000:0} ms, follows {followed} actors ({walking} walking), " +
                        $"late {w.Late} no-baseline {w.NoBaseline} malformed {w.Malformed} | {me.Stats.Report(span)}"));
                    totalUpdates += updates;
                    updates = 0;
                    lateness.Clear();
                }
                assert(totalUpdates > seconds * 5, $"world updates kept coming ({totalUpdates} in {seconds:0} s)");
                Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"[mp-peer {index}] lateness p95 first window {firstP95 * 1000:0} ms, last window {lastP95 * 1000:0} ms"));
                me.Dispose();
            }
        }
        finally
        {
            (platform as IDisposable)?.Dispose();
        }
        return ok() ? 0 : 1;
    }

    /// <summary>A world snapshot the size of a real one that will not compress
    /// below Epic's 1170-byte packet, the same on both copies.</summary>
    static byte[] SnapshotBytes()
    {
        var b = new byte[8192];
        new Random(20261107).NextBytes(b);
        return b;
    }

    /// <summary>Load the module, start the platform and log in.</summary>
    static (object? Platform, Func<bool> Ok, Action<bool, string> Assert, string Cache, string Cfg) Start(
        string tag, string playerName, string? peerOf, string? cfgPath)
    {
        NetLog.Verbose = true;
        bool ok = true;
        void Assert(bool cond, string what)
        {
            // Failures go to the console too, so the session log keeps them (a
            // program run under Wine has no visible stderr).
            if (!cond) { Console.WriteLine($"[{tag}] FAIL: {what}"); ok = false; }
            else Console.WriteLine($"[{tag}] ok: {what}");
        }
        (object?, Func<bool>, Action<bool, string>, string, string) none = (null, () => false, Assert, "", "");

        string? module = Render.RenderHost.MpAppFile("SiegeFX.Net.Eos.dll");
        Assert(module is not null, "the EOS module sits beside the program");
        if (module is null) return none;
        string userCfg = Path.Combine(Core.Save.SaveStore.DefaultSaveDirectory(), "eos_config.txt");
        string? cfg = cfgPath ?? (File.Exists(userCfg) ? userCfg : Render.RenderHost.MpAppFile("eos_config.txt"));
        Assert(cfg is not null, $"credentials found ({(cfgPath is not null ? "the host's" : cfg == userCfg ? "save folder" : "beside the program")})");
        if (cfg is null) return none;
        string cache = Environment.GetEnvironmentVariable("SIEGEFX_EOS_CACHE") is { Length: > 0 } c
            ? c : Path.Combine(Path.GetTempPath(), "siegefx-eos-selftest");
        if (peerOf is not null) Console.WriteLine($"[{tag}] joining host {peerOf[..Math.Min(8, peerOf.Length)]}…");

        object? platform;
        try
        {
            var register = Assembly.LoadFrom(module).GetType("SiegeFX.Net.Eos.EosBootstrap")?
                .GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
            platform = register?.Invoke(null, new object[] { cfg, cache, playerName });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{tag}] FAIL: loading the module: {ex.GetBaseException()}");
            return none;
        }
        Assert(platform is not null, "the EOS platform starts with these credentials");
        if (platform is null) return none;

        var loggedIn = platform.GetType().GetProperty("LoggedIn");
        Assert(PumpLoop(platform, null, null)(() => loggedIn?.GetValue(platform) is true, 30), "logged in with an anonymous device id");
        if (!ok) { (platform as IDisposable)?.Dispose(); return none; }
        return (platform, () => ok, Assert, cache, cfg);
    }

    /// <summary>A wait that keeps everything moving the way the game's frame
    /// does: EOS completes its calls only while the platform ticks, P2P
    /// packets wait until the transport is drained, and the session answers
    /// and re-sends from its Tick.</summary>
    static Func<Func<bool>, double, bool> PumpLoop(object platform, ISessionTransport? transport, MpSession? session,
                                                   ConcurrentQueue<string>? echo = null)
    {
        var tick = platform.GetType().GetMethod("Tick");
        var drain = transport?.GetType().GetMethod("Pump");
        return (done, seconds) =>
        {
            var sw = Stopwatch.StartNew();
            double last = 0;
            while (!done() && sw.Elapsed.TotalSeconds < seconds)
            {
                tick?.Invoke(platform, null);
                drain?.Invoke(transport, null);
                double now = sw.Elapsed.TotalSeconds;
                session?.Tick(now - last);
                last = now;
                if (echo is not null) while (echo.TryDequeue(out var line)) Console.WriteLine(line);
                Thread.Sleep(20);
            }
            return done();
        };
    }
}
