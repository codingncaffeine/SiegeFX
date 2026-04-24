using Silk.NET.OpenGL;

namespace SiegeFX.Runtime.Render;

/// <summary>A unit cube centered on the origin, laid out with the same
/// pos3 + normal3 + uv2 vertex stream as <see cref="StaticMesh"/> so it
/// draws cleanly through the mesh shader. Used by Phase 12e to render a
/// placeholder "loot pile" at each dead-actor position — the mesh shader's
/// default untextured tint (warm beige, see MeshFragmentSource) gives the
/// pile a gold look with zero extra shader work.
/// Flat-shaded: each face's four corners share one normal so lighting
/// shows cube faces distinctly instead of smearing across edges.</summary>
public sealed class DebugCubeMesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private readonly int _indexCount;

    public DebugCubeMesh(GL gl)
    {
        _gl = gl;

        // 6 faces × 4 verts = 24 vertices, 6 × 2 triangles × 3 indices = 36 indices.
        // Half-extent 0.5 so the caller scales by the pile diameter directly.
        float[] v = {
            // +X face (normal  1,0,0)
             0.5f,-0.5f,-0.5f,  1,0,0,  0,0,
             0.5f, 0.5f,-0.5f,  1,0,0,  1,0,
             0.5f, 0.5f, 0.5f,  1,0,0,  1,1,
             0.5f,-0.5f, 0.5f,  1,0,0,  0,1,
            // -X face (normal -1,0,0)
            -0.5f,-0.5f, 0.5f, -1,0,0,  0,0,
            -0.5f, 0.5f, 0.5f, -1,0,0,  1,0,
            -0.5f, 0.5f,-0.5f, -1,0,0,  1,1,
            -0.5f,-0.5f,-0.5f, -1,0,0,  0,1,
            // +Y face
            -0.5f, 0.5f,-0.5f,  0,1,0,  0,0,
            -0.5f, 0.5f, 0.5f,  0,1,0,  1,0,
             0.5f, 0.5f, 0.5f,  0,1,0,  1,1,
             0.5f, 0.5f,-0.5f,  0,1,0,  0,1,
            // -Y face
            -0.5f,-0.5f, 0.5f,  0,-1,0, 0,0,
            -0.5f,-0.5f,-0.5f,  0,-1,0, 1,0,
             0.5f,-0.5f,-0.5f,  0,-1,0, 1,1,
             0.5f,-0.5f, 0.5f,  0,-1,0, 0,1,
            // +Z face
             0.5f,-0.5f, 0.5f,  0,0,1,  0,0,
             0.5f, 0.5f, 0.5f,  0,0,1,  1,0,
            -0.5f, 0.5f, 0.5f,  0,0,1,  1,1,
            -0.5f,-0.5f, 0.5f,  0,0,1,  0,1,
            // -Z face
            -0.5f,-0.5f,-0.5f,  0,0,-1, 0,0,
            -0.5f, 0.5f,-0.5f,  0,0,-1, 1,0,
             0.5f, 0.5f,-0.5f,  0,0,-1, 1,1,
             0.5f,-0.5f,-0.5f,  0,0,-1, 0,1,
        };

        uint[] idx = new uint[36];
        for (uint f = 0; f < 6; f++)
        {
            uint b = f * 4;
            idx[f * 6 + 0] = b + 0;
            idx[f * 6 + 1] = b + 1;
            idx[f * 6 + 2] = b + 2;
            idx[f * 6 + 3] = b + 0;
            idx[f * 6 + 4] = b + 2;
            idx[f * 6 + 5] = b + 3;
        }
        _indexCount = idx.Length;

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);

        _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* p = v)
                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(v.Length * sizeof(float)), p, GLEnum.StaticDraw);
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
            fixed (uint* p = idx)
                _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(idx.Length * sizeof(uint)), p, GLEnum.StaticDraw);
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
