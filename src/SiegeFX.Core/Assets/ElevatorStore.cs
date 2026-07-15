using System.Globalization;
using System.Numerics;
using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>SC-ELEVATOR — one parsed elevator gizmo from a region's
/// <c>objects/elevator.gas</c>. DS1 elevators are moving SIEGE NODES: the car
/// (<see cref="CarNodeGuid"/>) is a real snode in nodes.gas that travels
/// between two stops. Each stop is defined by door alignment — the car's
/// <c>elevator_door_levelN</c> mates with the connect node's
/// <c>connect_door_levelN</c>, exactly the composition RegionLayout's BFS uses
/// for static placement. The <c>_2s_1c_1n</c> template family shares one
/// connect node for both stops (different door pairs); <c>_2s_1c_2n</c>
/// authors one connect node per level.
///
/// While moving, the engine fires <c>movingN_actioninfo</c> — semicolon-joined
/// fade_nodes tuples <c>region,section,level,object,mode[,delaySeconds]</c>
/// (the farm grate lift swaps the surface/basement cutaway sections mid-ride)
/// — and posts <c>movingN_message</c> at <c>movingN_scid</c> (the farm lift
/// targets its winch sound emitter). moving1 = departing level 1 (the fade
/// content confirms: it hides the surface and reveals the basement bulk);
/// moving2 = departing level 2.</summary>
public sealed record ElevatorDef(
    uint Scid,
    string TemplateName,
    string RegionPath,
    uint CarNodeGuid,
    uint Connect1Guid, int Connect1DoorId, int CarDoor1Id,
    uint Connect2Guid, int Connect2DoorId, int CarDoor2Id,
    float DurationSeconds,
    string Moving1ActionInfo, string Moving2ActionInfo,
    string Moving1Message, string Moving2Message,
    uint Moving1Scid, uint Moving2Scid,
    uint Level2Scid);

/// <summary>SC-STAIRWELL — the crypt rotating-stairwell mechanism
/// (<c>elevator_hidden_stairwell</c> / <c>_act_deact</c>): N stair-segment
/// snodes realign between the shaft node's authored <c>_up</c> and
/// <c>_down</c> door sets when a lever activates the gizmo — DS1's "secret
/// staircase rotates to the lower exit". Segment i mates one of ITS doors to
/// <c>stairwell_door{i}_up</c> or <c>_down</c> on
/// <see cref="StairwellNodeGuid"/>; the segment-side door is derived at load
/// from the authored nodes.gas link (whichever pose nodes.gas composes IS
/// one of the two stops).</summary>
public sealed record StairwellDef(
    uint Scid,
    string TemplateName,
    string RegionPath,
    uint StairwellNodeGuid,
    float DurationSeconds,
    IReadOnlyList<(uint StairNodeGuid, int UpDoorId, int DownDoorId)> Segments);

public static class ElevatorStore
{
    public const string FileName = "elevator.gas";

    /// <summary>SC-STAIRWELL — parse every hidden-stairwell gizmo in the
    /// region (component header contains "hidden_stairwell"). Same file as
    /// the 2-stop elevators; those parse via <see cref="Load"/>.</summary>
    public static (IReadOnlyList<StairwellDef> Defs, IReadOnlyList<string> Diagnostics) LoadStairwells(
        TankReader tank, string regionPath)
    {
        var (placements, diags) = RegionObjects.LoadPlacements(tank, regionPath, FileName);
        if (placements.Count == 0) return (Array.Empty<StairwellDef>(), diags);
        var extraDiags = new List<string>();
        var defs = new List<StairwellDef>();
        foreach (var p in placements)
        {
            GasNode? comp = null;
            foreach (var c in p.Node.Children)
                if ((c.Header ?? "").Contains("hidden_stairwell", StringComparison.OrdinalIgnoreCase))
                { comp = c; break; }
            if (comp is null) continue;
            uint shaft = Hex(Attr(comp, "stairwell_node"));
            if (shaft == 0)
            {
                extraDiags.Add($"{regionPath}: stairwell 0x{p.Scid:X8} authors no stairwell_node — skipped");
                continue;
            }
            var segs = new List<(uint, int, int)>();
            for (int n = 1; n <= 24; n++)
            {
                uint node = Hex(Attr(comp, $"stair_node_{n}"));
                if (node == 0) continue;
                int up = Int(Attr(comp, $"stairwell_door{n}_up"), 0);
                int down = Int(Attr(comp, $"stairwell_door{n}_down"), 0);
                if (up == 0 || down == 0)
                {
                    extraDiags.Add($"{regionPath}: stairwell 0x{p.Scid:X8} segment {n} missing up/down door ids — segment skipped");
                    continue;
                }
                segs.Add((node, up, down));
            }
            if (segs.Count == 0)
            {
                extraDiags.Add($"{regionPath}: stairwell 0x{p.Scid:X8} authors no stair segments — skipped");
                continue;
            }
            defs.Add(new StairwellDef(p.Scid, p.TemplateName, regionPath, shaft,
                Float(Attr(comp, "duration"), 5f), segs));
        }
        return (defs, extraDiags);
    }

    /// <summary>Parse every elevator gizmo in <paramref name="regionPath"/>.
    /// Regions without the file (or with the 39-byte empty stub DS1 ships
    /// everywhere) return an empty list. All fields are authored per instance
    /// in shipped data; a placement without an <c>elevator_node</c> is logged
    /// by the caller via the returned diagnostics.</summary>
    public static (IReadOnlyList<ElevatorDef> Defs, IReadOnlyList<string> Diagnostics) Load(
        TankReader tank, string regionPath)
    {
        var (placements, diags) = RegionObjects.LoadPlacements(tank, regionPath, FileName);
        if (placements.Count == 0) return (Array.Empty<ElevatorDef>(), diags);

        var extraDiags = new List<string>(diags);
        var defs = new List<ElevatorDef>(placements.Count);
        foreach (var p in placements)
        {
            // Component block header matches the template name
            // (elevator_2s_1c_1n / elevator_2s_1c_2n); fall back to any child
            // block that authors elevator_node so unknown variants still parse.
            GasNode? comp = null;
            foreach (var c in p.Node.Children)
                if (string.Equals(c.Header, p.TemplateName, StringComparison.OrdinalIgnoreCase)) { comp = c; break; }
            if (comp is null)
                foreach (var c in p.Node.Children)
                    if (Attr(c, "elevator_node") is not null) { comp = c; break; }
            if (comp is null)
            {
                extraDiags.Add($"{regionPath}: elevator 0x{p.Scid:X8} ({p.TemplateName}) has no component block — skipped");
                continue;
            }

            uint car = Hex(Attr(comp, "elevator_node"));
            if (car == 0)
            {
                extraDiags.Add($"{regionPath}: elevator 0x{p.Scid:X8} authors no elevator_node — skipped");
                continue;
            }

            uint shared = Hex(Attr(comp, "connect_node"));
            uint c1 = Hex(Attr(comp, "connect_node_level1"));
            uint c2 = Hex(Attr(comp, "connect_node_level2"));
            if (c1 == 0) c1 = shared;
            if (c2 == 0) c2 = shared;
            if (c1 == 0 || c2 == 0)
            {
                extraDiags.Add($"{regionPath}: elevator 0x{p.Scid:X8} missing connect node(s) — skipped");
                continue;
            }

            defs.Add(new ElevatorDef(
                Scid: p.Scid,
                TemplateName: p.TemplateName,
                RegionPath: regionPath,
                CarNodeGuid: car,
                Connect1Guid: c1,
                Connect1DoorId: Int(Attr(comp, "connect_door_level1"), 1),
                CarDoor1Id: Int(Attr(comp, "elevator_door_level1"), 1),
                Connect2Guid: c2,
                Connect2DoorId: Int(Attr(comp, "connect_door_level2"), 1),
                CarDoor2Id: Int(Attr(comp, "elevator_door_level2"), 1),
                DurationSeconds: Float(Attr(comp, "duration"), 5f),
                Moving1ActionInfo: Str(Attr(comp, "moving1_actioninfo")),
                Moving2ActionInfo: Str(Attr(comp, "moving2_actioninfo")),
                Moving1Message: StrOr(Attr(comp, "moving1_message"), "we_req_activate"),
                Moving2Message: StrOr(Attr(comp, "moving2_message"), "we_req_activate"),
                Moving1Scid: Hex(Attr(comp, "moving1_scid")),
                Moving2Scid: Hex(Attr(comp, "moving2_scid")),
                Level2Scid: Hex(Attr(comp, "level2_scid"))));
        }
        return (defs, extraDiags);
    }

    static string? Attr(GasNode node, string name)
    {
        foreach (var a in node.Attributes)
            if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                return a.Value;
        return null;
    }

    static string Str(string? v) => v?.Trim().Trim('"') ?? "";
    static string StrOr(string? v, string fallback)
    {
        var s = Str(v);
        return s.Length == 0 ? fallback : s;
    }

    static uint Hex(string? v)
    {
        if (v is null) return 0;
        var s = v.Trim().Trim('"');
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u) ? u : 0;
    }

    static int Int(string? v, int fallback) =>
        v is not null && int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;

    static float Float(string? v, float fallback)
    {
        if (v is null) return fallback;
        var s = v.Trim().TrimEnd('f', 'F');
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;
    }
}
