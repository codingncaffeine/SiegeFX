using System.Numerics;
using Silk.NET.OpenGL;
using SiegeFX.Core.Assets;

namespace SiegeFX.Runtime.Render;

/// <summary>
/// GL-ready static mesh built from an AspMesh. The per-corner data (position resolved
/// via vertex index, plus normal and UV) becomes the vertex stream; BTRI indices become
/// the element buffer. No bones applied — Phase 7 adds skinning on top.
/// </summary>
public sealed class StaticMesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private readonly int _indexCount;

    public Vector3 Min { get; }
    public Vector3 Max { get; }
    public Vector3 Center => (Min + Max) * 0.5f;
    public float Radius   => (Max - Min).Length() * 0.5f;

    public StaticMesh(GL gl, AspMesh mesh)
    {
        _gl = gl;

        // Interleaved per-corner vertex: position(3) + normal(3) + uv(2) = 8 floats.
        var vertexData = new float[mesh.Corners.Length * 8];
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);

        for (var i = 0; i < mesh.Corners.Length; i++)
        {
            var c = mesh.Corners[i];
            var p = mesh.Positions[c.VertexIndex];
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);

            var o = i * 8;
            vertexData[o + 0] = p.X;
            vertexData[o + 1] = p.Y;
            vertexData[o + 2] = p.Z;
            vertexData[o + 3] = c.Normal.X;
            vertexData[o + 4] = c.Normal.Y;
            vertexData[o + 5] = c.Normal.Z;
            vertexData[o + 6] = c.Uv.X;
            vertexData[o + 7] = c.Uv.Y;
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
            fixed (float* p = vertexData)
                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(vertexData.Length * sizeof(float)), p, GLEnum.StaticDraw);

            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, 8 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 3, GLEnum.Float, false, 8 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(2, 2, GLEnum.Float, false, 8 * sizeof(float), (void*)(6 * sizeof(float)));
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

    public void Draw()
    {
        _gl.BindVertexArray(_vao);
        unsafe { _gl.DrawElements(GLEnum.Triangles, (uint)_indexCount, GLEnum.UnsignedInt, (void*)0); }
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteVertexArray(_vao);
    }
}
