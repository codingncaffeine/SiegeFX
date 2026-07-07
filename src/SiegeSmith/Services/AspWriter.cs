using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace SiegeSmith.Services;

/// <summary>One per-triangle-vertex record for the ASP writer — a (position, normal, uv, colour) tuple.
/// Multiple corners can share a position (a UV/normal seam splits one vertex into several corners).</summary>
public readonly record struct AspCorner(int VertexIndex, Vector3 Normal, Vector2 Uv, uint Color)
{
    public static AspCorner White(int v, Vector3 n, Vector2 uv) => new(v, n, uv, 0xFFFFFFFF);
}

/// <summary>A triangle referencing three corners, tagged with the material (subtexture) it belongs to.</summary>
public readonly record struct AspFace(int A, int B, int C, int Material);

/// <summary>One bone: its name, its parent index (or -1 for a root), and its bind pose expressed in
/// PARENT space (rotation + translation). The composed world bind must be invertible — a rigid
/// rotation+translation always is.</summary>
public readonly record struct AspBone(string Name, int Parent, Quaternion Rotation, Vector3 Translation);

/// <summary>Per-corner skin binding: up to four (bone, weight) influences, parallel to the corner list.
/// Unused slots have weight 0. Bone indices are 0-based (the writer targets V4_1, whose WCRN reader is
/// 0-based).</summary>
public readonly record struct AspSkin(Vector4 Weights, byte B0, byte B1, byte B2, byte B3);

/// <summary>Writes a static (unrigged) Dungeon Siege <c>.asp</c> that <see cref="SiegeFX.Core.Assets.AspMesh"/>
/// reads back verbatim. Mirrors that reader exactly — the format has NO table of contents and NO
/// per-chunk length, so every stride here must match the parser or the file silently corrupts.
///
/// Chunks are contiguous: FourCC (4 ASCII) + version word (major,minor,0,0) + body. Emitted order
/// BMSH → BVTX → BCRN → BSMM → BTRI (the reader needs BMSH first and BSMM before its BTRI). Targets
/// V4_1 so BTRI carries the 8-byte (cornerStart,cornerSpan) subtexture header. All corner indices are
/// written submesh-global with every cornerStart = 0, which keeps multi-material meshes correct without
/// per-subtexture index bookkeeping. Little-endian, f32, Z-up verbatim, UV raw.</summary>
public static class AspWriter
{
    private const byte VerMajor = 4;
    private const byte VerMinor = 1;

    public static byte[] WriteStatic(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<AspCorner> corners,
        IReadOnlyList<AspFace> faces,
        IReadOnlyList<string> textureNames)
    {
        if (textureNames.Count == 0) textureNames = new[] { "custom" };

        // Order faces material-major and count faces per material so BSMM/BTRI agree face-for-face.
        var byMat = new List<AspFace>[textureNames.Count];
        for (int m = 0; m < byMat.Length; m++) byMat[m] = new List<AspFace>();
        foreach (var f in faces)
        {
            int m = f.Material >= 0 && f.Material < byMat.Length ? f.Material : 0;
            byMat[m].Add(f);
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        // ── BMSH ── 6×u32 counts + padded name blob (numTextures names, then numBones=0).
        var textBlob = BuildNameBlob(textureNames);
        WriteChunkHeader(w, "BMSH");
        w.Write((uint)textBlob.Length);       // sizeTextField
        w.Write(0u);                          // numBones
        w.Write((uint)textureNames.Count);    // numTextures
        w.Write((uint)positions.Count);       // numVerticesTotal (reader ignores)
        w.Write(1u);                          // numSubMeshes
        w.Write(0u);                          // renderFlags
        w.Write(textBlob);

        // ── BVTX ── u32 count + count × 3×f32 (Z-up verbatim).
        WriteChunkHeader(w, "BVTX");
        w.Write((uint)positions.Count);
        foreach (var p in positions) { w.Write(p.X); w.Write(p.Y); w.Write(p.Z); }

        // ── BCRN ── u32 count + count × 32B (vertIndex, normal, color, reserved=0, uv).
        WriteChunkHeader(w, "BCRN");
        w.Write((uint)corners.Count);
        foreach (var c in corners)
        {
            w.Write((uint)c.VertexIndex);
            w.Write(c.Normal.X); w.Write(c.Normal.Y); w.Write(c.Normal.Z);
            w.Write(c.Color);
            w.Write(0u);                      // reserved
            w.Write(c.Uv.X); w.Write(c.Uv.Y); // UV raw
        }

        // ── BSMM ── u32 numSubTextures + numSubTextures × (textureIndex, faceSpan).
        WriteChunkHeader(w, "BSMM");
        w.Write((uint)textureNames.Count);
        for (int m = 0; m < textureNames.Count; m++)
        {
            w.Write((uint)m);                 // textureIndex
            w.Write((uint)byMat[m].Count);    // faceSpan
        }

        // ── BTRI ── u32 numFaces + subtexture header (cornerStart=0, cornerSpan=corners) + faces.
        WriteChunkHeader(w, "BTRI");
        w.Write((uint)faces.Count);
        for (int m = 0; m < textureNames.Count; m++)
        {
            w.Write(0u);                      // cornerStart — 0 keeps indices submesh-global
            w.Write((uint)corners.Count);     // cornerSpan (reader records but does not use it)
        }
        foreach (var list in byMat)
            foreach (var f in list)
            {
                w.Write((uint)f.A); w.Write((uint)f.B); w.Write((uint)f.C);
            }

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Writes a rigged (skinned) ASP: the static geometry plus a skeleton (BONH bone hierarchy,
    /// RPOS bind pose) and per-corner skin weights (WCRN). <paramref name="skins"/> is parallel to
    /// <paramref name="corners"/>. Bone indices are 0-based (V4_1). The world-composed bind pose must be
    /// invertible or the engine rejects it at load — <see cref="AspBone"/> transforms are rigid, so it is.</summary>
    public static byte[] WriteSkinned(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<AspCorner> corners,
        IReadOnlyList<AspFace> faces,
        IReadOnlyList<string> textureNames,
        IReadOnlyList<AspBone> bones,
        IReadOnlyList<AspSkin> skins)
    {
        if (textureNames.Count == 0) textureNames = new[] { "custom" };
        if (skins.Count != corners.Count)
            throw new ArgumentException($"skins ({skins.Count}) must be parallel to corners ({corners.Count})");

        var byMat = new List<AspFace>[textureNames.Count];
        for (int m = 0; m < byMat.Length; m++) byMat[m] = new List<AspFace>();
        foreach (var f in faces)
        {
            int m = f.Material >= 0 && f.Material < byMat.Length ? f.Material : 0;
            byMat[m].Add(f);
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        // BMSH — names blob is textures THEN bones.
        var names = new List<string>(textureNames.Count + bones.Count);
        names.AddRange(textureNames);
        foreach (var b in bones) names.Add(b.Name);
        var textBlob = BuildNameBlob(names);
        WriteChunkHeader(w, "BMSH");
        w.Write((uint)textBlob.Length);
        w.Write((uint)bones.Count);        // numBones
        w.Write((uint)textureNames.Count); // numTextures
        w.Write((uint)positions.Count);
        w.Write(1u);                       // numSubMeshes
        w.Write(0u);                       // renderFlags
        w.Write(textBlob);

        // BONH — (idx, parent, textOffset) per bone; a root encodes parent == idx.
        WriteChunkHeader(w, "BONH");
        for (int i = 0; i < bones.Count; i++)
        {
            w.Write((uint)i);
            w.Write((uint)(bones[i].Parent < 0 ? i : bones[i].Parent));
            w.Write(0u); // textOffset (reader ignores)
        }

        // RPOS — u32 numBones + two transforms per bone (rot1,pos1,rot2,pos2); the reader binds on the
        // second, so both slots carry the same parent-space bind pose.
        WriteChunkHeader(w, "RPOS");
        w.Write((uint)bones.Count);
        foreach (var b in bones)
        {
            WriteTransform(w, b.Rotation, b.Translation);
            WriteTransform(w, b.Rotation, b.Translation);
        }

        // BVTX / BCRN — geometry, identical to the static path.
        WriteChunkHeader(w, "BVTX");
        w.Write((uint)positions.Count);
        foreach (var p in positions) { w.Write(p.X); w.Write(p.Y); w.Write(p.Z); }

        WriteChunkHeader(w, "BCRN");
        w.Write((uint)corners.Count);
        foreach (var c in corners)
        {
            w.Write((uint)c.VertexIndex);
            w.Write(c.Normal.X); w.Write(c.Normal.Y); w.Write(c.Normal.Z);
            w.Write(c.Color);
            w.Write(0u);
            w.Write(c.Uv.X); w.Write(c.Uv.Y);
        }

        // WCRN — 56B per corner, parallel to BCRN: pos(ignored) + weight[4] + bone[4] + normal/color/uv(ignored).
        WriteChunkHeader(w, "WCRN");
        w.Write((uint)skins.Count);
        for (int i = 0; i < skins.Count; i++)
        {
            var c = corners[i];
            var s = skins[i];
            var pos = c.VertexIndex >= 0 && c.VertexIndex < positions.Count ? positions[c.VertexIndex] : Vector3.Zero;
            w.Write(pos.X); w.Write(pos.Y); w.Write(pos.Z);                 // [0..12) ignored by reader
            w.Write(s.Weights.X); w.Write(s.Weights.Y); w.Write(s.Weights.Z); w.Write(s.Weights.W); // [12..28)
            w.Write(s.B0); w.Write(s.B1); w.Write(s.B2); w.Write(s.B3);     // [28..32) bone bytes
            w.Write(c.Normal.X); w.Write(c.Normal.Y); w.Write(c.Normal.Z);  // [32..44) ignored
            w.Write(c.Color);                                              // [44..48) ignored
            w.Write(c.Uv.X); w.Write(c.Uv.Y);                              // [48..56) ignored
        }

        // BSMM / BTRI — subtexture map + faces, identical to the static path.
        WriteChunkHeader(w, "BSMM");
        w.Write((uint)textureNames.Count);
        for (int m = 0; m < textureNames.Count; m++) { w.Write((uint)m); w.Write((uint)byMat[m].Count); }

        WriteChunkHeader(w, "BTRI");
        w.Write((uint)faces.Count);
        for (int m = 0; m < textureNames.Count; m++) { w.Write(0u); w.Write((uint)corners.Count); }
        foreach (var list in byMat)
            foreach (var f in list) { w.Write((uint)f.A); w.Write((uint)f.B); w.Write((uint)f.C); }

        w.Flush();
        return ms.ToArray();
    }

    private static void WriteTransform(BinaryWriter w, Quaternion q, Vector3 t)
    {
        w.Write(q.X); w.Write(q.Y); w.Write(q.Z); w.Write(q.W);
        w.Write(t.X); w.Write(t.Y); w.Write(t.Z);
    }

    private static void WriteChunkHeader(BinaryWriter w, string fourcc)
    {
        w.Write((byte)fourcc[0]); w.Write((byte)fourcc[1]); w.Write((byte)fourcc[2]); w.Write((byte)fourcc[3]);
        w.Write(VerMajor); w.Write(VerMinor); w.Write((byte)0); w.Write((byte)0);
    }

    /// <summary>Each name → ASCII bytes + NUL, padded so (string+null+pad) is a multiple of 4 bytes —
    /// the exact shape <c>ReadPaddedCStrings</c> expects.</summary>
    private static byte[] BuildNameBlob(IReadOnlyList<string> names)
    {
        using var ms = new MemoryStream();
        foreach (var name in names)
        {
            var bytes = Encoding.ASCII.GetBytes(name ?? "");
            ms.Write(bytes, 0, bytes.Length);
            ms.WriteByte(0);
            int consumed = bytes.Length + 1;
            int pad = (4 - (consumed % 4)) % 4;
            for (int i = 0; i < pad; i++) ms.WriteByte(0);
        }
        return ms.ToArray();
    }
}
