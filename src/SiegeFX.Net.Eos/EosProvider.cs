using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using SiegeFX.Core.Net;

namespace SiegeFX.Net.Eos;

/// <summary>SC-MP-EOS P4 — ILobbyService over EOS Lobbies. The host creates
/// a lobby carrying its ProductUserId (the P2P "address" clients connect to)
/// plus the game-authored attributes (map, difficulty, players, area, level
/// range). Clients search open lobbies and read those attributes into the
/// Internet game list. Callbacks are pumped by EosPlatform.Tick.</summary>
public sealed class EosLobbyService : ILobbyService
{
    const string BucketId = "SiegeFX:Ehb";
    const string AttrHostPuid = "HOST_PUID";
    const string AttrName = "NAME";

    readonly EosPlatform _plat;
    readonly LobbyInterface _lobby;
    readonly ProductUserId _local;
    string? _createdLobbyId;

    public string ProviderId => "eos";

    public EosLobbyService(EosPlatform plat)
    {
        _plat = plat;
        _lobby = plat.Platform!.GetLobbyInterface();
        _local = plat.LocalUser!;
    }

    public Task<LobbyInfo?> CreateAsync(string name, IReadOnlyDictionary<string, string> attributes, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<LobbyInfo?>();
        var opts = new CreateLobbyOptions
        {
            LocalUserId = _local,
            MaxLobbyMembers = 8,
            PermissionLevel = LobbyPermissionLevel.Publicadvertised,
            PresenceEnabled = false,
            AllowInvites = true,
            BucketId = BucketId,
        };
        _lobby.CreateLobby(ref opts, null, (ref CreateLobbyCallbackInfo info) =>
        {
            if (info.ResultCode != Result.Success)
            { NetLog.Error($"eos: CreateLobby {info.ResultCode}"); tcs.TrySetResult(null); return; }
            _createdLobbyId = info.LobbyId;
            _local.ToString(out var puidBuf);
            string puid = puidBuf?.ToString() ?? "";
            // Stamp the host puid + attributes onto the lobby so searchers
            // can read them.
            var allAttrs = new Dictionary<string, string>(attributes) { [AttrHostPuid] = puid, [AttrName] = name };
            ApplyAttributes(info.LobbyId!, allAttrs);
            NetLog.Info($"eos: lobby '{name}' created ({info.LobbyId}) host puid {puid[..Math.Min(8, puid.Length)]}…");
            tcs.TrySetResult(new LobbyInfo(info.LobbyId!, name, puid, allAttrs));
        });
        return tcs.Task;
    }

    void ApplyAttributes(string lobbyId, IReadOnlyDictionary<string, string> attrs)
    {
        var modOpts = new UpdateLobbyModificationOptions { LocalUserId = _local, LobbyId = lobbyId };
        if (_lobby.UpdateLobbyModification(ref modOpts, out var mod) != Result.Success || mod == null) return;
        foreach (var (k, v) in attrs)
        {
            var data = new AttributeData { Key = k, Value = new AttributeDataValue { AsUtf8 = v } };
            var addOpts = new LobbyModificationAddAttributeOptions { Attribute = data, Visibility = LobbyAttributeVisibility.Public };
            mod.AddAttribute(ref addOpts);
        }
        var updOpts = new UpdateLobbyOptions { LobbyModificationHandle = mod };
        _lobby.UpdateLobby(ref updOpts, null, (ref UpdateLobbyCallbackInfo _) => { });
        mod.Release();
    }

    public Task<IReadOnlyList<LobbyInfo>> ListAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<LobbyInfo>>();
        var searchOpts = new CreateLobbySearchOptions { MaxResults = 32 };
        if (_lobby.CreateLobbySearch(ref searchOpts, out var search) != Result.Success || search == null)
        { tcs.TrySetResult(Array.Empty<LobbyInfo>()); return tcs.Task; }
        // Filter to our bucket so we only see SiegeFX games.
        var param = new LobbySearchSetParameterOptions
        {
            Parameter = new AttributeData { Key = "bucket", Value = new AttributeDataValue { AsUtf8 = BucketId } },
            ComparisonOp = ComparisonOp.Equal,
        };
        search.SetParameter(ref param);
        var findOpts = new LobbySearchFindOptions { LocalUserId = _local };
        search.Find(ref findOpts, null, (ref LobbySearchFindCallbackInfo info) =>
        {
            var results = new List<LobbyInfo>();
            if (info.ResultCode == Result.Success)
            {
                var countOpts = new LobbySearchGetSearchResultCountOptions();
                uint n = search.GetSearchResultCount(ref countOpts);
                for (uint i = 0; i < n; i++)
                {
                    var copyOpts = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = i };
                    if (search.CopySearchResultByIndex(ref copyOpts, out var details) != Result.Success || details == null) continue;
                    string ReadAttr(string key)
                    {
                        var ao = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = key };
                        if (details.CopyAttributeByKey(ref ao, out var attr) == Result.Success && attr.HasValue)
                            return attr.Value.Data?.Value.AsUtf8?.ToString() ?? "";
                        return "";
                    }
                    string hostPuid = ReadAttr(AttrHostPuid);
                    string name = ReadAttr(AttrName);
                    var attrs = new Dictionary<string, string>();
                    foreach (var k in new[] { "map", "difficulty", "players", "host", "area", "levels" })
                    { var val = ReadAttr(k); if (val.Length > 0) attrs[k] = val; }
                    if (hostPuid.Length > 0)
                        results.Add(new LobbyInfo(hostPuid, name.Length > 0 ? name : "SiegeFX Game", hostPuid, attrs));
                    details.Release();
                }
                NetLog.Info($"eos: search found {results.Count} lobby(ies)");
            }
            else NetLog.Warn($"eos: LobbySearch.Find {info.ResultCode}");
            search.Release();
            tcs.TrySetResult(results);
        });
        return tcs.Task;
    }

    public Task<bool> JoinAsync(LobbyInfo lobby, CancellationToken ct) => Task.FromResult(true);

    public Task LeaveAsync()
    {
        if (_createdLobbyId != null)
        {
            var opts = new DestroyLobbyOptions { LocalUserId = _local, LobbyId = _createdLobbyId };
            _lobby.DestroyLobby(ref opts, null, (ref DestroyLobbyCallbackInfo _) => { });
            _createdLobbyId = null;
        }
        return Task.CompletedTask;
    }

    public void Dispose() => LeaveAsync();
}

/// <summary>SC-MP-EOS P4/P7 — registers the EOS provider with the Core
/// factory when a valid eos_config.txt exists. Call once at startup;
/// no-op (staying on LAN) when EOS isn't configured. This is the swappable
/// seam: EOS being discontinued means deleting this call, nothing else.</summary>
public static class EosBootstrap
{
    static EosPlatform? _platform;

    /// <summary>Returns the live platform (for the engine to Tick), or null
    /// when EOS isn't configured/available.</summary>
    public static EosPlatform? Register(string configPath, string cacheDir)
    {
        var cfg = EosPlatform.ReadConfig(configPath);
        if (cfg is null)
        {
            NetLog.Info("eos: no valid eos_config.txt — staying on LAN. Register a product in the Epic dev portal and fill in the config to enable EOS.");
            return null;
        }
        var plat = new EosPlatform();
        if (!plat.Init(cfg, cacheDir)) { plat.Dispose(); return null; }
        _platform = plat;
        // Device-ID login runs async; the provider factory only produces
        // usable transports once LoggedIn, so kick it now.
        plat.LoginDeviceId(ok => { if (!ok) NetLog.Warn("eos: device-id login failed — Internet games unavailable until retry"); });
        MpProviderFactory.Register("eos", () =>
        {
            if (_platform is null || !_platform.LoggedIn) return null;
            return ((ILobbyService)new EosLobbyService(_platform), (ISessionTransport)new EosTransport(_platform));
        });
        return plat;
    }
}
