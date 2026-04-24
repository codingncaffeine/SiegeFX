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
        // PRSImport.ms was only ever tested against version 3. A minority (~5.5%) of
        // shipped DS1 .prs files are stamped 0x0202 or 0x0302 — older authoring-tool
        // revisions where the chunk headers carry extra u32 fields between version and
        // nrk/npk. They're not documented in any surviving GPG reference, and the
        // shipping game loads them via a different code path. Reject them cleanly so
        // fuzzers don't produce confusing garbage counts.
        if (animVersion != 3)
            throw new NotSupportedException($"prs: anim version 0x{animVersion:X} not supported (only v3)");
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
                    // Tracer data — weapon trail/ammo hook info. Format isn't public;
                    // PRSImport.ms literally bails with "oh no, tracers!". We record the
                    // count and walk past the chunk later (AEND discipline keeps us aligned).
                    _ = r.ReadU32(); // chunk version
                    tracerCount = (int)r.ReadU32();
                    // Without known layout we can't skip precisely. In practice, files
                    // that contain tracers are rare in shipping DS1; bail loudly so we
                    // notice the first one instead of silently mis-parsing.
                    throw new NotSupportedException("prs: TRCR chunk parsing not implemented; report the file so we can figure out its layout");

                case TagRkey:
                    _ = r.ReadU32(); // chunk version
                    rootKeys = ReadKeyList(r);
                    break;

                case TagKlst:
                    _ = r.ReadU32(); // chunk version
                    var boneIdx = (int)r.ReadU32();
                    _ = r.ReadU32(); // text offset into the name blob — we already have names.
                    if ((uint)boneIdx >= (uint)numBones)
                        throw new InvalidDataException($"prs: KLST bone index {boneIdx} out of range ({numBones} bones)");
                    boneKeys[boneIdx] = ReadKeyList(r);
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
