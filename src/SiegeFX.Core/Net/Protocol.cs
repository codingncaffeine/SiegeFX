using System.Buffers.Binary;
using System.Text;

namespace SiegeFX.Core.Net;

/// <summary>SC-MP-EOS P3 — the wire protocol. Fixed-layout binary messages
/// over <see cref="ISessionTransport"/>; host-authoritative. Every message
/// is [type u8][body...]; bodies are bounds-checked with NO object
/// deserialization (the parser is fuzz-hardened like tank/gas — P6). The
/// reader refuses truncated/oversized frames rather than throwing on
/// attacker input.</summary>
public enum MpMsg : byte
{
    // client → host
    JoinRequest = 1,   // [nameLen u8][name utf8]
    Input       = 2,   // [tick u32][cmd u8][x f32][z f32][targetScid u32]
    Chat        = 3,   // [textLen u16][text utf8]
    ClientState = 5,   // client→host: this player's authoritative pose (movement is
                       // client-owned in the friend-trust model): [x f32][y f32][z f32][yaw f32][life u16][flags u8]
    // host → client
    JoinAccept  = 10,  // [assignedPlayer u8][worldSnapshotLen u32][snapshot bytes]
    JoinReject  = 11,  // [reasonLen u8][reason utf8]
    StateDelta  = 12,  // [tick u32][actorCount u16][ (scid u32,x f32,y f32,z f32,life u16) * ]  — world/enemy poses
    PlayerJoined= 13,  // [player u8][nameLen u8][name utf8]
    PlayerLeft  = 14,  // [player u8]
    ChatRelay   = 15,  // [player u8][textLen u16][text utf8]
    PlayerDelta = 16,  // [tick u32][count u8][ (player u8,x f32,y f32,z f32,yaw f32,life u16,flags u8) * ]  — all player poses
    GameStart   = 17,  // host→client: leave staging and relaunch into the region: [regionLen u8][region utf8][difficulty u8]
}

/// <summary>Input command verbs a client sends up (host resolves them
/// against its authoritative sim, exactly as the local player path does).</summary>
public enum MpInputCmd : byte { Move = 0, Attack = 1, CastPrimary = 2, CastSecondary = 3, Pickup = 4, Stop = 5 }

/// <summary>Flag bits in a player-state frame (<see cref="MpPlayerState.Flags"/>).</summary>
[Flags] public enum MpPlayerFlags : byte { None = 0, Moving = 1, Dead = 2 }

/// <summary>Bounds-checked writer — grows a pooled buffer; never throws on
/// caller data.</summary>
public sealed class MpWriter
{
    byte[] _buf;
    int _len;
    public MpWriter(int cap = 256) { _buf = new byte[cap]; }
    void Ensure(int extra) { if (_len + extra > _buf.Length) Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _len + extra)); }
    public MpWriter U8(byte v)  { Ensure(1); _buf[_len++] = v; return this; }
    public MpWriter U16(ushort v){ Ensure(2); BinaryPrimitives.WriteUInt16LittleEndian(_buf.AsSpan(_len), v); _len += 2; return this; }
    public MpWriter U32(uint v) { Ensure(4); BinaryPrimitives.WriteUInt32LittleEndian(_buf.AsSpan(_len), v); _len += 4; return this; }
    public MpWriter F32(float v){ Ensure(4); BinaryPrimitives.WriteSingleLittleEndian(_buf.AsSpan(_len), v); _len += 4; return this; }
    public MpWriter Str(string s, bool u16Len = false)
    {
        var bytes = Encoding.UTF8.GetBytes(s ?? "");
        if (u16Len) U16((ushort)Math.Min(bytes.Length, ushort.MaxValue));
        else U8((byte)Math.Min(bytes.Length, byte.MaxValue));
        int n = u16Len ? Math.Min(bytes.Length, ushort.MaxValue) : Math.Min(bytes.Length, byte.MaxValue);
        Ensure(n); Array.Copy(bytes, 0, _buf, _len, n); _len += n; return this;
    }
    public byte[] ToArray() => _buf.AsSpan(0, _len).ToArray();
    public ReadOnlySpan<byte> Span => _buf.AsSpan(0, _len);
}

/// <summary>Bounds-checked reader over a received frame. Every read that
/// would run past the end sets <see cref="Bad"/> and returns a zero/empty
/// value — callers check Bad once at the end and drop the whole frame. No
/// exceptions on malformed input (the RCE surface stays flat).</summary>
public ref struct MpReader
{
    readonly ReadOnlySpan<byte> _s;
    int _pos;
    public bool Bad { get; private set; }
    public MpReader(ReadOnlySpan<byte> s) { _s = s; _pos = 0; Bad = false; }
    bool Need(int n) { if (_pos + n > _s.Length) { Bad = true; return false; } return true; }
    public byte U8()  { if (!Need(1)) return 0; return _s[_pos++]; }
    public ushort U16(){ if (!Need(2)) return 0; var v = BinaryPrimitives.ReadUInt16LittleEndian(_s.Slice(_pos)); _pos += 2; return v; }
    public uint U32() { if (!Need(4)) return 0; var v = BinaryPrimitives.ReadUInt32LittleEndian(_s.Slice(_pos)); _pos += 4; return v; }
    public float F32(){ if (!Need(4)) return 0; var v = BinaryPrimitives.ReadSingleLittleEndian(_s.Slice(_pos)); _pos += 4; return v; }
    public string Str(bool u16Len = false)
    {
        int n = u16Len ? U16() : U8();
        if (Bad || !Need(n)) { Bad = true; return ""; }
        var str = Encoding.UTF8.GetString(_s.Slice(_pos, n));
        _pos += n;
        return str;
    }
    /// <summary>Raw payload tail (JoinAccept snapshot). Copies remaining bytes.</summary>
    public byte[] Rest(int declaredLen)
    {
        if (declaredLen < 0 || !Need(declaredLen)) { Bad = true; return Array.Empty<byte>(); }
        var outp = _s.Slice(_pos, declaredLen).ToArray();
        _pos += declaredLen;
        return outp;
    }
}

/// <summary>One actor's authoritative pose in a StateDelta. Kept a plain
/// struct so the delta loop allocates nothing per actor.</summary>
public readonly record struct MpActorState(uint Scid, float X, float Y, float Z, ushort Life);

/// <summary>One player's pose in a PlayerDelta / ClientState frame. Movement is
/// client-authoritative (retail friend-trust), so each machine reports its own
/// pose and the host fans the whole set back out. Yaw is the heading in radians
/// (atan2(facing.X, facing.Z), matching the engine's player-facing convention).</summary>
public readonly record struct MpPlayerState(byte Player, float X, float Y, float Z, float Yaw, ushort Life, byte Flags);
