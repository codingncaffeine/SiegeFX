using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using SiegeFX.Core.Assets;

namespace SiegeSmith.Services;

/// <summary>Writes a Dungeon Siege Siege Node (<c>.sno</c>) terrain tile that <see cref="SnoModel.Load"/>
/// reads back byte-for-byte. Targets v7.0 (the checksum field is present and written as 0). The reader is
/// length-less and section-contiguous, so field order and counts must match the reader exactly — verified
/// against SnoModel.cs and the OpenSiege ReaderWriterSNO.cpp writer.
///
/// SNO terrain space is Y-up: a flat floor lies in the XZ plane with a +Y normal (confirmed against shipped
/// tiles, e.g. t_cry01_flr_1a — a 4×4 tile, extent (4,0,4), floor normal (0,1,0)). This differs from ASP
/// prop/actor meshes, which are Z-up. Positions/normals/UVs are written verbatim; colors are R-B-G-A on
/// disk (the reader unswizzles).</summary>
public static class SnoWriter
{
    public const int Version = 7;
    public const int VersionMinor = 0;

    /// <summary>A terrain vertex. <paramref name="Rgba"/> is DS1's baked per-vertex radiosity (0xFFFFFFFF =
    /// unlit white). Positions are Y-up SNO-local.</summary>
    public readonly record struct Vertex(Vector3 Position, Vector3 Normal, uint Rgba, Vector2 Uv)
    {
        public static Vertex White(Vector3 pos, Vector3 normal, Vector2 uv) => new(pos, normal, 0xFFFFFFFFu, uv);
    }

    /// <summary>A render triangle referencing the vertex pool by global index.</summary>
    public readonly record struct Tri(int A, int B, int C);

    /// <summary>An edge door that stitches this tile to a neighbour. Rotation is identity; only translation
    /// and the perimeter hot-spot corner indices are authored.</summary>
    public readonly record struct DoorDef(uint Id, Vector3 Translation, uint[] HotSpots);

    /// <summary>Serializes a tile. When <paramref name="walkable"/> is set, a single <see cref="SnoModel.FloorKind.Floor"/>
    /// logical grouping is emitted over every triangle (with a single-leaf BSP) so the engine's nav builder
    /// treats the tile as walkable. Otherwise no grouping is written (geometry-only / cosmetic tile).</summary>
    public static byte[] Write(
        IReadOnlyList<Vertex> vertices,
        IReadOnlyList<Tri> tris,
        string textureName,
        IReadOnlyList<DoorDef>? doors = null,
        bool walkable = false)
    {
        doors ??= Array.Empty<DoorDef>();
        if (string.IsNullOrEmpty(textureName)) textureName = "custom";
        if (vertices.Count > ushort.MaxValue)
            throw new ArgumentException($"SNO surface indices are 16-bit; {vertices.Count} vertices exceeds {ushort.MaxValue}");
        if (walkable && tris.Count > ushort.MaxValue)
            throw new ArgumentException($"SNO BSP triangle indices are 16-bit; {tris.Count} triangles exceeds {ushort.MaxValue}");

        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var v in vertices) { min = Vector3.Min(min, v.Position); max = Vector3.Max(max, v.Position); }
        if (vertices.Count == 0) { min = Vector3.Zero; max = Vector3.Zero; }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        // --- Header (88 bytes) ---
        w.Write((byte)'S'); w.Write((byte)'N'); w.Write((byte)'O'); w.Write((byte)'D');
        w.Write((uint)Version);
        w.Write((uint)VersionMinor);
        w.Write((uint)doors.Count);
        w.Write(0u);                        // spotCount
        w.Write((uint)vertices.Count);      // cornerCount
        w.Write((uint)tris.Count);          // faceCount (advisory)
        w.Write(1u);                        // textureCount (single surface)
        WriteVec3(w, min);
        WriteVec3(w, max);
        for (int i = 0; i < 7; i++) w.Write(0u); // unused1..7
        w.Write(0u);                        // checksum (v7 field; 0 = unverified)

        // --- Doors --- (translation first, then row-major 3x3 identity, per the reader)
        foreach (var d in doors)
        {
            w.Write(d.Id);
            WriteXform(w, d.Translation, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
            var hs = d.HotSpots ?? Array.Empty<uint>();
            w.Write((uint)hs.Length);
            foreach (var h in hs) w.Write(h);
        }

        // --- Spots --- (none)

        // --- Corners ---
        foreach (var v in vertices)
        {
            WriteVec3(w, v.Position);
            WriteVec3(w, v.Normal);
            // Disk order R,B,G,A (the reader unswizzles to packed RGBA).
            w.Write((byte)(v.Rgba & 0xFF));          // R
            w.Write((byte)((v.Rgba >> 16) & 0xFF));  // B
            w.Write((byte)((v.Rgba >> 8) & 0xFF));   // G
            w.Write((byte)((v.Rgba >> 24) & 0xFF));  // A
            WriteVec2(w, v.Uv);
        }

        // --- Surface (single material) ---
        WriteCString(w, textureName);
        w.Write(0u);                        // startCorner (indices are already global → local)
        w.Write((uint)vertices.Count);      // spanCorner
        w.Write((uint)(tris.Count * 3));    // index count
        foreach (var t in tris)
        {
            w.Write((ushort)t.A);
            w.Write((ushort)t.B);
            w.Write((ushort)t.C);
        }

        // --- Logical groupings / nav ---
        if (walkable && tris.Count > 0)
        {
            w.Write(1u);                    // grouping count
            w.Write((byte)1);               // id
            WriteVec3(w, min);
            WriteVec3(w, max);
            w.Write((uint)SnoModel.FloorKind.Floor);
            w.Write(0u);                    // general_connection_section count
            w.Write(0u);                    // nodal_array count
            w.Write((uint)tris.Count);      // nav face count
            foreach (var t in tris)
            {
                var a = vertices[t.A].Position;
                var b = vertices[t.B].Position;
                var c = vertices[t.C].Position;
                WriteVec3(w, a); WriteVec3(w, b); WriteVec3(w, c);
                WriteVec3(w, TriNormal(a, b, c));
            }
            // Single-leaf BSP over all nav faces.
            WriteVec3(w, min);
            WriteVec3(w, max);
            w.Write((byte)1);               // is_leaf
            w.Write((ushort)tris.Count);    // triangle_count
            for (int i = 0; i < tris.Count; i++) w.Write((ushort)i);
            w.Write((byte)0);               // child count
        }
        else
        {
            w.Write(0u);                    // grouping count (geometry-only)
        }

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Generates a flat, walkable square tile: a <paramref name="res"/>×<paramref name="res"/> grid
    /// of quads in the XZ plane at Y=0, +Y normals, UVs tiled <paramref name="uvTiles"/> times across the
    /// tile. This is the parametric "first walkable custom terrain" primitive.</summary>
    public static (List<Vertex> Verts, List<Tri> Tris) BuildFlatTile(float sizeX, float sizeZ, int res, float uvTiles = 1f)
    {
        res = Math.Max(1, res);
        var verts = new List<Vertex>((res + 1) * (res + 1));
        // Centred on the origin like shipped tiles (t_cry01_flr_1a spans -2..2).
        float x0 = -sizeX * 0.5f, z0 = -sizeZ * 0.5f;
        for (int j = 0; j <= res; j++)
            for (int i = 0; i <= res; i++)
            {
                float fx = (float)i / res, fz = (float)j / res;
                var pos = new Vector3(x0 + fx * sizeX, 0f, z0 + fz * sizeZ);
                var uv = new Vector2(fx * uvTiles, fz * uvTiles);
                verts.Add(Vertex.White(pos, Vector3.UnitY, uv));
            }
        int Idx(int i, int j) => j * (res + 1) + i;
        var tris = new List<Tri>(res * res * 2);
        for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                // A=(i+1,j+1) B=(i+1,j) C=(i,j) D=(i,j+1) — winding matches shipped +Y-normal floor tris.
                int a = Idx(i + 1, j + 1), b = Idx(i + 1, j), c = Idx(i, j), d = Idx(i, j + 1);
                tris.Add(new Tri(a, b, c));
                tris.Add(new Tri(a, c, d));
            }
        return (verts, tris);
    }

    private static void WriteVec2(BinaryWriter w, Vector2 v) { w.Write(v.X); w.Write(v.Y); }
    private static void WriteVec3(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }

    private static void WriteXform(BinaryWriter w, Vector3 t, Vector3 r0, Vector3 r1, Vector3 r2)
    {
        WriteVec3(w, t); WriteVec3(w, r0); WriteVec3(w, r1); WriteVec3(w, r2);
    }

    private static void WriteCString(BinaryWriter w, string s)
    {
        foreach (char c in s) w.Write((byte)c);
        w.Write((byte)0);
    }

    private static Vector3 TriNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        var n = Vector3.Cross(b - a, c - a);
        return n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;
    }
}
