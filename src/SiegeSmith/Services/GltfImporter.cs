using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace SiegeSmith.Services;

/// <summary>Imports a glTF 2.0 model (<c>.glb</c> binary container or <c>.gltf</c> JSON with embedded /
/// sibling buffers) into the same <see cref="ObjImporter.Result"/> the ASP writer consumes. Walks the
/// default scene's node hierarchy to world-transform each mesh primitive, decodes POSITION / NORMAL /
/// TEXCOORD_0 / indices through their accessors + buffer views, converts glTF's Y-up right-handed space
/// to DS1's Z-up <c>(x,z,-y)</c>, and flips V (<c>v' = 1-v</c>). Skin data (JOINTS/WEIGHTS) is ignored —
/// this is the static path. Materials become subtextures (one per glTF material).</summary>
public static class GltfImporter
{
    private const uint GlbMagic = 0x46546C67; // "glTF"
    private const uint ChunkJson = 0x4E4F534A; // "JSON"
    private const uint ChunkBin = 0x004E4942;  // "BIN\0"

    public static ObjImporter.Result Parse(byte[] fileBytes, string? sourceDir)
    {
        string json;
        byte[]? glbBin = null;
        if (fileBytes.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(fileBytes) == GlbMagic)
            (json, glbBin) = ReadGlb(fileBytes);
        else
            json = Encoding.UTF8.GetString(fileBytes);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var buffers = LoadBuffers(root, glbBin, sourceDir);
        var bufferViews = root.TryGetProperty("bufferViews", out var bv) ? bv : default;
        var accessors = root.TryGetProperty("accessors", out var ac) ? ac : default;
        var meshes = root.TryGetProperty("meshes", out var me) ? me : default;
        var materials = root.TryGetProperty("materials", out var ma) ? ma : default;

        var r = new ObjImporter.Result();
        var matSlot = new Dictionary<int, int>();       // glTF material index → local subtexture slot
        var matNames = new List<string>();

        int LocalMaterial(int gltfMat)
        {
            if (gltfMat < 0) gltfMat = -1;
            if (matSlot.TryGetValue(gltfMat, out var s)) return s;
            s = matNames.Count;
            matSlot[gltfMat] = s;
            matNames.Add(MaterialName(materials, gltfMat, s));
            return s;
        }

        // Walk the default scene so each mesh inherits its node's world transform.
        foreach (var (nodeMesh, world) in EnumerateMeshNodes(root))
        {
            if (meshes.ValueKind != JsonValueKind.Array || nodeMesh >= meshes.GetArrayLength()) continue;
            var mesh = meshes[nodeMesh];
            if (!mesh.TryGetProperty("primitives", out var prims)) continue;
            var normalMat = NormalMatrix(world);

            foreach (var prim in prims.EnumerateArray())
            {
                if (!prim.TryGetProperty("attributes", out var attrs)) continue;
                if (!attrs.TryGetProperty("POSITION", out var posAcc)) continue;

                var positions = ReadVec3Accessor(posAcc.GetInt32(), accessors, bufferViews, buffers);
                var normals = attrs.TryGetProperty("NORMAL", out var nAcc)
                    ? ReadVec3Accessor(nAcc.GetInt32(), accessors, bufferViews, buffers) : null;
                var uvs = attrs.TryGetProperty("TEXCOORD_0", out var tAcc)
                    ? ReadVec2Accessor(tAcc.GetInt32(), accessors, bufferViews, buffers) : null;
                var indices = prim.TryGetProperty("indices", out var iAcc)
                    ? ReadIndexAccessor(iAcc.GetInt32(), accessors, bufferViews, buffers)
                    : SequentialIndices(positions.Length);
                int material = LocalMaterial(prim.TryGetProperty("material", out var mm) ? mm.GetInt32() : -1);

                int baseCorner = r.Corners.Count;
                for (int v = 0; v < positions.Length; v++)
                {
                    var wp = Vector3.Transform(positions[v], world);        // glTF-space world position
                    var zp = new Vector3(wp.X, wp.Z, -wp.Y);               // Y-up → Z-up
                    int vi = r.Positions.Count;
                    r.Positions.Add(zp);

                    Vector3 nrm = Vector3.UnitZ;
                    if (normals is not null && v < normals.Length)
                    {
                        var wn = Vector3.TransformNormal(normals[v], normalMat);
                        nrm = new Vector3(wn.X, wn.Z, -wn.Y);
                        if (nrm.LengthSquared() > 1e-12f) nrm = Vector3.Normalize(nrm);
                    }
                    var uv = uvs is not null && v < uvs.Length ? new Vector2(uvs[v].X, 1f - uvs[v].Y) : Vector2.Zero;
                    r.Corners.Add(AspCorner.White(vi, nrm, uv));
                }

                for (int t = 0; t + 2 < indices.Length; t += 3)
                    r.Faces.Add(new AspFace(baseCorner + indices[t], baseCorner + indices[t + 1], baseCorner + indices[t + 2], material));
            }
        }

        if (matNames.Count == 0) matNames.Add("custom");
        r.TextureNames = matNames;
        return r;
    }

    private static (string Json, byte[]? Bin) ReadGlb(byte[] b)
    {
        // Header: magic, version, length (12 bytes). Then chunks: length u32, type u32, data.
        int pos = 12;
        string json = "";
        byte[]? bin = null;
        while (pos + 8 <= b.Length)
        {
            uint clen = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos));
            uint ctype = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos + 4));
            pos += 8;
            if (pos + clen > b.Length) break;
            if (ctype == ChunkJson) json = Encoding.UTF8.GetString(b, pos, (int)clen);
            else if (ctype == ChunkBin) { bin = new byte[clen]; Array.Copy(b, pos, bin, 0, (int)clen); }
            pos += (int)clen;
            if ((clen & 3) != 0) pos += (int)(4 - (clen & 3)); // chunks are 4-byte aligned
        }
        return (json, bin);
    }

    private static List<byte[]> LoadBuffers(JsonElement root, byte[]? glbBin, string? sourceDir)
    {
        var list = new List<byte[]>();
        if (!root.TryGetProperty("buffers", out var buffers)) return list;
        foreach (var buf in buffers.EnumerateArray())
        {
            if (!buf.TryGetProperty("uri", out var uri))
            {
                list.Add(glbBin ?? Array.Empty<byte>()); // no uri ⇒ the GLB BIN chunk
                continue;
            }
            var u = uri.GetString() ?? "";
            const string dataPrefix = "base64,";
            int idx = u.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? u.IndexOf(dataPrefix, StringComparison.Ordinal) : -1;
            if (idx >= 0)
                list.Add(Convert.FromBase64String(u[(idx + dataPrefix.Length)..]));
            else if (sourceDir is not null)
                list.Add(File.ReadAllBytes(Path.Combine(sourceDir, Uri.UnescapeDataString(u))));
            else
                list.Add(Array.Empty<byte>());
        }
        return list;
    }

    private readonly record struct ViewInfo(byte[] Buffer, int Offset, int Stride, int Length);

    private static ViewInfo Resolve(int accessorIndex, JsonElement accessors, JsonElement bufferViews, List<byte[]> buffers,
        out int count, out int compType, out string type)
    {
        var acc = accessors[accessorIndex];
        count = acc.GetProperty("count").GetInt32();
        compType = acc.GetProperty("componentType").GetInt32();
        type = acc.GetProperty("type").GetString() ?? "SCALAR";
        int viewIdx = acc.TryGetProperty("bufferView", out var bvi) ? bvi.GetInt32() : -1;
        int accOffset = acc.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;
        if (viewIdx < 0) return new ViewInfo(Array.Empty<byte>(), 0, 0, 0);
        var view = bufferViews[viewIdx];
        int bufIdx = view.GetProperty("buffer").GetInt32();
        int viewOffset = view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0;
        int stride = view.TryGetProperty("byteStride", out var bs) ? bs.GetInt32() : 0;
        int len = view.TryGetProperty("byteLength", out var bl) ? bl.GetInt32() : 0;
        return new ViewInfo(buffers[bufIdx], viewOffset + accOffset, stride, len);
    }

    private static Vector3[] ReadVec3Accessor(int idx, JsonElement accessors, JsonElement bufferViews, List<byte[]> buffers)
    {
        var v = Resolve(idx, accessors, bufferViews, buffers, out int count, out _, out _);
        int stride = v.Stride == 0 ? 12 : v.Stride;
        var result = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            int o = v.Offset + i * stride;
            result[i] = new Vector3(
                BinaryPrimitives.ReadSingleLittleEndian(v.Buffer.AsSpan(o)),
                BinaryPrimitives.ReadSingleLittleEndian(v.Buffer.AsSpan(o + 4)),
                BinaryPrimitives.ReadSingleLittleEndian(v.Buffer.AsSpan(o + 8)));
        }
        return result;
    }

    private static Vector2[] ReadVec2Accessor(int idx, JsonElement accessors, JsonElement bufferViews, List<byte[]> buffers)
    {
        var v = Resolve(idx, accessors, bufferViews, buffers, out int count, out _, out _);
        int stride = v.Stride == 0 ? 8 : v.Stride;
        var result = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            int o = v.Offset + i * stride;
            result[i] = new Vector2(
                BinaryPrimitives.ReadSingleLittleEndian(v.Buffer.AsSpan(o)),
                BinaryPrimitives.ReadSingleLittleEndian(v.Buffer.AsSpan(o + 4)));
        }
        return result;
    }

    private static int[] ReadIndexAccessor(int idx, JsonElement accessors, JsonElement bufferViews, List<byte[]> buffers)
    {
        var v = Resolve(idx, accessors, bufferViews, buffers, out int count, out int compType, out _);
        int size = compType switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => 2 };
        int stride = v.Stride == 0 ? size : v.Stride;
        var result = new int[count];
        for (int i = 0; i < count; i++)
        {
            int o = v.Offset + i * stride;
            result[i] = compType switch
            {
                5121 => v.Buffer[o],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(v.Buffer.AsSpan(o)),
                5125 => (int)BinaryPrimitives.ReadUInt32LittleEndian(v.Buffer.AsSpan(o)),
                _ => BinaryPrimitives.ReadUInt16LittleEndian(v.Buffer.AsSpan(o)),
            };
        }
        return result;
    }

    private static int[] SequentialIndices(int n)
    {
        var a = new int[n];
        for (int i = 0; i < n; i++) a[i] = i;
        return a;
    }

    /// <summary>Yields (meshIndex, worldMatrix) for every node that references a mesh, composing the
    /// scene → node transform chain. Falls back to walking all nodes if no scene is declared.</summary>
    private static IEnumerable<(int Mesh, Matrix4x4 World)> EnumerateMeshNodes(JsonElement root)
    {
        if (!root.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
            yield break;

        int[] roots;
        if (root.TryGetProperty("scenes", out var scenes) && scenes.GetArrayLength() > 0)
        {
            int sceneIdx = root.TryGetProperty("scene", out var sc) ? sc.GetInt32() : 0;
            var scene = scenes[Math.Clamp(sceneIdx, 0, scenes.GetArrayLength() - 1)];
            var list = new List<int>();
            if (scene.TryGetProperty("nodes", out var sn)) foreach (var e in sn.EnumerateArray()) list.Add(e.GetInt32());
            roots = list.ToArray();
        }
        else
        {
            roots = SequentialIndices(nodes.GetArrayLength());
        }

        var stack = new Stack<(int Node, Matrix4x4 Parent)>();
        for (int i = roots.Length - 1; i >= 0; i--) stack.Push((roots[i], Matrix4x4.Identity));
        var results = new List<(int, Matrix4x4)>();
        var guard = new HashSet<int>();
        while (stack.Count > 0)
        {
            var (ni, parent) = stack.Pop();
            if (ni < 0 || ni >= nodes.GetArrayLength() || !guard.Add(ni)) continue;
            var node = nodes[ni];
            var local = LocalTransform(node);
            var world = local * parent;
            if (node.TryGetProperty("mesh", out var m)) results.Add((m.GetInt32(), world));
            if (node.TryGetProperty("children", out var ch))
                foreach (var c in ch.EnumerateArray()) stack.Push((c.GetInt32(), world));
        }
        foreach (var x in results) yield return x;
    }

    private static Matrix4x4 LocalTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out var mat) && mat.GetArrayLength() == 16)
        {
            var f = new float[16];
            int i = 0;
            foreach (var e in mat.EnumerateArray()) f[i++] = (float)e.GetDouble();
            return new Matrix4x4(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7],
                                 f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15]);
        }
        var t = ReadVec3(node, "translation", Vector3.Zero);
        var s = ReadVec3(node, "scale", Vector3.One);
        Quaternion q = Quaternion.Identity;
        if (node.TryGetProperty("rotation", out var rot) && rot.GetArrayLength() == 4)
        {
            var v = new float[4]; int i = 0;
            foreach (var e in rot.EnumerateArray()) v[i++] = (float)e.GetDouble();
            q = new Quaternion(v[0], v[1], v[2], v[3]);
        }
        return Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);
    }

    private static Vector3 ReadVec3(JsonElement node, string name, Vector3 fallback)
    {
        if (!node.TryGetProperty(name, out var arr) || arr.GetArrayLength() < 3) return fallback;
        var v = new float[3]; int i = 0;
        foreach (var e in arr.EnumerateArray()) { if (i < 3) v[i] = (float)e.GetDouble(); i++; }
        return new Vector3(v[0], v[1], v[2]);
    }

    private static Matrix4x4 NormalMatrix(Matrix4x4 world)
        => Matrix4x4.Invert(world, out var inv) ? Matrix4x4.Transpose(inv) : world;

    private static string MaterialName(JsonElement materials, int gltfMat, int slot)
    {
        if (gltfMat >= 0 && materials.ValueKind == JsonValueKind.Array && gltfMat < materials.GetArrayLength())
        {
            var m = materials[gltfMat];
            if (m.TryGetProperty("name", out var nm) && !string.IsNullOrWhiteSpace(nm.GetString()))
                return Sanitize(nm.GetString()!);
        }
        return slot == 0 ? "custom" : $"custom_{slot}";
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.ToLowerInvariant())
            sb.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' ? c : '_');
        var t = sb.ToString().Trim('_');
        return t.Length == 0 ? "custom" : t;
    }
}
