using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SiegeSmith.Services;

/// <summary>GAME-2 — writes Dungeon Siege <c>.raw</c> textures ('Rapi'/'8888'):
/// 16-byte header + BGRA 8888 surfaces stored bottom-up, with a full box-filtered
/// mip chain down to 1×1. Input decodes through WPF (PNG/JPG/BMP/anything WIC
/// reads), is scaled to the nearest power-of-two capped at 512 (the DS1 ceiling),
/// and the output round-trips through <see cref="SiegeFX.Core.Assets.RawImage"/>
/// before it's declared good — the same engine reader that will load it in-game.</summary>
public static class RawWriter
{
    public const int MaxSide = 512;

    /// <summary>Convert a decoded bitmap to .raw bytes (with mips).</summary>
    public static byte[] Write(BitmapSource source)
    {
        // Nearest power-of-two per axis, clamped to the DS1 ceiling.
        int w = NearestPot(source.PixelWidth);
        int h = NearestPot(source.PixelHeight);

        BitmapSource frame = source;
        if (frame.Format != PixelFormats.Bgra32)
            frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        if (w != frame.PixelWidth || h != frame.PixelHeight)
        {
            frame = new TransformedBitmap(frame,
                new ScaleTransform(w / (double)frame.PixelWidth, h / (double)frame.PixelHeight));
            if (frame.Format != PixelFormats.Bgra32)
                frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        }

        var top = new byte[w * h * 4]; // top-down BGRA from WPF
        frame.CopyPixels(top, w * 4, 0);

        // Surface 0, bottom-up (row 0 = image bottom — the DS1 convention).
        var s0 = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(top, (h - 1 - y) * w * 4, s0, y * w * 4, w * 4);

        int surfaces = 1 + (int)Math.Floor(Math.Log2(Math.Max(w, h)));
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((byte)'i'); bw.Write((byte)'p'); bw.Write((byte)'a'); bw.Write((byte)'R');
        bw.Write((byte)'8'); bw.Write((byte)'8'); bw.Write((byte)'8'); bw.Write((byte)'8');
        bw.Write((ushort)0);          // flags
        bw.Write((ushort)surfaces);
        bw.Write((ushort)w);
        bw.Write((ushort)h);

        var cur = s0;
        int cw = w, ch = h;
        bw.Write(cur);
        for (int level = 1; level < surfaces; level++)
        {
            int nw = Math.Max(1, w >> level);
            int nh = Math.Max(1, h >> level);
            cur = BoxHalve(cur, cw, ch, nw, nh);
            cw = nw; ch = nh;
            bw.Write(cur);
        }
        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>2×2 box filter (clamped at edges) from (sw,sh) down to (dw,dh).</summary>
    private static byte[] BoxHalve(byte[] src, int sw, int sh, int dw, int dh)
    {
        var dst = new byte[dw * dh * 4];
        for (int y = 0; y < dh; y++)
        {
            int sy0 = Math.Min(sh - 1, y * 2);
            int sy1 = Math.Min(sh - 1, y * 2 + 1);
            for (int x = 0; x < dw; x++)
            {
                int sx0 = Math.Min(sw - 1, x * 2);
                int sx1 = Math.Min(sw - 1, x * 2 + 1);
                for (int c = 0; c < 4; c++)
                {
                    int sum = src[(sy0 * sw + sx0) * 4 + c] + src[(sy0 * sw + sx1) * 4 + c]
                            + src[(sy1 * sw + sx0) * 4 + c] + src[(sy1 * sw + sx1) * 4 + c];
                    dst[(y * dw + x) * 4 + c] = (byte)(sum / 4);
                }
            }
        }
        return dst;
    }

    private static int NearestPot(int v)
    {
        v = Math.Clamp(v, 1, MaxSide);
        int lower = 1;
        while (lower * 2 <= v) lower *= 2;
        int upper = Math.Min(MaxSide, lower * 2);
        return (v - lower) <= (upper - v) ? lower : upper;
    }
}
