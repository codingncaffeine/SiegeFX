using System.Numerics;
using Silk.NET.OpenGL;
using SiegeFX.Core.Assets;

namespace SiegeFX.Runtime.Render;

/// <summary>
/// GL-ready Siege Node. The SNO format already gives us a flat corner pool
/// (pos + normal + uv per vertex) plus N material subsets whose triangles
/// reference that pool via local indices offset by <see cref="SnoModel.Surface.StartCorner"/>.
/// We upload one shared VBO for all corners and one EBO per surface so each
/// surface can be drawn with its own texture later — Phase 4d-2 still renders
/// untextured via the mesh shader's uHasTexture=0 fallback.
/// </summary>
public sealed class SnoMesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly Subset[] _subsets;

    public Vector3 Min { get; }
    public Vector3 Max { get; }
    public Vector3 Center => (Min + Max) * 0.5f;
    public float Radius   => (Max - Min).Length() * 0.5f;

    public IReadOnlyList<Subset> Subsets => _subsets;

    public sealed class Subset
    {
        public required string TextureName { get; init; }
        public required uint Ebo { get; init; }
        public required int IndexCount { get; init; }
    }

    public SnoMesh(GL gl, SnoModel model)
    {
        _gl = gl;

        // One flat interleaved vertex buffer: pos3 + normal3 + uv2 = 8 floats.
        var corners = model.Corners;
        var vertexData = new float[corners.Length * 8];
        for (var i = 0; i < corners.Length; i++)
        {
            var c = corners[i];
            var o = i * 8;
            vertexData[o + 0] = c.Position.X;
            vertexData[o + 1] = c.Position.Y;
            vertexData[o + 2] = c.Position.Z;
            vertexData[o + 3] = c.Normal.X;
            vertexData[o + 4] = c.Normal.Y;
            vertexData[o + 5] = c.Normal.Z;
            vertexData[o + 6] = c.Uv.X;
            vertexData[o + 7] = c.Uv.Y;
        }

        Min = model.MinBounds;
        Max = model.MaxBounds;

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
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

        // Unbind the VAO before creating per-surface EBOs. Binding GL_ELEMENT_ARRAY_BUFFER
        // while a VAO is bound mutates that VAO's remembered element buffer — if we left
        // the VAO bound here, it would "remember" whichever subset we uploaded last.
        // DrawSubset binds the right EBO per call; the VAO just owns the vertex stream.
        _gl.BindVertexArray(0);

        // SNO triangle indices are u16 LOCAL to the surface's StartCorner span, so resolve
        // to global indices here. Keeps the draw call a plain DrawElements rather than
        // DrawElementsBaseVertex.
        _subsets = new Subset[model.Surfaces.Length];
        for (var si = 0; si < model.Surfaces.Length; si++)
        {
            var surface = model.Surfaces[si];
            var indices = new uint[surface.TriangleIndices.Length];
            for (var i = 0; i < indices.Length; i++)
                indices[i] = (uint)(surface.TriangleIndices[i] + surface.StartCorner);

            var ebo = _gl.GenBuffer();
            _gl.BindBuffer(GLEnum.ElementArrayBuffer, ebo);
            unsafe
            {
                fixed (uint* p = indices)
                    _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), p, GLEnum.StaticDraw);
            }

            _subsets[si] = new Subset
            {
                TextureName = surface.TextureName,
                Ebo = ebo,
                IndexCount = indices.Length,
            };
        }

        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, 0);
    }

    /// <summary>Draws a single subset. Caller is responsible for binding the right
    /// texture (or none) and any per-subset uniforms before calling.</summary>
    public void DrawSubset(int index)
    {
        var s = _subsets[index];
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, s.Ebo);
        unsafe { _gl.DrawElements(GLEnum.Triangles, (uint)s.IndexCount, GLEnum.UnsignedInt, (void*)0); }
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        foreach (var s in _subsets) _gl.DeleteBuffer(s.Ebo);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
