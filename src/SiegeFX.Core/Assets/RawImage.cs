using SiegeFX.Core.IO;
using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>
/// Dungeon Siege .raw texture. 16-byte header + BGRA 8:8:8:8 pixels, optionally with
/// a full mipmap chain following surface 0.
/// </summary>
public sealed class RawImage
{
    public int Width  { get; }
    public int Height { get; }
    public int SurfaceCount { get; }
    public ushort Flags { get; }
    public byte[] Pixels { get; }

    /// <summary>Max permitted side length. Real DS1 textures top out at 512; this is a sanity cap to reject fuzzed/corrupt headers before overflow math matters.</summary>
    public const int MaxDimension = 8192;

    // Layout: [surface 0 BGRA pixels][surface 1 BGRA pixels]...[surface N-1]
    // where surface k has dimensions (max(1, W >> k), max(1, H >> k)).

    private static readonly FourCC MagicReversed = new('i', 'p', 'a', 'R'); // 'Rapi' stored little-endian-reversed
    private static readonly FourCC FormatBgra8888 = new('8', '8', '8', '8');

    public const int HeaderSize = 16;
    public const int BytesPerPixel = 4;

    public RawImage(int width, int height, int surfaceCount, ushort flags, byte[] pixels)
    {
        Width = width;
        Height = height;
        SurfaceCount = surfaceCount;
        Flags = flags;
        Pixels = pixels;
    }

    public static RawImage Load(byte[] data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException($"RAW too small: {data.Length} bytes");

        using var ms = new MemoryStream(data, writable: false);
        using var r = new SiegeBinaryReader(ms, leaveOpen: true);

        var magic = r.ReadFourCC();
        var format = r.ReadFourCC();
        if (magic != MagicReversed)
            throw new InvalidDataException($"Not a RAW image (magic='{magic}', expected 'ipaR')");
        if (format != FormatBgra8888)
            throw new InvalidDataException($"Unsupported RAW format '{format}' (expected '8888')");

        var flags        = r.ReadU16();
        var surfaceCount = r.ReadU16();
        var width        = r.ReadU16();
        var height       = r.ReadU16();

        if (surfaceCount == 0)
            throw new InvalidDataException("RAW has zero surfaces");
        if (width == 0 || height == 0)
            throw new InvalidDataException($"RAW has zero dimension: {width}x{height}");
        if (width > MaxDimension || height > MaxDimension)
            throw new InvalidDataException($"RAW dimensions exceed cap: {width}x{height} > {MaxDimension}");

        long totalPixels = 0;
        for (var i = 0; i < surfaceCount; i++)
        {
            var w = Math.Max(1, width >> i);
            var h = Math.Max(1, height >> i);
            totalPixels += (long)w * h;
        }

        var expectedBytes = totalPixels * BytesPerPixel;
        var available = data.Length - HeaderSize;
        if (available < expectedBytes)
            throw new InvalidDataException(
                $"RAW pixel data truncated: need {expectedBytes}, have {available}");
        if (expectedBytes > int.MaxValue)
            throw new InvalidDataException($"RAW pixel buffer too large: {expectedBytes} bytes");

        var pixels = new byte[expectedBytes];
        Buffer.BlockCopy(data, HeaderSize, pixels, 0, (int)expectedBytes);

        return new RawImage(width, height, surfaceCount, flags, pixels);
    }

    public int GetSurfaceWidth(int index)  => Math.Max(1, Width  >> index);
    public int GetSurfaceHeight(int index) => Math.Max(1, Height >> index);

    public int GetSurfaceOffset(int index)
    {
        var offset = 0;
        for (var i = 0; i < index; i++)
            offset += GetSurfaceWidth(i) * GetSurfaceHeight(i) * BytesPerPixel;
        return offset;
    }

    /// <summary>
    /// Returns a freshly allocated RGBA byte buffer for the requested surface.
    /// RAW is stored BGRA; this swizzles to RGBA for the PNG encoder.
    /// </summary>
    public byte[] GetSurfaceRgba(int index)
    {
        var w = GetSurfaceWidth(index);
        var h = GetSurfaceHeight(index);
        var count = w * h;
        var offset = GetSurfaceOffset(index);

        var rgba = new byte[count * 4];
        for (var i = 0; i < count; i++)
        {
            var s = offset + i * 4;
            var d = i * 4;
            rgba[d + 0] = Pixels[s + 2]; // R
            rgba[d + 1] = Pixels[s + 1]; // G
            rgba[d + 2] = Pixels[s + 0]; // B
            rgba[d + 3] = Pixels[s + 3]; // A
        }
        return rgba;
    }
}
