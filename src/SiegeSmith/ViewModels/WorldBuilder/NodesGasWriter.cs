using System.Text;

namespace SiegeSmith.ViewModels.WorldBuilder;

/// <summary>Serialises a <see cref="BuilderRegion"/> to DS1 <c>terrain_nodes/nodes.gas</c> text,
/// mirroring the shipped snode/door syntax so the engine's RegionGraph parser (and our own
/// preview) accept it verbatim.</summary>
public static class NodesGasWriter
{
    public static string Write(BuilderRegion region)
    {
        var sb = new StringBuilder();
        sb.Append("[t:terrain_nodes,n:siege_node_list]\r\n{\r\n");
        sb.Append($"\ttargetnode = 0x{region.TargetGuid:X8};\r\n");

        foreach (var node in region.Nodes)
        {
            sb.Append($"\t[t:snode,n:0x{node.Guid:X8}]\r\n\t{{\r\n");
            sb.Append("\t\tbounds_camera = true;\r\n");
            sb.Append("\t\tcamera_fade = false;\r\n");
            sb.Append($"\t\tguid = 0x{node.Guid:X8};\r\n");
            sb.Append($"\t\tmesh_guid = 0x{node.MeshGuid:X8};\r\n");
            sb.Append("\t\tnodelevel = 0;\r\n");
            sb.Append("\t\tnodeobject = 0;\r\n");
            sb.Append("\t\tnodesection = 0;\r\n");
            sb.Append("\t\toccludes_camera = true;\r\n");
            sb.Append("\t\toccludes_light = true;\r\n");
            sb.Append($"\t\ttexsetabbr = {(string.IsNullOrWhiteSpace(node.TexsetAbbr) ? "grs01" : node.TexsetAbbr)};\r\n");

            foreach (var d in node.Doors)
            {
                sb.Append("\t\t[door*]\r\n\t\t{\r\n");
                sb.Append($"\t\t\tid = {d.LocalId};\r\n");
                sb.Append($"\t\t\tfardoor = {d.FarDoorId};\r\n");
                sb.Append($"\t\t\tfarguid = 0x{d.FarGuid:X8};\r\n");
                sb.Append("\t\t}\r\n");
            }

            sb.Append("\t}\r\n");
        }

        sb.Append("}\r\n");
        return sb.ToString();
    }
}
