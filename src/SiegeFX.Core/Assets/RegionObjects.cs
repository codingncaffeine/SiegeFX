using System.Globalization;
using System.Numerics;
using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>A placed actor (or other object) inside a region. DS1 stores these per-region
/// as <c>objects/{kind}.gas</c>, each root block shaped like
/// <c>[t:TEMPLATE_NAME,n:SCID] { [placement] { q orientation=...; p position=...; } }</c>.
/// The SCID is the scene-unique id the engine uses to address the object at runtime
/// (quest hooks, save files, world messages).</summary>
public readonly record struct NodePlacement(
    Quaternion Orientation,
    Vector3 LocalPosition,
    uint NodeGuid);

public sealed class ActorInstance
{
    public string TemplateName { get; }
    public uint Scid { get; }                 // hex-encoded in source; we hold the numeric form
    public NodePlacement Placement { get; }
    public GasNode Node { get; }

    internal ActorInstance(string templateName, uint scid, NodePlacement placement, GasNode node)
    {
        TemplateName = templateName;
        Scid = scid;
        Placement = placement;
        Node = node;
    }

    /// <summary>Builds an ActorInstance for something not drawn from a region's
    /// actor.gas — the player character being the motivating case. Uses NodeGuid=0
    /// so <see cref="Actors.ActorSpawner.ComposeWorldTransform"/> falls through to
    /// the local pose, which means <paramref name="worldPosition"/> is interpreted
    /// directly in world space (no region-node indirection).</summary>
    public static ActorInstance CreateSynthetic(
        string templateName, uint scid, Vector3 worldPosition, Quaternion orientation)
    {
        var placement = new NodePlacement(orientation, worldPosition, NodeGuid: 0u);
        var emptyNode = new GasNode("synthetic", Array.Empty<GasNode>(), Array.Empty<GasAttribute>());
        return new ActorInstance(templateName, scid, placement, emptyNode);
    }

    public override string ToString() => $"[t:{TemplateName},n:0x{Scid:x8}]";
}

/// <summary>Loads region object files (<c>objects/actor.gas</c>,
/// <c>non_interactive.gas</c>, <c>container.gas</c>, etc.) into a flat list of
/// <see cref="ActorInstance"/> records. Every DS1 object .gas shares the
/// <c>[t:T,n:SCID] { [placement] { q,p } }</c> shape, so the same parser
/// handles them all — callers route by which file they pass.</summary>
public static class RegionObjects
{
    /// <summary>Filenames (under <c>objects/</c>) that carry visible static
    /// props alongside the actor list. <c>command.gas</c>, <c>special.gas</c>,
    /// and <c>generator.gas</c> are pure logic and are intentionally excluded.</summary>
    public static readonly IReadOnlyList<string> StaticPropFiles = new[]
    {
        "non_interactive.gas",
        "container.gas",
        "inventory.gas",
        "interactive.gas",
        "emitter.gas",
    };

    public static (IReadOnlyList<ActorInstance> Actors, IReadOnlyList<string> Diagnostics) LoadActors(
        TankReader tank, string regionPath) =>
        LoadPlacements(tank, regionPath, "actor.gas");

    /// <summary>Generic placement loader. Reads <c>{regionPath}/objects/{fileName}</c>
    /// and parses every <c>[t:T,n:SCID]</c> root block as an <see cref="ActorInstance"/>.
    /// "ActorInstance" is a misnomer for non-actor files (it just stores the placement
    /// + template name + scid + raw node) but the type already serves as the shared
    /// placement record so we keep one shape across the runtime.</summary>
    public static (IReadOnlyList<ActorInstance> Placements, IReadOnlyList<string> Diagnostics) LoadPlacements(
        TankReader tank, string regionPath, string fileName)
    {
        var diags = new List<string>();
        var norm = regionPath.TrimEnd('/');
        var actorPath = norm + "/objects/" + fileName;

        if (!tank.TryGetFile(actorPath, out _))
        {
            // Quiet: most region/file combos are simply absent (e.g. trap.gas is
            // empty in fh_r1, elevator.gas only exists in towns). Caller filters.
            return (Array.Empty<ActorInstance>(), diags);
        }

        byte[] bytes;
        try { bytes = tank.ExtractToMemory(actorPath); }
        catch (Exception ex) { diags.Add($"{actorPath}: extract failed: {ex.Message}"); return (Array.Empty<ActorInstance>(), diags); }

        GasDocument doc;
        try { doc = GasDocument.Load(bytes); }
        catch (Exception ex) { diags.Add($"{actorPath}: parse failed: {ex.Message}"); return (Array.Empty<ActorInstance>(), diags); }

        var list = new List<ActorInstance>(doc.Roots.Count);
        foreach (var node in doc.Roots)
        {
            if (!TemplateStore.TryParseHeader(node.Header, out var templateName, out var scidText))
            {
                diags.Add($"{actorPath}: malformed header '{node.Header}'"); continue;
            }
            if (!TryParseHexId(scidText, out var scid))
            {
                diags.Add($"{actorPath}: bad SCID '{scidText}' on [{node.Header}]"); continue;
            }

            var placement = node.Children.FirstOrDefault(c =>
                string.Equals(c.Header, "placement", StringComparison.OrdinalIgnoreCase));
            if (placement is null)
            {
                diags.Add($"{actorPath}: [{node.Header}] missing [placement]"); continue;
            }

            var orient = Quaternion.Identity;
            var pos = Vector3.Zero;
            uint nodeGuid = 0;
            foreach (var attr in placement.Attributes)
            {
                if (string.Equals(attr.Name, "orientation", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseQuaternion(attr.Value, out orient))
                        diags.Add($"{actorPath}: [{node.Header}] orientation='{attr.Value}' unparsable");
                }
                else if (string.Equals(attr.Name, "position", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParsePosition(attr.Value, out pos, out nodeGuid))
                        diags.Add($"{actorPath}: [{node.Header}] position='{attr.Value}' unparsable");
                }
            }

            list.Add(new ActorInstance(templateName, scid, new NodePlacement(orient, pos, nodeGuid), node));
        }

        return (list, diags);
    }

    /// <summary>Both SCIDs (<c>n:0x01c00ae0</c>) and node GUIDs in position tuples are hex
    /// with an <c>0x</c> prefix. <c>int.Parse</c> with <c>NumberStyles.HexNumber</c> refuses
    /// the prefix, so strip it first.</summary>
    static bool TryParseHexId(string s, out uint value)
    {
        value = 0;
        var span = s.AsSpan().Trim();
        if (span.Length >= 2 && span[0] == '0' && (span[1] == 'x' || span[1] == 'X')) span = span[2..];
        return uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>DS1 quaternions are stored as four comma-separated floats. Ordering in the
    /// file is <c>x,y,z,w</c> — matches <see cref="Quaternion"/>'s field order so no swizzle
    /// is needed.</summary>
    static bool TryParseQuaternion(string s, out Quaternion q)
    {
        q = Quaternion.Identity;
        var parts = s.Split(',');
        if (parts.Length != 4) return false;
        if (!TryFloat(parts[0], out var x)) return false;
        if (!TryFloat(parts[1], out var y)) return false;
        if (!TryFloat(parts[2], out var z)) return false;
        if (!TryFloat(parts[3], out var w)) return false;
        q = new Quaternion(x, y, z, w);
        return true;
    }

    /// <summary>Positions are four-tuples: three floats (node-local xyz) + a hex node GUID
    /// identifying which SNO node the position is relative to. Region placement is
    /// node-anchored in DS1 so moving a node drags its objects; world-space resolution
    /// is Phase 10c work once the siege-node transform is available.</summary>
    static bool TryParsePosition(string s, out Vector3 pos, out uint nodeGuid)
    {
        pos = Vector3.Zero;
        nodeGuid = 0;
        var parts = s.Split(',');
        if (parts.Length != 4) return false;
        if (!TryFloat(parts[0], out var x)) return false;
        if (!TryFloat(parts[1], out var y)) return false;
        if (!TryFloat(parts[2], out var z)) return false;
        if (!TryParseHexId(parts[3], out nodeGuid)) return false;
        pos = new Vector3(x, y, z);
        return true;
    }

    static bool TryFloat(string s, out float f) =>
        float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f);
}
