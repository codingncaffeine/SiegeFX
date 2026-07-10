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
    /// <summary>ALPHA-2V — Options → Video → Texture Filtering. Mip-mapped
    /// (world) textures register themselves at creation so a filter change
    /// re-applies to every live texture; single-mip UI/cursor textures are
    /// unaffected (no mip chain = nothing for the mode to change).</summary>
    public enum FilterMode { Bilinear, Trilinear, Anisotropic }

    static readonly HashSet<GlTexture> MipTextures = new();
    static FilterMode _filterMode = FilterMode.Trilinear;
    static float _anisoLevel = 8f;
    static float _maxAnisoSupported = -1f; // lazy driver query; 1 = unsupported

    /// <summary>Set the global filter mode and re-apply it to every live
    /// mip-mapped texture. Must run on the GL thread. New textures pick the
    /// mode up at creation. Anisotropic implies trilinear underneath.</summary>
    public static void SetFilterMode(FilterMode mode, float anisoLevel = 8f)
    {
        _filterMode = mode;
        _anisoLevel = Math.Max(1f, anisoLevel);
        foreach (var t in MipTextures)
        {
            t._gl.BindTexture(GLEnum.Texture2D, t.Handle);
            t.ApplyCurrentFilter();
        }
    }

    void ApplyCurrentFilter()
    {
        // Bilinear = nearest-mip picks (DS1's low setting); trilinear blends
        // between mips; anisotropic stacks the EXT sharpening on trilinear.
        var min = _filterMode == FilterMode.Bilinear
            ? GLEnum.LinearMipmapNearest : GLEnum.LinearMipmapLinear;
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)min);
        if (_maxAnisoSupported < 0f)
        {
            try { _gl.GetFloat(GLEnum.MaxTextureMaxAnisotropy, out _maxAnisoSupported); }
            catch { _maxAnisoSupported = 1f; }
            if (_maxAnisoSupported < 1f) _maxAnisoSupported = 1f;
        }
        if (_maxAnisoSupported > 1f)
        {
            float amount = _filterMode == FilterMode.Anisotropic
                ? Math.Min(_anisoLevel, _maxAnisoSupported) : 1f;
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMaxAnisotropy, amount);
        }
    }

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

        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS,     (int)GLEnum.Repeat);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT,     (int)GLEnum.Repeat);
        if (image.SurfaceCount > 1)
        {
            ApplyCurrentFilter();
            MipTextures.Add(this);
        }
        else
        {
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
        }

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

    public void Dispose()
    {
        MipTextures.Remove(this);
        _gl.DeleteTexture(Handle);
    }
}
