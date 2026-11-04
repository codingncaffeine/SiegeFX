using System.Numerics;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeFX.Runtime.Render;

// ── SC-ELEVATOR-INSTANT — the multiplayer_world town teleporters ────────────
// Every town's transport stone is an elevator_instant_4s_1c gizmo (see
// InstantElevatorSpec): the town's car and its twin in the new_hub island
// rise up their shafts, swap ends, and sink — whoever stood on the town car
// steps off in the hub. new_hub has no stitch to anything, so it is streamed
// on demand and placed as an island (WorldLayout islandOffsets). The far end
// of a teleporter is streamed the first time someone presses its button: the
// press is queued and handled from TickElevators, never inside the trigger
// dispatch that delivered it (streaming adds triggers and rebuilds nav).
public sealed partial class RenderHost
{
    // Island regions' pinned world offsets (see PreloadAroundRegion).
    private readonly Dictionary<string, Matrix4x4> _islandRegionOffsets = new(StringComparer.OrdinalIgnoreCase);
    // Instant gizmos parsed from a streamed region whose far end is not
    // streamed yet, by gizmo scid.
    private readonly Dictionary<uint, ElevatorDef> _pendingInstantElevators = new();
    // Queued activations (true = start the ride once placed).
    private readonly Dictionary<uint, bool> _instantElevatorRequests = new();
    private Dictionary<uint, string>? _nodeRegionIndex;
    // Objects placed on an elevator car node, with their pose relative to it.
    private readonly Dictionary<StaticPropInstance, Matrix4x4> _carPropLocal = new();

    /// <summary>Islands sit far above the stitched world on a grid keyed by
    /// the region's sorted index — the same spot on every machine of a co-op
    /// game, clear of every real region and of each other.</summary>
    private static Matrix4x4 IslandRegionOffset(int regionIndex)
    {
        const float Spacing = 2500f;
        int col = regionIndex % 12, row = regionIndex / 12;
        return Matrix4x4.CreateTranslation((col - 6) * Spacing, 6000f, (row - 6) * Spacing);
    }

    /// <summary>Instant gizmo scid (or the scid of its other end) the client
    /// can resolve without the far end streamed: its pending def.</summary>
    private bool TryGetPendingInstant(uint scid, out ElevatorDef def)
    {
        if (_pendingInstantElevators.TryGetValue(scid, out def!)) return true;
        foreach (var d in _pendingInstantElevators.Values)
            if (d.Instant!.OtherElevatorScid == scid) { def = d; return true; }
        def = null!;
        return false;
    }

    private void RequestInstantElevator(uint scid, bool startRide)
    {
        _instantElevatorRequests[scid] = startRide || (_instantElevatorRequests.TryGetValue(scid, out var s) && s);
        Console.WriteLine($"[elevator] 0x{scid:X8} teleporter called — streaming its far end");
    }

    /// <summary>From TickElevators: stream the regions a queued teleporter
    /// needs, place it, then ride (or apply the host's word).</summary>
    private void ProcessInstantElevatorRequests()
    {
        if (_instantElevatorRequests.Count == 0) return;
        var requests = _instantElevatorRequests.ToList();
        _instantElevatorRequests.Clear();
        foreach (var (scid, startRide) in requests)
        {
            if (!_elevatorsByScid.ContainsKey(scid) && TryGetPendingInstant(scid, out var def))
            {
                var spec = def.Instant!;
                var nodes = new[] { def.CarNodeGuid, spec.Car2NodeGuid,
                                    spec.Dock(0, 1, 1).ConnectGuid, spec.Dock(0, 2, 1).ConnectGuid };
                foreach (var g in nodes)
                {
                    if (TryGetNodeMeshGuid(g, out _)) continue;
                    var region = RegionOfNode(def.RegionPath, g);
                    if (region is null)
                    {
                        Console.WriteLine($"[elevator] 0x{def.Scid:X8} node 0x{g:X8} is in no region of this map — cannot place");
                        continue;
                    }
                    if (_loadedRegions.ContainsKey(region)) continue;
                    OnPlayerRegionChanged(region, updateAnchor: false);
                }
                if (!_elevatorsByScid.ContainsKey(def.Scid))
                {
                    LoadElevators(new[] { def.RegionPath });
                    if (_elevatorsByScid.ContainsKey(def.Scid))
                    {
                        var nav = RebuildNavMesh();
                        if (nav is not null) { _navMesh = nav; MarkAllObstacles(); RehomeAllFollowers(nav); }
                    }
                }
            }
            if (!_elevatorsByScid.TryGetValue(scid, out var el))
            {
                Console.WriteLine($"[elevator] 0x{scid:X8} teleporter could not be placed — press ignored");
                continue;
            }
            if (startRide)
                StartElevatorRide(el);
        }
    }

    /// <summary>Region path holding <paramref name="nodeGuid"/>, from every
    /// region's index/streamer_node_index.gas (indexed once).</summary>
    private string? RegionOfNode(string anyRegionPath, uint nodeGuid)
    {
        if (_playMapTank is null) return null;
        if (_nodeRegionIndex is null)
        {
            _nodeRegionIndex = new Dictionary<uint, string>();
            int at = anyRegionPath.IndexOf("/regions/", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;
            var prefix = anyRegionPath[..(at + "/regions/".Length)];
            var reader = new TankReader(_playMapTank);
            foreach (var f in reader.ListFiles())
            {
                if (!f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!f.EndsWith("/index/streamer_node_index.gas", StringComparison.OrdinalIgnoreCase)) continue;
                var region = f[..^"/index/streamer_node_index.gas".Length];
                try
                {
                    var doc = GasDocument.Load(reader.ExtractToMemory(f));
                    void Walk(GasNode n)
                    {
                        foreach (var a in n.Attributes)
                        {
                            var v = a.Value.Trim();
                            if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                                && uint.TryParse(v[2..], System.Globalization.NumberStyles.HexNumber,
                                                 System.Globalization.CultureInfo.InvariantCulture, out var g))
                                _nodeRegionIndex.TryAdd(g, region);
                        }
                        foreach (var c in n.Children) Walk(c);
                    }
                    foreach (var root in doc.Roots) Walk(root);
                }
                catch { /* an unreadable index just leaves its nodes unresolved */ }
            }
        }
        return _nodeRegionIndex.TryGetValue(nodeGuid, out var r) ? r : null;
    }

    /// <summary>Place an instant elevator once both cars and both locations
    /// are streamed. The town and hub gizmos of one teleporter drive the same
    /// two cars from opposite ends, and their stop numbers coincide (each
    /// one's stop 1 = its own car at its own location 1 = the other's car at
    /// the other's location 1), so the second one only aliases its scid.</summary>
    private bool TryPlaceInstantElevator(ElevatorDef def, Func<uint, SnoModel?> resolve)
    {
        var spec = def.Instant!;
        foreach (var other in _elevators)
        {
            if (other.Def.Instant is not { } os) continue;
            bool sameCars = (other.Def.CarNodeGuid == def.CarNodeGuid && os.Car2NodeGuid == spec.Car2NodeGuid)
                         || (other.Def.CarNodeGuid == spec.Car2NodeGuid && os.Car2NodeGuid == def.CarNodeGuid);
            if (!sameCars) continue;
            _elevatorsByScid[def.Scid] = other;
            _pendingInstantElevators.Remove(def.Scid);
            Console.WriteLine($"[elevator] 0x{def.Scid:X8} is the far end of teleporter 0x{other.Def.Scid:X8}");
            return true;
        }

        uint car1 = def.CarNodeGuid, car2 = spec.Car2NodeGuid;
        if (!TryGetNodeMeshGuid(car1, out var m1) || !TryGetNodeMeshGuid(car2, out var m2)) return false;
        var sno1 = resolve(m1);
        var sno2 = resolve(m2);
        if (sno1 is null || sno2 is null || _regionLayout is null) return false;
        var poses = new Matrix4x4[8];
        for (int car = 0; car < 2; car++)
            for (int loc = 1; loc <= 2; loc++)
                for (int lv = 1; lv <= 2; lv++)
                {
                    var d = spec.Dock(car, loc, lv);
                    if (!TryGetNodeMeshGuid(d.ConnectGuid, out var cm) || resolve(cm) is not { } csno ||
                        !_regionLayout.TryGetTransform(d.ConnectGuid, out var cw))
                        return false;   // that location is not streamed yet
                    if (!RegionLayout.TryAlignThroughDoor(csno, cw, d.ConnectDoorId, car == 0 ? sno1 : sno2,
                                                          d.CarDoorId, out var pose))
                    {
                        Console.WriteLine($"[elevator] 0x{def.Scid:X8} car {car + 1} dock location{loc} level{lv} " +
                                          $"(doors {d.ConnectDoorId}/{d.CarDoorId}) does not align — skipped");
                        _pendingInstantElevators.Remove(def.Scid);
                        return false;
                    }
                    poses[InstantElevatorPlan.DockIndex(car, loc, lv)] = pose;
                }

        // The stop nodes.gas composes: car 1 authored against location 2 = stop 2.
        int atStop = 1;
        foreach (var (_, graph, _) in _worldRegionGraphs)
            if (graph.TryGetNode(car1, out var n))
            {
                bool l1 = false, l2 = false;
                foreach (var d in n.Doors)
                {
                    if (d.FarGuid == def.Connect1Guid) l1 = true;
                    if (d.FarGuid == def.Connect2Guid) l2 = true;
                }
                if (l2 && !l1) atStop = 2;
                break;
            }

        var rt = new ElevatorRuntime
        {
            Def = def,
            Stop1 = poses[InstantElevatorPlan.DockIndex(0, 1, 1)],
            Stop2 = poses[InstantElevatorPlan.DockIndex(0, 2, 1)],
            InstantPoses = poses,
        };
        _elevators.Add(rt);
        _elevatorsByScid[def.Scid] = rt;
        if (spec.OtherElevatorScid != 0) _elevatorsByScid.TryAdd(spec.OtherElevatorScid, rt);
        _pendingInstantElevators.Remove(def.Scid);
        if (spec.OtherElevatorScid != 0) _pendingInstantElevators.Remove(spec.OtherElevatorScid);
        SnapElevatorToStop(rt, atStop);

        // A save taken with the cars swapped parks them swapped.
        for (int i = _pendingElevatorSnaps.Count - 1; i >= 0; i--)
        {
            var es = _pendingElevatorSnaps[i];
            if (es.Scid != def.Scid && es.Scid != spec.OtherElevatorScid) continue;
            SnapElevatorToStop(rt, es.AtStop);
            _pendingElevatorSnaps.RemoveAt(i);
        }
        var a = poses[InstantElevatorPlan.DockIndex(0, 1, 1)].Translation;
        var b = poses[InstantElevatorPlan.DockIndex(0, 2, 1)].Translation;
        Console.WriteLine($"[elevator] 0x{def.Scid:X8} {def.TemplateName} cars 0x{car1:X8}/0x{car2:X8} " +
                          $"location1=({a.X:F1},{a.Y:F1},{a.Z:F1}) location2=({b.X:F1},{b.Y:F1},{b.Z:F1}) — at stop{rt.AtStop}");
        return true;
    }

    private Matrix4x4 InstantPose(ElevatorRuntime el, int car, int location, int level) =>
        el.InstantPoses![InstantElevatorPlan.DockIndex(car, location, level)];

    /// <summary>Park a lift at <paramref name="stop"/> (both cars of a
    /// teleporter) — save restore, co-op host state, placement.</summary>
    private void SnapElevatorToStop(ElevatorRuntime el, int stop)
    {
        el.AtStop = el.TargetStop = stop == 2 ? 2 : 1;
        el.Moving = false;
        el.T = 0f;
        el.Phase = 0;
        var pose = el.AtStop == 2 ? el.Stop2 : el.Stop1;
        el.PrevCarPos = pose.Translation;
        _elevatorNodeOverrides[el.Def.CarNodeGuid] = pose;
        UpdateElevatorCarInstance(el, pose);
        if (el.Def.Instant is { } spec)
        {
            var pose2 = InstantPose(el, 1, InstantElevatorPlan.CarLocation(1, el.AtStop), 1);
            el.PrevCar2Pos = pose2.Translation;
            _elevatorNodeOverrides[spec.Car2NodeGuid] = pose2;
            UpdateCarNodeInstance(spec.Car2NodeGuid, ref el.Car2InstanceIdx, pose2);
        }
    }

    private void StartInstantElevatorRide(ElevatorRuntime el)
    {
        var spec = el.Def.Instant!;
        el.Ride = InstantElevatorPlan.Ride(spec, el.AtStop);
        el.TargetStop = el.Ride.ToStop;
        el.Moving = true;
        el.Phase = 0;
        el.T = 0f;
        el.ActionFired = false;
        el.PrevCarPos = InstantPose(el, 0, el.Ride.Car1.AscendLocation, 1).Translation;
        el.PrevCar2Pos = InstantPose(el, 1, el.Ride.Car2.AscendLocation, 1).Translation;
        CaptureCarRiders(el.Def.CarNodeGuid, el.CarInstanceIdx, el.Riders);
        CaptureCarRiders(spec.Car2NodeGuid, el.Car2InstanceIdx, el.Riders2);
        RunInstantEffect(el, spec.UpEffect, 0);
        Console.WriteLine($"[elevator] 0x{el.Def.Scid:X8} teleporter stop{el.Ride.FromStop} -> stop{el.Ride.ToStop}: " +
                          $"rise {el.Ride.AscendSeconds:F1}s, swap, sink {el.Ride.DescendSeconds:F1}s " +
                          $"({el.Riders.Count}+{el.Riders2.Count} rider(s))");
    }

    /// <summary>up_effect / down_effect on both cars (the gizmo and its
    /// other_elevator stand on them).</summary>
    private void RunInstantEffect(ElevatorRuntime el, string effect, int phase)
    {
        var spec = el.Def.Instant!;
        if (effect.Length == 0) return;
        var local = spec.GizmoNodeGuid == el.Def.CarNodeGuid || spec.GizmoNodeGuid == spec.Car2NodeGuid
            ? spec.GizmoLocalPosition : Vector3.Zero;
        for (int car = 0; car < 2; car++)
        {
            var leg = el.Ride.Leg(car);
            var pose = phase == 0 ? InstantPose(el, car, leg.AscendLocation, 1) : InstantPose(el, car, leg.ArriveLocation, 2);
            OnTriggerCallSfxScript(effect, null, Vector3.Transform(local, pose));
        }
    }

    /// <summary>The skrit's two transitions: rise at the old location, pop
    /// to the top of the other one (riders jump with their car), sink.</summary>
    private void TickInstantElevator(ElevatorRuntime el, float dt)
    {
        var spec = el.Def.Instant!;
        float dur = el.Phase == 0 ? el.Ride.AscendSeconds : el.Ride.DescendSeconds;
        el.T += dt / MathF.Max(0.25f, dur);
        float k = Math.Clamp(el.T, 0f, 1f);
        k = k * k * (3f - 2f * k);
        for (int car = 0; car < 2; car++)
        {
            var leg = el.Ride.Leg(car);
            var pose = el.Phase == 0
                ? LerpPose(InstantPose(el, car, leg.AscendLocation, 1), InstantPose(el, car, leg.AscendLocation, 2), k)
                : LerpPose(InstantPose(el, car, leg.ArriveLocation, 2), InstantPose(el, car, leg.ArriveLocation, 1), k);
            MoveInstantCar(el, car, pose);
        }

        if (!el.ActionFired && el.T * dur >= InstantRide.ActionDelay(dur))
        {
            el.ActionFired = true;
            for (int car = 0; car < 2; car++)
            {
                var leg = el.Ride.Leg(car);
                ApplyInstantActionInfo(el.Phase == 0 ? leg.AscendActionInfo : leg.ArriveActionInfo,
                                       car == 0 ? el.Riders : el.Riders2);
            }
        }

        if (el.T < 1f) return;
        if (el.Phase == 0)
        {
            // OnExitState of LocationN_ascending: SRequestNodeConnection pops
            // each car to level 2 of the other location.
            el.Phase = 1;
            el.T = 0f;
            el.ActionFired = false;
            for (int car = 0; car < 2; car++)
                MoveInstantCar(el, car, InstantPose(el, car, el.Ride.Leg(car).ArriveLocation, 2));
            RunInstantEffect(el, spec.DownEffect, 1);
            Console.WriteLine($"[elevator] 0x{el.Def.Scid:X8} teleporter cars swapped ends");
            return;
        }

        el.Moving = false;
        el.AtStop = el.TargetStop;
        el.Phase = 0;
        for (int car = 0; car < 2; car++)
        {
            var final = InstantPose(el, car, InstantElevatorPlan.CarLocation(car, el.AtStop), 1);
            MoveInstantCar(el, car, final);
            _elevatorNodeOverrides[car == 0 ? el.Def.CarNodeGuid : spec.Car2NodeGuid] = final;
        }
        var riders = new List<ActorRenderState>(el.Riders.Count + el.Riders2.Count);
        riders.AddRange(el.Riders);
        riders.AddRange(el.Riders2);
        el.Riders.Clear();
        el.Riders2.Clear();
        CompleteElevatorArrival(el, riders);
    }

    private void MoveInstantCar(ElevatorRuntime el, int car, Matrix4x4 pose)
    {
        Vector3 delta;
        if (car == 0)
        {
            UpdateElevatorCarInstance(el, pose);
            delta = pose.Translation - el.PrevCarPos;
            el.PrevCarPos = pose.Translation;
        }
        else
        {
            UpdateCarNodeInstance(el.Def.Instant!.Car2NodeGuid, ref el.Car2InstanceIdx, pose);
            delta = pose.Translation - el.PrevCar2Pos;
            el.PrevCar2Pos = pose.Translation;
        }
        if (delta == Vector3.Zero) return;
        foreach (var r in car == 0 ? el.Riders : el.Riders2) CarryRider(r, delta);
    }

    /// <summary>SSendActionToPartyMembersRemainingInNode: the multiplayer
    /// teleporters author mood names (multiplayer_world_teleport darkens the
    /// shaft, multiplayer_world_new_hub_teleport sets the arrival); a mood is
    /// this machine's view, so it applies when this machine's hero rides that
    /// car. Comma tuples are fade_nodes like the 2-stop lifts.</summary>
    private void ApplyInstantActionInfo(string info, List<ActorRenderState> riders)
    {
        if (info.Length == 0) return;
        if (info.Contains(',')) { ApplyElevatorActionInfo(info); return; }
        if (_player is null || !riders.Contains(_player)) return;
        foreach (var mood in info.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            OnTriggerMoodChange(mood);
    }

    /// <summary>Arrival: riders walk again on a nav mesh re-baked with the
    /// car at its new stop (shared by every lift kind).</summary>
    private void CompleteElevatorArrival(ElevatorRuntime el, List<ActorRenderState> riders)
    {
        var heldOrder = ReleaseRiders(riders);
        // Re-bake nav so the car floor welds into the arrival stop and
        // riders can walk off. Same swap pattern as region streaming.
        var nav = RebuildNavMesh();
        if (nav is not null)
        {
            _navMesh = nav;
            MarkAllObstacles();
            RehomeAllFollowers(nav);
            if (_player is not null && _playerFollower is not null)
            {
                var pos = _player.CurrentTransform.Translation;
                var speed = _playerFollower.Speed;
                _playerFollower = new SiegeFX.Core.Nav.NavFollower(nav, pos, speed)
                {
                    Traversal = SiegeFX.Core.Nav.NavTraversal.Player,
                    PartialPathFallback = true,
                    DiagnosticLogging = true,
                };
            }
            // Party riders get a fresh follower on the new mesh at
            // their ridden position (their old follower still holds
            // the pre-ride mesh — see RebuildFollowerBrain remesh).
            foreach (var r in riders)
            {
                if (ReferenceEquals(r, _player) || !r.IsPartyMember || r.IsDead) continue;
                var baseStats = r.Actor.Stats;
                var combatStats = InjectFollowerWeapon(r.Actor.Template, baseStats) ?? baseStats;
                RebuildFollowerBrain(r, combatStats, remesh: true);
            }
            // ALPHA-2 SHAFT RESCUE — anyone left standing where the
            // rebuilt mesh has no floor (the car departed under their
            // feet: its tris moved to the far stop, leaving an open
            // shaft) gets nudged to the nearest real floor. Without
            // this the follower's vertical re-bind gate correctly
            // refuses the shaft bottom 12u below — and the player
            // freezes over the hole with every click refused.
            RescueOffMeshAfterRebuild(nav);
        }
        riders.Clear();
        if (heldOrder is { } ho) _playerFollower?.SetTarget(ho);
        Console.WriteLine($"[elevator] 0x{el.Def.Scid:X8} arrived at stop{el.AtStop}");
    }

    /// <summary>Objects placed on a car node ride it (DS1 places everything
    /// node-relative; the teleporter's button and its use points sit on the
    /// car). Their load-time pose came from the static layout.</summary>
    private void MoveCarNodeProps(uint carGuid, Matrix4x4 pose)
    {
        if (_regionLayout is null) return;
        Matrix4x4 invLayout = default;
        bool haveInv = false;
        foreach (var prop in _staticProps)
        {
            if (prop.NodeGuid != carGuid) continue;
            if (!_carPropLocal.TryGetValue(prop, out var local))
            {
                if (!haveInv)
                {
                    if (!_regionLayout.TryGetTransform(carGuid, out var layoutPose) ||
                        !Matrix4x4.Invert(layoutPose, out invLayout))
                        return;
                    haveInv = true;
                }
                local = prop.World * invLayout;
                _carPropLocal[prop] = local;
            }
            prop.World = local * pose;
        }
    }

    /// <summary>After props stream in: put the ones standing on a car where
    /// that car is now (they load at the car's authored pose).</summary>
    private void SyncAllCarNodeProps()
    {
        foreach (var el in _elevators)
        {
            int idx = el.CarInstanceIdx;
            if (idx >= 0 && idx < _regionInstances.Count && _regionInstances[idx].SnodeGuid == el.Def.CarNodeGuid)
                MoveCarNodeProps(el.Def.CarNodeGuid, _regionInstances[idx].World);
            else if (_elevatorNodeOverrides.TryGetValue(el.Def.CarNodeGuid, out var p1))
                MoveCarNodeProps(el.Def.CarNodeGuid, p1);
            if (el.Def.Instant is not { } spec) continue;
            int idx2 = el.Car2InstanceIdx;
            if (idx2 >= 0 && idx2 < _regionInstances.Count && _regionInstances[idx2].SnodeGuid == spec.Car2NodeGuid)
                MoveCarNodeProps(spec.Car2NodeGuid, _regionInstances[idx2].World);
            else if (_elevatorNodeOverrides.TryGetValue(spec.Car2NodeGuid, out var p2))
                MoveCarNodeProps(spec.Car2NodeGuid, p2);
        }
    }
}
