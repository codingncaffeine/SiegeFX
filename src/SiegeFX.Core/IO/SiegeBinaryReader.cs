using System.Text;
using SiegeFX.Core.Tank;

namespace SiegeFX.Core.IO;

/// <summary>
/// BinaryReader wrapper for Dungeon Siege file formats. All tank data is little-endian
/// and uses DWORD-aligned length-prefixed strings (NSTRING / WNSTRING).
/// </summary>
public sealed class SiegeBinaryReader : IDisposable
{
    public Stream BaseStream => _stream;
    private readonly Stream _stream;
    private readonly BinaryReader _reader;
    private readonly bool _leaveOpen;

    public SiegeBinaryReader(Stream stream, bool leaveOpen = false)
    {
        _stream = stream;
        _reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        _leaveOpen = leaveOpen;
    }

    public long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }

    public long Length => _stream.Length;

    public void Seek(long absolute) => _stream.Position = absolute;

    public byte   ReadU8()  => _reader.ReadByte();
    public ushort ReadU16() => _reader.ReadUInt16();
    public uint   ReadU32() => _reader.ReadUInt32();
    public ulong  ReadU64() => _reader.ReadUInt64();
    public int    ReadI32() => _reader.ReadInt32();
    public byte[] ReadBytes(int count) => _reader.ReadBytes(count);

    public FourCC ReadFourCC() => new(ReadU8(), ReadU8(), ReadU8(), ReadU8());

    public ProductVersion ReadProductVersion() => new(ReadU32(), ReadU32(), ReadU32());

    public TankSystemTime ReadSystemTime() => new(
        ReadU16(), ReadU16(), ReadU16(), ReadU16(),
        ReadU16(), ReadU16(), ReadU16(), ReadU16());

    public TankFileTime ReadTankFileTime() => new(ReadU32(), ReadU32());

    public TankGuid ReadTankGuid() => new(ReadU32(), ReadU16(), ReadU16(), ReadU64());

    /// <summary>
    /// Reads a fixed-size wide (UTF-16LE) string buffer, trimming at the first NUL.
    /// </summary>
    public string ReadFixedWideString(int maxChars)
    {
        var bytes = ReadBytes(maxChars * 2);
        var nul = 0;
        while (nul < maxChars && !(bytes[nul * 2] == 0 && bytes[nul * 2 + 1] == 0)) nul++;
        return Encoding.Unicode.GetString(bytes, 0, nul * 2);
    }

    /// <summary>
    /// Reads an NSTRING — 16-bit character-count prefix followed by ASCII bytes, padded
    /// so the total size (including the prefix) is DWORD-aligned.
    /// </summary>
    public string ReadNString()
    {
        var len = ReadU16();
        // NSTRING on-disk: 2-byte prefix + len ASCII bytes; align the total byte count.
        var totalBytes = AlignToDword(2 + len);
        var dataBytes  = totalBytes - 2;
        var bytes = ReadBytes(dataBytes);
        return len == 0 ? string.Empty : Encoding.ASCII.GetString(bytes, 0, len);
    }

    /// <summary>
    /// Reads a WNSTRING — 16-bit character-count prefix followed by UTF-16LE code units,
    /// then DWORD-padded.
    /// </summary>
    public string ReadWideNString()
    {
        var len = ReadU16();
        // WNSTRING on-disk: 2-byte prefix + (len * 2) wide bytes; align the total byte count.
        var totalBytes = AlignToDword(2 + len * 2);
        var dataBytes  = totalBytes - 2;
        var bytes = ReadBytes(dataBytes);
        return len == 0 ? string.Empty : Encoding.Unicode.GetString(bytes, 0, len * 2);
    }

    // Matches glampert/Bilas: ALWAYS add 1–4 bytes so the resulting size sits at the next DWORD
    // boundary, even if it was already aligned (offset = 4 when size % 4 == 0).
    private static int AlignToDword(int size) => size + (4 - (size % 4));

    public void Dispose()
    {
        _reader.Dispose();
        if (!_leaveOpen) _stream.Dispose();
    }
}
