namespace SiegeFX.Core.Net;

/// <summary>SC-MP-EOS P4/P7 — provider selection is a CONFIG VALUE, which is
/// the load-bearing rug-proofing: EOS being discontinued is a one-line
/// config change, never a code change. The factory hands the game an
/// <see cref="ILobbyService"/> + <see cref="ISessionTransport"/> pair for
/// the chosen provider; unknown/unavailable providers fall back to LAN,
/// which needs no services and can never be taken away.</summary>
public static class MpProviderFactory
{
    /// <summary>Registered non-LAN provider builders (EOS registers itself
    /// from SiegeFX.Net.Eos when that assembly is present + configured). Key
    /// is the provider id used in config; value builds the pair or returns
    /// null when that provider isn't actually usable (missing creds/SDK).</summary>
    static readonly Dictionary<string, Func<(ILobbyService, ISessionTransport)?>> _builders =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string id, Func<(ILobbyService, ISessionTransport)?> builder)
    {
        _builders[id] = builder;
        NetLog.Info($"provider registered: {id}");
    }

    public static IReadOnlyCollection<string> Available =>
        new[] { "lan" }.Concat(_builders.Keys).ToArray();

    /// <summary>Build the provider named by <paramref name="providerId"/>.
    /// Falls back to LAN when the requested provider is unknown or reports
    /// itself unavailable — MP never hard-fails on a missing vendor.</summary>
    public static (ILobbyService Lobby, ISessionTransport Transport) Create(string providerId)
    {
        if (!string.Equals(providerId, "lan", StringComparison.OrdinalIgnoreCase)
            && _builders.TryGetValue(providerId, out var build))
        {
            var pair = build();
            if (pair is { } p) { NetLog.Info($"using provider: {providerId}"); return p; }
            NetLog.Warn($"provider '{providerId}' unavailable (not configured?) — falling back to LAN");
        }
        else if (!string.Equals(providerId, "lan", StringComparison.OrdinalIgnoreCase))
        {
            NetLog.Warn($"unknown provider '{providerId}' — falling back to LAN");
        }
        return (new LanLobbyService(), new UdpTransport());
    }

    /// <summary>Read the configured provider id: SIEGEFX_MP_PROVIDER env
    /// wins, then <paramref name="configPath"/>'s first line, else "lan".</summary>
    public static string ConfiguredProvider(string configPath)
    {
        var env = Environment.GetEnvironmentVariable("SIEGEFX_MP_PROVIDER");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        try
        {
            if (File.Exists(configPath))
            {
                foreach (var line in File.ReadAllLines(configPath))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t.StartsWith('#')) continue;
                    return t;
                }
            }
        }
        catch { }
        return "lan";
    }
}
