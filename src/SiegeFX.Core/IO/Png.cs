using System.Buffers.Binary;
using System.IO.Compression;

namespace SiegeFX.Core.IO;

/// <summary>
/// Minimal PNG encoder: 8-bit RGBA, no interlace, filter type 0 (None), zlib-wrapped DEFLATE.
/// Hand-rolled to keep SiegeFX.Core dependency-free.
/// </summary>
public static class Png
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static void EncodeRgba(Stream output, ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException($"Invalid PNG size {width}x{height}");
        if (rgba.Length != width * height * 4)
            throw new ArgumentException($"PNG pixel buffer size mismatch: have {rgba.Length}, expected {width * height * 4}");

        output.Write(Signature);

        // IHDR
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), (uint)height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type: truecolor + alpha
        ihdr[10] = 0; // compression: DEFLATE
        ihdr[11] = 0; // filter: adaptive (type byte per scanline)
        ihdr[12] = 0; // interlace: none
        WriteChunk(output, "IHDR", ihdr);

        // IDAT — filtered scanlines (filter byte 0 + row bytes), zlib-compressed
        var filtered = new byte[(width * 4 + 1) * height];
        var dst = 0;
        var src = 0;
        var stride = width * 4;
        for (var y = 0; y < height; y++)
        {
            filtered[dst++] = 0; // filter = None
            rgba.Slice(src, stride).CopyTo(filtered.AsSpan(dst, stride));
            dst += stride;
            src += stride;
        }

        var compressed = ZlibCompress(filtered);
        WriteChunk(output, "IDAT", compressed);

        // IEND
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        if (type.Length != 4) throw new ArgumentException("PNG chunk type must be 4 chars");

        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        output.Write(len);

        Span<byte> typeBytes = stackalloc byte[4];
        for (var i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
        output.Write(typeBytes);

        if (!data.IsEmpty) output.Write(data);

        var crc = Crc32.Compute(typeBytes);
        crc = Crc32.Append(crc, data);

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static byte[] ZlibCompress(byte[] input)
    {
        using var ms = new MemoryStream();
        // zlib header: deflate, 32K window, default compression, no dict
        ms.WriteByte(0x78);
        ms.WriteByte(0x9C);

        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(input, 0, input.Length);
        }

        // Adler-32 of the uncompressed data, big-endian
        var adler = Adler32.Compute(input);
        Span<byte> a = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(a, adler);
        ms.Write(a);

        return ms.ToArray();
    }
}

// Phase 23e — public: TankWriter stamps per-resource CRC-32s with this.
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint Compute(ReadOnlySpan<byte> data) => Append(0, data);

    public static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        var c = crc ^ 0xFFFFFFFFu;
        for (var i = 0; i < data.Length; i++)
            c = Table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}

internal static class Adler32
{
    private const uint Mod = 65521;

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint a = 1, b = 0;
        for (var i = 0; i < data.Length; i++)
        {
            a = (a + data[i]) % Mod;
            b = (b + a) % Mod;
        }
        return (b << 16) | a;
    }
}
