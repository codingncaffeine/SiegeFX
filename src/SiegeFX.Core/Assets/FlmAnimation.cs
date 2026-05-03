namespace SiegeFX.Core.Assets;

/// <summary>DS1 cursor "film strip" (.flm) decoder. Used for the animated
/// hammer-on-breakable (b_gui_c_smash1.flm) and hand-on-usable
/// (b_gui_c_grab1.flm) cursors referenced from
/// /ui/interfaces/cursors/cursors.gas via <c>loadanimatedtexture(...)</c>.
///
/// Format (deduced from sizing):
/// <list type="bullet">
///   <item>2048-byte zero-padded header (descriptor / palette slot).</item>
///   <item>N frames of 32x32 BGRA8888 = 4096 bytes each, written in
///         scanline order.</item>
/// </list>
/// Validated against the two shipped .flms: smash1 = 2048 + 21*4096 =
/// 88064, grab1 = 2048 + 30*4096 = 124928.</summary>
public static class FlmAnimation
{
    public const int FrameSize = 32;
    const int HeaderBytes = 2048;
    const int FrameBytes = FrameSize * FrameSize * 4;

    /// <summary>Returns one RGBA8888 buffer per frame in the bottom-up row
    /// order DS1 RAW textures use (matches the convention IconRenderer's
    /// fragment shader V-flips for, so .flm and .raw share one display path).
    /// Empty array if the file is shorter than the header or doesn't carry
    /// at least one full frame.</summary>
    public static byte[][] LoadFrames(byte[] bytes)
    {
        if (bytes.Length < HeaderBytes + FrameBytes) return Array.Empty<byte[]>();
        int frameCount = (bytes.Length - HeaderBytes) / FrameBytes;
        if (frameCount <= 0) return Array.Empty<byte[]>();

        const int rowBytes = FrameSize * 4;
        var frames = new byte[frameCount][];
        for (int i = 0; i < frameCount; i++)
        {
            var buf = new byte[FrameBytes];
            // Reverse row order while copying so the buffer ends up in the
            // bottom-up convention RawImage produces (DS1 RAW shipped in
            // D3D's V=0-at-top → our shader V-flips on sample). Without this
            // the .flm hammer/hand cursors render upside-down vs the static
            // .raw cursors (sword / red sword / talk) that share IconRenderer.
            int srcBase = HeaderBytes + i * FrameBytes;
            for (int row = 0; row < FrameSize; row++)
            {
                int src = srcBase + row * rowBytes;
                int dst = (FrameSize - 1 - row) * rowBytes;
                Buffer.BlockCopy(bytes, src, buf, dst, rowBytes);
            }
            // BGRA on disk → RGBA in memory so the GlTexture path treats it
            // the same as RawImage.GetSurfaceRgba (single source of truth for
            // GL upload format).
            for (int p = 0; p < FrameBytes; p += 4)
            {
                (buf[p], buf[p + 2]) = (buf[p + 2], buf[p]);
            }
            frames[i] = buf;
        }
        return frames;
    }
}
