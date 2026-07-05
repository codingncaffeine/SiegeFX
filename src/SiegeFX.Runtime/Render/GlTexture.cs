using System.Numerics;
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
    public int Width  { get; }
    public int Height { get; }
    public int MipCount { get; }

    /// <summary>Opaque-content bounding box in VISUAL top-down uv space
    /// (uMin, vMin, uMax, vMax). DS1 portrait icons author the face in a
    /// corner of the raw with transparent padding (and the exact framing
    /// varies per character-creator choice and per companion), so drawing
    /// this sub-rect stretched to a slot fills the box with the face instead
    /// of hugging one edge. Full (0,0,1,1) for large textures (skipped for
    /// cost) and for fully-opaque or fully-transparent images.</summary>
    public Vector4 ContentUv { get; } = new(0f, 0f, 1f, 1f);

    public GlTexture(GL gl, RawImage image)
    {
        if (image.SurfaceCount <= 0)
            throw new ArgumentException("RawImage has no surfaces to upload", nameof(image));
        _gl = gl;
        Handle = _gl.GenTexture();
        Width    = image.GetSurfaceWidth(0);
        Height   = image.GetSurfaceHeight(0);
        MipCount = image.SurfaceCount;
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

        ContentUv = ComputeContentUv(image);
    }

    // Opaque bounding box (alpha over a small threshold) of surface 0, as a
    // VISUAL top-down uv rect. Only small textures are scanned (icons/portraits);
    // larger ones return full to avoid a load-time hitch. The raw's rgba is
    // bottom-up (row 0 = image bottom), so the V bounds are flipped to visual.
    private static Vector4 ComputeContentUv(RawImage image)
    {
        int w = image.GetSurfaceWidth(0), h = image.GetSurfaceHeight(0);
        if (w <= 0 || h <= 0 || (long)w * h > 128 * 128) return new Vector4(0f, 0f, 1f, 1f);
        var rgba = image.GetSurfaceRgba(0);
        if (rgba.Length < w * h * 4) return new Vector4(0f, 0f, 1f, 1f);
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int py = 0; py < h; py++)
        {
            int row = py * w * 4;
            for (int px = 0; px < w; px++)
            {
                if (rgba[row + px * 4 + 3] > 16)
                {
                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;
                }
            }
        }
        if (maxX < minX || maxY < minY) return new Vector4(0f, 0f, 1f, 1f);
        float uMin = minX / (float)w, uMax = (maxX + 1) / (float)w;
        float vMin = 1f - (maxY + 1) / (float)h, vMax = 1f - minY / (float)h;
        return new Vector4(uMin, vMin, uMax, vMax);
    }

    /// <summary>Single-mip RGBA8888 upload. Used by the cursor pipeline for
    /// individual .flm frames (32x32 BGRA→RGBA-swapped buffers from
    /// <see cref="FlmAnimation"/>) where there's no on-disc mip chain to
    /// preserve. Filtering is point-style by default so the cursor reads as
    /// crisp pixel art rather than a smeared blur at native pointer size.</summary>
    public GlTexture(GL gl, byte[] rgba, int width, int height, bool nearestFilter = true)
    {
        if (rgba.Length < width * height * 4)
            throw new ArgumentException("rgba buffer too small for given dimensions", nameof(rgba));
        _gl = gl;
        Handle = _gl.GenTexture();
        Width = width;
        Height = height;
        MipCount = 1;
        _gl.BindTexture(GLEnum.Texture2D, Handle);
        unsafe
        {
            fixed (byte* p = rgba)
                _gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba8,
                    (uint)width, (uint)height, 0, GLEnum.Rgba, GLEnum.UnsignedByte, p);
        }
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureBaseLevel, 0);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMaxLevel, 0);
        var f = nearestFilter ? GLEnum.Nearest : GLEnum.Linear;
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)f);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)f);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.BindTexture(GLEnum.Texture2D, 0);
    }

    public void Bind(TextureUnit unit = TextureUnit.Texture0)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(GLEnum.Texture2D, Handle);
    }

    public void Dispose() => _gl.DeleteTexture(Handle);
}
