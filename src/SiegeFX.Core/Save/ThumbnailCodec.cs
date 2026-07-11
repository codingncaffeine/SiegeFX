using System;
using System.Buffers.Binary;

namespace SiegeFX.Core.Save;

/// <summary>Packs a downscaled RGBA screenshot into <see cref="SaveFile.Thumbnail"/>:
/// <c>[width:int32 LE][height:int32 LE][rgba row-major]</c>. Row order is the
/// caller's convention — the capture path stores rows bottom-up to match the
/// runtime's IconRenderer (which V-flips bottom-up textures for upright display).
/// Deliberately uncompressed (no PNG) so the Load Game window can upload the
/// bytes straight to a GL texture without pulling in an image decoder. A
/// ~96×72 thumb is ~27 KB raw / ~36 KB base64 in the JSON save.</summary>
public static class ThumbnailCodec
{
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
    {
        int need = width * height * 4;
        var buf = new byte[8 + need];
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0), width);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4), height);
        rgba.Slice(0, Math.Min(rgba.Length, need)).CopyTo(buf.AsSpan(8));
        return buf;
    }

    public static bool TryDecode(byte[]? blob, out int width, out int height, out byte[] rgba)
    {
        width = 0; height = 0; rgba = Array.Empty<byte>();
        if (blob is null || blob.Length < 8) return false;
        width  = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(0));
        height = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(4));
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return false;
        int need = width * height * 4;
        if (blob.Length - 8 < need) return false;
        rgba = blob.AsSpan(8, need).ToArray();
        return true;
    }
}
