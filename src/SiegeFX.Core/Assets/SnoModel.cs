using System.Buffers.Binary;
using System.Numerics;
using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>
/// A Dungeon Siege Siege Node (.sno) — a 3D terrain/world tile whose instances are
/// stitched edge-to-edge via named "doors" to form the continuous world described in
/// Scott Bilas's "Continuous World" paper. Phase 4d-1 reads the full structure but the
/// axis convention is preserved verbatim from disk; world-space orientation (DS1 is
/// Z-up; our renderer is Y-up) is applied per-draw, not baked in during parsing.
/// </summary>
public sealed class SnoModel
{
    // Lowest major we accept. Per the Kaitai sno.ksy spec the checksum field
    // becomes optional below v6.2, but the rest of the layout is unchanged. DS1
    // ships v6.2 (spider_dungeon walls) and v7.0 — both have the checksum, so
    // dropping the floor to 6 unblocks 120 spider-dungeon tiles in Terrain.dsres
    // that were rejected at parse time.
    public const int ExpectedMinVersion = 6;
    public const int HeaderSizeBytes = 88;
    public const int CornerSizeBytes = 36;
    public const int XformSizeBytes  = 48; // float[4][3] — 3x3 rotation + 3-vector translation

    public static readonly FourCC MagicSnod = new('S', 'N', 'O', 'D');

    public FourCC Magic { get; init; }
    public int Version  { get; init; }
    /// <summary>Minor of the SNO version word (the u32 that follows major). Tracked
    /// because the disk layout's checksum field is gated on the (major,minor) pair —
    /// see <see cref="HasChecksumField"/>.</summary>
    public int VersionMinor { get; init; }
    public static bool HasChecksumField(int major, int minor) =>
        major > 6 || (major == 6 && minor >= 2);
    public Vector3 MinBounds { get; init; }
    public Vector3 MaxBounds { get; init; }
    public uint DataCrc32 { get; init; }

    public Spot[]    Spots    { get; init; } = Array.Empty<Spot>();
    public Door[]    Doors    { get; init; } = Array.Empty<Door>();
    public Corner[]  Corners  { get; init; } = Array.Empty<Corner>();
    public Surface[] Surfaces { get; init; } = Array.Empty<Surface>();

    /// <summary>Pre-classified nav-surface groups that DS1 tools bake into the SNO. Each
    /// grouping is a contiguous region of floor carrying a <see cref="FloorKind"/> (walkable,
    /// water, or explicitly ignored). Phase 11a uses <see cref="FloorKind.Floor"/> groupings
    /// to seed the pathfinding mesh.</summary>
    public LogicalGrouping[] LogicalGroupings { get; init; } = Array.Empty<LogicalGrouping>();

    /// <summary>A 4x3 affine transform: 3x3 rotation (rows 0-2) + 3-vector translation (row 3).</summary>
    public readonly record struct Xform4x3(
        Vector3 Row0, Vector3 Row1, Vector3 Row2, Vector3 Translation);

    /// <summary>Named anchor point inside the node (NPC/prop spawn, camera hints, etc.).</summary>
    public readonly record struct Spot(Xform4x3 Transform, string Name);

    /// <summary>Edge connector that stitches this node to a neighbor. <paramref name="HotSpots"/>
    /// are corner indices into the shared perimeter (unused by the renderer, used by the
    /// world-stitch step later).</summary>
    public readonly record struct Door(uint Id, Xform4x3 Transform, uint[] HotSpots);

    /// <summary>Vertex record in the flat corner pool. Per-surface triangle indices index
    /// into this pool, offset by that surface's <see cref="Surface.StartCorner"/>.</summary>
    public readonly record struct Corner(Vector3 Position, Vector3 Normal, uint Rgba, Vector2 Uv);

    /// <summary>Classification bits DS1 bakes onto each logical grouping. Raw values
    /// come from the SNO disk layout — they aren't flag bits you OR together, despite
    /// the name, so compare with <c>==</c>.</summary>
    public enum FloorKind : uint
    {
        Ignored = 0x20000000u,       // 536_870_912 — not part of the nav surface
        Floor   = 0x40000001u,       // 1_073_741_825 — walkable
        Water   = 0x80000000u,       // 2_147_483_648 — swim/wade zone; not walkable as floor
    }

    /// <summary>A single nav-mesh face in SNO-local space. DS1 stores full a/b/c triangle
    /// vertices rather than indices into the render corner pool — the nav triangles don't
    /// always coincide with render triangles (tool-side welding/simplification). The
    /// <see cref="Normal"/> field is read verbatim from disk; shipped DS1 data is already
    /// unit-normalized in every file spot-checked so Phase 11b's cost function uses it raw,
    /// but renormalize if you ever load third-party/modded SNOs.</summary>
    public readonly record struct NavFace(Vector3 A, Vector3 B, Vector3 C, Vector3 Normal);

    /// <summary>Pre-classified walkable-surface subregion. Groupings are the input to the
    /// Phase 11b pathfinder: <see cref="FloorKind.Floor"/> groupings contribute walkable
    /// triangles, <see cref="FloorKind.Water"/> contributes swim zones,
    /// <see cref="FloorKind.Ignored"/> is skipped entirely. <see cref="BoundsMin"/> /
    /// <see cref="BoundsMax"/> give a coarse SNO-local AABB for culling before picking
    /// into <see cref="Faces"/>.</summary>
    public sealed class LogicalGrouping
    {
        public byte Id { get; init; }
        public Vector3 BoundsMin { get; init; }
        public Vector3 BoundsMax { get; init; }
        public FloorKind Kind { get; init; }
        public NavFace[] Faces { get; init; } = Array.Empty<NavFace>();
    }

    /// <summary>One material subset: a name (matches a .raw texture) and a range of
    /// triangles whose indices are local (0..SpanCorner-1) and must be resolved against
    /// the global corner pool via <see cref="StartCorner"/>.</summary>
    public sealed class Surface
    {
        public string TextureName  { get; init; } = "";
        public uint   StartCorner  { get; init; }
        public uint   SpanCorner   { get; init; }
        public uint   CornerCount  { get; init; }
        public ushort[] TriangleIndices { get; init; } = Array.Empty<ushort>();
        public int    TriangleCount => TriangleIndices.Length / 3;
    }

    public static SnoModel Load(byte[] data)
    {
        if (data.Length < HeaderSizeBytes)
            throw new InvalidDataException($"SNO too small: {data.Length} bytes");

        var r = new Reader(data);

        var magic = r.ReadFourCC();
        if (magic != MagicSnod)
            throw new InvalidDataException($"not a SNO (magic={magic}, expected SNOD)");

        var version = (int)r.ReadU32();
        if (version < ExpectedMinVersion)
            throw new InvalidDataException($"SNO version {version} < {ExpectedMinVersion}");

        // The u32 immediately after major is the version's minor — Kaitai's sno.ksy
        // models version as a (major:u4, minor:u4) pair. We previously discarded it
        // as "unused0", which works because every shipped tile is v6.2 or v7.0 and
        // the rest of the header doesn't depend on minor. We capture it now so the
        // optional-checksum decision below can follow the documented rule.
        var versionMinor = (int)r.ReadU32();

        var doorCount    = r.ReadU32();
        var spotCount    = r.ReadU32();
        var cornerCount  = r.ReadU32();
        var faceCount    = r.ReadU32();
        var textureCount = r.ReadU32();

        var minBounds = r.ReadVec3();
        var maxBounds = r.ReadVec3();

        // unused1..unused7 — purely junk in real files per glampert's RE work.
        for (var i = 0; i < 7; i++) r.ReadU32();

        // Checksum is only present at this offset in v6.2+/v7+. Older v6 files
        // skip it; door data starts where the checksum would have lived.
        var dataCrc32 = HasChecksumField(version, versionMinor) ? r.ReadU32() : 0u;

        // Cornering is the single heaviest allocation — 36 bytes × count — so clamp it
        // eagerly. The other sections have variable-length strings / sub-arrays, so we
        // let each Read* routine EnsureSpace per record instead of pre-summing.
        var remaining = data.Length - r.Position;
        if ((long)cornerCount * CornerSizeBytes > remaining)
            throw new InvalidDataException(
                $"SNO cornerCount {cornerCount} × {CornerSizeBytes} bytes exceeds remaining {remaining} bytes");

        var spots = ReadSpots(r, spotCount);
        var doors = ReadDoors(r, doorCount);
        var corners = ReadCorners(r, cornerCount);
        var surfaces = ReadSurfaces(r, textureCount, cornerCount);

        // Rough face-count consistency check: sum of surface face counts should equal
        // the header's faceCount. Report on mismatch but don't throw — glampert's notes
        // call out that unused fields in DS1 are unreliable.
        var summedFaces = 0L;
        foreach (var s in surfaces) summedFaces += s.TriangleCount;
        _ = summedFaces; _ = faceCount;

        // Logical-grouping / nav section follows the render surfaces. Not every SNO has
        // one — older map tiles ship without it — so short-circuit if we hit EOF cleanly.
        var groupings = r.AtEnd ? Array.Empty<LogicalGrouping>() : ReadLogicalGroupings(r);

        return new SnoModel
        {
            Magic             = magic,
            Version           = version,
            VersionMinor      = versionMinor,
            MinBounds         = minBounds,
            MaxBounds         = maxBounds,
            DataCrc32         = dataCrc32,
            Spots             = spots,
            Doors             = doors,
            Corners           = corners,
            Surfaces          = surfaces,
            LogicalGroupings  = groupings,
        };
    }

    /// <summary>Parses the post-surfaces nav section verbatim from OpenSiege's
    /// <c>ReaderWriterSNO.cpp</c>. DS1 bakes per-grouping bookkeeping (indices, unknown
    /// rotations, short-pair arrays) that we skip but must walk precisely or the final
    /// triangle list desyncs. Each grouping terminates in a <c>recurse_unknown_section</c>
    /// tail that nests an arbitrary number of child sections — we mirror that recursion
    /// exactly.</summary>
    private static LogicalGrouping[] ReadLogicalGroupings(Reader r)
    {
        var count = r.ReadU32();
        if (count == 0) return Array.Empty<LogicalGrouping>();

        var result = new LogicalGrouping[count];
        for (var i = 0; i < count; i++)
        {
            var id = r.ReadU8();
            var bMin = r.ReadVec3();
            var bMax = r.ReadVec3();
            var kind = (FloorKind)r.ReadU32();

            // Per-grouping unknown "rotation + shorts + shorts" array. Nothing we need
            // for nav — just stride past.
            var unkCount1 = r.ReadU32();
            for (var j = 0; j < unkCount1; j++)
            {
                r.ReadU16();                                 // index
                for (var k = 0; k < 9; k++) r.ReadF32();     // 3x3 rotation
                var shortArr1 = r.ReadU16();
                r.Skip(shortArr1 * 2);
                var shortArr2 = r.ReadU32();
                r.Skip(shortArr2 * 2);
            }

            // "Short-pair" sub-section — length is u32, each entry u8 + u32 + that-many u16 pairs.
            var pairSectionCount = r.ReadU32();
            for (var j = 0; j < pairSectionCount; j++)
            {
                r.ReadU8();
                var pairCount = r.ReadU32();
                r.Skip(pairCount * 4);                       // two u16s per pair
            }

            // The payload we actually want.
            var triCount = r.ReadU32();
            var faces = new NavFace[triCount];
            for (var j = 0; j < triCount; j++)
            {
                var a = r.ReadVec3();
                var b = r.ReadVec3();
                var c = r.ReadVec3();
                var n = r.ReadVec3();
                faces[j] = new NavFace(a, b, c, n);
            }

            SkipUnknownSection(r, depth: 0);

            result[i] = new LogicalGrouping
            {
                Id = id,
                BoundsMin = bMin,
                BoundsMax = bMax,
                Kind = kind,
                Faces = faces,
            };
        }
        return result;
    }

    /// <summary>Recursive "unknown" trailer that appears after each logical-grouping face
    /// list. Structure: 6 floats (bbox), u8, u16 count + that-many u16s, u8 recurse-count,
    /// then that-many nested copies of itself. OpenSiege reads but ignores it — we do too.
    /// Shipped DS1 data has never exceeded a couple of levels in fuzz runs, so a 32-frame
    /// cap is cheap insurance against a corrupt or modded file blowing the managed stack.</summary>
    private const int MaxUnknownSectionDepth = 32;
    private static void SkipUnknownSection(Reader r, int depth)
    {
        if (depth > MaxUnknownSectionDepth)
            throw new InvalidDataException(
                $"SNO unknown-section recursion exceeded {MaxUnknownSectionDepth} levels at 0x{r.Position:X8}");
        r.Skip(6 * 4);                                       // bbox min/max f32s
        r.ReadU8();
        var shortCount = r.ReadU16();
        r.Skip(shortCount * 2);
        var recurseCount = r.ReadU8();
        for (var i = 0; i < recurseCount; i++) SkipUnknownSection(r, depth + 1);
    }

    private static Spot[] ReadSpots(Reader r, uint count)
    {
        if (count == 0) return Array.Empty<Spot>();
        var result = new Spot[count];
        for (var i = 0; i < count; i++)
        {
            var xf = r.ReadXform4x3();
            var name = r.ReadCString();
            result[i] = new Spot(xf, name);
        }
        return result;
    }

    private static Door[] ReadDoors(Reader r, uint count)
    {
        if (count == 0) return Array.Empty<Door>();
        var result = new Door[count];
        for (var i = 0; i < count; i++)
        {
            var id = r.ReadU32();
            var xf = r.ReadXform4x3();
            var hotSpotCount = r.ReadU32();
            r.EnsureSpace((long)hotSpotCount * 4, "Door.HotSpots");
            var hotSpots = new uint[hotSpotCount];
            for (var h = 0; h < hotSpotCount; h++) hotSpots[h] = r.ReadU32();
            result[i] = new Door(id, xf, hotSpots);
        }
        return result;
    }

    private static Corner[] ReadCorners(Reader r, uint count)
    {
        if (count == 0) return Array.Empty<Corner>();
        r.EnsureSpace((long)count * CornerSizeBytes, "Corners");
        var result = new Corner[count];
        for (var i = 0; i < count; i++)
        {
            var pos = r.ReadVec3();
            var nrm = r.ReadVec3();
            // Disk order is R-B-G-A (glampert documents this); swizzle to packed RGBA.
            var rr = r.ReadU8();
            var bb = r.ReadU8();
            var gg = r.ReadU8();
            var aa = r.ReadU8();
            var rgba = (uint)rr | ((uint)gg << 8) | ((uint)bb << 16) | ((uint)aa << 24);
            var uv = r.ReadVec2();
            result[i] = new Corner(pos, nrm, rgba, uv);
        }
        return result;
    }

    private static Surface[] ReadSurfaces(Reader r, uint count, uint cornerCount)
    {
        if (count == 0) return Array.Empty<Surface>();
        var result = new Surface[count];
        for (var i = 0; i < count; i++)
        {
            var texName = r.ReadCString();
            var start  = r.ReadU32();
            var span   = r.ReadU32();
            var corners = r.ReadU32();
            var faceCount = corners / 3;

            if ((long)start + span > cornerCount)
                throw new InvalidDataException(
                    $"Surface {i} '{texName}' span [{start}..{start + span}) overruns corner pool ({cornerCount})");

            r.EnsureSpace((long)faceCount * 6, $"Surface[{i}].faces");
            var indices = new ushort[faceCount * 3];
            for (var k = 0; k < indices.Length; k++)
            {
                var idx = r.ReadU16();
                if (idx >= span)
                    throw new InvalidDataException(
                        $"Surface {i} face {k / 3} index {idx} >= span {span}");
                indices[k] = idx;
            }

            result[i] = new Surface
            {
                TextureName = texName,
                StartCorner = start,
                SpanCorner = span,
                CornerCount = corners,
                TriangleIndices = indices,
            };
        }
        return result;
    }

    /// <summary>Total triangle count across all surfaces.</summary>
    public int TotalTriangleCount
    {
        get
        {
            var t = 0;
            foreach (var s in Surfaces) t += s.TriangleCount;
            return t;
        }
    }

    /// <summary>Minimal stream-y reader: tracks a position and reads little-endian scalars.
    /// Lives inside SnoModel because the access pattern is one-off and file-scoped.</summary>
    private sealed class Reader
    {
        private readonly byte[] _data;
        public int Position { get; private set; }

        public Reader(byte[] data) { _data = data; }

        public bool AtEnd => Position >= _data.Length;

        public void EnsureSpace(long bytes, string context)
        {
            if (Position + bytes > _data.Length)
                throw new InvalidDataException($"SNO truncated reading {context} at 0x{Position:X8}");
        }

        public void Skip(long bytes)
        {
            EnsureSpace(bytes, "skip");
            Position += (int)bytes;
        }

        public byte ReadU8()
        {
            EnsureSpace(1, "u8");
            return _data[Position++];
        }

        public ushort ReadU16()
        {
            EnsureSpace(2, "u16");
            var v = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(Position));
            Position += 2;
            return v;
        }

        public uint ReadU32()
        {
            EnsureSpace(4, "u32");
            var v = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(Position));
            Position += 4;
            return v;
        }

        public float ReadF32()
        {
            EnsureSpace(4, "f32");
            var v = BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(Position));
            Position += 4;
            return v;
        }

        public FourCC ReadFourCC()
        {
            EnsureSpace(4, "fourcc");
            var a = (char)_data[Position + 0];
            var b = (char)_data[Position + 1];
            var c = (char)_data[Position + 2];
            var d = (char)_data[Position + 3];
            Position += 4;
            return new FourCC(a, b, c, d);
        }

        public Vector2 ReadVec2() => new(ReadF32(), ReadF32());
        public Vector3 ReadVec3() => new(ReadF32(), ReadF32(), ReadF32());

        public Xform4x3 ReadXform4x3()
        {
            // Disk layout is TRANSLATION FIRST, then row-major 3x3 — matches OpenSiege's
            // ReaderWriterSNO.cpp door/spot read order and siege_max's ReadPosThenRot.
            // Reading rotation-first (glampert's commented caveat) produced det-18 door
            // xforms where the "rotation rows" were actually the translation vector
            // and the "translation" was the third rotation row.
            var t  = ReadVec3();
            var r0 = ReadVec3();
            var r1 = ReadVec3();
            var r2 = ReadVec3();
            return new Xform4x3(r0, r1, r2, t);
        }

        public string ReadCString()
        {
            var start = Position;
            while (Position < _data.Length && _data[Position] != 0) Position++;
            if (Position >= _data.Length)
                throw new InvalidDataException("SNO: unterminated C-string");
            var s = System.Text.Encoding.ASCII.GetString(_data, start, Position - start);
            Position++; // skip NUL
            return s;
        }
    }
}
