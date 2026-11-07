namespace SiegeFX.Core.Net;

/// <summary>A host and any number of clients in one process, for tests: the
/// host's transport sees clients 1..N, each client sees the host as 0. Frames
/// are copied into the receiver's queue at once, in order.</summary>
public sealed class LoopbackHub
{
    public ISessionTransport Host => _host;
    public IReadOnlyList<ISessionTransport> Clients => _clients;
    readonly End _host;
    readonly List<End> _clients = new();

    public LoopbackHub(int clients)
    {
        _host = new End(this, 0);
        for (int i = 1; i <= clients; i++) _clients.Add(new End(this, i));
    }

    End? Find(int id) => id == 0 ? (_host.Open ? _host : null) : id - 1 < _clients.Count && _clients[id - 1].Open ? _clients[id - 1] : null;

    sealed class End : ISessionTransport
    {
        readonly LoopbackHub _hub;
        readonly int _id;
        readonly Queue<(int Peer, byte[] Data)> _rx = new();
        public bool Open = true;
        public End(LoopbackHub hub, int id) { _hub = hub; _id = id; }

        public string ProviderId => "loopback-hub";
        public IReadOnlyList<int> Peers => _id == 0
            ? _hub._clients.Where(c => c.Open).Select(c => c._id).ToList()
            : _hub._host.Open ? new[] { 0 } : Array.Empty<int>();
        // The hub's ends are connected from the start: nothing ever connects later.
        public event Action<int>? PeerConnected { add { } remove { } }
        public event Action<int>? PeerDisconnected;
        public Task<bool> ListenAsync(int port, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> ConnectAsync(string hostAddress, CancellationToken ct) => Task.FromResult(_hub._host.Open);

        public void Send(int peerId, ReadOnlySpan<byte> payload)
        {
            if (!Open) return;
            // Clients only talk to the host.
            var to = _id == 0 ? _hub.Find(peerId) : peerId == 0 ? _hub.Find(0) : null;
            if (to is null) return;
            to._rx.Enqueue((_id, payload.ToArray()));
        }

        public bool TryReceive(out int peerId, out byte[] payload)
        {
            if (_rx.Count == 0) { peerId = -1; payload = Array.Empty<byte>(); return false; }
            (peerId, payload) = _rx.Dequeue();
            return true;
        }

        public void Dispose()
        {
            if (!Open) return;
            Open = false;
            if (_id == 0) foreach (var c in _hub._clients.Where(c => c.Open)) c.PeerDisconnected?.Invoke(0);
            else if (_hub._host.Open) _hub._host.PeerDisconnected?.Invoke(_id);
        }
    }
}
