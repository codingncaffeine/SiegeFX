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

    // ED-4b — per-placement instance overrides. DS1 semantics: values inside
    // the [t:...,n:...] block win over the template chain (the engine reads
    // instance [aspect]/[inventory] first). Defaults mean "no override" and
    // emit nothing, so plain placements stay byte-identical.
    public float ScaleMult = 1f;   // [aspect] scale_multiplier (visual size)
    public float LifeOverride;     // [aspect] life + max_life; 0 = template's
    public string LootDrop = "";   // [inventory][pcontent][oneof] il_main — guaranteed drop/contents
    // ED-11 — [mind] initial_command: the first cmd_ai_patrol SCID of this
    // actor's patrol chain. 0 = no patrol. The engine walks next_scid links
    // from here and hands the route to the actor's brain at spawn.
    public uint InitialCommand;

    public bool HasOverrides =>
        LifeOverride > 0f || !string.IsNullOrWhiteSpace(LootDrop)
        || (ScaleMult > 0f && System.Math.Abs(ScaleMult - 1f) > 0.0001f);

    public PlacedObject Clone() => new()
    {
        Scid = Scid, Template = Template, NodeGuid = NodeGuid,
        LocalPos = LocalPos, Orientation = Orientation, File = File,
        ScaleMult = ScaleMult, LifeOverride = LifeOverride, LootDrop = LootDrop,
        InitialCommand = InitialCommand,
    };
}

/// <summary>Serialises placed objects into the engine's region-object gas. Header polarity matches
/// the engine (inverted vs template files): <c>t:</c> is the template NAME, <c>n:</c> the placement
/// SCID. <c>position</c> is 3 node-local floats + the hex snode GUID; <c>orientation</c> is an x,y,z,w
/// quaternion in System.Numerics order (no swizzle).</summary>
public static class PlacementWriter
{
    /// <summary>Emits one objects/*.gas body for a set of placements that share a file. Any placement
    /// whose SCID is in <paramref name="convBindings"/> gets a <c>[conversation][conversations]</c> block
    /// wiring the actor to its conversation key (LE-8 NPC binding).</summary>
    public static string WriteFile(IEnumerable<PlacedObject> objs, IReadOnlyDictionary<uint, string>? convBindings = null)
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
            sb.Append("\t}\r\n");
            // ED-4b — instance overrides, emitted only when authored so
            // untouched placements keep the minimal retail shape.
            bool scaleOverride = o.ScaleMult > 0f && System.Math.Abs(o.ScaleMult - 1f) > 0.0001f;
            if (scaleOverride || o.LifeOverride > 0f)
            {
                sb.Append("\t[aspect]\r\n\t{\r\n");
                if (scaleOverride)
                    sb.Append("\t\tscale_multiplier = ").Append(F(o.ScaleMult)).Append(";\r\n");
                if (o.LifeOverride > 0f)
                {
                    sb.Append("\t\tlife = ").Append(F(o.LifeOverride)).Append(";\r\n");
                    sb.Append("\t\tmax_life = ").Append(F(o.LifeOverride)).Append(";\r\n");
                }
                sb.Append("\t}\r\n");
            }
            if (o.InitialCommand != 0)
            {
                // ED-11 — the engine's patrol assigner reads instance-level
                // [mind] initial_command and walks the next_scid chain.
                sb.Append("\t[mind]\r\n\t{\r\n");
                sb.Append("\t\tinitial_command = 0x").Append(o.InitialCommand.ToString("X8")).Append(";\r\n");
                sb.Append("\t}\r\n");
            }
            if (!string.IsNullOrWhiteSpace(o.LootDrop))
            {
                // A chance-less [oneof] is always-on: the named item is a
                // GUARANTEED drop (actors) / content (containers), additive
                // to whatever the template's own pcontent rolls.
                sb.Append("\t[inventory]\r\n\t{\r\n\t\t[pcontent]\r\n\t\t{\r\n\t\t\t[oneof]\r\n\t\t\t{\r\n");
                sb.Append("\t\t\t\til_main = ").Append(o.LootDrop.Trim()).Append(";\r\n");
                sb.Append("\t\t\t}\r\n\t\t}\r\n\t}\r\n");
            }
            if (convBindings is not null && convBindings.TryGetValue(o.Scid, out var convKey) && !string.IsNullOrWhiteSpace(convKey))
            {
                sb.Append("\t[conversation]\r\n\t{\r\n\t\t[conversations]\r\n\t\t{\r\n");
                sb.Append("\t\t\t* = ").Append(convKey).Append(";\r\n");
                sb.Append("\t\t}\r\n\t}\r\n");
            }
            sb.Append("}\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Groups placements by their target file → (relative path under the region, gas body).</summary>
    public static IEnumerable<(string RelPath, string Gas)> WriteByFile(IEnumerable<PlacedObject> objs,
        IReadOnlyDictionary<uint, string>? convBindings = null)
    {
        var byFile = new Dictionary<string, List<PlacedObject>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var o in objs)
        {
            if (!byFile.TryGetValue(o.File, out var list)) byFile[o.File] = list = new List<PlacedObject>();
            list.Add(o);
        }
        foreach (var (file, list) in byFile)
            yield return ("objects/" + file, WriteFile(list, convBindings));
    }

    private static string F(float v) => v.ToString("0.0######", CultureInfo.InvariantCulture);
}
