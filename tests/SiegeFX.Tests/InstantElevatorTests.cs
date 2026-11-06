using System.Numerics;
using System.Text;
using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeFX.Tests;

public class InstantElevatorTests
{
    const string Region = "/world/maps/multiplayer_world/regions/town_center";

    // town_center's transport stone as shipped (objects/regular/elevator.gas),
    // plus a farm-style 2-stop lift to prove the old form still parses.
    const string ElevatorGas = """
        [t:elevator_instant_4s_1c,n:0x03200c7a]
        {
        	[elevator_instant_4s_1c]
        	{
        	  i connect_door_location1_level1 = 9;
        	  i connect_door_location1_level2 = 11;
        	  i connect_door_location2_level1 = 20;
        	  i connect_door_location2_level2 = 18;
        	  x connect_node_location1 = 0xF81ABFCE;
        	  x connect_node_location2 = 0xD14939A3;
        	  f duration_location1_moving_to_lv1 = 16;
        	  f duration_location2_moving_to_lv1 = 16;
        	  i e2_connect_door_location1_level1 = 10;
        	  i e2_connect_door_location1_level2 = 12;
        	  i e2_connect_door_location2_level1 = 19;
        	  i e2_connect_door_location2_level2 = 17;
        	  i e2_elevator_door_location1_level1 = 2;
        	  i e2_elevator_door_location1_level2 = 2;
        	  i e2_elevator_door_location2_level1 = 2;
        	  i e2_elevator_door_location2_level2 = 2;
        	  x e2_elevator_node = 0xA9CAB56D;
        	  x elevator_node = 0x07C15973;
        		location1_moving_to_lv1_actioninfo = "multiplayer_world_town_center_teleport";
        		location1_moving_to_lv2_actioninfo = "multiplayer_world_teleport";
        		location2_moving_to_lv1_actioninfo = "multiplayer_world_new_hub_teleport";
        		location2_moving_to_lv2_actioninfo = "multiplayer_world_teleport";
        		material = "hub";
        	  x other_elevator = 0x07F00038;
        	}
        	[placement]
        	{
        	  q orientation = 0,-0.707107,0,0.707107;
        	  p position = 0,5,0,0x07c15973;
        	}
        }
        [t:elevator_2s_1c_2n,n:0x081000cb]
        {
        	[elevator_2s_1c_2n]
        	{
        	  x elevator_node = 0x59DD9E68;
        	  x connect_node_level1 = 0x11111111;
        	  x connect_node_level2 = 0x22222222;
        	  i connect_door_level1 = 3;
        	  i connect_door_level2 = 4;
        	  f duration = 4;
        	}
        	[placement]
        	{
        	  p position = 0,0,0,0x59dd9e68;
        	}
        }
        """;

    static IReadOnlyList<ElevatorDef> LoadDefs(string gas, out IReadOnlyList<string> diags)
    {
        var writer = new TankWriter { Priority = TankPriority.Factory };
        writer.Add(Region + "/objects/regular/elevator.gas", Encoding.UTF8.GetBytes(gas));
        writer.Add(Region + "/objects/regular/actor.gas", "[a] { x = 1; }"u8.ToArray()); // world-level folder probe
        var path = Path.Combine(Path.GetTempPath(), $"siegefx-elevator-{Guid.NewGuid():N}.dsmap");
        try
        {
            writer.Write(path, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            using var tank = TankFile.Open(path);
            var (defs, d) = ElevatorStore.Load(new TankReader(tank), Region);
            diags = d;
            return defs;
        }
        finally { File.Delete(path); }
    }

    static ElevatorDef TownCenterStone()
    {
        var defs = LoadDefs(ElevatorGas, out var diags);
        Assert.Empty(diags);
        return Assert.Single(defs, d => d.Scid == 0x03200C7A);
    }

    [Fact]
    public void TheTownTeleporterParsesBothCarsAndBothLocations()
    {
        var def = TownCenterStone();
        var spec = Assert.IsType<InstantElevatorSpec>(def.Instant);
        Assert.Equal(0x07C15973u, def.CarNodeGuid);
        Assert.Equal(0xA9CAB56Du, spec.Car2NodeGuid);
        Assert.Equal(0x07F00038u, spec.OtherElevatorScid);
        // Car 1 (elevator_node): unset elevator doors default to 1.
        Assert.Equal(new InstantDock(0xF81ABFCE, 9, 1), spec.Dock(0, 1, 1));
        Assert.Equal(new InstantDock(0xF81ABFCE, 11, 1), spec.Dock(0, 1, 2));
        Assert.Equal(new InstantDock(0xD14939A3, 20, 1), spec.Dock(0, 2, 1));
        Assert.Equal(new InstantDock(0xD14939A3, 18, 1), spec.Dock(0, 2, 2));
        // Car 2 (e2_elevator_node) docks with its door 2.
        Assert.Equal(new InstantDock(0xF81ABFCE, 10, 2), spec.Dock(1, 1, 1));
        Assert.Equal(new InstantDock(0xD14939A3, 17, 2), spec.Dock(1, 2, 2));
        // The base record describes car 1's level-1 stops.
        Assert.Equal((0xF81ABFCEu, 9, 1), (def.Connect1Guid, def.Connect1DoorId, def.CarDoor1Id));
        Assert.Equal((0xD14939A3u, 20, 1), (def.Connect2Guid, def.Connect2DoorId, def.CarDoor2Id));
        // The skrit's 2s rise default, the authored 16s sink.
        Assert.Equal(new[] { 2f, 2f }, spec.AscendSeconds);
        Assert.Equal(new[] { 16f, 16f }, spec.DescendSeconds);
        Assert.Equal("hub_plate_up", spec.UpEffect);
        Assert.Equal(0x07C15973u, spec.GizmoNodeGuid);
        Assert.Equal(new Vector3(0, 5, 0), spec.GizmoLocalPosition);
    }

    [Fact]
    public void TwoStopLiftsStillParseWithoutTheInstantPart()
    {
        var lift = Assert.Single(LoadDefs(ElevatorGas, out _), d => d.Scid == 0x081000CB);
        Assert.Null(lift.Instant);
        Assert.Equal((0x11111111u, 3), (lift.Connect1Guid, lift.Connect1DoorId));
        Assert.Equal((0x22222222u, 4), (lift.Connect2Guid, lift.Connect2DoorId));
        Assert.Equal(4f, lift.DurationSeconds);
    }

    [Fact]
    public void AnInstantElevatorMissingAConnectDoorIsSkippedWithADiagnostic()
    {
        var gas = ElevatorGas.Replace("i e2_connect_door_location2_level2 = 17;", "");
        var defs = LoadDefs(gas, out var diags);
        Assert.DoesNotContain(defs, d => d.Scid == 0x03200C7A);
        Assert.Contains(diags, d => d.Contains("e2_connect_door_location2_level2"));
    }

    [Fact]
    public void ARideFromTheTownRisesThereSwapsAndSinksInTheHub()
    {
        var spec = TownCenterStone().Instant!;
        var ride = InstantElevatorPlan.Ride(spec, fromStop: 1);
        Assert.Equal((1, 2), (ride.FromStop, ride.ToStop));
        Assert.Equal(2f, ride.AscendSeconds);    // duration_location1_moving_to_lv2
        Assert.Equal(16f, ride.DescendSeconds);  // duration_location2_moving_to_lv1
        // The town car rises in the town and lands in the hub ...
        Assert.Equal((1, 2), (ride.Car1.AscendLocation, ride.Car1.ArriveLocation));
        Assert.Equal("multiplayer_world_teleport", ride.Car1.AscendActionInfo);
        Assert.Equal("multiplayer_world_new_hub_teleport", ride.Car1.ArriveActionInfo);
        // ... while the hub car makes the opposite trip.
        Assert.Equal((2, 1), (ride.Car2.AscendLocation, ride.Car2.ArriveLocation));
        Assert.Equal("multiplayer_world_town_center_teleport", ride.Car2.ArriveActionInfo);
        Assert.Equal(0.5f, InstantRide.ActionDelay(ride.DescendSeconds));
        Assert.Equal(0.25f, InstantRide.ActionDelay(0.5f));
    }

    [Fact]
    public void TheReturnRideMirrorsIt()
    {
        var spec = TownCenterStone().Instant!;
        var ride = InstantElevatorPlan.Ride(spec, fromStop: 2);
        Assert.Equal((2, 1), (ride.FromStop, ride.ToStop));
        Assert.Equal((2, 1), (ride.Car1.AscendLocation, ride.Car1.ArriveLocation));
        Assert.Equal((1, 2), (ride.Car2.AscendLocation, ride.Car2.ArriveLocation));
        Assert.Equal("multiplayer_world_town_center_teleport", ride.Car1.ArriveActionInfo);
        // Cars always sit at opposite locations.
        Assert.Equal(1, InstantElevatorPlan.CarLocation(0, 1));
        Assert.Equal(2, InstantElevatorPlan.CarLocation(1, 1));
        Assert.Equal(2, InstantElevatorPlan.CarLocation(0, 2));
        Assert.Equal(1, InstantElevatorPlan.CarLocation(1, 2));
    }

    // ── the button trigger: pressed AND someone on the platform ─────────────
    sealed class NodeCtx : TriggerContext
    {
        public bool OnPlatform;
        public readonly List<uint> Sent = new();
        public override bool PartyMemberWithinNode(uint regionGuid, int nodeSection, int nodeLevel, int nodeObject)
            => OnPlatform && regionGuid == 0xD5A3ACD9 && nodeSection == 4;
        public override void PostWorldMessage(string name, uint fromScid, uint toScid) => Sent.Add(toScid);
    }

    static TriggerRuntime ButtonTrigger()
    {
        // trigger_snd_msg_activate 0x03200cae, town_center special.gas.
        var doc = GasDocument.Parse("""
            [common] { [instance_triggers] {
              [*] {
                action* = send_world_message("we_req_activate",0x03200C7A,0f,"default","",0);
                condition* = receive_world_message("we_req_activate",0,"on_every_message");
                condition* = party_member_within_node(0xD5A3ACD9,4,-1,-1,"wait_for_every_message");
                b multi_player = true; b single_player = true; b single_shot = false;
              }
            } }
            """);
        var rows = doc.Roots.Single().Children.Single().Children;
        var matrix = new TriggerMatrix(rows.Select(r => TriggerRow.Parse(r, "test", null)).ToList());
        var rt = new TriggerRuntime();
        rt.Register(new TriggerInstance(0x03200CAE, 0, Vector3.Zero, matrix, startActive: true));
        return rt;
    }

    [Fact]
    public void SteppingOntoThePlatformDoesNotLaunchTheTeleporter()
    {
        var rt = ButtonTrigger();
        var ctx = new NodeCtx();
        rt.Tick(0.1, ctx);
        ctx.OnPlatform = true;
        rt.Tick(0.1, ctx);
        rt.Tick(0.1, ctx);
        Assert.Empty(ctx.Sent);
    }

    [Fact]
    public void PressingTheButtonOnThePlatformLaunchesItEveryTime()
    {
        var rt = ButtonTrigger();
        var ctx = new NodeCtx { OnPlatform = true };
        for (int press = 0; press < 3; press++)
        {
            rt.PostInboundMessage(0x03200CAE, "we_req_activate");
            rt.Tick(0.1, ctx);
            rt.Tick(0.1, ctx);
        }
        Assert.Equal(new uint[] { 0x03200C7A, 0x03200C7A, 0x03200C7A }, ctx.Sent);
    }

    [Fact]
    public void PressingTheButtonWithNobodyOnThePlatformDoesNothing()
    {
        var rt = ButtonTrigger();
        var ctx = new NodeCtx();
        rt.PostInboundMessage(0x03200CAE, "we_req_activate");
        rt.Tick(0.1, ctx);
        ctx.OnPlatform = true;   // the dropped press does not linger
        rt.Tick(0.1, ctx);
        Assert.Empty(ctx.Sent);
    }
}
