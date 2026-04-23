using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>
/// Resolves a <c>mesh_guid</c> (as stored in a region's <c>nodes.gas</c>) to the full tank
/// path of its .sno asset. DS1 splits the mapping in two: every
/// <c>/world/global/siege_nodes/&lt;set&gt;/misc_*.gas</c> file holds <c>[mesh_file*]</c>
/// entries that map bare SNO filenames to guids, and the .sno files themselves live under
/// <c>/art/terrain/&lt;set&gt;/&lt;filename&gt;.sno</c>. We build both lookups up front and
/// combine them at resolve time.
/// </summary>
public sealed class SnoMeshIndex
{
    private readonly Dictionary<uint, string> _guidToBareName;
    private readonly Dictionary<string, string> _bareToFullPath;

    public int GuidCount => _guidToBareName.Count;
    public int SnoCount  => _bareToFullPath.Count;

    private SnoMeshIndex(
        Dictionary<uint, string> guidToBareName,
        Dictionary<string, string> bareToFullPath)
    {
        _guidToBareName = guidToBareName;
        _bareToFullPath = bareToFullPath;
    }

    public bool TryResolve(uint meshGuid, [MaybeNullWhen(false)] out string tankPath)
    {
        if (_guidToBareName.TryGetValue(meshGuid, out var bare) &&
            _bareToFullPath.TryGetValue(bare, out var full))
        {
            tankPath = full;
            return true;
        }
        tankPath = null;
        return false;
    }

    /// <summary>Scans <paramref name="tank"/> for siege-node index files and .sno entries,
    /// returning a combined mesh-guid resolver. Missing entries (guid has no matching
    /// .sno, or vice versa) are not an error — callers decide how to handle them.</summary>
    public static SnoMeshIndex Build(TankReader tank)
    {
        var guidToBare = new Dictionary<uint, string>();
        var bareToFull = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in tank.ListFiles())
        {
            if (path.EndsWith(".sno", StringComparison.OrdinalIgnoreCase))
            {
                var bare = BareName(path);
                bareToFull[bare] = path;
                continue;
            }

            // The index sits in any .gas file under /world/global/siege_nodes/. DS1 uses a
            // mix of names — `misc_<set>.gas`, `generic.gas`, per-level `misc_*_NN.gas` —
            // and any of them can hold `[mesh_file*]` blocks. Other .gas files in these
            // folders (naming_key, etc.) simply have no matching header and get ignored.
            if (!path.EndsWith(".gas", StringComparison.OrdinalIgnoreCase)) continue;
            if (!path.Contains("/siege_nodes/", StringComparison.OrdinalIgnoreCase)) continue;

            var bytes = tank.ExtractToMemory(path);
            var doc = GasDocument.Load(bytes);
            IndexGasDoc(doc, guidToBare);
        }

        return new SnoMeshIndex(guidToBare, bareToFull);
    }

    private static void IndexGasDoc(GasDocument doc, Dictionary<uint, string> guidToBare)
    {
        foreach (var root in doc.Roots)
            IndexNode(root, guidToBare);
    }

    private static void IndexNode(GasNode node, Dictionary<uint, string> guidToBare)
    {
        // [mesh_file*] blocks carry `filename=...; guid=0x...;` — the first-wins policy
        // is fine: real DS1 data has no intra-tank collisions (verified by fuzz).
        if (node.Header.StartsWith("mesh_file", StringComparison.OrdinalIgnoreCase))
        {
            string? filename = null;
            uint guid = 0;
            foreach (var a in node.Attributes)
            {
                if (a.Name.Equals("filename", StringComparison.OrdinalIgnoreCase))
                    filename = a.Value;
                else if (a.Name.Equals("guid", StringComparison.OrdinalIgnoreCase))
                    guid = ParseHexU32(a.Value);
            }
            if (guid != 0 && !string.IsNullOrEmpty(filename))
                guidToBare.TryAdd(guid, filename!);
        }
        foreach (var child in node.Children)
            IndexNode(child, guidToBare);
    }

    private static string BareName(string path)
    {
        var slash = path.LastIndexOf('/');
        var start = slash < 0 ? 0 : slash + 1;
        var dot = path.LastIndexOf('.');
        var end = dot < start ? path.Length : dot;
        return path[start..end];
    }

    private static uint ParseHexU32(string text)
    {
        var v = text.Trim();
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) v = v[2..];
        return uint.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u) ? u : 0;
    }
}
