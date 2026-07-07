using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace SiegeSmith.Services;

/// <summary>One placed game object, anchored to a terrain node. This is the universal shape the
/// SiegeFX engine loads from every <c>objects/*.gas</c> — a <c>[t:TEMPLATE,n:SCID]</c> root with a
/// <c>[placement]</c> child. <see cref="File"/> selects the bucket (non_interactive.gas, actor.gas,
/// …) which decides which engine pass consumes it; the pose is identical across buckets.</summary>
public sealed class PlacedObject
{
    public uint Scid;
    public string Template = "";
    public uint NodeGuid;
    public Vector3 LocalPos;
    public Quaternion Orientation = Quaternion.Identity;
    public string File = "non_interactive.gas"; // objects/<File>

    public PlacedObject Clone() => new()
    {
        Scid = Scid, Template = Template, NodeGuid = NodeGuid,
        LocalPos = LocalPos, Orientation = Orientation, File = File,
    };
}

/// <summary>Serialises placed objects into the engine's region-object gas. Header polarity matches
/// the engine (inverted vs template files): <c>t:</c> is the template NAME, <c>n:</c> the placement
/// SCID. <c>position</c> is 3 node-local floats + the hex snode GUID; <c>orientation</c> is an x,y,z,w
/// quaternion in System.Numerics order (no swizzle).</summary>
public static class PlacementWriter
{
    /// <summary>Emits one objects/*.gas body for a set of placements that share a file.</summary>
    public static string WriteFile(IEnumerable<PlacedObject> objs)
    {
        var sb = new StringBuilder();
        foreach (var o in objs)
        {
            sb.Append("[t:").Append(o.Template).Append(",n:0x").Append(o.Scid.ToString("X8")).Append("]\r\n{\r\n");
            sb.Append("\t[placement]\r\n\t{\r\n");
            sb.Append("\t\torientation = ")
              .Append(F(o.Orientation.X)).Append(',').Append(F(o.Orientation.Y)).Append(',')
              .Append(F(o.Orientation.Z)).Append(',').Append(F(o.Orientation.W)).Append(";\r\n");
            sb.Append("\t\tposition = ")
              .Append(F(o.LocalPos.X)).Append(',').Append(F(o.LocalPos.Y)).Append(',').Append(F(o.LocalPos.Z))
              .Append(",0x").Append(o.NodeGuid.ToString("X8")).Append(";\r\n");
            sb.Append("\t}\r\n}\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Groups placements by their target file → (relative path under the region, gas body).</summary>
    public static IEnumerable<(string RelPath, string Gas)> WriteByFile(IEnumerable<PlacedObject> objs)
    {
        var byFile = new Dictionary<string, List<PlacedObject>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var o in objs)
        {
            if (!byFile.TryGetValue(o.File, out var list)) byFile[o.File] = list = new List<PlacedObject>();
            list.Add(o);
        }
        foreach (var (file, list) in byFile)
            yield return ("objects/" + file, WriteFile(list));
    }

    private static string F(float v) => v.ToString("0.0######", CultureInfo.InvariantCulture);
}
