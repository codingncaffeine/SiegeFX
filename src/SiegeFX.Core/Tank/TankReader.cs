using System.IO.Compression;

namespace SiegeFX.Core.Tank;

/// <summary>
/// Indexes a TankFile and exposes directory/file listing and resource extraction.
/// Paths in the returned tables use '/' as separator and always start with '/'.
/// </summary>
public sealed class TankReader
{
    private readonly TankFile _tank;
    private TankDirEntry[]  _dirs  = Array.Empty<TankDirEntry>();
    private uint[]          _dirOffsets = Array.Empty<uint>();
    private TankFileEntry[] _files = Array.Empty<TankFileEntry>();
    private readonly Dictionary<string, Entry> _table = new(StringComparer.OrdinalIgnoreCase);

    private const char Sep = '/';

    public IReadOnlyList<TankDirEntry>  Dirs  => _dirs;
    public IReadOnlyList<TankFileEntry> Files => _files;

    public int DirCount  => _dirs.Length;
    public int FileCount => _files.Length;

    public TankReader(TankFile tank)
    {
        _tank = tank;
        ReadDirSet();
        ReadFileSet();
        BuildDirPaths();
        BuildFilePaths();
    }

    public IEnumerable<string> ListFiles() =>
        _table.Where(kv => kv.Value.Type == EntryType.File).Select(kv => kv.Key);

    public IEnumerable<string> ListDirectories() =>
        _table.Where(kv => kv.Value.Type == EntryType.Dir).Select(kv => kv.Key);

    public bool TryGetFile(string path, out TankFileEntry entry)
    {
        if (_table.TryGetValue(path, out var e) && e.Type == EntryType.File)
        {
            entry = e.File!;
            return true;
        }
        entry = null!;
        return false;
    }

    public byte[] ExtractToMemory(string path)
    {
        if (!TryGetFile(path, out var entry))
            throw new TankException($"Resource \"{path}\" not found in {_tank.Path}");

        return ExtractEntry(entry);
    }

    public byte[] ExtractEntry(TankFileEntry entry)
    {
        if (entry.IsInvalid || entry.Size == 0) return Array.Empty<byte>();

        var header = _tank.Header;
        var reader = _tank._reader;

        if (!entry.IsCompressed)
        {
            reader.Seek(header.DataOffset + entry.Offset);
            return reader.ReadBytes((int)entry.Size);
        }

        if (entry.Format == TankDataFormat.Lzo)
            throw new TankException("LZO-compressed tank entries are not yet supported");

        var info = entry.Compressed
            ?? throw new TankException($"Compressed entry {entry.Name} has no compression info");

        var output = new byte[entry.Size];
        var outPos = 0;

        for (var c = 0; c < info.NumChunks; c++)
        {
            var chunk = info.Chunks[c];
            reader.Seek(header.DataOffset + entry.Offset + chunk.Offset);

            if (chunk.IsCompressed)
            {
                var compressed = reader.ReadBytes((int)(chunk.CompressedSize + chunk.ExtraBytes));
                using var ms = new MemoryStream(compressed, 0, (int)chunk.CompressedSize);
                using var zlib = new ZLibStream(ms, CompressionMode.Decompress);

                var written = 0;
                while (written < chunk.UncompressedSize)
                {
                    var n = zlib.Read(output, outPos + written, (int)(chunk.UncompressedSize - written));
                    if (n == 0) break;
                    written += n;
                }
                outPos += written;

                // extraBytes tail is stored uncompressed and appended verbatim
                if (chunk.ExtraBytes != 0)
                {
                    Buffer.BlockCopy(compressed, (int)chunk.CompressedSize,
                                     output, outPos, (int)chunk.ExtraBytes);
                    outPos += (int)chunk.ExtraBytes;
                }
            }
            else
            {
                reader.ReadBytes((int)chunk.UncompressedSize).CopyTo(output, outPos);
                outPos += (int)chunk.UncompressedSize;
            }
        }

        return output;
    }

    public void ExtractToFile(string path, string destPath)
    {
        var bytes = ExtractToMemory(path);
        var dir = System.IO.Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(destPath, bytes);
    }

    private void ReadDirSet()
    {
        var r = _tank._reader;
        r.Seek(_tank.Header.DirSetOffset);

        var numDirs = r.ReadU32();
        _dirs       = new TankDirEntry[numDirs];
        _dirOffsets = new uint[numDirs];

        for (var d = 0; d < numDirs; d++)
            _dirOffsets[d] = r.ReadU32();

        for (var d = 0; d < numDirs; d++)
        {
            r.Seek(_tank.Header.DirSetOffset + _dirOffsets[d]);

            var parentOffset = r.ReadU32();
            var childCount   = r.ReadU32();
            var fileTime     = r.ReadTankFileTime();
            var name         = r.ReadNString();

            // Dummy root entry has parentOffset == 0 and empty name.
            if (parentOffset == 0 && name.Length == 0) name = "/";

            var children = new uint[childCount];
            for (var c = 0; c < childCount; c++) children[c] = r.ReadU32();

            _dirs[d] = new TankDirEntry
            {
                ParentOffset = parentOffset,
                ChildCount   = childCount,
                FileTime     = fileTime,
                Name         = name,
                ChildOffsets = children,
            };
        }
    }

    private void ReadFileSet()
    {
        var r = _tank._reader;
        r.Seek(_tank.Header.FileSetOffset);

        var numFiles = r.ReadU32();
        _files = new TankFileEntry[numFiles];
        var offsets = new uint[numFiles];

        for (var f = 0; f < numFiles; f++)
            offsets[f] = r.ReadU32();

        for (var f = 0; f < numFiles; f++)
        {
            r.Seek(_tank.Header.FileSetOffset + offsets[f]);

            var parentOffset = r.ReadU32();
            var size         = r.ReadU32();
            var dataOffset   = r.ReadU32();
            var crc32        = r.ReadU32();
            var fileTime     = r.ReadTankFileTime();
            var format       = (TankDataFormat)r.ReadU16();
            var flags        = (TankFileFlags)r.ReadU16();
            var name         = r.ReadNString();

            TankCompressedFileInfo? compressed = null;
            if (format.IsCompressed() && size != 0)
            {
                var compressedSize = r.ReadU32();
                var chunkSize      = r.ReadU32();
                var numChunks = chunkSize == 0 ? 0u : (uint)Math.Ceiling((double)size / chunkSize);
                var chunks = new TankChunkHeader[numChunks];
                for (var c = 0; c < numChunks; c++)
                {
                    chunks[c] = new TankChunkHeader(
                        UncompressedSize: r.ReadU32(),
                        CompressedSize:   r.ReadU32(),
                        ExtraBytes:       r.ReadU32(),
                        Offset:           r.ReadU32());
                }
                compressed = new TankCompressedFileInfo
                {
                    CompressedSize = compressedSize,
                    ChunkSize      = chunkSize,
                    NumChunks      = numChunks,
                    Chunks         = chunks,
                };
            }

            _files[f] = new TankFileEntry
            {
                ParentOffset = parentOffset,
                Size         = size,
                Offset       = dataOffset,
                Crc32        = crc32,
                FileTime     = fileTime,
                Format       = format,
                Flags        = flags,
                Name         = name,
                Compressed   = compressed,
            };
        }
    }

    private void BuildDirPaths()
    {
        for (var d = 0; d < _dirs.Length; d++)
        {
            var path = BuildDirPath(d) + Sep;
            _table[path] = new Entry(EntryType.Dir, _dirs[d], null);
        }
    }

    private void BuildFilePaths()
    {
        for (var f = 0; f < _files.Length; f++)
        {
            var file = _files[f];
            var dirPath = file.ParentOffset == 0 ? "" : BuildDirPath(FindDirIndex(file.ParentOffset));
            var full = dirPath + Sep + file.Name;
            _table[full] = new Entry(EntryType.File, null, file);
        }
    }

    private string BuildDirPath(int dirIndex)
    {
        var entry = _dirs[dirIndex];
        if (entry.ParentOffset == 0) return "";
        var parentIndex = FindDirIndex(entry.ParentOffset);
        return BuildDirPath(parentIndex) + Sep + entry.Name;
    }

    private int FindDirIndex(uint offset)
    {
        for (var i = 0; i < _dirOffsets.Length; i++)
            if (_dirOffsets[i] == offset) return i;
        throw new TankException($"Orphan directory offset: {offset}");
    }

    private enum EntryType { Dir, File }

    private readonly record struct Entry(EntryType Type, TankDirEntry? Dir, TankFileEntry? File);
}
