using System.Diagnostics;
using System.Globalization;

namespace SiegeFX.Core.Net;

/// <summary>A slow link to test against: the uplink's rate, the share of
/// packets lost and the one-way delay. Parsed from
/// <c>SIEGEFX_DEV_NET="kbit=1000,loss=2,delay=100"</c>; optional keys:
/// <c>overhead</c> (bytes each packet costs on the wire beyond its payload,
/// default 48: IP + UDP + the P2P service's own header), <c>piece</c> (the
/// largest payload one packet carries, default 1169: Epic's), <c>queue</c>
/// (how long, in ms, a lossy frame may wait for the link before the router
/// drops it, default 500) and <c>seed</c>.</summary>
public sealed record MpLinkShape(double Kbit, double LossPct, double DelayMs,
                                 int OverheadBytes = 48, int PieceBytes = 1169, double QueueMs = 500, int Seed = 1)
{
    public const string EnvVar = "SIEGEFX_DEV_NET";

    public static MpLinkShape? FromEnvironment() => Parse(Environment.GetEnvironmentVariable(EnvVar));

    public static MpLinkShape? Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        var s = new MpLinkShape(0, 0, 0);
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || !double.TryParse(part[(eq + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw new FormatException($"{EnvVar}: '{part}' is not key=number");
            s = part[..eq].ToLowerInvariant() switch
            {
                "kbit" => s with { Kbit = v },
                "loss" => s with { LossPct = v },
                "delay" => s with { DelayMs = v },
                "overhead" => s with { OverheadBytes = (int)v },
                "piece" => s with { PieceBytes = Math.Max(16, (int)v) },
                "queue" => s with { QueueMs = v },
                "seed" => s with { Seed = (int)v },
                var k => throw new FormatException($"{EnvVar}: unknown key '{k}'"),
            };
        }
        return s;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{(Kbit > 0 ? $"{Kbit:0} kbit/s" : "unlimited")}, {LossPct:0.#} % loss, {DelayMs:0} ms delay, {OverheadBytes} B/packet");
}

/// <summary>Sends through a modeled slow uplink, for testing: every packet
/// from this machine, to any peer, waits its turn on one link of
/// <see cref="MpLinkShape.Kbit"/>, then travels <see cref="MpLinkShape.DelayMs"/>.
/// A lossy frame (<see cref="SendUnreliable"/>) is lost at the loss rate, or
/// dropped when it would wait longer than the router's queue. A reliable
/// frame is never lost: a lost piece is sent again (costing the link twice)
/// and arrives a retransmit later, holding back every reliable frame behind
/// it to the same peer, as an ordered channel does. Frames bigger than one
/// packet cost one packet header per piece. Receiving is not shaped (the
/// other machine shapes its own uplink). Delivery happens on the thread that
/// calls Send, TryReceive or <see cref="Pump"/>.</summary>
public sealed class ShapedTransport : ISessionTransport
{
    readonly ISessionTransport _inner;
    readonly Func<double> _now;
    readonly Random _rng;
    double _linkFree;                                       // when the link finishes what it already holds (s)
    readonly Dictionary<int, double> _lastReliable = new(); // per peer: the last reliable frame's arrival
    readonly PriorityQueue<(int Peer, byte[] Data, bool Lossy), (double At, long Seq)> _inFlight = new();
    long _seq;

    public MpLinkShape Shape { get; }
    public ISessionTransport Inner => _inner;

    // What the link carried (wire bytes include the per-packet overhead).
    public long WireBytes { get; private set; }
    public long Packets { get; private set; }
    public long LossyFrames { get; private set; }
    public long LossyLost { get; private set; }
    public long LossyQueueDrops { get; private set; }
    public long ReliableResends { get; private set; }
    /// <summary>The longest any frame waited for the link (s).</summary>
    public double MaxQueueDelay { get; private set; }
    /// <summary>How long a frame sent now would wait for the link (s).</summary>
    public double QueueDelay => Math.Max(0, _linkFree - _now());

    public ShapedTransport(ISessionTransport inner, MpLinkShape shape, Func<double>? clock = null)
    {
        _inner = inner;
        Shape = shape;
        _now = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        _rng = new Random(shape.Seed);
    }

    public string ProviderId => _inner.ProviderId;
    public IReadOnlyList<int> Peers => _inner.Peers;
    public event Action<int>? PeerConnected { add => _inner.PeerConnected += value; remove => _inner.PeerConnected -= value; }
    public event Action<int>? PeerDisconnected { add => _inner.PeerDisconnected += value; remove => _inner.PeerDisconnected -= value; }
    public int MaxUnreliableBytes => _inner.MaxUnreliableBytes;
    public Task<bool> ListenAsync(int port, CancellationToken ct) => _inner.ListenAsync(port, ct);
    public Task<bool> ConnectAsync(string hostAddress, CancellationToken ct) => _inner.ConnectAsync(hostAddress, ct);

    public void Send(int peerId, ReadOnlySpan<byte> payload) => Enqueue(peerId, payload, lossy: false);
    public void SendUnreliable(int peerId, ReadOnlySpan<byte> payload) => Enqueue(peerId, payload, lossy: true);

    double SecondsFor(int wireBytes) => Shape.Kbit > 0 ? wireBytes * 8.0 / (Shape.Kbit * 1000.0) : 0;
    bool Lost() => Shape.LossPct > 0 && _rng.NextDouble() * 100.0 < Shape.LossPct;

    void Enqueue(int peer, ReadOnlySpan<byte> payload, bool lossy)
    {
        double now = _now();
        Pump(now);
        int pieces = Math.Max(1, (payload.Length + Shape.PieceBytes - 1) / Shape.PieceBytes);
        int wire = payload.Length + pieces * Shape.OverheadBytes;
        double start = Math.Max(now, _linkFree);
        if (lossy)
        {
            LossyFrames++;
            if (start - now > Shape.QueueMs / 1000.0) { LossyQueueDrops++; return; }
        }
        MaxQueueDelay = Math.Max(MaxQueueDelay, start - now);
        _linkFree = start + SecondsFor(wire);
        WireBytes += wire;
        Packets += pieces;
        double arrive = _linkFree + Shape.DelayMs / 1000.0;
        if (lossy)
        {
            if (Lost()) { LossyLost++; return; }
        }
        else
        {
            // Each lost piece is noticed about a round trip later and sent again.
            double rto = Math.Max(0.2, 3 * Shape.DelayMs / 1000.0);
            for (int i = 0; i < pieces; i++)
                if (Lost())
                {
                    ReliableResends++;
                    int pieceWire = Math.Min(Shape.PieceBytes, payload.Length) + Shape.OverheadBytes;
                    _linkFree += SecondsFor(pieceWire);
                    WireBytes += pieceWire;
                    Packets++;
                    arrive += rto;
                }
            if (_lastReliable.TryGetValue(peer, out var prev) && prev > arrive) arrive = prev;
            _lastReliable[peer] = arrive;
        }
        _inFlight.Enqueue((peer, payload.ToArray(), lossy), (arrive, _seq++));
    }

    /// <summary>Hand every frame whose time has come to the real transport.</summary>
    public void Pump() => Pump(_now());

    void Pump(double now)
    {
        while (_inFlight.TryPeek(out var f, out var at) && at.At <= now)
        {
            _inFlight.Dequeue();
            if (f.Lossy) _inner.SendUnreliable(f.Peer, f.Data); else _inner.Send(f.Peer, f.Data);
        }
    }

    public bool TryReceive(out int peerId, out byte[] payload)
    {
        Pump(_now());
        return _inner.TryReceive(out peerId, out payload);
    }

    /// <summary>One line for the session log: what the link carried since the
    /// last call, over <paramref name="seconds"/>.</summary>
    public string Report(double seconds)
    {
        double kbit = (WireBytes - _repBytes) * 8.0 / 1000.0 / Math.Max(1e-6, seconds);
        long lossy = LossyFrames - _repLossy, lost = LossyLost - _repLost, qd = LossyQueueDrops - _repQd;
        var line = string.Create(CultureInfo.InvariantCulture,
            $"link ({Shape}): up {kbit:0.0} kbit/s, queue {QueueDelay * 1000:0} ms (longest {MaxQueueDelay * 1000:0} ms), " +
            $"lossy frames lost {lost}/{lossy}, dropped by the full queue {qd}, reliable pieces re-sent {ReliableResends - _repResends}");
        _repBytes = WireBytes; _repLossy = LossyFrames; _repLost = LossyLost; _repQd = LossyQueueDrops; _repResends = ReliableResends;
        MaxQueueDelay = 0;
        return line;
    }
    long _repBytes, _repLossy, _repLost, _repQd, _repResends;

    public void Dispose() => _inner.Dispose();
}
