using System.Diagnostics;
using System.Reflection;
using SiegeFX.Core.Net;

namespace SiegeFX.Runtime;

/// <summary>Internet play, end to end, for the build it runs in: loads the
/// optional EOS module beside the program the way the multiplayer menu does,
/// logs in with the game's credentials (eos_config.txt in the save folder, or
/// the one shipped beside the program), creates a lobby, finds it through
/// Epic's lobby search and leaves it, and checks that the login tells Epic the
/// player's name and the platform, nothing about the computer. Needs the
/// network. The anonymous device identity lives in SIEGEFX_EOS_CACHE when set
/// (a test identity of its own), else in a temporary folder. Exits 0 only when
/// every step works.</summary>
public static class EosSelfTest
{
    const string PlayerName = "SiegeFX selftest";

    public static bool Run()
    {
        NetLog.Verbose = true;
        bool ok = true;
        void Assert(bool cond, string what)
        {
            // Failures go to the console too, so the session log keeps them (a
            // program run under Wine has no visible stderr).
            if (!cond) { Console.WriteLine($"[selftest-eos] FAIL: {what}"); ok = false; }
            else Console.WriteLine($"[selftest-eos] ok: {what}");
        }

        string? module = Render.RenderHost.MpAppFile("SiegeFX.Net.Eos.dll");
        Assert(module is not null, "the EOS module sits beside the program");
        if (module is null) return false;
        string userCfg = Path.Combine(Core.Save.SaveStore.DefaultSaveDirectory(), "eos_config.txt");
        string? cfg = File.Exists(userCfg) ? userCfg : Render.RenderHost.MpAppFile("eos_config.txt");
        Assert(cfg is not null, $"credentials found ({(cfg == userCfg ? "save folder" : "beside the program")})");
        if (cfg is null) return false;
        string cache = Environment.GetEnvironmentVariable("SIEGEFX_EOS_CACHE") is { Length: > 0 } c
            ? c : Path.Combine(Path.GetTempPath(), "siegefx-eos-selftest");

        object? platform;
        try
        {
            var register = Assembly.LoadFrom(module).GetType("SiegeFX.Net.Eos.EosBootstrap")?
                .GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
            platform = register?.Invoke(null, new object[] { cfg, cache, PlayerName });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[selftest-eos] FAIL: loading the module: {ex.GetBaseException()}");
            return false;
        }
        Assert(platform is not null, "the EOS platform starts with these credentials");
        if (platform is null) return false;

        // EOS completes its calls only while the platform ticks.
        var tick = platform.GetType().GetMethod("Tick");
        var loggedIn = platform.GetType().GetProperty("LoggedIn");
        bool PumpUntil(Func<bool> done, double seconds)
        {
            var sw = Stopwatch.StartNew();
            while (!done() && sw.Elapsed.TotalSeconds < seconds)
            {
                tick?.Invoke(platform, null);
                Thread.Sleep(20);
            }
            return done();
        }

        try
        {
            Assert(PumpUntil(() => loggedIn?.GetValue(platform) is true, 30), "logged in with an anonymous device id");
            if (!ok) return false;
            // What the login told Epic: the player's own name, and the platform
            // rather than the computer's name.
            var sentName = platform.GetType().GetProperty("SentDisplayName")?.GetValue(platform) as string;
            Assert(sentName == PlayerName && sentName != Environment.UserName,
                   $"Epic gets the player's name, not the computer's login name (sent '{sentName}')");
            var sentModel = platform.GetType().GetProperty("SentDeviceModel")?.GetValue(platform) as string;
            Assert(sentModel is { Length: > 0 } && !sentModel.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase),
                   $"the device id names the platform, not the computer (sent '{sentModel}')");
            if (!ok) return false;
            var (lobby, transport) = MpProviderFactory.Create("eos");
            using var lobbyService = lobby;
            using var sessionTransport = transport;
            Assert(lobby.ProviderId == "eos", $"the multiplayer factory hands out EOS (got '{lobby.ProviderId}')");
            if (!ok) return false;

            string name = $"selftest {Environment.ProcessId}";
            var attributes = new Dictionary<string, string> { ["map"] = "map_world", ["difficulty"] = "normal" };
            var create = lobby.CreateAsync(name, attributes, CancellationToken.None);
            PumpUntil(() => create.IsCompleted, 30);
            var created = create.IsCompletedSuccessfully ? create.Result : null;
            Assert(created is not null, $"created lobby '{name}'");
            if (created is null) return false;

            // A fresh lobby can take a moment to show up in the search.
            IReadOnlyList<LobbyInfo> found = Array.Empty<LobbyInfo>();
            for (int attempt = 0; attempt < 6 && !found.Any(l => l.HostAddress == created.HostAddress); attempt++)
            {
                if (attempt > 0) PumpUntil(() => false, 2);
                var list = lobby.ListAsync(CancellationToken.None);
                PumpUntil(() => list.IsCompleted, 30);
                found = list.IsCompletedSuccessfully ? list.Result : Array.Empty<LobbyInfo>();
            }
            // A listed lobby is known by its host's player id (what a joiner connects to).
            Assert(found.Any(l => l.HostAddress == created.HostAddress), $"Epic's lobby search finds it ({found.Count} SiegeFX lobbies listed)");

            var leave = lobby.LeaveAsync();
            PumpUntil(() => leave.IsCompleted, 15);
            Assert(leave.IsCompletedSuccessfully, "left the lobby");
        }
        finally
        {
            (platform as IDisposable)?.Dispose();
        }
        Console.WriteLine(ok ? "[selftest-eos] internet play works" : "[selftest-eos] FAILED");
        return ok;
    }
}
