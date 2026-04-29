using System.Numerics;

namespace SiegeFX.Core.Assets;

/// <summary>
/// A Dungeon Siege skeletal animation clip parsed from a <c>.prs</c> file. PRS is a
/// flat sequence of FourCC-tagged chunks — <c>ANIM</c> header, then any of
/// <c>NOTE</c>/<c>TRCR</c>/<c>RKEY</c>/<c>KLST</c>, terminated by <c>AEND</c> + <c>INFO</c>.
///
/// The authoritative reference is GPG's <c>PRSImport.ms</c> (siege_max MaxScript). This
/// loader ports its layout faithfully; axis/scale conventions (Max's Z-up vs GL's Y-up)
/// are deferred to the runtime skinning path rather than baked in at load time.
/// </summary>
public sealed class PrsAnimation
{
    public uint AnimVersion { get; }
    public int NumBones { get; }
    /// <summary>Clip length in seconds (the key-time floats are normalized 0..1, so
    /// the real time at a key is <c>key.time * AnimLength</c>).</summary>
    public float AnimLength { get; }
    public Vector3 RootTravel { get; }
    public IReadOnlyList<string> BoneNames { get; }
    public IReadOnlyList<NoteEvent> Notes { get; }
    public KeyList? RootKeys { get; }
    /// <summary>One entry per bone, indexed by the same order as <see cref="BoneNames"/>.
    /// May be null for bones the animator didn't key; empty key lists are also valid.</summary>
    public IReadOnlyList<KeyList?> BoneKeys { get; }
    public int TracerCount { get; }
    public IReadOnlyList<string> InfoStrings { get; }

    private PrsAnimation(
        uint animVersion, int numBones, float animLength, Vector3 rootTravel,
        IReadOnlyList<string> boneNames, IReadOnlyList<NoteEvent> notes,
        KeyList? rootKeys, IReadOnlyList<KeyList?> boneKeys,
        int tracerCount, IReadOnlyList<string> infoStrings)
    {
        AnimVersion = animVersion;
        NumBones = numBones;
        AnimLength = animLength;
        RootTravel = rootTravel;
        BoneNames = boneNames;
        Notes = notes;
        RootKeys = rootKeys;
        BoneKeys = boneKeys;
        TracerCount = tracerCount;
        InfoStrings = infoStrings;
    }

    // FourCC tags on disk: "ANIM" etc. are read as little-endian u32 with the first
    // byte in the low byte — so these constants match the disk order when loaded.
    private const uint TagAnim = 0x4D494E41; // 'ANIM'
    private const uint TagNote = 0x45544F4E; // 'NOTE'
    private const uint TagTrcr = 0x52435254; // 'TRCR'
    private const uint TagRkey = 0x59454B52; // 'RKEY'
    private const uint TagKlst = 0x54534C4B; // 'KLST'
    private const uint TagAend = 0x444E4541; // 'AEND'
    private const uint TagInfo = 0x4F464E49; // 'INFO'

    public static PrsAnimation Load(byte[] data)
    {
        var r = new Reader(data);

        if (r.ReadU32() != TagAnim)
            throw new InvalidDataException("prs: missing ANIM tag");
        var animVersion = r.ReadU32();
        // Three known PRS versions ship in DS1: v3 (1724 / 1791 = 95.5%, the layout
        // PRSImport.ms documents), and the older legacy pair v0x0202 (62) + v0x0302 (45)
        // emitted by an earlier siege_max revision. The ANIM header, NOTE, TRCR, AEND
        // and INFO chunks are identical across all three. The keylist chunks differ:
        //   * v3 RKEY/KLST split keys into a rotation list (time + quat = 20 bytes)
        //     followed by a position list (time + vec3 = 16 bytes), with both counts
        //     in the chunk header (nrk, npk).
        //   * v0x0202 / v0x0302 RKEY/KLST collapse the lists into a single combined
        //     stream of 32-byte (time, quat[xyzw], vec3) bundles, with only nrk in
        //     the header (no npk field).
        // The runtime AnimationRuntime path consumes RotKey/PosKey arrays, so the
        // legacy branch fans the combined stream back into matching parallel lists.
        bool legacyKeys = animVersion is 0x202u or 0x302u;
        if (animVersion != 3 && !legacyKeys)
            throw new NotSupportedException($"prs: anim version 0x{animVersion:X} not supported (only v3, v0x202, v0x302)");
        var sizeTextField = r.ReadU32();
        var numBones = (int)r.ReadU32();
        var animLength = r.ReadF32();
        var rootTravel = r.ReadVec3();
        _ = r.ReadQuat();  // unkrot1 — purpose unclear; PRSImport.ms doesn't consume it either.
        _ = r.ReadQuat();  // unkrot2 — same.
        _ = r.ReadF32();   // unk float — same.

        // Bone-name text blob: null-terminated strings, each padded to 4-byte alignment.
        // sizeTextField is the total bytes including padding; we read exactly that span.
        var boneNames = new List<string>(numBones);
        var textEnd = r.Position + (int)sizeTextField;
        while (r.Position < textEnd)
        {
            var start = r.Position;
            while (r.Position < textEnd && data[r.Position] != 0) r.Position++;
            var name = System.Text.Encoding.ASCII.GetString(data, start, r.Position - start);
            boneNames.Add(name);
            // Consume null + pad to 4-byte alignment (count = name.Length + 1 null).
            var consumed = (r.Position - start) + 1;
            var pad = (4 - (consumed % 4)) % 4;
            r.Position += 1 + pad;
        }
        // Defensive: if the text blob over-ran (rare), snap to the declared end.
        r.Position = textEnd;

        // The text blob must yield exactly numBones names; a mismatch would desync the
        // BoneNames index space from the bone-keyed KLST chunks below. Downstream
        // AnimationRuntime maps ASP bone names to PRS indices via BoneNames, so a short
        // read would silently drop keyed bones; a long read would index past the KLST
        // key array. Either is an asset bug worth failing loud.
        if (boneNames.Count != numBones)
            throw new InvalidDataException(
                $"prs: bone-name blob yielded {boneNames.Count} names but header declared {numBones}");

        var notes = new List<NoteEvent>();
        KeyList? rootKeys = null;
        var boneKeys = new KeyList?[numBones];
        var tracerCount = 0;
        var infoStrings = new List<string>();

        while (true)
        {
            var tag = r.ReadU32();
            if (tag == TagAend)
            {
                // AEND is followed immediately by an INFO sub-section with authoring
                // strings (animator name, tool versions, etc.). Best-effort parse.
                if (r.Remaining >= 4 && r.PeekU32() == TagInfo)
                {
                    r.ReadU32();
                    var infoCount = (int)r.ReadU32();
                    for (var i = 0; i < infoCount; i++) infoStrings.Add(ReadCString(r));
                }
                break;
            }

            switch (tag)
            {
                case TagNote:
                    _ = r.ReadU32(); // chunk version
                    var nnotes = (int)r.ReadU32();
                    for (var i = 0; i < nnotes; i++)
                        notes.Add(new NoteEvent(r.ReadF32(), r.ReadU32()));
                    break;

                case TagTrcr:
                {
                    // Tracer data — weapon-trail / ammo-hook info. The internal layout isn't
                    // public (PRSImport.ms bails with "oh no, tracers!") and the runtime
                    // skinning path doesn't render trails, so we just need to step past it.
                    // Strategy: read the (version, count) header, then resync to the next
                    // valid chunk via 4-byte-aligned tag scan with a post-tag plausibility
                    // check. The payload is float-heavy and the four follow-up tags are
                    // narrow ASCII u32s, so a false-positive match inside the payload is
                    // vanishingly unlikely. The canary case is fb stance-1 attack
                    // (a_c_gah_fb_fs1_at.prs): TRCR ver=3 count=81, 3896-byte payload, then
                    // RKEY/KLST/AEND. Without this resync the loader fell through to stance-0
                    // (unarmed punch) when the player equipped a dagger.
                    _ = r.ReadU32(); // chunk version
                    tracerCount = (int)r.ReadU32();
                    int payloadStart = r.Position;
                    int found = -1;
                    for (int p = payloadStart; p + 8 <= r.Length; p += 4)
                    {
                        uint candidate = BitConverter.ToUInt32(r.Data, p);
                        if (candidate != TagRkey && candidate != TagKlst && candidate != TagAend)
                            continue;
                        if (candidate == TagAend)
                        {
                            // AEND has no chunk-version / count field, so accept directly.
                            found = p;
                            break;
                        }
                        // RKEY / KLST: validate the chunk-version u32 is small (shipped DS1
                        // uses 1 or 3) and, for KLST, that the bone index is in range.
                        uint chunkVer = BitConverter.ToUInt32(r.Data, p + 4);
                        if (chunkVer > 16) continue;
                        if (candidate == TagKlst)
                        {
                            if (p + 12 > r.Length) continue;
                            uint klstBoneIdx = BitConverter.ToUInt32(r.Data, p + 8);
                            if (klstBoneIdx >= (uint)numBones) continue;
                        }
                        found = p;
                        break;
                    }
                    if (found < 0)
                        throw new InvalidDataException(
                            $"prs: TRCR resync failed — no RKEY/KLST/AEND with plausible header found after offset 0x{payloadStart:X}");
                    r.Position = found;
                    break;
                }

                case TagRkey:
                    _ = r.ReadU32(); // chunk version
                    rootKeys = legacyKeys ? ReadCombinedKeyList(r) : ReadKeyList(r);
                    break;

                case TagKlst:
                    _ = r.ReadU32(); // chunk version
                    var boneIdx = (int)r.ReadU32();
                    _ = r.ReadU32(); // text offset into the name blob — we already have names.
                    if ((uint)boneIdx >= (uint)numBones)
                        throw new InvalidDataException($"prs: KLST bone index {boneIdx} out of range ({numBones} bones)");
                    boneKeys[boneIdx] = legacyKeys ? ReadCombinedKeyList(r) : ReadKeyList(r);
                    break;

                default:
                    throw new InvalidDataException($"prs: unknown chunk tag 0x{tag:X8} at offset 0x{r.Position - 4:X}");
            }
        }

        return new PrsAnimation(animVersion, numBones, animLength, rootTravel,
            boneNames, notes, rootKeys, boneKeys, tracerCount, infoStrings);
    }

    private static KeyList ReadKeyList(Reader r)
    {
        var nrk = (int)r.ReadU32();
        var npk = (int)r.ReadU32();
        var rot = new RotKey[nrk];
        for (var i = 0; i < nrk; i++)
            rot[i] = new RotKey(r.ReadF32(), r.ReadQuat());
        var pos = new PosKey[npk];
        for (var i = 0; i < npk; i++)
            pos[i] = new PosKey(r.ReadF32(), r.ReadVec3());
        return new KeyList(rot, pos);
    }

    // v0x0202 / v0x0302 keylist: nrk only (no npk), then nrk * 32-byte combined keys
    // laid out as (time, qx, qy, qz, qw, px, py, pz). Verified against the wraith
    // (v0x202, 45 keys * 32 = 1440 bytes data, 1665 unit quats across RKEY + 36 KLST),
    // the skeleton-guard pose anim (v0x302, 2 keys per chunk, 62 unit quats over 30
    // KLST + 1 RKEY) and a swamp-stinger 2-frame pose (v0x202, 35 KLST + 1 RKEY, 72
    // unit quats). All 1799 quats normalize within 1e-6, times stride 1/12s monotonic.
    private static KeyList ReadCombinedKeyList(Reader r)
    {
        var nrk = (int)r.ReadU32();
        var rot = new RotKey[nrk];
        var pos = new PosKey[nrk];
        for (var i = 0; i < nrk; i++)
        {
            var t = r.ReadF32();
            var q = r.ReadQuat();
            var p = r.ReadVec3();
            rot[i] = new RotKey(t, q);
            pos[i] = new PosKey(t, p);
        }
        return new KeyList(rot, pos);
    }

    private static string ReadCString(Reader r)
    {
        // INFO strings are null-terminated ASCII packed back-to-back (no length prefix,
        // no alignment padding). Matches PRSImport.ms's ReadString, which loops ReadByte
        // until a 0x00 terminator.
        var start = r.Position;
        while (r.Position < r.Length && r.Data[r.Position] != 0) r.Position++;
        if (r.Position >= r.Length)
            throw new InvalidDataException("prs: INFO string ran past EOF without terminator");
        var s = System.Text.Encoding.ASCII.GetString(r.Data, start, r.Position - start);
        r.Position++; // skip the null
        return s;
    }

    public readonly record struct NoteEvent(float Time, uint Token);

    public readonly record struct RotKey(float Time, Quaternion Rotation);
    public readonly record struct PosKey(float Time, Vector3 Position);

    public sealed class KeyList
    {
        public IReadOnlyList<RotKey> RotKeys { get; }
        public IReadOnlyList<PosKey> PosKeys { get; }
        public KeyList(IReadOnlyList<RotKey> rot, IReadOnlyList<PosKey> pos)
        {
            RotKeys = rot;
            PosKeys = pos;
        }
    }

    private sealed class Reader
    {
        public byte[] Data { get; }
        public int Position;
        public int Length => Data.Length;
        public int Remaining => Data.Length - Position;

        public Reader(byte[] data) { Data = data; }

        public uint ReadU32()
        {
            if (Position + 4 > Data.Length) throw new EndOfStreamException("prs: unexpected EOF reading u32");
            var v = BitConverter.ToUInt32(Data, Position);
            Position += 4;
            return v;
        }
        public uint PeekU32()
        {
            if (Position + 4 > Data.Length) throw new EndOfStreamException("prs: unexpected EOF peeking u32");
            return BitConverter.ToUInt32(Data, Position);
        }
        public float ReadF32()
        {
            if (Position + 4 > Data.Length) throw new EndOfStreamException("prs: unexpected EOF reading f32");
            var v = BitConverter.ToSingle(Data, Position);
            Position += 4;
            return v;
        }
        public Vector3 ReadVec3() => new(ReadF32(), ReadF32(), ReadF32());
        public Quaternion ReadQuat() => new(ReadF32(), ReadF32(), ReadF32(), ReadF32());
    }
}
