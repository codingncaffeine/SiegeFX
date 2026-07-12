using System.Net;

namespace SiegeFX.Core.Net;

/// <summary>SC-MP-EOS P1 — the transport seam. Game code talks ONLY to
/// these interfaces; providers (loopback → LAN → EOS → whatever replaces
/// EOS) are swappable implementations. This is the load-bearing rug-proofing:
/// losing a vendor means writing a new implementation, not touching the game.</summary>
public interface ILobbyService : IDisposable
{
    /// <summary>Create a session and become its host. Attributes are
    /// game-authored (map, difficulty, players, host area, level range).</summary>
    Task<LobbyInfo?> CreateAsync(string name, IReadOnlyDictionary<string, string> attributes, CancellationToken ct);
    /// <summary>List joinable sessions (LAN broadcast scan / EOS search).</summary>
    Task<IReadOnlyList<LobbyInfo>> ListAsync(CancellationToken ct);
    Task<bool> JoinAsync(LobbyInfo lobby, CancellationToken ct);
    Task LeaveAsync();
    /// <summary>Provider id for logs/config ("loopback", "lan", "eos").</summary>
    string ProviderId { get; }
}

public sealed record LobbyInfo(
    string Id,
    string Name,
    string HostAddress,
    IReadOnlyDictionary<string, string> Attributes);

/// <summary>Datagram transport to one or more session peers. Reliability
/// channels ride ABOVE this (protocol layer, P3); implementations only move
/// bytes: loopback queues, LAN UDP sockets, EOS P2P connections.</summary>
public interface ISessionTransport : IDisposable
{
    /// <summary>Open as HOST — accept incoming peers.</summary>
    Task<bool> ListenAsync(int port, CancellationToken ct);
    /// <summary>Open as CLIENT toward a host address (ip:port for direct
    /// providers; opaque peer id for relayed ones).</summary>
    Task<bool> ConnectAsync(string hostAddress, CancellationToken ct);
    void Send(int peerId, ReadOnlySpan<byte> payload);
    /// <summary>Drain one received datagram; false when the queue is empty.
    /// peerId identifies the sender (host sees clients 1..N; clients see 0).</summary>
    bool TryReceive(out int peerId, out byte[] payload);
    /// <summary>Connected peer ids (host: all clients; client: {0}).</summary>
    IReadOnlyList<int> Peers { get; }
    event Action<int>? PeerConnected;
    event Action<int>? PeerDisconnected;
    string ProviderId { get; }
}

/// <summary>P1 receipt — in-process loopback provider: host and client in
/// the same process (or two instances via a named-pipe-free localhost UDP
/// pair would be P2's job; this one proves the seam compiles against real
/// call sites and lets the session screens drive a fake session end-to-end).</summary>
public sealed class LoopbackTransport : ISessionTransport
{
    readonly Queue<(int Peer, byte[] Data)> _rx = new();
    LoopbackTransport? _other;
    int _selfId;
    public IReadOnlyList<int> Peers => _other is null ? Array.Empty<int>() : new[] { _other._selfId };
    public event Action<int>? PeerConnected;
    public event Action<int>? PeerDisconnected;
    public string ProviderId => "loopback";

    public static (LoopbackTransport Host, LoopbackTransport Client) CreatePair()
    {
        var h = new LoopbackTransport { _selfId = 0 };
        var c = new LoopbackTransport { _selfId = 1 };
        h._other = c; c._other = h;
        h.PeerConnected?.Invoke(1);
        c.PeerConnected?.Invoke(0);
        return (h, c);
    }

    public Task<bool> ListenAsync(int port, CancellationToken ct) => Task.FromResult(true);
    public Task<bool> ConnectAsync(string hostAddress, CancellationToken ct) => Task.FromResult(_other is not null);
    public void Send(int peerId, ReadOnlySpan<byte> payload)
    {
        var copy = payload.ToArray();
        lock (_other!._rx) _other._rx.Enqueue((_selfId, copy));
    }
    public bool TryReceive(out int peerId, out byte[] payload)
    {
        lock (_rx)
        {
            if (_rx.Count == 0) { peerId = -1; payload = Array.Empty<byte>(); return false; }
            (peerId, payload) = _rx.Dequeue();
            return true;
        }
    }
    public void Dispose()
    {
        if (_other is not null) { _other.PeerDisconnected?.Invoke(_selfId); _other._other = null; }
        _other = null;
    }
}
