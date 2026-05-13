namespace SiegeFX.Core.Assets;

/// <summary>
/// Phase 24-NAV-LOGICAL-FLAGS — DS1's per-snode / per-logical-node
/// flag table. Sourced from <c>terrain_nodes/editor/logical_flags.gas</c>
/// inside the region. Sample format:
///
/// <code>
/// [logical_flags]
/// {
///     [t:lf_snode,n:0xcf30116d]
///     {
///         [t:lf_lnode,n:0]
///         {
///             * = lf_human_player;
///             * = lf_computer_player;
///             * = lf_dirt;
///         }
///     }
/// }
/// </code>
///
/// Each <c>lf_snode</c> block names the snode by its 32-bit guid;
/// each <c>lf_lnode</c> child names the SNO's
/// <see cref="SnoModel.LogicalGrouping.Id"/> (a u8). The flag list
/// is an arbitrary set of <c>*=lf_*</c> attributes. SiegeFX
/// recognizes the two actor-class gates and parses all others
/// permissively into <see cref="Entry.OtherFlags"/> for surface-
/// type queries (footstep audio hooks etc.).
/// </summary>
public sealed class LogicalFlagsStore
{
    public readonly record struct Key(uint SnodeGuid, byte Lnode);
    public sealed record Entry(bool HumanPlayer, bool ComputerPlayer,
        System.Collections.Generic.IReadOnlyList<string> OtherFlags);

    public enum ActorClass { Neutral, HumanPlayer, ComputerPlayer }

    private readonly System.Collections.Generic.Dictionary<Key, Entry> _entries = new();

    /// <summary>True when the region shipped a logical_flags.gas + we
    /// loaded at least one entry. False means the region didn't author
    /// flags — the store returns "no gate" for every query so existing
    /// nav behavior is preserved.</summary>
    public bool HasData => _entries.Count > 0;
    public int EntryCount => _entries.Count;

    public Entry? TryGet(uint snodeGuid, byte lnode)
    {
        return _entries.TryGetValue(new Key(snodeGuid, lnode), out var e) ? e : null;
    }

    /// <summary>True when (snode,lnode) is OPEN to the given actor
    /// class. Unflagged terrain is open by default.</summary>
    public bool CanEnter(uint snodeGuid, byte lnode, ActorClass actor)
    {
        var e = TryGet(snodeGuid, lnode);
        if (e is null) return true;
        return actor switch
        {
            ActorClass.HumanPlayer    => e.HumanPlayer,
            ActorClass.ComputerPlayer => e.ComputerPlayer,
            _ => true,
        };
    }

    /// <summary>Parse a logical_flags.gas blob. Returns an empty store
    /// (HasData=false) when the file is missing or malformed.</summary>
    public static LogicalFlagsStore Parse(byte[] gasBytes)
    {
        var store = new LogicalFlagsStore();
        if (gasBytes is null || gasBytes.Length == 0) return store;
        GasDocument doc;
        try { doc = GasDocument.Load(gasBytes); }
        catch { return store; }

        foreach (var root in doc.Roots)
        {
            if (!HeaderMatchesType(root.Header, "logical_flags") &&
                !HeaderMatchesPlain(root.Header, "logical_flags")) continue;
            foreach (var snodeNode in root.Children)
            {
                if (!HeaderMatchesType(snodeNode.Header, "lf_snode")) continue;
                if (!TryGetName(snodeNode.Header, out var snodeName)) continue;
                if (!TryParseGuid(snodeName, out var snodeGuid)) continue;
                foreach (var lnodeNode in snodeNode.Children)
                {
                    if (!HeaderMatchesType(lnodeNode.Header, "lf_lnode")) continue;
                    if (!TryGetName(lnodeNode.Header, out var lnodeName)) continue;
                    if (!byte.TryParse(lnodeName, out var lnodeIdx)) continue;
                    bool human = false, computer = false;
                    var others = new System.Collections.Generic.List<string>();
                    foreach (var attr in lnodeNode.Attributes)
                    {
                        var v = (attr.Value ?? "").Trim().Trim('"');
                        if (string.IsNullOrEmpty(v)) continue;
                        if (string.Equals(v, "lf_human_player", System.StringComparison.OrdinalIgnoreCase))
                            human = true;
                        else if (string.Equals(v, "lf_computer_player", System.StringComparison.OrdinalIgnoreCase))
                            computer = true;
                        else
                            others.Add(v);
                    }
                    store._entries[new Key(snodeGuid, lnodeIdx)] =
                        new Entry(human, computer, others);
                }
            }
        }
        return store;
    }

    /// <summary>Header format: <c>t:type,n:name</c> or just <c>type</c>
    /// for plain blocks. Returns true when the header's type prefix
    /// matches.</summary>
    private static bool HeaderMatchesType(string header, string expectedType)
    {
        if (string.IsNullOrEmpty(header)) return false;
        // Look for "t:<type>" or "t:<type>,..."
        int tIdx = header.IndexOf("t:", System.StringComparison.OrdinalIgnoreCase);
        if (tIdx < 0) return false;
        int start = tIdx + 2;
        int end = header.IndexOf(',', start);
        if (end < 0) end = header.Length;
        return string.Equals(header.AsSpan(start, end - start).Trim().ToString(),
            expectedType, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Plain block header (no t:/n:): just the type name.</summary>
    private static bool HeaderMatchesPlain(string header, string expectedType)
    {
        return string.Equals(header.Trim(), expectedType,
            System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetName(string header, out string name)
    {
        name = "";
        if (string.IsNullOrEmpty(header)) return false;
        int nIdx = header.IndexOf("n:", System.StringComparison.OrdinalIgnoreCase);
        if (nIdx < 0) return false;
        int start = nIdx + 2;
        int end = header.IndexOf(',', start);
        if (end < 0) end = header.Length;
        name = header.AsSpan(start, end - start).Trim().ToString();
        return name.Length > 0;
    }

    private static bool TryParseGuid(string s, out uint guid)
    {
        guid = 0;
        if (string.IsNullOrEmpty(s)) return false;
        var t = s.Trim();
        if (t.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(t[2..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out guid);
        return uint.TryParse(t, out guid);
    }
}
