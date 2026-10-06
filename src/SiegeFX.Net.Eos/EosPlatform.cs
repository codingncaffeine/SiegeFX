using System.Runtime.InteropServices;
using Epic.OnlineServices;
using Epic.OnlineServices.Platform;
using Epic.OnlineServices.Connect;
using SiegeFX.Core.Net;

namespace SiegeFX.Net.Eos;

/// <summary>SC-MP-EOS P4 — EOS platform lifecycle: SDK init, Platform.Create
/// with the dev-portal credentials, anonymous Device-ID login (no Epic
/// account for the player), and the per-frame Tick pump. Credentials come
/// from a config file the USER fills after registering a product in the
/// Epic dev portal — the one manual step; absent config = null platform and
/// the game stays on LAN.</summary>
public sealed class EosPlatform : IDisposable
{
    public PlatformInterface? Platform { get; private set; }
    public ProductUserId? LocalUser { get; private set; }
    public bool LoggedIn => LocalUser != null;
    /// <summary>What the login told Epic about this player and machine. Epic does
    /// not hand the name back to a client for a device-id login (Connect reports
    /// no external account), so the EOS self-test checks these instead.</summary>
    public string? SentDisplayName { get; private set; }
    public string? SentDeviceModel { get; private set; }

    static bool _initialized;
    // The platform this process created. EOS must be shut down before the process
    // ends: its worker threads otherwise run on while exit() destroys the SDK's
    // statics, which crashed every Linux exit after multiplayer was used.
    static EosPlatform? _live;

    public sealed record Config(string ProductId, string SandboxId, string DeploymentId,
                                string ClientId, string ClientSecret);

    /// <summary>Parse eos_config.txt: key=value lines (product_id, sandbox_id,
    /// deployment_id, client_id, client_secret). Returns null when any
    /// required field is missing — the "not configured, use LAN" signal.</summary>
    public static Config? ReadConfig(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadAllLines(path))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith('#')) continue;
                int eq = t.IndexOf('=');
                if (eq > 0) kv[t[..eq].Trim()] = Unwrap(t[(eq + 1)..].Trim());
            }
            string? G(string k) => kv.TryGetValue(k, out var v) && v.Length > 0 ? v : null;
            var p = G("product_id"); var s = G("sandbox_id"); var d = G("deployment_id");
            var ci = G("client_id"); var cs = G("client_secret");
            if (p is null || s is null || d is null || ci is null || cs is null)
            {
                NetLog.Warn("eos_config.txt present but missing fields — need product_id, sandbox_id, deployment_id, client_id, client_secret");
                return null;
            }
            return new Config(p, s, d, ci, cs);
        }
        catch (Exception ex) { NetLog.Error($"eos config read: {ex.Message}"); return null; }
    }

    /// <summary>A value pasted with the placeholder's brackets or quotes around it
    /// ("&lt;id&gt;", "\"id\""): no EOS id or secret contains them, so one
    /// surrounding pair is dropped.</summary>
    static string Unwrap(string v) =>
        v.Length >= 2 && (v[0] == '<' && v[^1] == '>' || v[0] == '"' && v[^1] == '"') ? v[1..^1].Trim() : v;

    public bool Init(Config cfg, string cacheDir)
    {
        try
        {
            if (!_initialized)
            {
                var initOpts = new InitializeOptions { ProductName = "SiegeFX", ProductVersion = "1.0" };
                var ir = PlatformInterface.Initialize(ref initOpts);
                if (ir != Result.Success && ir != Result.AlreadyConfigured)
                { NetLog.Error($"EOS Initialize failed: {ir}"); return false; }
                _initialized = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    _live?.Dispose();
                    PlatformInterface.Shutdown();
                };
            }
            Directory.CreateDirectory(cacheDir);
            var opts = new Options
            {
                ProductId = cfg.ProductId,
                SandboxId = cfg.SandboxId,
                DeploymentId = cfg.DeploymentId,
                ClientCredentials = new ClientCredentials { ClientId = cfg.ClientId, ClientSecret = cfg.ClientSecret },
                IsServer = false,
                CacheDirectory = cacheDir,
                Flags = PlatformFlags.DisableOverlay,
            };
            Platform = PlatformInterface.Create(ref opts);
            if (Platform == null) { NetLog.Error("EOS Platform.Create returned null (bad credentials?)"); return false; }
            _live = this;
            NetLog.Info($"EOS platform up (product {cfg.ProductId[..Math.Min(8, cfg.ProductId.Length)]}...)");
            return true;
        }
        catch (Exception ex) { NetLog.Error($"EOS init: {ex.Message}"); return false; }
    }

    /// <summary>Anonymous Device-ID login — mints a per-device identity with
    /// no Epic account/sign-in. First run creates the device id, then logs in.
    /// Epic keeps two strings with the identity: the model is the OS family and
    /// architecture, the display name the player's multiplayer name (by default
    /// the hero's) — never the computer's name or the login name.</summary>
    public void LoginDeviceId(string playerName, Action<bool> done)
    {
        var connect = Platform?.GetConnectInterface();
        if (connect == null) { done(false); return; }
        SentDeviceModel = DeviceModel();
        var createOpts = new CreateDeviceIdOptions { DeviceModel = SentDeviceModel };
        connect.CreateDeviceId(ref createOpts, null, (ref CreateDeviceIdCallbackInfo ci) =>
        {
            // Success or "already exists" both mean we can log in.
            if (ci.ResultCode != Result.Success && ci.ResultCode != Result.DuplicateNotAllowed)
            { NetLog.Error($"EOS CreateDeviceId: {ci.ResultCode}"); done(false); return; }
            SentDisplayName = DisplayNameFor(playerName);
            var loginOpts = new LoginOptions
            {
                Credentials = new Credentials { Type = ExternalCredentialType.DeviceidAccessToken, Token = null },
                UserLoginInfo = new UserLoginInfo { DisplayName = SentDisplayName },
            };
            connect.Login(ref loginOpts, null, (ref LoginCallbackInfo li) =>
            {
                if (li.ResultCode == Result.Success)
                {
                    LocalUser = li.LocalUserId;
                    NetLog.Info("EOS device-id login ok (anonymous, no Epic account)");
                    done(true);
                }
                else { NetLog.Error($"EOS Connect.Login: {li.ResultCode}"); done(false); }
            });
        });
    }

    /// <summary>"Linux X64", "Windows X64": what a new device id records as its model.</summary>
    static string DeviceModel()
    {
        string os = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux"
                  : OperatingSystem.IsMacOS() ? "macOS" : "Unknown OS";
        return $"{os} {RuntimeInformation.OSArchitecture}";
    }

    /// <summary>The player's name within the SDK's limit (32, counted in UTF-8
    /// bytes, the stricter of its two readings); "Player" when there is none.</summary>
    static string DisplayNameFor(string? playerName) =>
        PlayerNames.Fit(playerName, ConnectInterface.USERLOGININFO_DISPLAYNAME_MAX_LENGTH);

    /// <summary>Drive EOS callbacks — call once per engine frame.</summary>
    public void Tick() => Platform?.Tick();

    public void Dispose()
    {
        Platform?.Release();
        Platform = null;
        if (ReferenceEquals(_live, this)) _live = null;
    }
}
