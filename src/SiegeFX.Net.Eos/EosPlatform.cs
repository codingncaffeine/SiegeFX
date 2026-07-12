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

    static bool _initialized;

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
                if (eq > 0) kv[t[..eq].Trim()] = t[(eq + 1)..].Trim();
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
            NetLog.Info($"EOS platform up (product {cfg.ProductId[..Math.Min(8, cfg.ProductId.Length)]}...)");
            return true;
        }
        catch (Exception ex) { NetLog.Error($"EOS init: {ex.Message}"); return false; }
    }

    /// <summary>Anonymous Device-ID login — mints a per-device identity with
    /// no Epic account/sign-in. First run creates the device id, then logs in.</summary>
    public void LoginDeviceId(Action<bool> done)
    {
        var connect = Platform?.GetConnectInterface();
        if (connect == null) { done(false); return; }
        var createOpts = new CreateDeviceIdOptions { DeviceModel = Environment.MachineName };
        connect.CreateDeviceId(ref createOpts, null, (ref CreateDeviceIdCallbackInfo ci) =>
        {
            // Success or "already exists" both mean we can log in.
            if (ci.ResultCode != Result.Success && ci.ResultCode != Result.DuplicateNotAllowed)
            { NetLog.Error($"EOS CreateDeviceId: {ci.ResultCode}"); done(false); return; }
            var loginOpts = new LoginOptions
            {
                Credentials = new Credentials { Type = ExternalCredentialType.DeviceidAccessToken, Token = null },
                UserLoginInfo = new UserLoginInfo { DisplayName = Environment.UserName },
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

    /// <summary>Drive EOS callbacks — call once per engine frame.</summary>
    public void Tick() => Platform?.Tick();

    public void Dispose()
    {
        Platform?.Release();
        Platform = null;
    }
}
