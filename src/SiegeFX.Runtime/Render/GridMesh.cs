using System.Numerics;
using Silk.NET.OpenGL;

namespace SiegeFX.Runtime.Render;

/// <summary>
/// A flat XZ-plane grid drawn with GL_LINES at y=0. Gives the camera something to
/// orient against while the renderer has no real scene geometry yet.
/// </summary>
public sealed class GridMesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly int _vertexCount;

    public GridMesh(GL gl, int halfExtent = 20, float step = 1f)
    {
        _gl = gl;
        var verts = new List<float>();
        var maj = new Vector3(0.60f, 0.60f, 0.60f);
        var axX = new Vector3(0.85f, 0.30f, 0.30f);
        var axZ = new Vector3(0.30f, 0.65f, 0.95f);
        var ext = halfExtent * step;

        for (var i = -halfExtent; i <= halfExtent; i++)
        {
            var p = i * step;
            var color = i == 0 ? axX : maj;
            verts.AddRange(new[] { p, 0f, -ext, color.X, color.Y, color.Z });
            verts.AddRange(new[] { p, 0f,  ext, color.X, color.Y, color.Z });

            color = i == 0 ? axZ : maj;
            verts.AddRange(new[] { -ext, 0f, p, color.X, color.Y, color.Z });
            verts.AddRange(new[] {  ext, 0f, p, color.X, color.Y, color.Z });
        }

        var arr = verts.ToArray();
        _vertexCount = arr.Length / 6;

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);

        unsafe
        {
            fixed (float* p = arr)
                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(arr.Length * sizeof(float)), p, GLEnum.StaticDraw);

            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, 6 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 3, GLEnum.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
        }

        _gl.BindVertexArray(0);
        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
    }

    public void Draw()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(GLEnum.Lines, 0, (uint)_vertexCount);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
