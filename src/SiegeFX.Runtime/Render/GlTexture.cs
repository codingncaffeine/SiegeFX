using Silk.NET.OpenGL;
using SiegeFX.Core.Assets;

namespace SiegeFX.Runtime.Render;

/// <summary>
/// GL 2D texture uploaded from a <see cref="RawImage"/>. Each .raw surface is an
/// already-generated mip level, so we upload them directly rather than calling
/// glGenerateMipmap; this matches what the original game shipped on disc.
/// </summary>
public sealed class GlTexture : IDisposable
{
    private readonly GL _gl;
    public uint Handle { get; }

    public GlTexture(GL gl, RawImage image)
    {
        _gl = gl;
        Handle = _gl.GenTexture();
        _gl.BindTexture(GLEnum.Texture2D, Handle);

        for (var level = 0; level < image.SurfaceCount; level++)
        {
            var w = image.GetSurfaceWidth(level);
            var h = image.GetSurfaceHeight(level);
            var rgba = image.GetSurfaceRgba(level);
            unsafe
            {
                fixed (byte* p = rgba)
                    _gl.TexImage2D(GLEnum.Texture2D, level, (int)GLEnum.Rgba8,
                        (uint)w, (uint)h, 0, GLEnum.Rgba, GLEnum.UnsignedByte, p);
            }
        }

        // Cap the mip pyramid explicitly — GL would otherwise expect a full chain down to 1x1
        // and sample an undefined level if the .raw stopped short.
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureBaseLevel, 0);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMaxLevel, image.SurfaceCount - 1);

        var minFilter = image.SurfaceCount > 1 ? GLEnum.LinearMipmapLinear : GLEnum.Linear;
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)minFilter);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS,     (int)GLEnum.Repeat);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT,     (int)GLEnum.Repeat);

        _gl.BindTexture(GLEnum.Texture2D, 0);
    }

    public void Bind(TextureUnit unit = TextureUnit.Texture0)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(GLEnum.Texture2D, Handle);
    }

    public void Dispose() => _gl.DeleteTexture(Handle);
}
