namespace SiegeFX.Core.Assets;

/// <summary>DS1 cursor "film strip" (.flm) decoder. Used for the animated
/// hammer-on-breakable (b_gui_c_smash1.flm) and hand-on-usable
/// (b_gui_c_grab1.flm) cursors referenced from
/// /ui/interfaces/cursors/cursors.gas via <c>loadanimatedtexture(...)</c>.
///
/// Format (deduced by dumping each putative frame to PNG and finding
/// every-third-frame was clean cursor art while the in-between frames
/// were stale debug-overlay-looking pixels):
/// <list type="bullet">
///   <item>Each frame is laid out as a 3-mip pyramid (32x32, 16x16, 8x8
///         BGRA), with each mip padded to a 4096-byte alignment slot —
///         total 12288 bytes per frame on disk. Old D3D loaders
///         pre-allocate mip slots at fixed stride for direct GPU
///         upload; SiegeFX only ever needs mip 0 since the cursor
///         renders at 1:1.</item>
///   <item>Frames pack from offset 0; a 2048-byte zero trailer follows
///         the last frame.</item>
///   <item>Frame N's mip-0 32x32 BGRA8888 sprite lives at
///         <c>N * 12288 .. N * 12288 + 4095</c>; the next 8192 bytes
///         are the 16x16 + 8x8 mips, ignored on read.</item>
/// </list>
/// Validated against the two shipped .flms: smash1 = 7*12288 + 2048 =
/// 88064 bytes (7 frames); grab1 = 10*12288 + 2048 = 124928 bytes
/// (10 frames). Reading at the wrong stride (4096) decoded 21/30
/// "frames" where 2-of-3 were the wasted mip slots, producing the
/// visible "cycling through numbers in a rectangle" effect that
/// triggered this rewrite.</summary>
public static class FlmAnimation
{
    public const int FrameSize = 32;
    const int FrameStride = 12288;
    const int TrailerBytes = 2048;
    const int FrameBytes = FrameSize * FrameSize * 4;

    /// <summary>Returns one RGBA8888 buffer per frame in the bottom-up row
    /// order DS1 RAW textures use (matches the convention IconRenderer's
    /// fragment shader V-flips for, so .flm and .raw share one display path).
    /// Empty array if the file is shorter than the header or doesn't carry
    /// at least one full frame.</summary>
    public static byte[][] LoadFrames(byte[] bytes)
    {
        if (bytes.Length < FrameStride) return Array.Empty<byte[]>();
        // Each disk frame is 12288 bytes = 32x32 mip0 + padded 16x16 mip1 +
        // padded 8x8 mip2. We read mip0 only (cursor renders at 1:1) and
        // skip the rest. Integer division naturally rounds past the 2048-
        // byte trailer at the end of the file.
        int frameCount = bytes.Length / FrameStride;
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
            int srcBase = i * FrameStride;
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
