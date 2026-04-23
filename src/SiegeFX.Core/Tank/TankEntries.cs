namespace SiegeFX.Core.Tank;

public sealed class TankDirEntry
{
    public uint          ParentOffset { get; init; }
    public uint          ChildCount   { get; init; }
    public TankFileTime  FileTime     { get; init; }
    public string        Name         { get; init; } = string.Empty;
    public uint[]        ChildOffsets { get; init; } = Array.Empty<uint>();

    public bool IsRoot => ParentOffset == 0;
}

public sealed class TankFileEntry
{
    public uint            ParentOffset   { get; init; }
    public uint            Size           { get; init; }
    public uint            Offset         { get; init; }
    public uint            Crc32          { get; init; }
    public TankFileTime    FileTime       { get; init; }
    public TankDataFormat  Format         { get; init; }
    public TankFileFlags   Flags          { get; init; }
    public string          Name           { get; init; } = string.Empty;

    // Only populated for compressed entries.
    public TankCompressedFileInfo? Compressed { get; init; }

    public bool IsInvalid    => (Flags & TankFileFlags.Invalid) != 0;
    public bool IsCompressed => Format.IsCompressed();
}

public sealed class TankCompressedFileInfo
{
    public required uint CompressedSize { get; init; }
    public required uint ChunkSize      { get; init; }
    public required uint NumChunks      { get; init; }
    public required TankChunkHeader[] Chunks { get; init; }
}

public readonly record struct TankChunkHeader(
    uint UncompressedSize,
    uint CompressedSize,
    uint ExtraBytes,
    uint Offset)
{
    public bool IsCompressed => UncompressedSize != CompressedSize;
}
