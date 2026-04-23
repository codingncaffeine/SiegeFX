using System.Buffers.Binary;
using System.Numerics;
using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>
/// Parsed Dungeon Siege .asp (Animatable Aspect) mesh. Phase 4 targets the static-geometry
/// path: positions (BVTX), corners (BCRN — unique per-triangle-vertex records carrying
/// normal + UV), and triangle indices (BTRI). Bone/skin data is read and preserved but
/// not applied; animation comes in Phase 7.
/// </summary>
public sealed class AspMesh
{
    public string SkeletonName { get; init; } = "";
    public string MeshName     { get; init; } = "";
    public int AspVersionMajor { get; init; }
    public int AspVersionMinor { get; init; }

    /// <summary>Vertex positions (BVTX). Shared by all corners of the same vertex.</summary>
    public Vector3[] Positions { get; init; } = Array.Empty<Vector3>();

    /// <summary>Per-corner records. A corner is one (vertex, normal, uv) tuple; a vertex
    /// that straddles a UV seam appears as multiple corners.</summary>
    public Corner[] Corners { get; init; } = Array.Empty<Corner>();

    /// <summary>Triangle indices (BTRI). Each triplet indexes into <see cref="Corners"/>.</summary>
    public int[] TriangleIndices { get; init; } = Array.Empty<int>();

    public int TriangleCount => TriangleIndices.Length / 3;

    public readonly record struct Corner(int VertexIndex, Vector3 Normal, uint Color, Vector2 Uv);

    public static AspMesh Load(byte[] data)
    {
        var chunks = AspScanner.Scan(data);
        if (chunks.Count == 0)
            throw new InvalidDataException("ASP has no recognizable chunks");

        string skelName = "", meshName = "";
        int verMajor = 0, verMinor = 0;
        var positions = Array.Empty<Vector3>();
        var corners   = Array.Empty<Corner>();
        var tris      = Array.Empty<int>();

        foreach (var chunk in chunks)
        {
            if (chunk.Offset + 8 > data.Length)
                throw new InvalidDataException($"ASP chunk {chunk.Id} header past EOF at 0x{chunk.Offset:X8}");
            var body = data.AsSpan(chunk.Offset + 8);
            var id = chunk.Id;

            if (id == new FourCC('B','M','S','H'))
            {
                verMajor = chunk.VersionRaw & 0xFF;
                verMinor = (chunk.VersionRaw >> 8) & 0xFF;
                // Layout observed: 8-byte header (already skipped), then 6*u32 of counts
                // (first is 0x20, rest match later subset info), then two 16-char NUL-padded
                // names (skeleton, mesh).
                if (body.Length < 56) throw new InvalidDataException("BMSH body truncated");
                skelName = ReadFixedAscii(body.Slice(24, 16));
                meshName = ReadFixedAscii(body.Slice(40, 16));
            }
            else if (id == new FourCC('B','V','T','X'))
            {
                var count = ReadChunkCount(body, stride: 12, chunkName: "BVTX");
                positions = new Vector3[count];
                for (var i = 0; i < count; i++)
                {
                    var b = body.Slice(4 + i * 12, 12);
                    positions[i] = new Vector3(
                        BinaryPrimitives.ReadSingleLittleEndian(b),
                        BinaryPrimitives.ReadSingleLittleEndian(b.Slice(4)),
                        BinaryPrimitives.ReadSingleLittleEndian(b.Slice(8)));
                }
            }
            else if (id == new FourCC('B','C','R','N'))
            {
                // Record is 32 bytes even though we only consume 28 of them:
                // [0..4) vertIndex, [4..16) normal, [16..20) color, [20..24) padding/reserved
                // (observed zero; possibly a second color or weight slot), [24..32) uv.
                var count = ReadChunkCount(body, stride: 32, chunkName: "BCRN");
                corners = new Corner[count];
                for (var i = 0; i < count; i++)
                {
                    var b = body.Slice(4 + i * 32, 32);
                    var vi     = (int)BinaryPrimitives.ReadUInt32LittleEndian(b);
                    var nx     = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(4));
                    var ny     = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(8));
                    var nz     = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(12));
                    var color  = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(16));
                    var u      = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(24));
                    var v      = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(28));
                    corners[i] = new Corner(vi, new Vector3(nx, ny, nz), color, new Vector2(u, v));
                }
            }
            else if (id == new FourCC('B','T','R','I'))
            {
                var count = ReadChunkCount(body, stride: 12, chunkName: "BTRI");
                tris = new int[count * 3];
                for (var i = 0; i < tris.Length; i++)
                    tris[i] = (int)BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(4 + i * 4, 4));
            }
            // BONH/BSUB/BSMM/BVMP/BVWL/STCH/RPOS: preserved for later phases, not consumed here.
        }

        // BTRI indices point into Corners, not directly into Positions. Validate both hops
        // here so bad asset data fails fast with a diagnosable error instead of crashing the
        // GL driver with an out-of-range EBO or hitting IndexOutOfRangeException deep in
        // StaticMesh.
        for (var i = 0; i < tris.Length; i++)
        {
            var ti = tris[i];
            if ((uint)ti >= (uint)corners.Length)
                throw new InvalidDataException($"BTRI index {ti} at triangle {i / 3} out of range (corners={corners.Length})");
        }
        for (var i = 0; i < corners.Length; i++)
        {
            var vi = corners[i].VertexIndex;
            if ((uint)vi >= (uint)positions.Length)
                throw new InvalidDataException($"BCRN corner {i} references vertex {vi} out of range (positions={positions.Length})");
        }

        return new AspMesh
        {
            SkeletonName   = skelName,
            MeshName       = meshName,
            AspVersionMajor = verMajor,
            AspVersionMinor = verMinor,
            Positions       = positions,
            Corners         = corners,
            TriangleIndices = tris,
        };
    }

    /// <summary>
    /// Reads a u32 count prefix and clamps it against the chunk body length so a corrupt
    /// count (e.g. uint.MaxValue) can never cause a gigabyte allocation or a negative
    /// int cast. Returns the validated count as a non-negative int.
    /// </summary>
    private static int ReadChunkCount(ReadOnlySpan<byte> body, int stride, string chunkName)
    {
        if (body.Length < 4)
            throw new InvalidDataException($"{chunkName} body missing count");
        var raw = BinaryPrimitives.ReadUInt32LittleEndian(body);
        var maxCount = (body.Length - 4) / stride;
        if (raw > (uint)maxCount)
            throw new InvalidDataException($"{chunkName} count {raw} exceeds body capacity {maxCount}");
        return (int)raw;
    }

    private static string ReadFixedAscii(ReadOnlySpan<byte> span)
    {
        var nul = span.IndexOf((byte)0);
        if (nul < 0) nul = span.Length;
        return System.Text.Encoding.ASCII.GetString(span.Slice(0, nul));
    }
}
