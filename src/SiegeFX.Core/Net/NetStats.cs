using System.Globalization;
using System.Text;

namespace SiegeFX.Core.Net;

/// <summary>What a session sent and received, per peer and per message type
/// (a frame's first byte), counted at the session: payload bytes as handed to
/// the transport, before any transport framing, sealing or packet headers.
/// <see cref="Report"/> gives one log line for the time since its last call.</summary>
public sealed class MpNetStats
{
    sealed class Tally
    {
        public readonly long[] Bytes = new long[256];
        public readonly long[] Frames = new long[256];
        public long Total, Count;
        public void Add(byte type, int bytes) { Bytes[type] += bytes; Frames[type]++; Total += bytes; Count++; }
        public void Clear() { Array.Clear(Bytes); Array.Clear(Frames); Total = 0; Count = 0; }
    }

    readonly SortedDictionary<int, Tally> _up = new(), _down = new();
    readonly Tally _allUp = new(), _allDown = new();
    // Since the session began (not cleared by Report).
    public long TotalUpBytes { get; private set; }
    public long TotalDownBytes { get; private set; }

    static Tally For(SortedDictionary<int, Tally> d, int peer)
    {
        if (!d.TryGetValue(peer, out var t)) d[peer] = t = new Tally();
        return t;
    }

    public void Sent(int peer, ReadOnlySpan<byte> frame)
    {
        if (frame.Length == 0) return;
        For(_up, peer).Add(frame[0], frame.Length);
        _allUp.Add(frame[0], frame.Length);
        TotalUpBytes += frame.Length;
    }

    public void Received(int peer, ReadOnlySpan<byte> frame)
    {
        if (frame.Length == 0) return;
        For(_down, peer).Add(frame[0], frame.Length);
        _allDown.Add(frame[0], frame.Length);
        TotalDownBytes += frame.Length;
    }

    /// <summary>Payload sent to <paramref name="peer"/> since the last report (bytes).</summary>
    public long UpSinceReport(int peer) => _up.TryGetValue(peer, out var t) ? t.Total : 0;
    public long AllUpSinceReport => _allUp.Total;

    static string Kbit(long bytes, double seconds) =>
        (bytes * 8.0 / 1000.0 / Math.Max(1e-6, seconds)).ToString("0.0", CultureInfo.InvariantCulture);

    static string Name(int type) => Enum.IsDefined(typeof(MpMsg), (byte)type) ? ((MpMsg)type).ToString() : $"0x{type:x2}";

    static void Types(StringBuilder sb, Tally t, double seconds)
    {
        var top = Enumerable.Range(0, 256).Where(i => t.Bytes[i] > 0).OrderByDescending(i => t.Bytes[i]).Take(4);
        sb.Append(" [");
        bool first = true;
        foreach (var i in top)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(Name(i)).Append(' ').Append(Kbit(t.Bytes[i], seconds))
              .Append(" (").Append((t.Bytes[i] / (double)Math.Max(1, t.Frames[i])).ToString("0", CultureInfo.InvariantCulture)).Append(" B)");
        }
        sb.Append(']');
    }

    /// <summary>"up X kbit/s, down Y kbit/s | to peer 1: … [type kbit/s (B/frame) …]"
    /// for the last <paramref name="seconds"/>, then the counts start again.</summary>
    public string Report(double seconds)
    {
        var sb = new StringBuilder();
        sb.Append("up ").Append(Kbit(_allUp.Total, seconds)).Append(" kbit/s (")
          .Append((_allUp.Count / Math.Max(1e-6, seconds)).ToString("0", CultureInfo.InvariantCulture)).Append(" frames/s), down ")
          .Append(Kbit(_allDown.Total, seconds)).Append(" kbit/s");
        foreach (var (peer, t) in _up)
        {
            if (t.Count == 0) continue;
            sb.Append(" | to ").Append(peer).Append(": ").Append(Kbit(t.Total, seconds)).Append(" kbit/s");
            Types(sb, t, seconds);
        }
        foreach (var (peer, t) in _down)
        {
            if (t.Count == 0) continue;
            sb.Append(" | from ").Append(peer).Append(": ").Append(Kbit(t.Total, seconds)).Append(" kbit/s");
            Types(sb, t, seconds);
        }
        foreach (var t in _up.Values) t.Clear();
        foreach (var t in _down.Values) t.Clear();
        _allUp.Clear(); _allDown.Clear();
        return sb.ToString();
    }
}
