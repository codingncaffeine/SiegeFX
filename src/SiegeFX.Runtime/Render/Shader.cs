using System.Numerics;
using Silk.NET.OpenGL;

namespace SiegeFX.Runtime.Render;

/// <summary>
/// Minimal GL shader program wrapper: compiles vertex + fragment source,
/// links, reports errors, exposes uniform setters commonly used by the renderer.
/// </summary>
public sealed class Shader : IDisposable
{
    private readonly GL _gl;
    public uint Handle { get; }

    public Shader(GL gl, string vertexSource, string fragmentSource)
    {
        _gl = gl;
        var vs = Compile(ShaderType.VertexShader, vertexSource);
        var fs = Compile(ShaderType.FragmentShader, fragmentSource);

        Handle = _gl.CreateProgram();
        _gl.AttachShader(Handle, vs);
        _gl.AttachShader(Handle, fs);
        _gl.LinkProgram(Handle);

        _gl.GetProgram(Handle, GLEnum.LinkStatus, out var linked);
        if (linked == 0)
        {
            var log = _gl.GetProgramInfoLog(Handle);
            throw new InvalidOperationException($"shader link failed: {log}");
        }

        _gl.DetachShader(Handle, vs);
        _gl.DetachShader(Handle, fs);
        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);
    }

    private uint Compile(ShaderType type, string source)
    {
        var handle = _gl.CreateShader(type);
        _gl.ShaderSource(handle, source);
        _gl.CompileShader(handle);

        _gl.GetShader(handle, GLEnum.CompileStatus, out var status);
        if (status == 0)
        {
            var log = _gl.GetShaderInfoLog(handle);
            _gl.DeleteShader(handle);
            throw new InvalidOperationException($"{type} compile failed: {log}");
        }
        return handle;
    }

    public void Use() => _gl.UseProgram(Handle);

    public unsafe void SetMatrix4(string name, Matrix4x4 m)
    {
        var loc = Loc(name);
        if (loc < 0) return;
        _gl.UniformMatrix4(loc, 1, false, (float*)&m);
    }

    /// <summary>Uploads <paramref name="matrices"/> as a contiguous <c>mat4</c> uniform array
    /// starting at <paramref name="name"/>. Matrices ride straight to GL with transpose=false:
    /// <see cref="Matrix4x4"/> stores row-major, GL reads column-major, the swap turns row-vector
    /// math into the equivalent column-vector form the shader uses.</summary>
    public unsafe void SetMatrix4Array(string name, ReadOnlySpan<Matrix4x4> matrices)
    {
        if (matrices.Length == 0) return;
        var loc = Loc(name);
        if (loc < 0) return;
        fixed (Matrix4x4* p = matrices)
            _gl.UniformMatrix4(loc, (uint)matrices.Length, false, (float*)p);
    }

    public void SetVec2(string name, Vector2 v)
    {
        var loc = Loc(name);
        if (loc < 0) return;
        _gl.Uniform2(loc, v.X, v.Y);
    }

    public void SetVec3(string name, Vector3 v)
    {
        var loc = Loc(name);
        if (loc < 0) return;
        _gl.Uniform3(loc, v.X, v.Y, v.Z);
    }

    public void SetVec4(string name, float x, float y, float z, float w)
    {
        var loc = Loc(name);
        if (loc < 0) return;
        _gl.Uniform4(loc, x, y, z, w);
    }

    public void SetInt(string name, int value)
    {
        var loc = Loc(name);
        if (loc < 0) return;
        _gl.Uniform1(loc, value);
    }

    public void SetFloat(string name, float value)
    {
        var loc = Loc(name);
        if (loc < 0) return;
        _gl.Uniform1(loc, value);
    }

    /// <summary>Uploads <paramref name="vectors"/> as a <c>vec3</c> uniform array
    /// starting at <paramref name="name"/>. Used for the per-region directional
    /// light arrays (positions/colors) so the shader can loop over a single
    /// uniform block instead of N+N hand-written setters.</summary>
    public unsafe void SetVec3Array(string name, ReadOnlySpan<Vector3> vectors)
    {
        if (vectors.Length == 0) return;
        var loc = Loc(name);
        if (loc < 0) return;
        fixed (Vector3* p = vectors)
            _gl.Uniform3(loc, (uint)vectors.Length, (float*)p);
    }

    // ALPHA-PERF — uniform locations are immutable after link, but every
    // setter was calling glGetUniformLocation (a string-marshalled driver
    // round-trip) on EVERY set. The world draw path sets a dozen uniforms
    // per draw call across hundreds of draws per frame, so those lookups
    // alone were tens of thousands of driver calls per frame. One-time
    // lookup, cached per shader.
    private readonly Dictionary<string, int> _uniformLocs = new();

    private int Loc(string name)
    {
        if (_uniformLocs.TryGetValue(name, out var loc)) return loc;
        loc = _gl.GetUniformLocation(Handle, name);
        _uniformLocs[name] = loc;
        return loc;
    }

    public void Dispose() => _gl.DeleteProgram(Handle);
}
