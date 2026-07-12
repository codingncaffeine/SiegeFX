using System.Reflection;
using System.Runtime.InteropServices;

namespace SiegeFX.Core.Net;

/// <summary>F6 — a shareable multiplayer diagnostics header. Written through
/// <see cref="NetLog"/> (which tees into the session log) at each host / join
/// and at in-region init, so a user report ("couldn't connect") opens with the
/// environment in plain sight: build + wire protocol, the effective provider
/// (and whether EOS quietly fell back), the address, ports, and encryption —
/// followed by the usual failure causes. Never logs secrets.</summary>
public static class MpDiag
{
    /// <param name="role">"host" / "client" (+ " (in region)").</param>
    /// <param name="requestedProvider">the provider the player picked ("eos"/"lan").</param>
    /// <param name="activeProvider">the transport actually in use (may be a fallback).</param>
    /// <param name="address">host: own endpoint; client: the host address dialed.</param>
    public static void WriteSession(string role, string requestedProvider, string activeProvider,
        string address, int gamePort, int beaconPort, bool encrypted)
    {
        bool providerFellBack =
            !string.Equals(requestedProvider, "lan", StringComparison.OrdinalIgnoreCase)
            && !activeProvider.StartsWith(requestedProvider, StringComparison.OrdinalIgnoreCase);
        NetLog.Info("=== SiegeFX multiplayer diagnostics ==========================");
        NetLog.Info($"  build      : {BuildString()}   (wire protocol v{MpProtocol.Version})");
        NetLog.Info($"  os         : {RuntimeInformation.OSDescription} / {RuntimeInformation.OSArchitecture}");
        NetLog.Info($"  role       : {role}");
        NetLog.Info(providerFellBack
            ? $"  provider   : requested '{requestedProvider}', ACTIVE '{activeProvider}'  <-- EOS not ready (unconfigured or still logging in); using direct/LAN"
            : $"  provider   : {activeProvider}");
        NetLog.Info($"  address    : {(string.IsNullOrEmpty(address) ? "(pending)" : address)}");
        NetLog.Info($"  ports      : game UDP {gamePort}   |   LAN beacon UDP {beaconPort}");
        NetLog.Info($"  encryption : {(encrypted ? "on (shared passphrase)" : "off")}");
        NetLog.Info("  if a join fails, the usual causes:");
        NetLog.Info("    - host still loading a region (client retries ~60s)");
        NetLog.Info($"    - UDP {gamePort} not port-forwarded / blocked by firewall (direct & LAN only; EOS relays)");
        NetLog.Info("    - wrong host address / id");
        NetLog.Info("    - the two machines are on different SiegeFX builds (protocol mismatch)");
        NetLog.Info("==============================================================");
    }

    /// <summary>Best-effort build identity — informational version if the build
    /// stamped one (git describe / CI), else the assembly version.</summary>
    static string BuildString()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info)) return info!;
        return asm.GetName().Version?.ToString() ?? "unknown";
    }
}
