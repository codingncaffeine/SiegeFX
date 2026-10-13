using System.Buffers.Binary;
using System.Text;

namespace SiegeFX.OriginalSaves;

/// <summary>
/// The original game's world persistence: <c>world.xidx</c> (magic
/// <c>DSigIXfr</c>) indexes a tree whose names and values live in
/// <c>world.xdat</c> (magic <c>DSigDXfr</c>). Every entry in the data file is a
/// NUL-terminated name followed by the raw value bytes; the index gives each
/// node its value entries and its child nodes as offsets into the data file.
/// Values carry no type tag — a value runs from the end of its name to the
/// next indexed entry, so its size (1 = bool, 4 = int/float/id, 8 = double,
/// NUL-terminated = text) is the only type evidence. See
/// docs/original-save-format.md for the evidence behind each rule.
/// </summary>
public sealed class XferTree
{
    public const int DataHeaderSize = 16;
    static ReadOnlySpan<byte> IndexMagic => "DSigIXfr"u8;
    static ReadOnlySpan<byte> DataMagic => "DSigDXfr"u8;

    public XferNode Root { get; }
    public int NodeCount { get; }
    public uint Version { get; }

    XferTree(XferNode root, int nodeCount, uint version)
    {
        Root = root;
        NodeCount = nodeCount;
        Version = version;
    }

    /// <summary>Parse an index/data pair. Throws <see cref="InvalidDataException"/>
    /// on anything malformed: wrong magic, counts past the end, offsets outside
    /// the data, a node referenced twice, or a name without its terminator.</summary>
    public static XferTree Load(byte[] index, byte[] data)
    {
        if (index.Length < 20 || !index.AsSpan(0, 8).SequenceEqual(IndexMagic))
            throw new InvalidDataException("not a world.xidx (missing DSigIXfr magic)");
        if (data.Length < DataHeaderSize || !data.AsSpan(0, 8).SequenceEqual(DataMagic))
            throw new InvalidDataException("not a world.xdat (missing DSigDXfr magic)");
        if (index.Length % 4 != 0)
            throw new InvalidDataException("world.xidx length is not a whole number of words");

        int words = index.Length / 4;
        uint Word(int i) => BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(i * 4, 4));

        uint version = Word(2);
        uint nodeCount = Word(4);
        if (nodeCount == 0 || nodeCount > words)
            throw new InvalidDataException($"world.xidx declares {nodeCount} nodes in {words} words");

        // Pass 1: node records, in index order.
        var values = new uint[nodeCount][];
        var kids = new (uint NameOffset, uint Node)[nodeCount][];
        int p = 5;
        for (int n = 0; n < nodeCount; n++)
        {
            uint vc = Next();
            if (vc > words - p) throw Truncated(n);
            values[n] = new uint[vc];
            for (int i = 0; i < vc; i++) values[n][i] = CheckOffset(Next(), n);
            uint cc = Next();
            if (cc > (words - p) / 2) throw Truncated(n);
            kids[n] = new (uint, uint)[cc];
            for (int i = 0; i < cc; i++)
            {
                uint off = CheckOffset(Next(), n);
                uint child = Next();
                if (child == 0 || child >= nodeCount)
                    throw new InvalidDataException($"node {n} names child {child}, outside 1..{nodeCount - 1}");
                kids[n][i] = (off, child);
            }
        }
        if (p != words)
            throw new InvalidDataException($"world.xidx has {words - p} unread word(s) after {nodeCount} nodes");

        // Every indexed entry, in data order: a value runs to the next one.
        var starts = new List<uint>();
        foreach (var v in values) starts.AddRange(v);
        foreach (var k in kids) foreach (var (o, _) in k) starts.Add(o);
        starts.Sort();
        var nextStart = new Dictionary<uint, uint>(starts.Count);
        for (int i = 0; i < starts.Count; i++)
        {
            if (i > 0 && starts[i] == starts[i - 1])
                throw new InvalidDataException($"two entries share data offset {starts[i]}");
            nextStart[starts[i]] = i + 1 < starts.Count ? starts[i + 1] : (uint)data.Length;
        }

        // Pass 2: build nodes top-down without recursion (save trees nest deep).
        var built = new XferNode?[nodeCount];
        var root = built[0] = new XferNode("", "/", 0);
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            int n = stack.Pop();
            var node = built[n]!;
            var entries = new List<(uint Offset, XferEntry Entry)>(values[n].Length + kids[n].Length);
            foreach (var off in values[n])
            {
                var (name, nameEnd) = ReadName(data, off);
                var raw = data.AsMemory(nameEnd, (int)(nextStart[off] - nameEnd));
                entries.Add((off, new XferValue(name, raw, off)));
            }
            foreach (var (off, child) in kids[n])
            {
                var (name, nameEnd) = ReadName(data, off);
                if (nameEnd != nextStart[off])
                    throw new InvalidDataException($"child '{name}' at {off} is followed by {nextStart[off] - nameEnd} unindexed byte(s)");
                if (built[child] is not null)
                    throw new InvalidDataException($"node {child} is referenced more than once");
                var c = built[child] = new XferNode(name, node.Path == "/" ? "/" + name : node.Path + "/" + name, (int)child);
                entries.Add((off, c));
                stack.Push((int)child);
            }
            entries.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            node.SetEntries(entries.Select(e => e.Entry));
        }
        for (int n = 1; n < nodeCount; n++)
            if (built[n] is null) throw new InvalidDataException($"node {n} is never referenced");

        return new XferTree(root, (int)nodeCount, version);

        uint Next()
        {
            if (p >= words) throw Truncated(-1);
            return Word(p++);
        }
        uint CheckOffset(uint off, int n)
        {
            if (off < DataHeaderSize || off >= data.Length)
                throw new InvalidDataException($"node {n} points at data offset {off}, outside {DataHeaderSize}..{data.Length - 1}");
            return off;
        }
        static InvalidDataException Truncated(int n) =>
            new($"world.xidx ends inside node {(n < 0 ? "?" : n.ToString())}");
    }

    static (string Name, int End) ReadName(byte[] data, uint off)
    {
        int nul = Array.IndexOf(data, (byte)0, (int)off);
        if (nul < 0) throw new InvalidDataException($"name at {off} has no terminator");
        return (Encoding.Latin1.GetString(data, (int)off, nul - (int)off), nul + 1);
    }

    /// <summary>The node at a '/'-separated path from the root, e.g.
    /// <c>/world/m_pvictory/m_quests</c>; null when any step is missing.</summary>
    public XferNode? Find(string path)
    {
        var node = Root;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node.Child(part);
            if (node is null) return null;
        }
        return node;
    }
}

/// <summary>A value or a child node, in the order the game wrote them.</summary>
public abstract class XferEntry
{
    public abstract string Name { get; }
}

public sealed class XferNode : XferEntry
{
    readonly string _name;
    List<XferEntry> _entries = new();

    internal XferNode(string name, string path, int index)
    {
        _name = name;
        Path = path;
        Index = index;
    }

    public override string Name => _name;
    /// <summary>'/'-separated names from the root.</summary>
    public string Path { get; }
    /// <summary>The node's number in world.xidx (0 = root).</summary>
    public int Index { get; }

    /// <summary>Values and children interleaved, in data-file order.</summary>
    public IReadOnlyList<XferEntry> Entries => _entries;
    public IEnumerable<XferValue> Values => _entries.OfType<XferValue>();
    public IEnumerable<XferNode> Children => _entries.OfType<XferNode>();

    internal void SetEntries(IEnumerable<XferEntry> entries) => _entries = entries.ToList();

    public XferValue? Value(string name) => Values.FirstOrDefault(v => v.Name == name);
    public XferNode? Child(string name) => Children.FirstOrDefault(c => c.Name == name);

    /// <summary>This node and every node under it, depth-first.</summary>
    public IEnumerable<XferNode> DescendantsAndSelf()
    {
        var stack = new Stack<XferNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            var kids = n.Children.ToList();
            for (int i = kids.Count - 1; i >= 0; i--) stack.Push(kids[i]);
        }
    }
}

public sealed class XferValue : XferEntry
{
    readonly string _name;

    internal XferValue(string name, ReadOnlyMemory<byte> raw, uint offset)
    {
        _name = name;
        Raw = raw;
        Offset = offset;
    }

    public override string Name => _name;
    /// <summary>The value's bytes, exactly as stored.</summary>
    public ReadOnlyMemory<byte> Raw { get; }
    /// <summary>Where the entry's name starts in world.xdat.</summary>
    public uint Offset { get; }

    public bool? AsBool => Raw.Length == 1 ? Raw.Span[0] != 0 : null;
    public int? AsInt32 => Raw.Length == 4 ? BinaryPrimitives.ReadInt32LittleEndian(Raw.Span) : null;
    public uint? AsUInt32 => Raw.Length == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(Raw.Span) : null;
    public float? AsSingle => Raw.Length == 4 ? BinaryPrimitives.ReadSingleLittleEndian(Raw.Span) : null;
    public double? AsDouble => Raw.Length == 8 ? BinaryPrimitives.ReadDoubleLittleEndian(Raw.Span) : null;

    /// <summary>The value as text when it is one NUL-terminated printable
    /// string (the empty string counts), else null.</summary>
    public string? AsText
    {
        get
        {
            var s = Raw.Span;
            if (s.Length == 0 || s[^1] != 0) return null;
            for (int i = 0; i < s.Length - 1; i++)
                if (s[i] < 0x20 && s[i] != '\t' || s[i] == 0x7F) return null;
            return Encoding.Latin1.GetString(s[..^1]);
        }
    }

    /// <summary>The value as UTF-16 text when it is one NUL-terminated
    /// printable wide string (quest titles and speakers are stored this way),
    /// else null. An empty wide string is two zero bytes.</summary>
    public string? AsWideText
    {
        get
        {
            var s = Raw.Span;
            if (s.Length < 2 || s.Length % 2 != 0 || s[^1] != 0 || s[^2] != 0) return null;
            var text = Encoding.Unicode.GetString(s[..^2]);
            foreach (var c in text)
                if (char.IsControl(c) && c != '\t' || char.IsSurrogate(c)) return null;
            return text;
        }
    }

    /// <summary>Text in either encoding: narrow first, then UTF-16.</summary>
    public string? AsAnyText => AsText is { Length: > 0 } t ? t : AsWideText is { Length: > 0 } w ? w : AsText ?? AsWideText;

    float[]? Floats(int n)
    {
        var r = new float[n];
        for (int i = 0; i < n; i++)
        {
            r[i] = BinaryPrimitives.ReadSingleLittleEndian(Raw.Span[(i * 4)..]);
            if (!float.IsFinite(r[i]) || MathF.Abs(r[i]) > 1e6f || r[i] != 0 && MathF.Abs(r[i]) < 1e-6f) return null;
        }
        return r;
    }

    /// <summary>A best-guess rendering for dumps: text when it reads as
    /// text, else by size (bool, int with its float reading, double), else hex.
    /// The format carries no types, so this is a reading aid, not a decode.</summary>
    public string Display()
    {
        if (AsText is { } t && (t.Length > 0 || Raw.Length > 1 || Name.StartsWith('s'))) return $"\"{t}\"";
        // Wide text only when it reads as at least two plain characters: a
        // small 4-byte number is otherwise a one-character "wide string".
        if (Raw.Length >= 6 && AsWideText is { Length: >= 2 } wt && wt.All(c => c is >= ' ' and <= '\u00ff'))
            return $"L\"{wt}\"";
        switch (Raw.Length)
        {
            case 0: return "(empty)";
            case 1: return Raw.Span[0] switch { 0 => "0", 1 => "1", var b => $"0x{b:x2}" };
            case 4:
            {
                int i = AsInt32!.Value;
                float f = AsSingle!.Value;
                bool plausibleFloat = float.IsFinite(f) && f != 0 && MathF.Abs(f) is > 1e-6f and < 1e9f
                                      && (i & 0x7F800000) != 0;
                return plausibleFloat ? $"{i} (f {f:G7})" : $"{i} (0x{(uint)i:x8})";
            }
            case 8: return $"{AsDouble!.Value:G10}";
            // 12 = three floats (a vector); 16 = three floats + a terrain node
            // id (a position, the same shape as info.gas "teleport"). Shown as
            // a reading when every float is finite and modest; '~' = a guess.
            case 12 when Floats(3) is { } v:
                return $"[12] ~({v[0]:0.###}, {v[1]:0.###}, {v[2]:0.###})";
            case 16 when Floats(3) is { } p:
                // The node id's bytes are stored in the reverse of the order
                // info.gas prints them ("teleport ... 0x5353287c" is stored
                // 53 53 28 7c), so read big-endian to match the text form.
                return $"[16] ~pos ({p[0]:0.###}, {p[1]:0.###}, {p[2]:0.###}) node 0x{BinaryPrimitives.ReadUInt32BigEndian(Raw.Span[12..]):x8}";
            default:
                var hex = Convert.ToHexString(Raw.Span[..Math.Min(Raw.Length, 32)]).ToLowerInvariant();
                return Raw.Length > 32 ? $"[{Raw.Length}] {hex}…" : $"[{Raw.Length}] {hex}";
        }
    }
}
