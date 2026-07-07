using System;
using System.Globalization;
using System.IO;
using SiegeFX.Core.Assets;

namespace SiegeSmith.ViewModels.WorldBuilder;

/// <summary>Reads a DS1 <c>terrain_nodes/nodes.gas</c> back into an editable <see cref="BuilderRegion"/>
/// — the inverse of <see cref="NodesGasWriter"/>. Parses the same shape the engine's
/// <c>RegionGraph.FromDocument</c> consumes (<c>[t:terrain_nodes]</c> → <c>targetnode</c>;
/// <c>[t:snode]</c> → guid / mesh_guid / texsetabbr; <c>[door]</c> → id / fardoor / farguid), so
/// the World Builder can open a real shipped region — or one it saved — and keep editing it.</summary>
public static class NodesGasReader
{
    public static BuilderRegion Read(GasDocument doc)
    {
        var region = new BuilderRegion();

        GasNode? root = null;
        foreach (var r in doc.Roots)
            if (r.Header.StartsWith("t:terrain_nodes", StringComparison.OrdinalIgnoreCase)) { root = r; break; }
        if (root is null)
            throw new InvalidDataException("nodes.gas: missing [t:terrain_nodes] root block.");

        foreach (var a in root.Attributes)
            if (a.Name.Equals("targetnode", StringComparison.OrdinalIgnoreCase))
                region.TargetGuid = ParseHexU32(a.Value);

        foreach (var child in root.Children)
        {
            if (!child.Header.StartsWith("t:snode", StringComparison.OrdinalIgnoreCase)) continue;

            uint guid = 0, meshGuid = 0;
            string texset = "";
            foreach (var a in child.Attributes)
            {
                if (a.Name.Equals("guid", StringComparison.OrdinalIgnoreCase)) guid = ParseHexU32(a.Value);
                else if (a.Name.Equals("mesh_guid", StringComparison.OrdinalIgnoreCase)) meshGuid = ParseHexU32(a.Value);
                else if (a.Name.Equals("texsetabbr", StringComparison.OrdinalIgnoreCase)) texset = a.Value.Trim();
            }
            if (guid == 0) continue;

            var node = new BuilderNode { Guid = guid, MeshGuid = meshGuid, TexsetAbbr = texset };
            foreach (var dc in child.Children)
            {
                if (!dc.Header.StartsWith("door", StringComparison.OrdinalIgnoreCase)) continue;
                int id = 0, farDoor = 0;
                uint farGuid = 0;
                foreach (var a in dc.Attributes)
                {
                    if (a.Name.Equals("id", StringComparison.OrdinalIgnoreCase)) int.TryParse(a.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
                    else if (a.Name.Equals("fardoor", StringComparison.OrdinalIgnoreCase)) int.TryParse(a.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out farDoor);
                    else if (a.Name.Equals("farguid", StringComparison.OrdinalIgnoreCase)) farGuid = ParseHexU32(a.Value);
                }
                node.Doors.Add(new BuilderDoor(id, farGuid, farDoor));
            }
            region.Nodes.Add(node);
        }

        if (region.TargetGuid == 0 && region.Nodes.Count > 0)
            region.TargetGuid = region.Nodes[0].Guid;
        return region;
    }

    private static uint ParseHexU32(string text)
    {
        var v = text.Trim();
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) v = v[2..];
        return uint.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u) ? u : 0;
    }
}
