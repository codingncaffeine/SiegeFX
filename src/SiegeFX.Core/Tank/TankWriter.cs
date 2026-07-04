using System.Text;

namespace SiegeFX.Core.Tank;

/// <summary>Phase 23e — minimal DS1-compatible tank writer (RAW entries,
/// no compression). Layout per GPG's voluntarily-published
/// TankStructure.h (local copy in the glampert reference repo):
/// [Header][DirSet][FileSet][Data]. NSTRING names are ALWAYS padded to
/// the NEXT dword boundary (1–4 pad bytes, even when already aligned —
/// mirrors SiegeBinaryReader.AlignToDword). Directory child offsets are
/// DirSet-relative for BOTH subdirs and files ("use range to determine
/// if file or dir" — file children land past the DirSet extent because
/// the FileSet physically follows it). Header Index/Data CRCs are
/// written as 0 (TankHeader.InvalidChecksum = "not present"); per-file
/// CRC-32s are real.</summary>
public sealed class TankWriter
{
    readonly SortedDictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    public TankPriority Priority   = TankPriority.User;
    public string Title            = "";
    public string Author           = "";
    public string Description     = "";
    public string BuildText        = "";
    public string CopyrightText    = "";

    /// <summary>Add (or replace) a resource. Path must be '/'-separated
    /// and start with '/'.</summary>
    public void Add(string path, byte[] bytes)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/')
            throw new ArgumentException($"Tank paths must start with '/': '{path}'");
        // Phase 23-fold F5 — GPG requires lowercase resource paths
        // (retail asserts on mixed case).
        _files[path.Replace('\\', '/').ToLowerInvariant()] = bytes;
    }

    public int FileCount => _files.Count;

    sealed class DirNode
    {
        public string Name = "";
        public DirNode? Parent;
        public readonly SortedDictionary<string, DirNode> Dirs = new(StringComparer.OrdinalIgnoreCase);
        public readonly SortedDictionary<string, byte[]> Files = new(StringComparer.OrdinalIgnoreCase);
        public uint EntryOffset;   // DirSet-relative
    }

    public void Write(string outPath, DateTime utcBuildTime)
    {
        if (_files.Count == 0) throw new InvalidOperationException("Tank has no files");

        // ---- build the directory tree ---------------------------------
        var root = new DirNode();
        foreach (var (path, bytes) in _files)
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var node = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!node.Dirs.TryGetValue(parts[i], out var child))
                    node.Dirs[parts[i]] = child = new DirNode { Name = parts[i], Parent = node };
                node = child;
            }
            node.Files[parts[^1]] = bytes;
        }

        // Flatten dirs breadth-first with root first (root is the dummy
        // entry with ParentOffset 0 and empty name).
        var dirs = new List<DirNode> { root };
        for (int i = 0; i < dirs.Count; i++)
            foreach (var d in dirs[i].Dirs.Values) dirs.Add(d);

        long fileTime = utcBuildTime.ToFileTimeUtc();
        uint ftLow  = (uint)(fileTime & 0xFFFFFFFF);
        uint ftHigh = (uint)((ulong)fileTime >> 32);

        // ---- lay out the DirSet ----------------------------------------
        // DirSet = count(4) + offsets[n](4 each) + entries.
        static int NStringSize(string s) { int raw = 2 + s.Length; return raw + (4 - (raw % 4)); }
        static int DirEntrySize(DirNode d) =>
            4 + 4 + 8 + NStringSize(d.Name == "/" ? "" : d.Name) + 4 * (d.Dirs.Count + d.Files.Count);

        uint dirCursor = (uint)(4 + 4 * dirs.Count);
        foreach (var d in dirs)
        {
            d.EntryOffset = dirCursor;
            dirCursor += (uint)DirEntrySize(d);
        }
        uint dirSetSize = dirCursor;

        // ---- lay out the FileSet ---------------------------------------
        // FileSet = count(4) + offsets[n](4 each) + entries. File entry:
        // parent(4) size(4) offset(4) crc(4) filetime(8) format(2) flags(2) + name.
        var fileList = new List<(DirNode Dir, string Name, byte[] Bytes)>();
        foreach (var d in dirs)
            foreach (var (name, bytes) in d.Files)
                fileList.Add((d, name, bytes));

        uint fileCursor = (uint)(4 + 4 * fileList.Count);
        var fileEntryOffsets = new uint[fileList.Count];   // FileSet-relative
        for (int i = 0; i < fileList.Count; i++)
        {
            fileEntryOffsets[i] = fileCursor;
            fileCursor += (uint)(28 + NStringSize(fileList[i].Name));
        }
        uint fileSetSize = fileCursor;

        // ---- data region -----------------------------------------------
        var dataOffsets = new uint[fileList.Count];        // Data-relative
        uint dataCursor = 0;
        for (int i = 0; i < fileList.Count; i++)
        {
            dataOffsets[i] = dataCursor;
            dataCursor += (uint)((fileList[i].Bytes.Length + 3) & ~3);
        }

        // ---- header size ------------------------------------------------
        // Fixed part = 784 bytes (7 u32 + 2 ProductVersion + priority +
        // flags + creator + guid + 2 crc + SYSTEMTIME + 100/100/100/40
        // wide chars), then the description WNSTRING.
        int wnDescSize; { int raw = 2 + Description.Length * 2; wnDescSize = raw + (4 - (raw % 4)); }
        uint headerSize    = (uint)(784 + wnDescSize);
        // Phase 23-fold F5 — retail tank layout is header → DATA → index
        // at EOF (verified byte-level on retail Logic.dsres: DataOffset
        // sits immediately after the header and IndexSize == fileSize −
        // DirSetOffset). A front-index tank risks rejection by mappers
        // that locate the index from EOF.
        uint dataOffset    = headerSize;
        uint dirSetOffset  = dataOffset + dataCursor;
        uint fileSetOffset = dirSetOffset + dirSetSize;

        // Child offsets for files are DirSet-relative: FileSet follows the
        // DirSet, so a file entry's dirset-relative address is
        // dirSetSize + its fileset-relative offset ("use range").
        uint FileChildOffset(int fileIndex) => dirSetSize + fileEntryOffsets[fileIndex];
        var fileIndexByRef = new Dictionary<(DirNode, string), int>();
        for (int i = 0; i < fileList.Count; i++)
            fileIndexByRef[(fileList[i].Dir, fileList[i].Name)] = i;

        // ---- write -------------------------------------------------------
        using var fs = File.Create(outPath);
        using var w = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: true);

        void WriteFourCC(string s) { for (int i = 0; i < 4; i++) w.Write((byte)s[i]); }
        void WriteNString(string s)
        {
            w.Write((ushort)s.Length);
            foreach (var ch in s) w.Write((byte)ch);
            int raw = 2 + s.Length;
            int pad = 4 - (raw % 4);
            for (int i = 0; i < pad; i++) w.Write((byte)0);
        }
        void WriteFixedWide(string s, int chars)
        {
            for (int i = 0; i < chars; i++)
                w.Write((ushort)(i < s.Length ? s[i] : 0));
        }

        // Header.
        WriteFourCC("DSig");
        WriteFourCC("Tank");
        w.Write(TankVersion.Ds1);
        w.Write(dirSetOffset);
        w.Write(fileSetOffset);
        w.Write(dirSetSize + fileSetSize);   // IndexSize
        w.Write(dataOffset);
        w.Write(1u); w.Write(0u); w.Write(0u);   // ProductVersion 1.0.0
        w.Write(1u); w.Write(0u); w.Write(0u);   // MinimumVersion 1.0.0
        w.Write((uint)Priority);
        w.Write((uint)TankFlags.None);
        WriteFourCC("USER");
        // Fixed GUID — regenerating a byte-identical tank for the same
        // inputs beats uniqueness here (deterministic builds).
        w.Write(0x53464653u); w.Write((ushort)0x2023); w.Write((ushort)0x0E01);
        w.Write(0x5346582D53504CUL);
        w.Write(0u);                              // IndexCrc32 = not present
        w.Write(0u);                              // DataCrc32  = not present
        w.Write((ushort)utcBuildTime.Year); w.Write((ushort)utcBuildTime.Month);
        w.Write((ushort)utcBuildTime.DayOfWeek); w.Write((ushort)utcBuildTime.Day);
        w.Write((ushort)utcBuildTime.Hour); w.Write((ushort)utcBuildTime.Minute);
        w.Write((ushort)utcBuildTime.Second); w.Write((ushort)utcBuildTime.Millisecond);
        WriteFixedWide(CopyrightText, 100);
        WriteFixedWide(BuildText, 100);
        WriteFixedWide(Title, 100);
        WriteFixedWide(Author, 40);
        // Description WNSTRING.
        w.Write((ushort)Description.Length);
        foreach (var ch in Description) w.Write((ushort)ch);
        { int raw = 2 + Description.Length * 2; int pad = 4 - (raw % 4); for (int i = 0; i < pad; i++) w.Write((byte)0); }

        if (fs.Position != headerSize)
            throw new TankException($"Header layout drift: wrote {fs.Position}, expected {headerSize}");

        // Data (retail order: data precedes the index).
        for (int i = 0; i < fileList.Count; i++)
        {
            var bytes = fileList[i].Bytes;
            w.Write(bytes);
            int pad = (int)(((bytes.Length + 3) & ~3) - bytes.Length);
            for (int p = 0; p < pad; p++) w.Write((byte)0);
        }
        if (fs.Position != dirSetOffset)
            throw new TankException("Data layout drift");

        // DirSet.
        w.Write((uint)dirs.Count);
        foreach (var d in dirs) w.Write(d.EntryOffset);
        foreach (var d in dirs)
        {
            w.Write(d.Parent?.EntryOffset ?? 0u);
            var children = new List<(string Name, uint Offset)>();
            foreach (var sub in d.Dirs.Values) children.Add((sub.Name, sub.EntryOffset));
            foreach (var f in d.Files.Keys) children.Add((f, FileChildOffset(fileIndexByRef[(d, f)])));
            children.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name)); // byte order per retail
            w.Write((uint)children.Count);
            // Root dir carries FILETIME 0 in retail tanks.
            if (d.Parent is null) { w.Write(0u); w.Write(0u); }
            else { w.Write(ftLow); w.Write(ftHigh); }
            WriteNString(d.Parent is null ? "" : d.Name);
            foreach (var (_, off) in children) w.Write(off);
        }
        if (fs.Position != dirSetOffset + dirSetSize)
            throw new TankException("DirSet layout drift");

        // FileSet.
        w.Write((uint)fileList.Count);
        foreach (var off in fileEntryOffsets) w.Write(off);
        for (int i = 0; i < fileList.Count; i++)
        {
            var (dir, name, bytes) = fileList[i];
            w.Write(dir.EntryOffset);
            w.Write((uint)bytes.Length);
            w.Write(dataOffsets[i]);
            w.Write(SiegeFX.Core.IO.Crc32.Compute(bytes));
            w.Write(ftLow); w.Write(ftHigh);
            w.Write((ushort)TankDataFormat.Raw);
            w.Write((ushort)TankFileFlags.None);
            WriteNString(name);
        }
        if (fs.Position != fileSetOffset + fileSetSize)
            throw new TankException("FileSet layout drift");
    }

}
