using System.Globalization;

namespace SiegeFX.Core.Assets;

/// <summary>
/// Cross-region connections for one region, parsed from <c>editor/stitch_helper.gas</c>.
/// DS1's per-region <c>nodes.gas</c> only records intra-region door edges — every
/// cross-region boundary instead lives in this sibling file as a bag of
/// <c>(stitchPairId → localSnode, localDoor)</c> entries grouped by destination region.
///
/// To reconstruct a cross-region edge you need BOTH regions' stitch files: matching
/// <c>stitchPairId</c> values give you the (local, far) snode+door pair. This class
/// parses one side — see <see cref="WorldLayout"/> for the pairing step.
/// </summary>
public sealed class RegionStitchHelper
{
    public uint SourceRegionGuid { get; }
    public string SourceRegionName { get; }

    /// <summary>Per-destination-region stitch list. The outer key is the destination
    /// region name (e.g. <c>ds_r2</c>); the inner list gives, for each stitch on our side,
    /// the pair id plus our local snode+door that sit at that boundary.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Stitch>> ByDestination { get; }

    private RegionStitchHelper(
        uint sourceRegionGuid,
        string sourceRegionName,
        IReadOnlyDictionary<string, IReadOnlyList<Stitch>> byDestination)
    {
        SourceRegionGuid = sourceRegionGuid;
        SourceRegionName = sourceRegionName;
        ByDestination = byDestination;
    }

    public static RegionStitchHelper Load(byte[] stitchGasBytes) =>
        FromDocument(GasDocument.Load(stitchGasBytes));

    public static RegionStitchHelper FromDocument(GasDocument doc)
    {
        GasNode? root = null;
        foreach (var r in doc.Roots)
        {
            if (r.Header.Equals("stitch_helper_data", StringComparison.OrdinalIgnoreCase))
            {
                root = r;
                break;
            }
        }
        if (root is null)
            throw new InvalidDataException("stitch_helper.gas: missing [stitch_helper_data] root");

        uint sourceGuid = 0;
        var sourceName = "";
        foreach (var a in root.Attributes)
        {
            if (a.Name.Equals("source_region_guid", StringComparison.OrdinalIgnoreCase))
                sourceGuid = ParseHexU32(a.Value, "source_region_guid");
            else if (a.Name.Equals("source_region_name", StringComparison.OrdinalIgnoreCase))
                sourceName = a.Value;
        }

        var byDest = new Dictionary<string, IReadOnlyList<Stitch>>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in root.Children)
        {
            // Headers look like [t:stitch_editor,n:ds_r2]. The dest_region attribute
            // is also present and redundant; prefer it since it's the canonical field.
            if (!child.Header.StartsWith("t:stitch_editor", StringComparison.OrdinalIgnoreCase))
                continue;

            var destRegion = "";
            foreach (var a in child.Attributes)
                if (a.Name.Equals("dest_region", StringComparison.OrdinalIgnoreCase))
                    destRegion = a.Value;
            if (string.IsNullOrEmpty(destRegion)) continue;

            GasNode? nodeIds = null;
            foreach (var inner in child.Children)
                if (inner.Header.Equals("node_ids", StringComparison.OrdinalIgnoreCase))
                {
                    nodeIds = inner;
                    break;
                }
            if (nodeIds is null) continue;

            var stitches = new List<Stitch>(nodeIds.Attributes.Count);
            foreach (var a in nodeIds.Attributes)
            {
                // Attribute form:  0x0000ab01 = 0xEA6507AB,4
                //   name = stitch pair id (hex u32)
                //   value = "<far-guid-hex>,<door-id>"   — despite the pair-id framing,
                //           the snode+door here is our LOCAL side; the matching entry
                //           in the neighbor's stitch file carries their local pair.
                var pairId = ParseHexU32(a.Name, "stitch pair id");
                var comma = a.Value.IndexOf(',');
                if (comma < 0)
                    throw new InvalidDataException($"stitch_helper.gas: malformed node_ids value '{a.Value}'");
                var guid = ParseHexU32(a.Value[..comma], "stitch snode");
                if (!int.TryParse(a.Value.AsSpan(comma + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var doorId))
                    throw new InvalidDataException($"stitch_helper.gas: stitch door '{a.Value[(comma + 1)..]}' is not an int");

                stitches.Add(new Stitch(pairId, guid, doorId));
            }

            byDest[destRegion] = stitches;
        }

        return new RegionStitchHelper(sourceGuid, sourceName, byDest);
    }

    private static uint ParseHexU32(string text, string field)
    {
        var v = text.Trim();
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) v = v[2..];
        if (!uint.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u))
            throw new InvalidDataException($"stitch_helper.gas: {field}='{text}' is not a hex u32");
        return u;
    }

    /// <summary>One side of a cross-region stitch. <see cref="PairId"/> is the shared
    /// matchmaker — the neighbor region's stitch file uses the same id and gives their
    /// own local <see cref="SnodeGuid"/> + <see cref="DoorId"/>.</summary>
    public readonly record struct Stitch(uint PairId, uint SnodeGuid, int DoorId);
}
