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
            var span = data.AsSpan(chunk.Offset);
            var id = chunk.Id;

            if (id == new FourCC('B','M','S','H'))
            {
                verMajor = chunk.VersionRaw & 0xFF;
                verMinor = (chunk.VersionRaw >> 8) & 0xFF;
                // Layout observed: 8-byte header, then 6*u32 of counts (first is 0x20, rest match
                // later subset info), then two 16-char NUL-padded names (skeleton, mesh).
                var body = span.Slice(8);
                skelName = ReadFixedAscii(body.Slice(24, 16));
                meshName = ReadFixedAscii(body.Slice(40, 16));
            }
            else if (id == new FourCC('B','V','T','X'))
            {
                var body = span.Slice(8);
                var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(body);
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
                var body = span.Slice(8);
                var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(body);
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
                var body = span.Slice(8);
                var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(body);
                tris = new int[count * 3];
                for (var i = 0; i < tris.Length; i++)
                    tris[i] = (int)BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(4 + i * 4, 4));
            }
            // BONH/BSUB/BSMM/BVMP/BVWL/STCH/RPOS: preserved for later phases, not consumed here.
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

    private static string ReadFixedAscii(ReadOnlySpan<byte> span)
    {
        var nul = span.IndexOf((byte)0);
        if (nul < 0) nul = span.Length;
        return System.Text.Encoding.ASCII.GetString(span.Slice(0, nul));
    }
}
