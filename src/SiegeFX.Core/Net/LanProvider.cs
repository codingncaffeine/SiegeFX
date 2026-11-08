using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SiegeFX.Core.Net;

/// <summary>SC-MP-EOS P2 — shared net diagnostics. Every connection-path
/// event logs through here with a timestamp so a user report ("couldn't
/// join") pins to beacon/hello/welcome/timeout lines in the session log.</summary>
public static class NetLog
{
    public static bool Verbose = true;
    public static void Info(string msg)  => Console.WriteLine($"[net {DateTime.Now:HH:mm:ss.fff}] {msg}");
    public static void Warn(string msg)  => Console.WriteLine($"[net {DateTime.Now:HH:mm:ss.fff}] WARN {msg}");
    public static void Error(string msg) => Console.Error.WriteLine($"[net {DateTime.Now:HH:mm:ss.fff}] ERROR {msg}");
}

/// <summary>SC-MP-EOS P2 — LAN lobby service: hosts broadcast a beacon on
/// UDP <see cref="BeaconPort"/> once a second; List() collects beacons for
/// ~1.2s. Zero external services — the rug-proof rung. Beacon format:
/// <c>SFXMP1|name|gamePort|k=v;k=v</c> (attributes are game-authored:
/// map, difficulty, players, area, levels).</summary>
public sealed class LanLobbyService : ILobbyService
{
    public const int BeaconPort = 47777;
    const string Magic = "SFXMP1";
    const string PingMagic = "SFXPING", PongMagic = "SFXPONG";
    UdpClient? _beaconTx;
    System.Threading.Timer? _beaconTimer;
    string _beaconPayload = "";
    string _beaconName = "";
    int _beaconGamePort;
    volatile byte[] _beaconBytes = Array.Empty<byte>();
    CancellationTokenSource? _pongCts;
    public string ProviderId => "lan";

    static string Payload(string name, int gamePort, IReadOnlyDictionary<string, string> attributes) =>
        $"{Magic}|{name.Replace('|', ' ')}|{gamePort}|" +
        string.Join(';', attributes.Where(kv => kv.Key != "port").Select(kv => $"{kv.Key}={kv.Value.Replace(';', ',').Replace('|', ' ')}"));

    /// <summary>Host: new listing details (player count, password mark, state);
    /// the next beacon carries them.</summary>
    public void UpdateAttributes(IReadOnlyDictionary<string, string> attributes)
    {
        if (_beaconTx is null) return;
        _beaconPayload = Payload(_beaconName, _beaconGamePort, attributes);
        _beaconBytes = Encoding.UTF8.GetBytes(_beaconPayload);
    }

    // Host: answer a game list's ping on the beacon socket (the scanner sends
    // it to the address a beacon came from), so the list can show the round
    // trip as the original's Ping column did.
    async Task AnswerPings(UdpClient sock, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult res;
            try { res = await sock.ReceiveAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }
            if (res.Buffer.Length > 64) continue;
            var text = Encoding.UTF8.GetString(res.Buffer);
            if (!text.StartsWith(PingMagic + "|", StringComparison.Ordinal)) continue;
            var reply = Encoding.UTF8.GetBytes(PongMagic + text[PingMagic.Length..]);
            try { await sock.SendAsync(reply, reply.Length, res.RemoteEndPoint).ConfigureAwait(false); }
            catch (Exception) { }
        }
    }

    public Task<LobbyInfo?> CreateAsync(string name, IReadOnlyDictionary<string, string> attributes, CancellationToken ct)
    {
        try
        {
            int gamePort = attributes.TryGetValue("port", out var p) && int.TryParse(p, out var pv) ? pv : UdpTransport.DefaultGamePort;
            _beaconName = name;
            _beaconGamePort = gamePort;
            _beaconPayload = Payload(name, gamePort, attributes);
            _beaconBytes = Encoding.UTF8.GetBytes(_beaconPayload);
            _beaconTx = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
            var target = new IPEndPoint(IPAddress.Broadcast, BeaconPort);
            _pongCts = new CancellationTokenSource();
            var tx = _beaconTx;
            _ = Task.Run(() => AnswerPings(tx, _pongCts.Token));
            // Also to this computer's own loopback: a firewall that drops
            // inbound broadcasts (firewalld's default) would otherwise hide a
            // game from a second copy running on the same computer.
            var self = new IPEndPoint(IPAddress.Loopback, BeaconPort);
            _beaconTimer = new System.Threading.Timer(_ =>
            {
                var bytes = _beaconBytes;
                try { _beaconTx?.Send(bytes, bytes.Length, target); }
                catch (Exception ex) { NetLog.Warn($"beacon send failed: {ex.Message}"); }
                try { _beaconTx?.Send(bytes, bytes.Length, self); }
                catch (Exception) { }
            }, null, 0, 1000);
            NetLog.Info($"lan host beacon up: '{name}' game port {gamePort} (broadcast :{BeaconPort})");
            return Task.FromResult<LobbyInfo?>(new LobbyInfo("lan-self", name, $"127.0.0.1:{gamePort}",
                new Dictionary<string, string>(attributes)));
        }
        catch (Exception ex)
        {
            NetLog.Error($"lan host beacon failed: {ex.Message}");
            return Task.FromResult<LobbyInfo?>(null);
        }
    }

    public async Task<IReadOnlyList<LobbyInfo>> ListAsync(CancellationToken ct)
    {
        var found = new Dictionary<string, LobbyInfo>();
        var pinged = new Dictionary<string, (long Sent, string Host)>();   // nonce -> when, which game
        try
        {
            using var rx = new UdpClient();
            rx.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            rx.Client.Bind(new IPEndPoint(IPAddress.Any, BeaconPort));
            var deadline = DateTime.UtcNow.AddMilliseconds(1200);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                var remain = deadline - DateTime.UtcNow;
                var recvTask = rx.ReceiveAsync();
                var done = await Task.WhenAny(recvTask, Task.Delay(remain, ct)).ConfigureAwait(false);
                if (done != recvTask) break;
                var res = recvTask.Result;
                var text = Encoding.UTF8.GetString(res.Buffer);
                if (text.StartsWith(PongMagic + "|", StringComparison.Ordinal))
                {
                    string nonce = text[(PongMagic.Length + 1)..];
                    if (pinged.Remove(nonce, out var sent) && found.TryGetValue(sent.Host, out var game))
                    {
                        long ms = (System.Diagnostics.Stopwatch.GetTimestamp() - sent.Sent) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                        var withPing = new Dictionary<string, string>(game.Attributes) { ["ping"] = ms.ToString(System.Globalization.CultureInfo.InvariantCulture) };
                        found[sent.Host] = game with { Attributes = withPing };
                    }
                    continue;
                }
                var parts = text.Split('|');
                if (parts.Length < 3 || parts[0] != Magic) continue;
                string name = parts[1];
                string host = $"{res.RemoteEndPoint.Address}:{parts[2]}";
                var attrs = new Dictionary<string, string>();
                if (parts.Length > 3)
                    foreach (var kv in parts[3].Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        int eq = kv.IndexOf('=');
                        if (eq > 0) attrs[kv[..eq]] = kv[(eq + 1)..];
                    }
                // The same game heard on loopback and on the network (a copy on
                // the host's own computer): keep the network address.
                bool loop = IPAddress.IsLoopback(res.RemoteEndPoint.Address);
                string twin = found.Values.FirstOrDefault(g => g.Name == name && g.HostAddress.EndsWith($":{parts[2]}", StringComparison.Ordinal)
                    && g.HostAddress != host)?.HostAddress ?? "";
                if (twin.Length > 0)
                {
                    if (loop) continue;
                    if (IPAddress.TryParse(twin.Split(':')[0], out var ta) && IPAddress.IsLoopback(ta)) found.Remove(twin);
                }
                if (!found.ContainsKey(host))
                {
                    found[host] = new LobbyInfo(host, name, host, attrs);
                    NetLog.Info($"lan beacon: '{name}' at {host} ({attrs.Count} attr)");
                    // Ping the beacon's own socket: it answers with the nonce.
                    string nonce = Guid.NewGuid().ToString("N")[..12];
                    pinged[nonce] = (System.Diagnostics.Stopwatch.GetTimestamp(), host);
                    var ping = Encoding.UTF8.GetBytes($"{PingMagic}|{nonce}");
                    try { rx.Send(ping, ping.Length, res.RemoteEndPoint); } catch (Exception) { }
                }
            }
        }
        catch (Exception ex) { NetLog.Warn($"lan scan: {ex.Message}"); }
        return found.Values.ToList();
    }

    public Task<bool> JoinAsync(LobbyInfo lobby, CancellationToken ct) => Task.FromResult(true);

    public Task LeaveAsync()
    {
        bool wasUp = _beaconTx is not null;
        _beaconTimer?.Dispose(); _beaconTimer = null;
        _pongCts?.Cancel(); _pongCts?.Dispose(); _pongCts = null;
        _beaconTx?.Dispose(); _beaconTx = null;
        if (wasUp) NetLog.Info("lan host beacon down");
        return Task.CompletedTask;
    }

    public void Dispose() => LeaveAsync();
}

/// <summary>SC-MP-EOS P2 — direct UDP transport with a tiny connection
/// layer: HELLO/WELCOME handshake (magic + protocol version + assigned
/// peer id), PING keepalive every 2s, 8s silence = disconnect. Works on a
/// LAN out of the box and over the internet with a forwarded port — which
/// is exactly what DS1's own Internet screen did. Frame: [magic u32]
/// [type u8][peer u8][payload]. Payload framing above this is P3.</summary>
public sealed class UdpTransport : ISessionTransport
{
    public const int DefaultGamePort = 47778;
    // Connect retry budget (HELLO resend). Default suits the frontend staging
    // connect; the in-region reconnect raises ConnectAttempts because the host
    // is loading a region in parallel and won't be listening for many seconds.
    public int ConnectAttempts = 5;
    public int ConnectDelayMs = 600;
    const uint Magic = 0x53465831; // "SFX1"
    const byte ProtocolVersion = 1;
    enum Ft : byte { Hello = 1, Welcome = 2, Ping = 3, Data = 4, Bye = 5 }

    UdpClient? _sock;
    CancellationTokenSource? _cts;
    bool _isHost;
    readonly object _lock = new();
    readonly Dictionary<int, (IPEndPoint Ep, DateTime LastSeen)> _peers = new();
    readonly Queue<(int Peer, byte[] Data)> _rx = new();
    int _nextPeerId = 1;
    IPEndPoint? _hostEp;
    DateTime _hostLastSeen;
    int _selfId = -1;

    public string ProviderId => "udp";
    public IReadOnlyList<int> Peers { get { lock (_lock) return _peers.Keys.ToList(); } }
    public event Action<int>? PeerConnected;
    public event Action<int>? PeerDisconnected;
    public bool Connected => _isHost || _selfId >= 0;

    public Task<bool> ListenAsync(int port, CancellationToken ct)
    {
        try
        {
            _sock = new UdpClient(port);
            _isHost = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = Task.Run(() => PumpAsync(_cts.Token));
            _ = Task.Run(() => KeepaliveAsync(_cts.Token));
            NetLog.Info($"host listening on udp :{port}");
            return Task.FromResult(true);
        }
        catch (SocketException ex)
        {
            NetLog.Error($"host listen failed on :{port} — {ex.SocketErrorCode}: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    public async Task<bool> ConnectAsync(string hostAddress, CancellationToken ct)
    {
        try
        {
            var (ip, port) = ParseAddress(hostAddress);
            if (ip is null) { NetLog.Error($"connect: could not resolve '{hostAddress}'"); return false; }
            _hostEp = new IPEndPoint(ip, port);
            _sock = new UdpClient(0);
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = Task.Run(() => PumpAsync(_cts.Token));
            _ = Task.Run(() => KeepaliveAsync(_cts.Token));
            NetLog.Info($"connecting to {_hostEp} (local :{((IPEndPoint)_sock.Client.LocalEndPoint!).Port})...");
            // HELLO resend loop — each unanswered attempt is logged, the first
            // diagnostic for "port not forwarded / firewall / host still loading".
            int attempts = Math.Max(1, ConnectAttempts);
            for (int attempt = 1; attempt <= attempts && !ct.IsCancellationRequested; attempt++)
            {
                SendFrame(_hostEp, Ft.Hello, 0, new[] { ProtocolVersion });
                await Task.Delay(Math.Max(50, ConnectDelayMs), ct).ConfigureAwait(false);
                if (_selfId >= 0)
                {
                    NetLog.Info($"connected: assigned peer id {_selfId} by {_hostEp}");
                    return true;
                }
                NetLog.Warn($"no WELCOME yet (attempt {attempt}/{attempts}) — host down, port blocked, still loading, or version mismatch");
            }
            NetLog.Error($"connect to {_hostEp} timed out after {attempts} attempts");
            return false;
        }
        catch (Exception ex)
        {
            NetLog.Error($"connect failed: {ex.Message}");
            return false;
        }
    }

    static (IPAddress?, int) ParseAddress(string addr)
    {
        addr = addr.Trim();
        int port = DefaultGamePort;
        string hostPart = addr;
        int colon = addr.LastIndexOf(':');
        if (colon > 0 && int.TryParse(addr[(colon + 1)..], out var pp)) { port = pp; hostPart = addr[..colon]; }
        if (IPAddress.TryParse(hostPart, out var ip)) return (ip, port);
        try
        {
            var entry = Dns.GetHostAddresses(hostPart);
            var v4 = entry.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (v4 is not null) { NetLog.Info($"resolved '{hostPart}' -> {v4}"); return (v4, port); }
        }
        catch (Exception ex) { NetLog.Warn($"dns '{hostPart}': {ex.Message}"); }
        return (null, port);
    }

    async Task PumpAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _sock is not null)
        {
            UdpReceiveResult res;
            try { res = await _sock.ReceiveAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (SocketException ex)
            {
                // 10054 = remote sent ICMP port-unreachable (peer gone).
                if (ex.SocketErrorCode != SocketError.ConnectionReset)
                    NetLog.Warn($"recv: {ex.SocketErrorCode}: {ex.Message}");
                continue;
            }
            catch (ObjectDisposedException) { return; }
            var buf = res.Buffer;
            if (buf.Length < 6 || BitConverter.ToUInt32(buf, 0) != Magic) continue;
            var type = (Ft)buf[4];
            byte peerByte = buf[5];
            switch (type)
            {
                case Ft.Hello when _isHost:
                    HandleHello(res.RemoteEndPoint, buf);
                    break;
                case Ft.Welcome when !_isHost:
                    if (buf.Length >= 7 && buf[6] == ProtocolVersion) { _selfId = peerByte; _hostLastSeen = DateTime.UtcNow; }
                    else NetLog.Error($"WELCOME version mismatch (host {(buf.Length >= 7 ? buf[6] : -1)} vs local {ProtocolVersion}) — update both sides");
                    break;
                case Ft.Ping:
                    Touch(res.RemoteEndPoint, peerByte);
                    break;
                case Ft.Data:
                {
                    int from = _isHost ? FindPeer(res.RemoteEndPoint) : 0;
                    if (from < 0) { NetLog.Warn($"data from unknown {res.RemoteEndPoint} — dropped (no HELLO)"); break; }
                    Touch(res.RemoteEndPoint, peerByte);
                    var payload = new byte[buf.Length - 6];
                    Array.Copy(buf, 6, payload, 0, payload.Length);
                    lock (_lock) _rx.Enqueue((from, payload));
                    break;
                }
                case Ft.Bye:
                {
                    int gone = _isHost ? FindPeer(res.RemoteEndPoint) : 0;
                    if (gone >= 0) DropPeer(gone, "BYE");
                    break;
                }
            }
        }
    }

    void HandleHello(IPEndPoint ep, byte[] buf)
    {
        if (buf.Length >= 7 && buf[6] != ProtocolVersion)
        {
            NetLog.Warn($"HELLO from {ep} with protocol v{buf[6]} (local v{ProtocolVersion}) — refused");
            return;
        }
        int existing = FindPeer(ep);
        int id;
        if (existing >= 0) id = existing;
        else
        {
            lock (_lock) { id = _nextPeerId++; _peers[id] = (ep, DateTime.UtcNow); }
            NetLog.Info($"peer {id} connected from {ep}");
            PeerConnected?.Invoke(id);
        }
        SendFrame(ep, Ft.Welcome, (byte)id, new[] { ProtocolVersion });
    }

    int FindPeer(IPEndPoint ep)
    {
        lock (_lock)
            foreach (var (id, v) in _peers)
                if (v.Ep.Equals(ep)) return id;
        return -1;
    }

    void Touch(IPEndPoint ep, byte peerByte)
    {
        if (_isHost)
        {
            int id = FindPeer(ep);
            if (id >= 0) lock (_lock) _peers[id] = (ep, DateTime.UtcNow);
        }
        else _hostLastSeen = DateTime.UtcNow;
    }

    async Task KeepaliveAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(2000, ct).ConfigureAwait(false); } catch { return; }
            if (_isHost)
            {
                List<int> dead = new();
                lock (_lock)
                    foreach (var (id, v) in _peers)
                        if ((DateTime.UtcNow - v.LastSeen).TotalSeconds > 8) dead.Add(id);
                        else SendFrame(v.Ep, Ft.Ping, 0, Array.Empty<byte>());
                foreach (var id in dead) DropPeer(id, "timeout (8s silence)");
            }
            else if (_hostEp is not null && _selfId >= 0)
            {
                SendFrame(_hostEp, Ft.Ping, (byte)_selfId, Array.Empty<byte>());
                if ((DateTime.UtcNow - _hostLastSeen).TotalSeconds > 8)
                {
                    NetLog.Error("host silent for 8s — disconnected");
                    _selfId = -1;
                    PeerDisconnected?.Invoke(0);
                }
            }
        }
    }

    void DropPeer(int id, string why)
    {
        lock (_lock) _peers.Remove(id);
        NetLog.Warn($"peer {id} dropped: {why}");
        PeerDisconnected?.Invoke(id);
    }

    void SendFrame(IPEndPoint ep, Ft type, byte peer, byte[] payload)
    {
        if (_sock is null) return;
        var frame = new byte[6 + payload.Length];
        BitConverter.TryWriteBytes(frame.AsSpan(0, 4), Magic);
        frame[4] = (byte)type;
        frame[5] = peer;
        payload.CopyTo(frame, 6);
        try { _sock.Send(frame, frame.Length, ep); }
        catch (Exception ex) { NetLog.Warn($"send {type} to {ep}: {ex.Message}"); }
    }

    public void Send(int peerId, ReadOnlySpan<byte> payload)
    {
        IPEndPoint? ep = null;
        if (_isHost) { lock (_lock) if (_peers.TryGetValue(peerId, out var v)) ep = v.Ep; }
        else ep = _hostEp;
        if (ep is null) { NetLog.Warn($"send to unknown peer {peerId}"); return; }
        var frame = new byte[6 + payload.Length];
        BitConverter.TryWriteBytes(frame.AsSpan(0, 4), Magic);
        frame[4] = (byte)Ft.Data;
        frame[5] = (byte)(_isHost ? 0 : _selfId);
        payload.CopyTo(frame.AsSpan(6));
        try { _sock?.Send(frame, frame.Length, ep); }
        catch (Exception ex) { NetLog.Warn($"send data to {ep}: {ex.Message}"); }
    }

    public bool TryReceive(out int peerId, out byte[] payload)
    {
        lock (_lock)
        {
            if (_rx.Count == 0) { peerId = -1; payload = Array.Empty<byte>(); return false; }
            (peerId, payload) = _rx.Dequeue();
            return true;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_sock is not null)
            {
                if (_isHost) { lock (_lock) foreach (var v in _peers.Values) SendFrame(v.Ep, Ft.Bye, 0, Array.Empty<byte>()); }
                else if (_hostEp is not null) SendFrame(_hostEp, Ft.Bye, (byte)Math.Max(0, _selfId), Array.Empty<byte>());
            }
        }
        catch { }
        _cts?.Cancel();
        _sock?.Dispose();
        _sock = null;
        NetLog.Info("transport closed");
    }
}
