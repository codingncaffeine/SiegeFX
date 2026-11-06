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
    uint Level2Scid)
{
    /// <summary>SC-ELEVATOR-INSTANT — set for <c>elevator_instant_4s_1c</c>
    /// (the multiplayer_world town teleporters). The base record then
    /// describes car 1 only: Connect1/Connect2 are its level-1 docks at
    /// location 1/2, so stop 1 = car 1 at location 1.</summary>
    public InstantElevatorSpec? Instant { get; init; }
}

/// <summary>One docking pose of an instant-elevator car: the car's
/// <see cref="CarDoorId"/> mates with <see cref="ConnectDoorId"/> on
/// <see cref="ConnectGuid"/> (same door alignment as every elevator stop).</summary>
public readonly record struct InstantDock(uint ConnectGuid, int ConnectDoorId, int CarDoorId);

/// <summary>SC-ELEVATOR-INSTANT — the hub teleporter (component
/// <c>elevator_instant_4s_1c.skrit</c>). TWO cars (<c>elevator_node</c> and
/// <c>e2_elevator_node</c>) and two locations (<c>connect_node_location1/2</c>,
/// long shaft nodes), each with a level 1 (the platform at the floor) and a
/// level 2 (up the shaft). The cars always sit at opposite locations. A ride:
/// both cars rise to level 2 of their own location, then swap — each pops to
/// level 2 of the other location (SRequestNodeConnection, no travel) — and
/// sink to level 1 there. The multiplayer_world towns each pair a car with
/// one in the new_hub island region; the town and hub gizmos describe the
/// same two cars from either end (<c>other_elevator</c>).
/// Lists index by location − 1.</summary>
public sealed record InstantElevatorSpec(
    uint Car2NodeGuid,
    IReadOnlyList<InstantDock> Docks,
    IReadOnlyList<float> AscendSeconds,
    IReadOnlyList<float> DescendSeconds,
    IReadOnlyList<string> ToLevel2ActionInfo,
    IReadOnlyList<string> ToLevel1ActionInfo,
    string UpEffect,
    string DownEffect,
    uint OtherElevatorScid,
    uint GizmoNodeGuid,
    Vector3 GizmoLocalPosition)
{
    /// <summary>Car 0 = <c>elevator_node</c>, car 1 = <c>e2_elevator_node</c>.</summary>
    public InstantDock Dock(int car, int location, int level) =>
        Docks[InstantElevatorPlan.DockIndex(car, location, level)];

    public uint CarGuid(uint car1Guid, int car) => car == 0 ? car1Guid : Car2NodeGuid;
}

/// <summary>One car's moves in one instant-elevator ride: rise lv1 → lv2 at
/// <see cref="AscendLocation"/>, pop to lv2 of <see cref="ArriveLocation"/>,
/// sink to lv1 there.</summary>
public readonly record struct InstantCarLeg(
    int Car,
    int AscendLocation,
    int ArriveLocation,
    string AscendActionInfo, // locationN_moving_to_lv2_actioninfo of the rise
    string ArriveActionInfo); // locationN_moving_to_lv1_actioninfo of the arrival

public readonly record struct InstantRide(
    int FromStop, int ToStop, float AscendSeconds, float DescendSeconds,
    InstantCarLeg Car1, InstantCarLeg Car2)
{
    public InstantCarLeg Leg(int car) => car == 0 ? Car1 : Car2;

    /// <summary>The skrit's action delay: <c>min(duration / 2, 0.5)</c> into
    /// each move, the party members still on the car get the actioninfo.</summary>
    public static float ActionDelay(float duration) => MathF.Min(duration / 2f, 0.5f);
}

/// <summary>SC-ELEVATOR-INSTANT — the state machine of
/// <c>elevator_instant_4s_1c.skrit</c> as data. Stop s = car 1 at location s,
/// car 2 at the other one.</summary>
public static class InstantElevatorPlan
{
    public static int DockIndex(int car, int location, int level) => car * 4 + (location - 1) * 2 + (level - 1);

    public static int CarLocation(int car, int stop) => car == 0 ? stop : 3 - stop;

    /// <summary>The ride leaving <paramref name="fromStop"/>. Location1_start
    /// rises for <c>duration_location1_moving_to_lv2</c>; its swap lands in
    /// Location2_descending (<c>duration_location2_moving_to_lv1</c>).
    /// Location2_start mirrors it. Each car's rise takes the actioninfo of
    /// its own location's lv2 move, its arrival that of the location it
    /// lands at (OnTimer 1–4 in the skrit).</summary>
    public static InstantRide Ride(InstantElevatorSpec spec, int fromStop)
    {
        int from = fromStop == 2 ? 2 : 1, to = 3 - from;
        InstantCarLeg Leg(int car)
        {
            int rise = CarLocation(car, from), land = CarLocation(car, to);
            return new InstantCarLeg(car, rise, land,
                spec.ToLevel2ActionInfo[rise - 1], spec.ToLevel1ActionInfo[land - 1]);
        }
        return new InstantRide(from, to,
            spec.AscendSeconds[from - 1], spec.DescendSeconds[to - 1], Leg(0), Leg(1));
    }
}

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

            if (Attr(comp, "connect_node_location1") is not null || Attr(comp, "connect_node_location2") is not null)
            {
                if (ParseInstant(p, comp, regionPath, car, extraDiags) is { } inst) defs.Add(inst);
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

    /// <summary>SC-ELEVATOR-INSTANT — <c>elevator_instant_4s_1c</c>. Defaults
    /// are the skrit's: car doors 1, connect doors 0 (= unset), rise 2s,
    /// sink 10s, no actioninfo, effects hub_plate_up / hub_plate_down.</summary>
    static ElevatorDef? ParseInstant(ActorInstance p, GasNode comp, string regionPath, uint car1, List<string> diags)
    {
        uint car2 = Hex(Attr(comp, "e2_elevator_node"));
        uint loc1 = Hex(Attr(comp, "connect_node_location1"));
        uint loc2 = Hex(Attr(comp, "connect_node_location2"));
        if (car2 == 0 || loc1 == 0 || loc2 == 0)
        {
            diags.Add($"{regionPath}: instant elevator 0x{p.Scid:X8} missing e2_elevator_node / connect_node_location1/2 — skipped");
            return null;
        }
        var docks = new InstantDock[8];
        for (int c = 0; c < 2; c++)
            for (int loc = 1; loc <= 2; loc++)
                for (int lv = 1; lv <= 2; lv++)
                {
                    string pre = c == 0 ? "" : "e2_";
                    int connectDoor = Int(Attr(comp, $"{pre}connect_door_location{loc}_level{lv}"), 0);
                    int carDoor = Int(Attr(comp, $"{pre}elevator_door_location{loc}_level{lv}"), 1);
                    if (connectDoor == 0)
                    {
                        diags.Add($"{regionPath}: instant elevator 0x{p.Scid:X8} has no {pre}connect_door_location{loc}_level{lv} — skipped");
                        return null;
                    }
                    docks[InstantElevatorPlan.DockIndex(c, loc, lv)] = new InstantDock(loc == 1 ? loc1 : loc2, connectDoor, carDoor);
                }
        float Dur(string name, float fallback) => Float(Attr(comp, name), fallback);
        var spec = new InstantElevatorSpec(
            Car2NodeGuid: car2,
            Docks: docks,
            AscendSeconds: new[] { Dur("duration_location1_moving_to_lv2", 2f), Dur("duration_location2_moving_to_lv2", 2f) },
            DescendSeconds: new[] { Dur("duration_location1_moving_to_lv1", 10f), Dur("duration_location2_moving_to_lv1", 10f) },
            ToLevel2ActionInfo: new[] { Str(Attr(comp, "location1_moving_to_lv2_actioninfo")), Str(Attr(comp, "location2_moving_to_lv2_actioninfo")) },
            ToLevel1ActionInfo: new[] { Str(Attr(comp, "location1_moving_to_lv1_actioninfo")), Str(Attr(comp, "location2_moving_to_lv1_actioninfo")) },
            UpEffect: StrOr(Attr(comp, "up_effect"), "hub_plate_up"),
            DownEffect: StrOr(Attr(comp, "down_effect"), "hub_plate_down"),
            OtherElevatorScid: Hex(Attr(comp, "other_elevator")),
            GizmoNodeGuid: p.Placement.NodeGuid,
            GizmoLocalPosition: p.Placement.LocalPosition);
        var a1 = spec.Dock(0, 1, 1);
        var a2 = spec.Dock(0, 2, 1);
        return new ElevatorDef(
            Scid: p.Scid,
            TemplateName: p.TemplateName,
            RegionPath: regionPath,
            CarNodeGuid: car1,
            Connect1Guid: a1.ConnectGuid, Connect1DoorId: a1.ConnectDoorId, CarDoor1Id: a1.CarDoorId,
            Connect2Guid: a2.ConnectGuid, Connect2DoorId: a2.ConnectDoorId, CarDoor2Id: a2.CarDoorId,
            DurationSeconds: spec.AscendSeconds[0] + spec.DescendSeconds[1],
            Moving1ActionInfo: "", Moving2ActionInfo: "",
            Moving1Message: "we_req_activate", Moving2Message: "we_req_activate",
            Moving1Scid: 0, Moving2Scid: 0, Level2Scid: 0)
        { Instant = spec };
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
