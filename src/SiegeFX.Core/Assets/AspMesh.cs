using System.Buffers.Binary;
using System.Numerics;
using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>
/// Parsed Dungeon Siege .asp (Animatable Aspect) mesh. Phase 4 targets the static-geometry
/// path: positions (BVTX), corners (BCRN — unique per-triangle-vertex records carrying
/// normal + UV), and triangle indices (BTRI). Phase 7b adds the skeleton binding (bone
/// names, parent hierarchy, bind-pose transforms) so PRS animation clips can be applied
/// on top; skin-weight data (BVWL) is still pending until we actually run the skin.
/// </summary>
public sealed class AspMesh
{
    public string MeshName     { get; init; } = "";
    public int AspVersionMajor { get; init; }
    public int AspVersionMinor { get; init; }

    /// <summary>Texture atlas names from the BMSH text field (first <c>numTextures</c> tokens).
    /// DS1 assets usually carry one entry identifying the model's default texset base.</summary>
    public IReadOnlyList<string> TextureNames { get; init; } = Array.Empty<string>();

    /// <summary>Bone names in skeleton order. For static (unrigged) meshes this is empty.</summary>
    public IReadOnlyList<string> BoneNames { get; init; } = Array.Empty<string>();

    /// <summary>Parent bone index per <see cref="BoneNames"/> entry, or -1 for root bones.
    /// On disk a root bone is encoded as <c>parent == idx</c>; this array has it normalized
    /// to -1 so downstream tree-walks don't have to special-case the self-reference.</summary>
    public int[] BoneParents { get; init; } = Array.Empty<int>();

    /// <summary>Per-bone bind pose in parent space: rotation + translation. Matches
    /// <c>dataRPOS[2]</c> in ASPImport.ms — the pose that actually parents the hierarchy
    /// at rest. <c>dataRPOS[1]</c> is also preserved below for debugging.</summary>
    public Transform[] BindPose { get; init; } = Array.Empty<Transform>();

    /// <summary>The first of the two RPOS transforms. ASPImport.ms reads it but uses
    /// <see cref="BindPose"/> (dataRPOS[2]) for the actual skeleton; kept here for diag.</summary>
    public Transform[] BindPoseAlt { get; init; } = Array.Empty<Transform>();

    /// <summary>Vertex positions (BVTX). Shared by all corners of the same vertex.</summary>
    public Vector3[] Positions { get; init; } = Array.Empty<Vector3>();

    /// <summary>Per-corner records. A corner is one (vertex, normal, uv) tuple; a vertex
    /// that straddles a UV seam appears as multiple corners.</summary>
    public Corner[] Corners { get; init; } = Array.Empty<Corner>();

    /// <summary>Triangle indices (BTRI). Each triplet indexes into <see cref="Corners"/>.</summary>
    public int[] TriangleIndices { get; init; } = Array.Empty<int>();

    public int TriangleCount => TriangleIndices.Length / 3;
    public int BoneCount => BoneNames.Count;

    public readonly record struct Corner(int VertexIndex, Vector3 Normal, uint Color, Vector2 Uv);

    public readonly record struct Transform(Quaternion Rotation, Vector3 Translation);

    public static AspMesh Load(byte[] data)
    {
        var chunks = AspScanner.Scan(data);
        if (chunks.Count == 0)
            throw new InvalidDataException("ASP has no recognizable chunks");

        string meshName = "";
        int verMajor = 0, verMinor = 0;
        string[] textureNames = Array.Empty<string>();
        string[] boneNames    = Array.Empty<string>();
        int[] boneParents     = Array.Empty<int>();
        Transform[] bindPose    = Array.Empty<Transform>();
        Transform[] bindPoseAlt = Array.Empty<Transform>();
        // Character ASPs split their geometry across multiple submeshes (numSubMeshes > 1),
        // each emitting its own BVTX/BCRN/BTRI chunk. Indices within each submesh are
        // local to that submesh, so combining them into the flat arrays that downstream
        // StaticMesh expects needs a cumulative offset: vertPosBase tracks the running
        // BVTX count, cornerBase tracks the running BCRN count.
        var positionList = new List<Vector3>();
        var cornerList   = new List<Corner>();
        var triList      = new List<int>();
        Vector3[] positions;
        Corner[] corners;
        int[] tris;
        int vertPosBase = 0, cornerBase = 0;
        int declaredNumBones = 0;
        // numSubTextures for the *current* submesh, carried from the most recent BSMM into
        // the following BTRI so we can skip its version-dependent subtexture header. BSMM
        // always precedes BTRI in the per-submesh cycle, and the default 1 covers the
        // older-format case where BSMM is absent (single-texture meshes).
        int curSubTextures = 1;

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
                // Canonical BMSH per ASPImport.ms: 6*u32 counts (sizeTextField, numBones,
                // numTextures, numVerticesTotal, numSubMeshes, renderFlags) followed by a
                // variable-length text blob containing numTextures + numBones null-terminated
                // names, each padded to 4-byte alignment.
                if (body.Length < 24) throw new InvalidDataException("BMSH header truncated");
                var sizeTextField    = (int)BinaryPrimitives.ReadUInt32LittleEndian(body);
                var numBones         = (int)BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(4));
                var numTextures      = (int)BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(8));
                _                    = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(12)); // numVerticesTotal
                _                    = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(16)); // numSubMeshes
                _                    = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(20)); // renderFlags
                if (body.Length < 24 + sizeTextField)
                    throw new InvalidDataException("BMSH text blob past chunk end");

                var tokens = ReadPaddedCStrings(body.Slice(24, sizeTextField));
                if (tokens.Count < numTextures + numBones)
                    throw new InvalidDataException(
                        $"BMSH text blob has {tokens.Count} tokens but header declares {numTextures}+{numBones}");
                textureNames = tokens.Take(numTextures).ToArray();
                boneNames    = tokens.Skip(numTextures).Take(numBones).ToArray();
                declaredNumBones = numBones;
                // Preserve a human-readable name for the mesh. The first texture name is the
                // DS1 "texset base" — downstream code already uses that elsewhere, so we
                // surface it as MeshName for continuity with the pre-Phase-7 API surface.
                meshName = numTextures > 0 ? textureNames[0] : "";
            }
            else if (id == new FourCC('B','O','N','H'))
            {
                if (declaredNumBones == 0) continue; // static mesh with no skeleton
                // Each entry: (idx, parent, textOffset) u32s. Entries may be out of order —
                // idx is the explicit array slot they populate. Parent == idx is the root
                // sentinel; we normalize that to -1.
                if (body.Length < declaredNumBones * 12)
                    throw new InvalidDataException("BONH body truncated");
                boneParents = new int[declaredNumBones];
                Array.Fill(boneParents, -1);
                for (var i = 0; i < declaredNumBones; i++)
                {
                    var b = body.Slice(i * 12, 12);
                    var idx    = (int)BinaryPrimitives.ReadUInt32LittleEndian(b);
                    var parent = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(4));
                    _          = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(8)); // textOffset
                    if ((uint)idx >= (uint)declaredNumBones)
                        throw new InvalidDataException($"BONH bone idx {idx} out of range (numBones={declaredNumBones})");
                    boneParents[idx] = (parent == idx) ? -1 : parent;
                }
            }
            else if (id == new FourCC('R','P','O','S'))
            {
                if (declaredNumBones == 0) continue;
                // RPOS stores two transforms per bone: dataRPOS[1] and dataRPOS[2] in
                // ASPImport.ms. Interleaved on disk as (rot1, pos1, rot2, pos2) per bone.
                // ASPImport.ms uses dataRPOS[2] to parent the skeleton at rest — that's
                // what we publish as BindPose.
                if (body.Length < 4 + declaredNumBones * 2 * 28)
                    throw new InvalidDataException("RPOS body truncated");
                _ = BinaryPrimitives.ReadUInt32LittleEndian(body); // redundant numBones field
                bindPoseAlt = new Transform[declaredNumBones];
                bindPose    = new Transform[declaredNumBones];
                for (var i = 0; i < declaredNumBones; i++)
                {
                    var b = body.Slice(4 + i * 56, 56);
                    bindPoseAlt[i] = ReadTransform(b);
                    bindPose[i]    = ReadTransform(b.Slice(28));
                }
            }
            else if (id == new FourCC('B','S','M','M'))
            {
                // ASPImport.ms ReadBSMM: u32 numSubTextures, then numSubTextures × (textureIndex, faceSpan).
                // We only care about numSubTextures — it sizes BTRI's header below.
                if (body.Length < 4) throw new InvalidDataException("BSMM body missing numSubTextures");
                curSubTextures = (int)BinaryPrimitives.ReadUInt32LittleEndian(body);
                if (curSubTextures <= 0 || curSubTextures > 4096)
                    throw new InvalidDataException($"BSMM numSubTextures {curSubTextures} out of plausible range");
            }
            else if (id == new FourCC('B','V','T','X'))
            {
                // Start a new submesh's vertex block. Record where its corners/verts will
                // live in the flattened arrays so subsequent BCRN/BTRI can offset into them.
                vertPosBase = positionList.Count;
                cornerBase  = cornerList.Count;
                var count = ReadChunkCount(body, stride: 12, chunkName: "BVTX");
                for (var i = 0; i < count; i++)
                {
                    var b = body.Slice(4 + i * 12, 12);
                    positionList.Add(new Vector3(
                        BinaryPrimitives.ReadSingleLittleEndian(b),
                        BinaryPrimitives.ReadSingleLittleEndian(b.Slice(4)),
                        BinaryPrimitives.ReadSingleLittleEndian(b.Slice(8))));
                }
            }
            else if (id == new FourCC('B','C','R','N'))
            {
                // Record is 32 bytes even though we only consume 28 of them:
                // [0..4) vertIndex (submesh-local), [4..16) normal, [16..20) color,
                // [20..24) reserved (observed zero), [24..32) uv. Translate vertIndex
                // from submesh-local to the flattened positions array via vertPosBase.
                var count = ReadChunkCount(body, stride: 32, chunkName: "BCRN");
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
                    cornerList.Add(new Corner(vi + vertPosBase, new Vector3(nx, ny, nz), color, new Vector2(u, v)));
                }
            }
            else if (id == new FourCC('B','T','R','I'))
            {
                // ASPImport.ms ReadBTRI body layout (chunk version already consumed by the scanner):
                //   u32 numFaces
                //   version-dependent subtexture header (see below), sized by numSubTextures
                //   numFaces × (u32 a, u32 b, u32 c)   — flat triangle indices into the submesh BCRN
                //
                // VersionOf(v) maps (major<<8|minor) → decimal "minor*10 + major"; shipping DS1
                // assets are all V2_2/V2_3/V4_0 → 22/23/40. The header rule:
                //   decimal == 22   : numSubTextures × (cornerSpan:u32)
                //   decimal > 22    : numSubTextures × (cornerStart:u32, cornerSpan:u32)
                //   decimal < 22    : no header (single implicit subtexture covers the whole mesh)
                // We parse only the byte-count — we don't consume the subtexture table for
                // anything because BCRN indices already live in a flat submesh-local space.
                if (body.Length < 4)
                    throw new InvalidDataException("BTRI body missing numFaces");
                var numFaces = (int)BinaryPrimitives.ReadUInt32LittleEndian(body);
                var pos = 4;
                var vdec = (chunk.VersionRaw & 0xFF) * 10 + ((chunk.VersionRaw >> 8) & 0xFF);
                int headerBytes =
                    vdec == 22 ? curSubTextures * 4 :
                    vdec  > 22 ? curSubTextures * 8 :
                    0;
                if (pos + headerBytes > body.Length)
                    throw new InvalidDataException("BTRI subtexture header past chunk end");
                pos += headerBytes;
                if (pos + numFaces * 12 > body.Length)
                    throw new InvalidDataException($"BTRI face data ({numFaces} faces) past chunk end");
                for (var f = 0; f < numFaces; f++)
                {
                    var a = (int)BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(pos));
                    var b = (int)BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(pos + 4));
                    var c = (int)BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(pos + 8));
                    pos += 12;
                    triList.Add(a + cornerBase);
                    triList.Add(b + cornerBase);
                    triList.Add(c + cornerBase);
                }
            }
            // BSUB/BSMM/BVMP/BVWL/STCH: preserved for later phases, not consumed here.
        }

        positions = positionList.ToArray();
        corners   = cornerList.ToArray();
        tris      = triList.ToArray();

        // Preflight the cross-chunk invariants so a missing chunk produces a useful error
        // instead of the confusing "vertex N out of range (positions=0)" further down.
        if (corners.Length > 0 && positions.Length == 0)
            throw new InvalidDataException("ASP has BCRN but no BVTX");
        if (tris.Length > 0 && corners.Length == 0)
            throw new InvalidDataException("ASP has BTRI but no BCRN");

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

        // If the file declared bones but a BONH chunk was absent, leave parents as
        // "all root" — cleaner than failing hard, and matches Phase 6 rendering paths
        // that don't yet care about hierarchy.
        if (declaredNumBones > 0 && boneParents.Length == 0)
        {
            boneParents = new int[declaredNumBones];
            Array.Fill(boneParents, -1);
        }

        return new AspMesh
        {
            MeshName        = meshName,
            AspVersionMajor = verMajor,
            AspVersionMinor = verMinor,
            TextureNames    = textureNames,
            BoneNames       = boneNames,
            BoneParents     = boneParents,
            BindPose        = bindPose,
            BindPoseAlt     = bindPoseAlt,
            Positions       = positions,
            Corners         = corners,
            TriangleIndices = tris,
        };
    }

    private static Transform ReadTransform(ReadOnlySpan<byte> b)
    {
        var qx = BinaryPrimitives.ReadSingleLittleEndian(b);
        var qy = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(4));
        var qz = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(8));
        var qw = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(12));
        var px = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(16));
        var py = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(20));
        var pz = BinaryPrimitives.ReadSingleLittleEndian(b.Slice(24));
        return new Transform(new Quaternion(qx, qy, qz, qw), new Vector3(px, py, pz));
    }

    /// <summary>Splits a BMSH text blob into successive null-terminated strings, each
    /// padded so the (string + null + pad) consumes a multiple of 4 bytes. Tokens are
    /// returned in file order; callers apply the numTextures/numBones split themselves.</summary>
    private static List<string> ReadPaddedCStrings(ReadOnlySpan<byte> span)
    {
        var list = new List<string>();
        var pos = 0;
        while (pos < span.Length)
        {
            var start = pos;
            while (pos < span.Length && span[pos] != 0) pos++;
            if (pos == span.Length) break; // ran out of data mid-string; tolerate
            var name = System.Text.Encoding.ASCII.GetString(span.Slice(start, pos - start));
            list.Add(name);
            var consumed = (pos - start) + 1; // +1 for the null terminator
            var pad = (4 - (consumed % 4)) % 4;
            pos += 1 + pad;
        }
        return list;
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

}
