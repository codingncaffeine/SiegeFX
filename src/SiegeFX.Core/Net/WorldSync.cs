using System.Buffers.Binary;

namespace SiegeFX.Core.Net;

/// <summary>One player's or world actor's state as it travels: position on a
/// 2 cm grid (absolute, in grid units), heading in 256 steps, life, flags
/// (<see cref="MpPlayerFlags"/>: Moving, Dead, Ghost) and the last chore.</summary>
public readonly record struct MpQState(int X, int Y, int Z, byte Yaw, ushort Life, byte Flags, MpChore Chore);

/// <summary>An entry of a world update: its key (0..255 = the player with that
/// id, from 16 = a world actor's number in this session), the actor's scid
/// and its state.</summary>
public struct MpEnt
{
    public uint Key;
    public uint Scid;
    public MpQState S;
}

/// <summary>Quantization of what a world update carries. 2 cm and 1/256 of a
/// turn keep every error under the ~5 cm a player could notice: the grid is
/// off by at most 1 cm per axis, and a heading step moves the edge of a
/// character half a meter from its middle by about 1.2 cm.</summary>
public static class MpQuant
{
    public const float Unit = 0.02f;
    /// <summary>Anchors sit on cells of 2^12 grid units (81.92 m).</summary>
    public const int AnchorShift = 12;
    /// <summary>Keys below this are players (by id); actors are numbered from it.</summary>
    public const uint FirstActorKey = 256;

    public static int Q(float meters)
    {
        double q = Math.Round(meters / (double)Unit);
        return q > int.MaxValue / 2 ? int.MaxValue / 2 : q < int.MinValue / 2 ? int.MinValue / 2 : (int)q;
    }
    public static float M(int q) => q * Unit;

    public static byte Yaw(float radians)
    {
        if (!float.IsFinite(radians)) return 0;
        double turns = radians / (2 * Math.PI);
        turns -= Math.Floor(turns);
        return (byte)((int)Math.Round(turns * 256) & 0xFF);
    }
    public static float Yaw(byte step) => step * (MathF.PI * 2f / 256f);

    public static MpQState Of(in MpActorState a) =>
        new(Q(a.X), Q(a.Y), Q(a.Z), Yaw(a.Yaw), a.Life, a.Flags, a.Chore);
    public static MpQState Of(in MpPlayerState p) =>
        new(Q(p.X), Q(p.Y), Q(p.Z), Yaw(p.Yaw), p.Life, (byte)(p.Flags & ~(byte)MpPlayerFlags.OutOfHostView), p.Chore);

    public static MpActorState Actor(uint scid, in MpQState s) =>
        new(scid, M(s.X), M(s.Y), M(s.Z), s.Life, s.Chore, Yaw(s.Yaw), s.Flags);
    public static MpPlayerState Player(byte id, in MpQState s, byte extraFlags = 0) =>
        new(id, M(s.X), M(s.Y), M(s.Z), Yaw(s.Yaw), s.Life, (byte)(s.Flags | extraFlags), s.Chore);
}

/// <summary>The world update (protocol v8, <see cref="MpMsg.Snapshot"/>): the
/// host's view of the players and of the world actors near the receiving
/// player, sent 15 times a second on the lossy lane. Each update is written
/// against the newest one the receiver acknowledged (its "baseline"), naming
/// only what differs from it, so a lost update costs nothing: the next one
/// is written against the same baseline and carries the change again.
/// <code>
/// header: [type u8][tick u32][ticks back to the baseline u8, 0 = none][part u8][parts u8]
///         [flags u8: 1 = the receiver is out of the host's view][anchor x, y, z i16, cells of 81.92 m]
/// entry:  [key gap varint][mask u8][scid u32 if New and an actor][fields the mask names]
///         Pos: x, y, z i16 from the anchor | PosSmall: x, y, z i8 from the baseline's
///         Yaw u8 | Life u16 | Flags u8 | Chore 3 × u8 | New: every field | Gone: no fields
/// </code>
/// An update bigger than one packet is split into parts, each with the full
/// header; the receiver uses it once every part has arrived.</summary>
public static class MpWorldUpdate
{
    public const byte Pos = 1, PosSmall = 2, YawBit = 4, LifeBit = 8, FlagsBit = 16, ChoreBit = 32, New = 64, Gone = 128;
    public const int HeaderBytes = 15;

    public static void WriteVarint(List<byte> b, uint v)
    {
        while (v >= 0x80) { b.Add((byte)(v | 0x80)); v >>= 7; }
        b.Add((byte)v);
    }

    public static bool ReadVarint(ReadOnlySpan<byte> s, ref int pos, out uint v)
    {
        v = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            if (pos >= s.Length) return false;
            byte b = s[pos++];
            v |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
        }
        return false;
    }

    /// <summary>The fields in which <paramref name="cur"/> differs from <paramref name="b"/>.</summary>
    public static byte Diff(in MpQState cur, in MpQState b)
    {
        byte m = 0;
        int dx = cur.X - b.X, dy = cur.Y - b.Y, dz = cur.Z - b.Z;
        if (dx != 0 || dy != 0 || dz != 0)
            m |= (Math.Abs(dx) <= 127 && Math.Abs(dy) <= 127 && Math.Abs(dz) <= 127) ? PosSmall : Pos;
        if (cur.Yaw != b.Yaw) m |= YawBit;
        if (cur.Life != b.Life) m |= LifeBit;
        if (cur.Flags != b.Flags) m |= FlagsBit;
        if (cur.Chore != b.Chore) m |= ChoreBit;
        return m;
    }

    public static bool FitsAnchor(in MpQState s, int ax, int ay, int az) =>
        Fits16(s.X - (ax << MpQuant.AnchorShift)) && Fits16(s.Y - (ay << MpQuant.AnchorShift)) && Fits16(s.Z - (az << MpQuant.AnchorShift));
    static bool Fits16(int v) => v >= short.MinValue && v <= short.MaxValue;

    /// <summary>The anchor cell for a position (grid units).</summary>
    public static short Cell(int q) => (short)Math.Clamp((q + (1 << (MpQuant.AnchorShift - 1))) >> MpQuant.AnchorShift, short.MinValue, short.MaxValue);

    /// <summary>One entry's bytes: <paramref name="mask"/> fields of
    /// <paramref name="cur"/>, PosSmall relative to <paramref name="b"/>.</summary>
    public static void WriteEntry(List<byte> o, uint gap, byte mask, in MpEnt cur, in MpQState b, int ax, int ay, int az)
    {
        WriteVarint(o, gap);
        o.Add(mask);
        if ((mask & Gone) != 0) return;
        Span<byte> t = stackalloc byte[4];
        if ((mask & New) != 0 && cur.Key >= MpQuant.FirstActorKey)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(t, cur.Scid);
            o.Add(t[0]); o.Add(t[1]); o.Add(t[2]); o.Add(t[3]);
        }
        var s = cur.S;
        if ((mask & Pos) != 0)
        {
            I16(o, s.X - (ax << MpQuant.AnchorShift));
            I16(o, s.Y - (ay << MpQuant.AnchorShift));
            I16(o, s.Z - (az << MpQuant.AnchorShift));
        }
        else if ((mask & PosSmall) != 0)
        {
            o.Add((byte)(sbyte)(s.X - b.X)); o.Add((byte)(sbyte)(s.Y - b.Y)); o.Add((byte)(sbyte)(s.Z - b.Z));
        }
        if ((mask & YawBit) != 0) o.Add(s.Yaw);
        if ((mask & LifeBit) != 0) { o.Add((byte)s.Life); o.Add((byte)(s.Life >> 8)); }
        if ((mask & FlagsBit) != 0) o.Add(s.Flags);
        if ((mask & ChoreBit) != 0) { o.Add(s.Chore.Seq); o.Add(s.Chore.Clip); o.Add(s.Chore.Duration); }
    }

    static void I16(List<byte> o, int v) { short s = (short)v; o.Add((byte)s); o.Add((byte)(s >> 8)); }

    public static int EntryBytes(uint gap, byte mask, uint key)
    {
        int n = 1 + (gap < 0x80 ? 1 : gap < 0x4000 ? 2 : gap < 0x200000 ? 3 : gap < 0x10000000 ? 4 : 5);
        if ((mask & Gone) != 0) return n;
        if ((mask & New) != 0 && key >= MpQuant.FirstActorKey) n += 4;
        if ((mask & Pos) != 0) n += 6; else if ((mask & PosSmall) != 0) n += 3;
        if ((mask & YawBit) != 0) n += 1;
        if ((mask & LifeBit) != 0) n += 2;
        if ((mask & FlagsBit) != 0) n += 1;
        if ((mask & ChoreBit) != 0) n += 3;
        return n;
    }

    public const byte AllFields = Pos | YawBit | LifeBit | FlagsBit | ChoreBit;
}

/// <summary>How far from a player the host keeps it up to date on world
/// actors: every update within <see cref="Near"/>, every third update (5 a
/// second) out to <see cref="Far"/>, and an actor already followed stays
/// followed until it is <see cref="Hysteresis"/> beyond that.</summary>
public sealed record MpInterest(float Near = 45f, float Far = 90f, float Hysteresis = 8f)
{
    public static readonly MpInterest Default = new();
}

/// <summary>The host's side of one receiver's world updates: the updates
/// sent and not yet superseded by an acknowledgement, the receiver's newest
/// acknowledgement, and the writer.</summary>
public sealed class MpWorldSender
{
    /// <summary>Updates further back than this are never a baseline.</summary>
    public const int MaxBack = 255;
    /// <summary>With nothing to say, an update still goes out this often (ticks).</summary>
    public const int HeartbeatTicks = 8;

    readonly SortedDictionary<uint, MpEnt[]> _sent = new();
    uint _acked;
    uint _lastSentTick;
    byte _lastFlags;
    uint _flagsChangedTick;
    readonly List<(uint Key, byte Mask, MpEnt Cur, MpQState Base)> _changes = new();
    readonly List<MpEnt> _target = new();
    readonly List<byte> _entry = new();

    // Measured, for the stats line and the tests.
    public long FullUpdates, DeltaUpdates, Parts, Skipped;
    public uint Acked => _acked;

    /// <summary>The receiver says it holds update <paramref name="tick"/> (0 =
    /// it holds none: start again from a full update).</summary>
    public void Ack(uint tick)
    {
        if (tick == 0) { _acked = 0; _sent.Clear(); return; }
        if (tick <= _acked || !_sent.ContainsKey(tick)) return;
        _acked = tick;
        while (_sent.Count > 0)
        {
            uint first = _sent.Keys.First();
            if (first >= tick) break;
            _sent.Remove(first);
        }
    }

    /// <summary>Start again from a full update (a new join on this peer id).</summary>
    public void Reset() { _acked = 0; _sent.Clear(); _lastSentTick = 0; }

    /// <summary>Write update <paramref name="tick"/> for one receiver from the
    /// host's full, key-sorted view <paramref name="all"/>. The receiver's own
    /// player (<paramref name="recipient"/>) is left out; world actors follow
    /// <paramref name="interest"/> around <paramref name="center"/> (grid
    /// units). Returns the frames to send, none when there is nothing to say
    /// and the heartbeat is not due.</summary>
    public List<byte[]> Write(uint tick, IReadOnlyList<MpEnt> all, int recipient, (int X, int Y, int Z) center,
                              bool outOfView, MpInterest interest, int maxFrame)
    {
        while (_sent.Count > 0 && _sent.Keys.First() + MaxBack < tick) _sent.Remove(_sent.Keys.First());
        MpEnt[] baseline = Array.Empty<MpEnt>();
        uint back = 0;
        if (_acked != 0 && tick - _acked <= MaxBack && _sent.TryGetValue(_acked, out var b)) { baseline = b; back = tick - _acked; }

        short ax = MpWorldUpdate.Cell(center.X), ay = MpWorldUpdate.Cell(center.Y), az = MpWorldUpdate.Cell(center.Z);
        float nearQ = interest.Near / MpQuant.Unit, farQ = interest.Far / MpQuant.Unit, keepQ = (interest.Far + interest.Hysteresis) / MpQuant.Unit;
        double near2 = (double)nearQ * nearQ, far2 = (double)farQ * farQ, keep2 = (double)keepQ * keepQ;

        _changes.Clear();
        _target.Clear();
        int i = 0, j = 0;
        while (i < all.Count || j < baseline.Length)
        {
            uint ka = i < all.Count ? all[i].Key : uint.MaxValue;
            uint kb = j < baseline.Length ? baseline[j].Key : uint.MaxValue;
            if (ka < kb)
            {
                var cur = all[i++];
                if (!Wanted(cur, inBase: false, out _)) continue;
                _target.Add(cur);
                _changes.Add((cur.Key, (byte)(MpWorldUpdate.New | MpWorldUpdate.AllFields), cur, default));
            }
            else if (ka == kb)
            {
                var cur = all[i++];
                var old = baseline[j++];
                if (!Wanted(cur, inBase: true, out bool far)) { _changes.Add((old.Key, MpWorldUpdate.Gone, old, old.S)); continue; }
                // Beyond Near an actor is brought up to date on every third update only.
                if (far && (cur.Key + tick) % 3 != 0) { _target.Add(old); continue; }
                _target.Add(cur);
                byte m = MpWorldUpdate.Diff(cur.S, old.S);
                if (m != 0) _changes.Add((cur.Key, m, cur, old.S));
            }
            else
            {
                var old = baseline[j++];
                _changes.Add((old.Key, MpWorldUpdate.Gone, old, old.S));
            }
        }

        byte flags = outOfView ? (byte)1 : (byte)0;
        if (flags != _lastFlags) { _lastFlags = flags; _flagsChangedTick = tick; }
        bool flagsFresh = tick - _flagsChangedTick < HeartbeatTicks;
        var frames = new List<byte[]>();
        if (_changes.Count == 0 && !flagsFresh && _lastSentTick != 0 && tick - _lastSentTick < HeartbeatTicks && back != 0)
        {
            Skipped++;
            return frames;
        }

        // Split the entries into frames of at most maxFrame bytes.
        var part = new List<byte>(Math.Min(maxFrame, 1200));
        uint prevKey = 0;
        void Begin() { part.Clear(); part.AddRange(new byte[MpWorldUpdate.HeaderBytes]); prevKey = 0; }
        void Finish()
        {
            var f = part.ToArray();
            f[0] = (byte)MpMsg.Snapshot;
            BinaryPrimitives.WriteUInt32LittleEndian(f.AsSpan(1), tick);
            f[5] = (byte)back;
            f[6] = (byte)frames.Count;
            f[8] = flags;
            BinaryPrimitives.WriteInt16LittleEndian(f.AsSpan(9), ax);
            BinaryPrimitives.WriteInt16LittleEndian(f.AsSpan(11), ay);
            BinaryPrimitives.WriteInt16LittleEndian(f.AsSpan(13), az);
            frames.Add(f);
        }
        Begin();
        foreach (var c in _changes)
        {
            _entry.Clear();
            MpWorldUpdate.WriteEntry(_entry, c.Key - prevKey, c.Mask, c.Cur, c.Base, ax, ay, az);
            if (part.Count > MpWorldUpdate.HeaderBytes && part.Count + _entry.Count > maxFrame)
            {
                Finish();
                Begin();
                _entry.Clear();
                MpWorldUpdate.WriteEntry(_entry, c.Key, c.Mask, c.Cur, c.Base, ax, ay, az);
            }
            part.AddRange(_entry);
            prevKey = c.Key;
        }
        Finish();
        if (frames.Count > 255) { NetLog.Warn($"world update {tick}: {frames.Count} parts — not sent"); return new List<byte[]>(); }
        foreach (var f in frames) f[7] = (byte)frames.Count;

        _sent[tick] = _target.ToArray();
        _lastSentTick = tick;
        if (back == 0) FullUpdates++; else DeltaUpdates++;
        Parts += frames.Count;
        return frames;

        bool Wanted(in MpEnt e, bool inBase, out bool far)
        {
            far = false;
            if (!MpWorldUpdate.FitsAnchor(e.S, ax, ay, az)) return false;
            if (e.Key < MpQuant.FirstActorKey) return e.Key != recipient;
            double dx = e.S.X - center.X, dz = e.S.Z - center.Z;
            double d2 = dx * dx + dz * dz;
            if (d2 > (inBase ? keep2 : far2)) return false;
            far = d2 > near2;
            return true;
        }
    }
}

/// <summary>The receiver's side: joins the parts of an update, rebuilds the
/// whole update from its baseline, keeps the recent ones (any may be the
/// next baseline) and reports what changed since the update it used last.</summary>
public sealed class MpWorldReceiver
{
    const int Keep = MpWorldSender.MaxBack + 1;
    readonly SortedDictionary<uint, MpEnt[]> _have = new();
    readonly Dictionary<uint, byte[]?[]> _partial = new();
    MpEnt[] _applied = Array.Empty<MpEnt>();

    /// <summary>The newest update used (0 = none yet): what the next ClientState acknowledges.</summary>
    public uint Newest { get; private set; }
    /// <summary>The flags byte of the newest update used.</summary>
    public byte Flags { get; private set; }
    public long Used, Late, NoBaseline, Malformed;

    public void Reset() { _have.Clear(); _partial.Clear(); _applied = Array.Empty<MpEnt>(); Newest = 0; Flags = 0; }

    /// <summary>The newest update's entries, key-sorted.</summary>
    public IReadOnlyList<MpEnt> Current => _applied;

    /// <summary>Take one frame. When it completes an update newer than the
    /// last one used, returns true with that update (key-sorted) and the
    /// previous one, for the caller to compare.</summary>
    public bool Take(ReadOnlySpan<byte> frame, out MpEnt[] now, out MpEnt[] before)
    {
        now = before = Array.Empty<MpEnt>();
        if (frame.Length < MpWorldUpdate.HeaderBytes) { Malformed++; return false; }
        uint tick = BinaryPrimitives.ReadUInt32LittleEndian(frame[1..]);
        int back = frame[5], part = frame[6], parts = frame[7];
        if (parts == 0 || part >= parts || tick == 0) { Malformed++; return false; }
        if (tick <= Newest && (Newest - tick > MpWorldSender.MaxBack || _have.ContainsKey(tick))) { Late++; return false; }

        byte[][] pieces;
        if (parts == 1) pieces = new[] { frame.ToArray() };
        else
        {
            if (!_partial.TryGetValue(tick, out var got) || got.Length != parts) _partial[tick] = got = new byte[]?[parts];
            got[part] = frame.ToArray();
            if (Array.Exists(got, p => p is null)) { DropOldPartials(); return false; }
            _partial.Remove(tick);
            pieces = got!;
        }

        MpEnt[] baseline = Array.Empty<MpEnt>();
        if (back != 0 && !_have.TryGetValue(tick - (uint)back, out baseline!)) { NoBaseline++; return false; }
        var built = Rebuild(pieces, baseline);
        if (built is null) { Malformed++; return false; }

        _have[tick] = built;
        while (_have.Count > 0 && _have.Keys.First() + Keep < Math.Max(tick, Newest)) _have.Remove(_have.Keys.First());
        if (tick <= Newest) { Late++; return false; }   // rebuilt, kept as a baseline, but older than what is shown
        before = _applied;
        _applied = built;
        now = built;
        Newest = tick;
        Flags = pieces[0][8];
        Used++;
        DropOldPartials();
        return true;
    }

    void DropOldPartials()
    {
        if (_partial.Count == 0) return;
        foreach (var t in _partial.Keys.ToList())
            if (t + 32 < Newest || _partial.Count > 64) _partial.Remove(t);
    }

    static MpEnt[]? Rebuild(byte[][] pieces, MpEnt[] baseline)
    {
        var changes = new List<(uint Key, byte Mask, uint Scid, int X, int Y, int Z, byte Yaw, ushort Life, byte Flags, MpChore Chore)>();
        uint lastKey = 0;
        bool any = false;
        foreach (var f in pieces)
        {
            int ax = BinaryPrimitives.ReadInt16LittleEndian(f.AsSpan(9)) << MpQuant.AnchorShift;
            int ay = BinaryPrimitives.ReadInt16LittleEndian(f.AsSpan(11)) << MpQuant.AnchorShift;
            int az = BinaryPrimitives.ReadInt16LittleEndian(f.AsSpan(13)) << MpQuant.AnchorShift;
            int pos = MpWorldUpdate.HeaderBytes;
            uint key = 0;
            var s = f.AsSpan();
            int len = f.Length;
            while (pos < len)
            {
                if (!MpWorldUpdate.ReadVarint(s, ref pos, out uint gap)) return null;
                key += gap;
                if (any && key <= lastKey) return null;     // keys only rise
                if (pos >= s.Length) return null;
                byte mask = s[pos++];
                uint scid = 0; int x = 0, y = 0, z = 0; byte yaw = 0, fl = 0; ushort life = 0; MpChore chore = default;
                bool Need(int n) => pos + n <= len;
                if ((mask & MpWorldUpdate.Gone) == 0)
                {
                    if ((mask & MpWorldUpdate.New) != 0)
                    {
                        if ((mask & MpWorldUpdate.AllFields) != MpWorldUpdate.AllFields) return null;
                        if (key >= MpQuant.FirstActorKey) { if (!Need(4)) return null; scid = BinaryPrimitives.ReadUInt32LittleEndian(s[pos..]); pos += 4; }
                    }
                    if ((mask & MpWorldUpdate.Pos) != 0)
                    {
                        if (!Need(6)) return null;
                        x = ax + BinaryPrimitives.ReadInt16LittleEndian(s[pos..]);
                        y = ay + BinaryPrimitives.ReadInt16LittleEndian(s[(pos + 2)..]);
                        z = az + BinaryPrimitives.ReadInt16LittleEndian(s[(pos + 4)..]);
                        pos += 6;
                    }
                    else if ((mask & MpWorldUpdate.PosSmall) != 0)
                    {
                        if (!Need(3)) return null;
                        x = (sbyte)s[pos]; y = (sbyte)s[pos + 1]; z = (sbyte)s[pos + 2];
                        pos += 3;
                    }
                    if ((mask & MpWorldUpdate.YawBit) != 0) { if (!Need(1)) return null; yaw = s[pos++]; }
                    if ((mask & MpWorldUpdate.LifeBit) != 0) { if (!Need(2)) return null; life = BinaryPrimitives.ReadUInt16LittleEndian(s[pos..]); pos += 2; }
                    if ((mask & MpWorldUpdate.FlagsBit) != 0) { if (!Need(1)) return null; fl = s[pos++]; }
                    if ((mask & MpWorldUpdate.ChoreBit) != 0) { if (!Need(3)) return null; chore = new MpChore(s[pos], s[pos + 1], s[pos + 2]); pos += 3; }
                }
                changes.Add((key, mask, scid, x, y, z, yaw, life, fl, chore));
                lastKey = key;
                any = true;
            }
        }

        var outp = new List<MpEnt>(baseline.Length + changes.Count);
        int i = 0, j = 0;
        while (i < baseline.Length || j < changes.Count)
        {
            uint kb = i < baseline.Length ? baseline[i].Key : uint.MaxValue;
            uint kc = j < changes.Count ? changes[j].Key : uint.MaxValue;
            if (kb < kc) { outp.Add(baseline[i++]); continue; }
            var c = changes[j++];
            if (kb == kc)
            {
                var old = baseline[i++];
                if ((c.Mask & MpWorldUpdate.Gone) != 0) continue;
                if ((c.Mask & MpWorldUpdate.New) != 0) { outp.Add(NewEnt(c)); continue; }
                var s = old.S;
                int x = s.X, y = s.Y, z = s.Z;
                if ((c.Mask & MpWorldUpdate.Pos) != 0) { x = c.X; y = c.Y; z = c.Z; }
                else if ((c.Mask & MpWorldUpdate.PosSmall) != 0) { x += c.X; y += c.Y; z += c.Z; }
                outp.Add(new MpEnt
                {
                    Key = old.Key,
                    Scid = old.Scid,
                    S = new MpQState(x, y, z,
                        (c.Mask & MpWorldUpdate.YawBit) != 0 ? c.Yaw : s.Yaw,
                        (c.Mask & MpWorldUpdate.LifeBit) != 0 ? c.Life : s.Life,
                        (c.Mask & MpWorldUpdate.FlagsBit) != 0 ? c.Flags : s.Flags,
                        (c.Mask & MpWorldUpdate.ChoreBit) != 0 ? c.Chore : s.Chore),
                });
            }
            else
            {
                // Not in the baseline: only a new entry makes sense.
                if ((c.Mask & MpWorldUpdate.New) == 0) return null;
                outp.Add(NewEnt(c));
            }
        }
        return outp.ToArray();

        static MpEnt NewEnt(in (uint Key, byte Mask, uint Scid, int X, int Y, int Z, byte Yaw, ushort Life, byte Flags, MpChore Chore) c) =>
            new() { Key = c.Key, Scid = c.Scid, S = new MpQState(c.X, c.Y, c.Z, c.Yaw, c.Life, c.Flags, c.Chore) };
    }
}
