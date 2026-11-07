using System.Numerics;
using Silk.NET.OpenGL;
using SiegeFX.Core.Assets;

namespace SiegeFX.Runtime.Render;

/// <summary>
/// GL-ready skinned mesh built from a rigged <see cref="AspMesh"/>. Same per-corner
/// rest-pose vertex stream as <see cref="StaticMesh"/>, plus two extra attributes:
/// four bone weights (vec4) and four bone indices packed into one byte each. The
/// vertex shader reads <c>uBones[i]</c> matrices uploaded by the host and composes
/// the skinned position on the GPU.
///
/// Hard cap at <see cref="MaxBones"/> uniforms — covers all 2626 shipping ASPs.
/// Throws at construction if the mesh exceeds it so we fail loud rather than
/// silently overflow the uniform array.
/// </summary>
public sealed class SkinnedMesh : IDisposable
{
    /// <summary>Maximum bones per draw, sized to fit a vec4-uniform-array bound on
    /// the conservative GL 3.3 minimum (1024 vec4 components ≈ 64 mat4). Goblin = 35,
    /// largest shipped DS1 actor we've seen sits well under this. Bump if a real
    /// asset trips the throw — the only constraint is GL_MAX_VERTEX_UNIFORM_COMPONENTS.</summary>
    public const int MaxBones = 64;

    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private readonly int _indexCount;
    // A second index buffer in which the triangles on one bone are collapsed
    // to nothing (same layout, so subset offsets still hold); 0 = none built.
    private uint _eboHidden;
    private int _hiddenBone = -1;

    /// <summary>Draw with the triangles marked by <see cref="HideBoneTriangles"/>
    /// left out. The GL mesh is shared by every actor using the model, so a
    /// caller sets this for its own draws and clears it after.</summary>
    public bool HideMarked { get; set; }

    public Vector3 Min { get; }
    public Vector3 Max { get; }
    public Vector3 Center => (Min + Max) * 0.5f;
    public float Radius   => (Max - Min).Length() * 0.5f;

    public SkinnedMesh(GL gl, AspMesh mesh)
    {
        if (!mesh.HasSkin)
            throw new InvalidOperationException("SkinnedMesh requires an AspMesh with WCRN skin data");
        if (mesh.BoneCount > MaxBones)
            throw new InvalidOperationException(
                $"AspMesh has {mesh.BoneCount} bones but SkinnedMesh.MaxBones is {MaxBones}");

        _gl = gl;

        // Per corner: pos(3f) + normal(3f) + uv(2f) + weights(4f) + bones(4×u8 packed as u32)
        // = 12 + 12 + 8 + 16 + 4 = 52 bytes. Keeping the bones as four packed bytes (rather
        // than four floats) lets us hand them straight to glVertexAttribIPointer as ivec4
        // without inflating the buffer.
        const int stride = 52;
        var vertexData = new byte[mesh.Corners.Length * stride];
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);

        for (var i = 0; i < mesh.Corners.Length; i++)
        {
            var c = mesh.Corners[i];
            var p = mesh.Positions[c.VertexIndex];
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);

            var w = mesh.SkinWeights[i];
            var b = mesh.SkinBones[i];

            var span = vertexData.AsSpan(i * stride, stride);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(span,           p.X);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(span.Slice(4),  p.Y);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(span.Slice(8),  p.Z);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(span.Slice(12), c.Normal.X);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(span.Slice(16), c.Normal.Y);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(span.Slice(20), c.Normal.Z);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(span.Slice(24), c.Uv.X);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(span.Slice(28), c.Uv.Y);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(span.Slice(32), w.X);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(span.Slice(36), w.Y);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(span.Slice(40), w.Z);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(span.Slice(44), w.W);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(48), b);
        }

        Min = min;
        Max = max;

        var indices = new uint[mesh.TriangleIndices.Length];
        for (var i = 0; i < indices.Length; i++) indices[i] = (uint)mesh.TriangleIndices[i];
        _indexCount = indices.Length;

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);

        _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (byte* p = vertexData)
                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)vertexData.Length, p, GLEnum.StaticDraw);

            // 0: pos, 1: normal, 2: uv, 3: weights, 4: bones (integer attribute)
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, stride, (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 3, GLEnum.Float, false, stride, (void*)12);
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(2, 2, GLEnum.Float, false, stride, (void*)24);
            _gl.EnableVertexAttribArray(3);
            _gl.VertexAttribPointer(3, 4, GLEnum.Float, false, stride, (void*)32);
            _gl.EnableVertexAttribArray(4);
            // Integer attribute path — unsigned-byte data fed straight to a uvec4 in the
            // shader. Going through glVertexAttribPointer(normalize=false) instead would
            // either silently float-cast (undefined for shader uvec4) or sample 0..1.
            _gl.VertexAttribIPointer(4, 4, GLEnum.UnsignedByte, stride, (void*)48);
        }

        _gl.BindBuffer(GLEnum.ElementArrayBuffer, _ebo);
        unsafe
        {
            fixed (uint* p = indices)
                _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), p, GLEnum.StaticDraw);
        }

        _gl.BindVertexArray(0);
        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, 0);
    }

    /// <summary>Mark the triangles that hang on <paramref name="boneIndex"/> or
    /// a bone below it (at least two corners weighted mostly to them) for
    /// <see cref="HideMarked"/>: the hero's own head with anything hung off it
    /// (the farm girl's ponytail), left out while a separate head mesh is
    /// drawn in its place. Built once per mesh and bone.</summary>
    public void HideBoneTriangles(AspMesh mesh, int boneIndex)
    {
        if (boneIndex < 0 || _hiddenBone == boneIndex) return;
        var hidden = BoneAndBelow(mesh, boneIndex);
        var indices = new uint[mesh.TriangleIndices.Length];
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            int onBone = 0;
            for (int k = 0; k < 3; k++)
            {
                int b = PrimaryBone(mesh, mesh.TriangleIndices[t + k]);
                if (b >= 0 && b < hidden.Length && hidden[b]) onBone++;
            }
            for (int k = 0; k < 3; k++)
                indices[t + k] = onBone >= 2 ? (uint)mesh.TriangleIndices[t] : (uint)mesh.TriangleIndices[t + k];
        }
        if (_eboHidden == 0) _eboHidden = _gl.GenBuffer();
        _gl.BindVertexArray(0);
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, _eboHidden);
        unsafe
        {
            fixed (uint* p = indices)
                _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), p, GLEnum.StaticDraw);
        }
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, 0);
        _hiddenBone = boneIndex;
    }

    /// <summary>A bone and every bone parented below it, as flags by index.</summary>
    public static bool[] BoneAndBelow(AspMesh mesh, int boneIndex)
    {
        var set = new bool[Math.Max(mesh.BoneCount, boneIndex + 1)];
        set[boneIndex] = true;
        var parents = mesh.BoneParents;
        for (bool grew = true; grew;)
        {
            grew = false;
            for (int i = 0; i < parents.Length && i < set.Length; i++)
                if (!set[i] && parents[i] >= 0 && parents[i] < set.Length && set[parents[i]])
                    set[i] = grew = true;
        }
        return set;
    }

    /// <summary>The bone a corner is weighted to most.</summary>
    public static int PrimaryBone(AspMesh mesh, int corner)
    {
        if (!mesh.HasSkin || corner >= mesh.SkinWeights.Length) return 0;
        var w = mesh.SkinWeights[corner];
        uint b = mesh.SkinBones[corner];
        int primary = (int)(b & 0xFF);
        float pw = w.X;
        if (w.Y > pw) { primary = (int)((b >> 8) & 0xFF); pw = w.Y; }
        if (w.Z > pw) { primary = (int)((b >> 16) & 0xFF); pw = w.Z; }
        if (w.W > pw) { primary = (int)((b >> 24) & 0xFF); }
        return primary;
    }

    // Swap the VAO's element buffer for the hidden-triangle one while drawing.
    private bool BeginHidden()
    {
        if (!HideMarked || _eboHidden == 0) return false;
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, _eboHidden);
        return true;
    }

    private void EndHidden(bool swapped)
    {
        if (swapped) _gl.BindBuffer(GLEnum.ElementArrayBuffer, _ebo);
    }

    public void Draw()
    {
        _gl.BindVertexArray(_vao);
        bool swapped = BeginHidden();
        unsafe { _gl.DrawElements(GLEnum.Triangles, (uint)_indexCount, GLEnum.UnsignedInt, (void*)0); }
        EndHidden(swapped);
        _gl.BindVertexArray(0);
    }

    /// <summary>Phase 21d-2a-ii — draw a contiguous span of triangles out of the
    /// flattened index buffer. Used by the actor renderer to issue one draw per
    /// ASP subset (BSMM-defined (textureIndex, faceSpan) records) so multi-texture
    /// characters like farmboy bind the right .raw per submesh instead of letting
    /// the first-bound texture leak across the clothing strip.</summary>
    public void DrawSubset(int firstTriangle, int triangleCount)
    {
        if (triangleCount <= 0) return;
        _gl.BindVertexArray(_vao);
        bool swapped = BeginHidden();
        unsafe
        {
            var byteOffset = (nint)(firstTriangle * 3 * sizeof(uint));
            _gl.DrawElements(GLEnum.Triangles, (uint)(triangleCount * 3), GLEnum.UnsignedInt, (void*)byteOffset);
        }
        EndHidden(swapped);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        if (_eboHidden != 0) _gl.DeleteBuffer(_eboHidden);
        _gl.DeleteVertexArray(_vao);
    }
}
