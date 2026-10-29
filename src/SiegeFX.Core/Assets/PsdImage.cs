using System.Buffers.Binary;

namespace SiegeFX.Core.Assets;

/// <summary>
/// Photoshop (.psd) images in DS1 texture slots. DS1 sniffs a texture's bytes
/// rather than trusting the .raw name, and mods ship PSDs under .raw names: the
/// Czech translation's fonts and menu art are 8-bit RGB/RGBA PSDs. Only the
/// flattened composite is read (raw or PackBits), which is all DS1 used.
/// </summary>
public static class PsdImage
{
    public static bool IsPsd(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[0] == '8' && data[1] == 'B' && data[2] == 'P' && data[3] == 'S';

    /// <summary>Decodes the composite into a single-surface <see cref="RawImage"/>:
    /// BGRA, rows bottom-up like a .raw, alpha 255 when the PSD has none.</summary>
    public static RawImage Load(byte[] data)
    {
        var s = (ReadOnlySpan<byte>)data;
        if (s.Length < 26 || !IsPsd(s)) throw new InvalidDataException("Not a PSD image");
        if (BinaryPrimitives.ReadUInt16BigEndian(s[4..]) != 1)
            throw new InvalidDataException("Unsupported PSD version (PSB?)");
        int channels = BinaryPrimitives.ReadUInt16BigEndian(s[12..]);
        long height  = BinaryPrimitives.ReadUInt32BigEndian(s[14..]);
        long width   = BinaryPrimitives.ReadUInt32BigEndian(s[18..]);
        int depth    = BinaryPrimitives.ReadUInt16BigEndian(s[22..]);
        int mode     = BinaryPrimitives.ReadUInt16BigEndian(s[24..]);
        if (depth != 8 || mode != 3 || channels < 3)
            throw new InvalidDataException($"Unsupported PSD: {channels} channel(s), {depth}-bit, mode {mode} (need 8-bit RGB)");
        if (width == 0 || height == 0 || width > RawImage.MaxDimension || height > RawImage.MaxDimension)
            throw new InvalidDataException($"PSD dimensions out of range: {width}x{height}");
        int w = (int)width, h = (int)height;

        // Colour mode data, image resources, layer & mask info: each length-prefixed.
        long pos = 26;
        for (int section = 0; section < 3; section++)
        {
            if (pos + 4 > s.Length) throw new InvalidDataException("PSD truncated");
            pos += 4 + BinaryPrimitives.ReadUInt32BigEndian(s[(int)pos..]);
        }
        if (pos + 2 > s.Length) throw new InvalidDataException("PSD truncated");
        int compression = BinaryPrimitives.ReadUInt16BigEndian(s[(int)pos..]);
        pos += 2;

        // Planar channels; the composite keeps R, G, B and the first extra channel (alpha).
        int kept = Math.Min(channels, 4);
        var planes = new byte[kept][];
        for (int c = 0; c < kept; c++) planes[c] = new byte[w * h];

        if (compression == 0)
        {
            for (int c = 0; c < kept; c++)
            {
                if (pos + (long)w * h > s.Length) throw new InvalidDataException("PSD pixel data truncated");
                s.Slice((int)pos, w * h).CopyTo(planes[c]);
                pos += (long)w * h;
            }
        }
        else if (compression == 1)
        {
            // A byte count per row of every channel, then the PackBits rows.
            int rows = channels * h;
            if (pos + 2L * rows > s.Length) throw new InvalidDataException("PSD row table truncated");
            var counts = new int[rows];
            for (int i = 0; i < rows; i++)
                counts[i] = BinaryPrimitives.ReadUInt16BigEndian(s[(int)(pos + 2L * i)..]);
            pos += 2L * rows;
            for (int c = 0; c < kept; c++)
                for (int y = 0; y < h; y++)
                {
                    int n = counts[c * h + y];
                    if (pos + n > s.Length) throw new InvalidDataException("PSD pixel data truncated");
                    UnpackBits(s.Slice((int)pos, n), planes[c].AsSpan(y * w, w));
                    pos += n;
                }
        }
        else throw new InvalidDataException($"Unsupported PSD compression {compression}");

        var pixels = new byte[w * h * RawImage.BytesPerPixel];
        for (int y = 0; y < h; y++)
        {
            int src = y * w;
            int dst = (h - 1 - y) * w * 4;
            for (int x = 0; x < w; x++, src++, dst += 4)
            {
                pixels[dst + 0] = planes[2][src];
                pixels[dst + 1] = planes[1][src];
                pixels[dst + 2] = planes[0][src];
                pixels[dst + 3] = kept > 3 ? planes[3][src] : (byte)255;
            }
        }
        return new RawImage(w, h, 1, 0, pixels);
    }

    static void UnpackBits(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        int i = 0, o = 0;
        while (i < src.Length && o < dst.Length)
        {
            int n = (sbyte)src[i++];
            if (n >= 0)
            {
                int len = Math.Min(n + 1, Math.Min(dst.Length - o, src.Length - i));
                src.Slice(i, len).CopyTo(dst[o..]);
                i += n + 1; o += len;
            }
            else if (n != -128)
            {
                if (i >= src.Length) break;
                int len = Math.Min(1 - n, dst.Length - o);
                dst.Slice(o, len).Fill(src[i++]);
                o += len;
            }
        }
    }
}
