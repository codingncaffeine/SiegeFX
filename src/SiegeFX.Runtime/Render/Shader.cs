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
        var loc = _gl.GetUniformLocation(Handle, name);
        if (loc < 0) return;
        _gl.UniformMatrix4(loc, 1, false, (float*)&m);
    }

    public void SetVec3(string name, Vector3 v)
    {
        var loc = _gl.GetUniformLocation(Handle, name);
        if (loc < 0) return;
        _gl.Uniform3(loc, v.X, v.Y, v.Z);
    }

    public void SetInt(string name, int value)
    {
        var loc = _gl.GetUniformLocation(Handle, name);
        if (loc < 0) return;
        _gl.Uniform1(loc, value);
    }

    public void Dispose() => _gl.DeleteProgram(Handle);
}
