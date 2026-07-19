using System.Numerics;

namespace SiegeFX.Core.Nav;

/// <summary>
/// Walks a point along a <see cref="NavMesh"/> toward a target, one tick at a time.
/// The follower is stateful: it caches a path computed from the current position to the
/// target and advances through the triangle sequence as the point enters each triangle.
///
/// Movement is purely XZ-planar in region-space; Y is resampled from whatever triangle
/// the follower is currently standing on so it sticks to the terrain. The waypoint
/// list comes from <see cref="NavFunnel"/>'s "simple stupid funnel" string-pull over
/// the triangle path, so the route hugs inside corners instead of zig-zagging through
/// every triangle centroid.
///
/// <b>Not reentrant.</b> <see cref="Tick"/> and <see cref="SetTarget"/> share the
/// pathfinder workspace; calling SetTarget from inside a Tick-driven callback (e.g. a
/// skrit event handler fired by the actor) will stomp the in-flight A* scratch. Keep
/// the follower single-threaded per actor and defer retargeting to the next tick.
/// </summary>
public sealed class NavFollower
{
    public NavMesh Mesh { get; }

    /// <summary>Current world position (region-space). Y is resampled from the triangle
    /// the follower is standing on after every tick.</summary>
    public Vector3 Position { get; private set; }

    /// <summary>Target world position (region-space). Only X/Z are used for pathing;
    /// the target's Y floats free since we snap to the goal triangle's surface Y on
    /// arrival.</summary>
    public Vector3 Target { get; private set; }

    /// <summary>Linear speed in world units per second. DS1 characters walk at roughly
    /// 5-7 u/s depending on gait; callers override as needed.</summary>
    public float Speed { get; set; }

    /// <summary>True once the follower is within <see cref="GoalRadius"/> of the target
    /// in XZ. Subsequent Tick calls are no-ops until <see cref="SetTarget"/> is called.</summary>
    public bool ReachedGoal { get; private set; }

    /// <summary>True when the follower can't reach the target (start or goal triangle
    /// not on the mesh, or triangles are in disconnected components). Tick becomes a
    /// no-op. Call <see cref="SetTarget"/> to retry with a different goal.</summary>
    public bool PathBlocked { get; private set; }

    /// <summary>XZ distance at which the follower considers the target "reached". Also
    /// used for the waypoint-advance threshold — once the follower's XZ position is
    /// within this much of the next triangle's centroid, it advances the path index.</summary>
    public float GoalRadius { get; set; } = 0.75f;

    /// <summary>SC-NAV-STAIR-DIAG — when true, the follower emits a one-line
    /// log on every stuck-recovery attempt + perpendicular escape + give-up.
    /// Default false (the 347 NPC wanderers would drown the log). RenderHost
    /// flips this true on the player's follower only so we can see what
    /// the player path is doing during a problem reproduction without
    /// flooding the log with NPC drift events.</summary>
    public bool DiagnosticLogging { get; set; }

    /// <summary>The triangle the follower is currently standing on. -1 before the first
    /// tick or when the follower strays off the mesh.</summary>
    public int CurrentTriangle { get; private set; } = -1;

    /// <summary>Remaining path as a read-only snapshot: triangles from the one just
    /// stepped into through the goal triangle. Empty when <see cref="ReachedGoal"/> is
    /// true or <see cref="PathBlocked"/> is true.</summary>
    public IReadOnlyList<int> RemainingPath => _path;

    private readonly List<int> _path = new();
    private readonly List<Vector3> _waypoints = new();
    private readonly NavPathfinder.Workspace _workspace = new();
    private int _pathIdx;
    private int _waypointIdx;

    // Phase 24-NAV fold (post-test) — stuck detection. When an actor's
    // XZ position fails to advance by at least StuckEpsilon for
    // StuckMaxTicks consecutive ticks despite the follower having an
    // active path, force a Replan from the current position. Without
    // this, a single bad TryFindTriangle tie-break at a triangle edge
    // can pin an actor in place indefinitely — the funnel keeps trying
    // to push forward but the per-tick advance never matches the path
    // triangle's expected adjacency. Real DS1 doesn't have this
    // failure mode because its BSP returns the canonical triangle for
    // edge-on points (deferred to SC-NAV-BSP-LOOKUP); until that lands,
    // the replan recovers.
    private Vector3 _lastTickPos;
    private bool _hasLastTickPos;
    private int _stuckTicks;
    private int _stuckRecoveryAttempts;
    private const float StuckEpsilon = 0.05f;
    private const int StuckMaxTicks = 8;
    // SC-NAV-STUCK-RECOVERY — how far to teleport perpendicular when
    // a plain replan didn't unstick. 0.4u is small enough to stay
    // visually plausible but big enough to slip off a sliver triangle
    // or out of a 1-unit-wide wedge against a wall/rail.
    private const float EscapeStepDist = 0.4f;
    private const int MaxRecoveryAttempts = 4;
    // SC-NAV-RECOVERY-STICKY — the perpendicular escape itself displaces the
    // walker ~0.4u, which the per-tick progress check used to read as "moving
    // again" and RESET the escalation counter: wedge → escape → reset →
    // wedge, forever. MaxRecoveryAttempts was unreachable, PathBlocked never
    // fired, and the owning brain never re-rolled the leg — the field
    // report's "frozen mobs in the open" (roam-audit: skrubb_farm jittering
    // 0.36u between 15s-stall snapshots, spider bit-frozen all session).
    // Attempts now clear only once the walker has genuinely LEFT the
    // recovery neighborhood; anchor set on the first attempt of an episode.
    private Vector3 _recoveryAnchor;
    private const float RecoveryProgressDist = 1.25f;

    // SC-NAV-VERTICAL-REBIND — a walker can step, not teleport. Every
    // standing-triangle (re)bind must land within this much of the
    // walker's current Y. Without the gate, any probe over a coverage
    // gap resolves to whatever unrelated surface overlaps that XZ —
    // the sd_r1 mine-ledge walker re-bound to the path2sd mountain top
    // 27u above (test 110) and stranded the player on a disconnected
    // component. 2.0u clears any authored step or per-tick ramp rise
    // while rejecting cross-layer snaps (stacked floors are 3u+ apart).
    private const float MaxRebindDy = 2.0f;

    // SC-NAV-STEP-DOWN-GATE — the largest per-sub-step DESCENT a walker may
    // take. Legit ramp/stair descents move a few tenths per step (7u/s ×
    // 50ms × 45° ≈ 0.35u); a bigger single-step drop is a coverage-seam
    // fall-through onto the layer below (see the bridge-edge dip). Ascents
    // are ungated (the ±MaxRebindDy probe already bounds them).
    private const float MaxStepDownDy = 1.0f;

    // SC-NAV-SEAM-HOP — door-stitched seams (NavMesh.StitchSnoDoorSeams) wire
    // A* adjacency between boundary edges up to DoorSeamEdgePairDistance
    // (1.5u) apart WITHOUT any covering geometry: the strip between the two
    // edges has no triangles. A walker whose per-tick step (~0.22u at
    // 4.5u/s / 20Hz) is shorter than that gap can never land a stand probe
    // on the far side — the containment clamp pins it at the near edge and
    // stuck-recovery loops to PathBlocked. Field case: the path2crypts
    // crypt-stair seam (gap 0.94u) — paths reported blocked=False but the
    // player jiggled at the stair lip and never descended; the fh_r1→hc_r1
    // basement seam only ever worked because its gap is narrower than one
    // tick's step. When a forward sub-step leaves the mesh but the active
    // path's next triangle is a direct (stitched) neighbor of the one we're
    // standing on, step ACROSS the seam onto the nearest point of that
    // triangle. Bounds: XZ hop ≤ MaxSeamHopDist, Y within MaxRebindDy,
    // descent within MaxStepDownDy — inside every anti-teleport gate above.
    private const float MaxSeamHopDist = 2.0f;

    /// <summary>Standing-triangle probe: <see cref="NavMesh.TryFindTriangleNear"/>
    /// including fade-hidden ground (it's physical), but only accepting a
    /// triangle within <see cref="MaxRebindDy"/> of the walker's current Y.
    /// A far-vertical hit means the walkable floor here has a hole — treat
    /// as off-mesh so the swept clamp / stuck recovery handles it.
    /// SC-NAV-GROUND-DIP — the probe prefers the CURRENT triangle and its
    /// direct neighbors among vertically stacked candidates (bridge deck
    /// over a stream bed converging at the banks), so a walker's Y can't
    /// flicker onto the other layer mid-stride and read as "dipping into
    /// the ground".</summary>
    private bool TryFindStandTriangle(Vector3 pos, out int tri)
    {
        // SC-NAV-KIND-BIND — pass the walker's traversal so candidates it
        // can't traverse (Water for LandOnly) rank below every legal layer;
        // see TryFindTriangleNear for the bridge-bank flip this prevents.
        if (Mesh.TryFindTriangleNear(pos, CurrentTriangle, MaxRebindDy,
                includeFadeHidden: true, out tri, Traversal))
            return true;
        tri = -1;
        return false;
    }

    /// <summary>Per-actor traversal policy: which kinds (Floor / Water) the follower can
    /// enter and at what cost. Defaults to <see cref="NavTraversal.LandOnly"/>; assign
    /// <see cref="NavTraversal.Amphibious"/> (or a custom one) for swimmers. Re-read on
    /// every <see cref="SetTarget"/> — change before retargeting.</summary>
    public NavTraversal Traversal { get; set; } = NavTraversal.LandOnly;

    /// <summary>Funnel-smoothed waypoint list as a read-only snapshot. Empty when
    /// the follower has no active path. Each waypoint sits on a portal corner of the
    /// corridor (or is the goal); chasing them produces a visibly straight walk
    /// across wide tiles compared to centroid-chasing.</summary>
    public IReadOnlyList<Vector3> Waypoints => _waypoints;

    public NavFollower(NavMesh mesh, Vector3 startPos, float speed)
    {
        Mesh = mesh;
        Position = startPos;
        Speed = speed;
        Target = startPos;
        ReachedGoal = true;
    }

    /// <summary>Sets a new target and recomputes the path from <see cref="Position"/>.
    /// Safe to call mid-traversal; the follower will replan from wherever it is now.</summary>
    public void SetTarget(Vector3 target)
    {
        Target = target;
        ReachedGoal = false;
        PathBlocked = false;
        Replan();
        if (DiagnosticLogging)
        {
            string reason = PathBlocked ? $" reason=\"{NavPathfinder.LastFailure}\"" : "";
            System.Console.WriteLine(
                $"[nav-target] pos=({Position.X:F1},{Position.Y:F1},{Position.Z:F1}) " +
                $"target=({target.X:F1},{target.Y:F1},{target.Z:F1}) " +
                $"path={_path.Count}tri waypoints={_waypoints.Count} " +
                $"blocked={PathBlocked}{reason}");
        }
    }

    /// <summary>Phase 19b — drop the follower at <paramref name="pos"/> and clear
    /// any in-flight path. The next <see cref="Tick"/> is a no-op until
    /// <see cref="SetTarget"/> assigns a goal. Used by save/load to restore an
    /// actor's position without dragging in the original target — a saved-then-
    /// loaded actor should idle until the AI picks a fresh wander leg.</summary>
    public void Teleport(Vector3 pos)
    {
        Position = pos;
        Target = pos;
        ReachedGoal = true;
        PathBlocked = false;
        _path.Clear();
        _waypoints.Clear();
        _pathIdx = 0;
        _waypointIdx = 0;
        CurrentTriangle = -1;
    }

    // SC-NAV-PARTIAL-PATH — DS1 walks you AS FAR AS IT CAN toward an
    // unreachable click (across a chasm, onto a roof) instead of refusing
    // the order. When the goal can't be resolved or pathed, retarget to the
    // enterable point in the walker's own component nearest the goal.
    // One retry per plan (the guard) so a still-unreachable fallback can't
    // recurse; a fallback that lands basically where we stand is treated as
    // a genuine block so the "can't move there" feedback still fires.
    private bool _partialRetry;

    /// <summary>OPT-IN, player-order followers only. The fallback scans the
    /// whole triangle list — fine for a click, catastrophic for the hundreds
    /// of ambient wanderers whose legs routinely blocked-fail against closed
    /// doors (field report: 394ms frames, every blocked chicken replan
    /// sweeping a 139k-tri mesh). Default off: NPC wander keeps the cheap
    /// fail-and-reroll it always had.</summary>
    public bool PartialPathFallback { get; set; }

    private bool TryPartialGoal(int startTri)
    {
        if (!PartialPathFallback || _partialRetry) return false;
        int comp = Mesh.ComponentOf(startTri);
        if (comp < 0) return false;
        int bestTri = -1;
        float bestD2 = float.MaxValue;
        var bestPt = Target;
        int triCount = Mesh.TriangleCount;
        for (int t = 0; t < triCount; t++)
        {
            if (Mesh.ComponentOf(t) != comp) continue;
            if (Mesh.IsBlocked(t)) continue;
            if (!Traversal.CanEnter(Mesh.Kinds[t])) continue;
            var pt = Mesh.NearestPointInTriangleXZ(t, Target);
            float dx = pt.X - Target.X, dz = pt.Z - Target.Z;
            float d2 = dx * dx + dz * dz;
            if (d2 < bestD2) { bestD2 = d2; bestTri = t; bestPt = pt; }
        }
        if (bestTri < 0) return false;
        float px = bestPt.X - Position.X, pz = bestPt.Z - Position.Z;
        if (px * px + pz * pz <= GoalRadius * GoalRadius * 4f) return false;
        if (DiagnosticLogging)
            System.Console.WriteLine(
                $"[nav-partial] unreachable ({Target.X:F1},{Target.Z:F1}) — walking to " +
                $"nearest reachable ({bestPt.X:F1},{bestPt.Z:F1}) tri={bestTri}");
        _partialRetry = true;
        Target = bestPt;
        Replan();
        _partialRetry = false;
        return !PathBlocked;
    }

    /// <summary>SC-BODY-SEPARATION — displace the walker by a small XZ delta
    /// (crowd push-out) without disturbing the active path. The landing point
    /// must pass the same Y-gated stand probe as normal movement; an off-mesh
    /// or cross-layer push is dropped, so separation can never shove a body
    /// through a wall or off the floor. Path state is left alone — the next
    /// tick's standing resolution / drift handling re-glues the walker.</summary>
    public void Nudge(float dx, float dz)
    {
        var cand = new Vector3(Position.X + dx, Position.Y, Position.Z + dz);
        if (!TryFindStandTriangle(cand, out var tri)) return;
        float y = Mesh.SampleYOnTriangle(tri, cand);
        if (y < Position.Y - MaxStepDownDy) return;
        Position = new Vector3(cand.X, y, cand.Z);
        CurrentTriangle = tri;
    }

    private void Replan()
    {
        _path.Clear();
        _waypoints.Clear();
        _pathIdx = 0;
        _waypointIdx = 0;

        // SC-FADE-WALKABLE — the tile we're STANDING on is physical ground
        // even if a fade just hid it (a cutaway firing mid-walk must not
        // strand the walker); include hidden tris in the start lookup.
        // SC-NAV-VERTICAL-REBIND — the start bind is Y-gated: replanning
        // from a position over a coverage hole must fail (blocked) rather
        // than re-bind the walker to a vertically unrelated surface.
        if (!TryFindStandTriangle(Position, out var startTri))
        {
            // SC-NAV-GROUND-DIP — un-strand fallback. A walker whose Y has
            // ended up outside the rebind gate of EVERY surface (sunk into
            // the ground by a bad snap, or parked over a coverage hole) can
            // never satisfy the gated probe again: every SetTarget fails and
            // the actor is permanently stuck. A fresh player click is an
            // explicit "get me out of here" — accept the nearest-Y surface
            // regardless of vertical distance and lift the walker onto it.
            if (Mesh.TryFindTriangle(Position, out startTri, includeFadeHidden: true))
            {
                float y = Mesh.SampleYOnTriangle(startTri, Position);
                if (DiagnosticLogging)
                    System.Console.WriteLine(
                        $"[nav-rebind] gated stand probe failed at " +
                        $"({Position.X:F1},{Position.Y:F1},{Position.Z:F1}); " +
                        $"lifting onto tri {startTri} (Y {y:F1})");
                Position = new Vector3(Position.X, y, Position.Z);
                CurrentTriangle = startTri;
            }
            else
            {
                PathBlocked = true;
                return;
            }
        }
        // SC-NAV-KIND-BIND recovery — the walker's bind sits on a kind it
        // can't traverse (the bridge-bank layer flip parked the hero on the
        // stream's Water sheet). That sheet is usually NOT edge-connected
        // to the bank floor, so no path request can walk out of it — the
        // only exit is a physical step onto the nearest legal ground. 8u
        // covers a stream's width from mid-span; 3u of vertical keeps the
        // recovery on this layer stack.
        // SC-PATHING — obstacle-BLOCKED starts take the same escape: an
        // actor spawned (or knocked) inside a prop footprint whose whole
        // neighborhood is blocked can never expand out through A* (blocked
        // tris are excluded from expansion); step to the nearest enterable
        // point instead of freezing (parity roam soak: 35 spawns frozen).
        if ((!Traversal.CanEnter(Mesh.Kinds[startTri]) || Mesh.IsBlocked(startTri))
            && Mesh.TryFindNearestEnterable(Position, radius: 8f, maxDy: 3f,
                    Traversal, out var liftTri, out var liftPos))
        {
            if (DiagnosticLogging)
                System.Console.WriteLine(
                    $"[nav-rebind] start tri {startTri} kind={Mesh.Kinds[startTri]} " +
                    $"blocked={Mesh.IsBlocked(startTri)} — stepping to nearest legal " +
                    $"ground ({liftPos.X:F1},{liftPos.Y:F1},{liftPos.Z:F1}) tri={liftTri}");
            Position = liftPos;
            CurrentTriangle = liftTri;
            startTri = liftTri;
        }
        // SC-NAV-GOAL-COMPONENT — resolve the goal in the START's raw
        // component when stacked layers offer a choice. Plain best-|dy|
        // could bind the goal to an unwelded sliver at the target's XZ
        // (trap cover / decorative slab under the player's feet) and
        // every request failed RAW-DISCONNECTED — a chasing room froze
        // solid while the player stood on perfectly good floor.
        if (!Mesh.TryFindTriangleForGoal(Target, startTri, out var goalTri))
        {
            if (TryPartialGoal(startTri)) return;
            PathBlocked = true;
            return;
        }
        if (!NavPathfinder.TryFindPath(Mesh, startTri, goalTri, _path, _workspace, Traversal))
        {
            if (TryPartialGoal(startTri)) return;
            PathBlocked = true;
            return;
        }

        // String-pull the triangle path into a waypoint list. _path stays around because
        // the Y resampler still uses it to figure out which triangle the follower is
        // standing on as it advances through the corridor.
        NavFunnel.BuildWaypoints(Mesh, _path, Position, Target, _waypoints);

        CurrentTriangle = startTri;
        // Snap Y to the starting triangle's surface so the first tick doesn't jump.
        Position = new Vector3(Position.X, Mesh.SampleYOnTriangle(startTri, Position), Position.Z);
    }

    /// <summary>Advances the follower by one simulation tick. No-op if the follower has
    /// already reached the goal or is blocked.</summary>
    public void Tick(float dt)
    {
        if (ReachedGoal || PathBlocked || _path.Count == 0 || _waypoints.Count == 0) return;
        if (dt <= 0f) return;

        // Stuck detection (multi-stage recovery, SC-NAV-STUCK-RECOVERY
        // fold). When the follower has an open path but isn't making
        // forward progress, we now escalate through:
        //   1. Plain Replan — same as before; covers the case where
        //      the current waypoint sequence has drifted off-path.
        //   2. Perpendicular escape — if Replan didn't help, nudge
        //      the actor sideways (perpendicular to the current
        //      waypoint vector) so they slip OUT of a wedge against
        //      whatever's pinning them. The next tick's path is
        //      computed from the escape position.
        //   3. Diag log — emit a single line per stuck event with
        //      position, current waypoint, and remaining path
        //      length so we can identify wedge hot spots from
        //      gameplay logs.
        var tickStartPos = Position;
        if (_hasLastTickPos)
        {
            float dxs = Position.X - _lastTickPos.X;
            float dzs = Position.Z - _lastTickPos.Z;
            if (MathF.Sqrt(dxs * dxs + dzs * dzs) < StuckEpsilon)
            {
                _stuckTicks++;
                if (_stuckTicks >= StuckMaxTicks)
                {
                    _stuckTicks = 0;
                    _stuckRecoveryAttempts++;
                    if (_stuckRecoveryAttempts == 1)
                    {
                        // SC-NAV-RECOVERY-STICKY — anchor the episode at the
                        // first attempt; progress is measured against THIS
                        // spot, not the last tick, so escape-jiggle can't
                        // reset the escalation.
                        _recoveryAnchor = Position;
                        if (DiagnosticLogging)
                        {
                            var wpDbg = _waypointIdx < _waypoints.Count
                                ? _waypoints[_waypointIdx]
                                : Target;
                            System.Console.WriteLine(
                                $"[nav-stuck] pos=({Position.X:F1},{Position.Z:F1}) " +
                                $"Y={Position.Y:F1} tri={CurrentTriangle} " +
                                $"wp[{_waypointIdx}/{_waypoints.Count}]=" +
                                $"({wpDbg.X:F1},{wpDbg.Z:F1}) " +
                                $"target=({Target.X:F1},{Target.Z:F1}) replan");
                        }
                        // First try: plain replan.
                        Replan();
                    }
                    else
                    {
                        // Second+ try: perpendicular escape. Compute
                        // a unit vector toward the current waypoint
                        // and rotate 90° to nudge sideways. Direction
                        // (CCW vs CW) alternates each attempt so we
                        // don't keep hitting the same wall.
                        var wp = _waypointIdx < _waypoints.Count ? _waypoints[_waypointIdx] : Target;
                        float ndx = wp.X - Position.X;
                        float ndz = wp.Z - Position.Z;
                        float nlen = MathF.Sqrt(ndx * ndx + ndz * ndz);
                        if (nlen > 0.01f)
                        {
                            ndx /= nlen; ndz /= nlen;
                            // 90° rotation: (x,z) → (-z,x) CCW or (z,-x) CW.
                            bool ccw = (_stuckRecoveryAttempts % 2) == 0;
                            float ex = ccw ? -ndz : ndz;
                            float ez = ccw ?  ndx : -ndx;
                            var escape = new Vector3(
                                Position.X + ex * EscapeStepDist,
                                Position.Y,
                                Position.Z + ez * EscapeStepDist);
                            if (TryFindStandTriangle(escape, out var escapeTri))
                            {
                                if (DiagnosticLogging)
                                {
                                    System.Console.WriteLine(
                                        $"[nav-stuck] perpendicular escape attempt {_stuckRecoveryAttempts} " +
                                        $"({(ccw ? "CCW" : "CW")}) at pos=({Position.X:F1},{Position.Z:F1})");
                                }
                                // SC-NAV-GROUND-DIP — sample Y on the triangle the
                                // escape point actually landed on. Sampling on the
                                // stale _path[_pathIdx] plane EXTRAPOLATED at an
                                // outside point could drop the walker under the
                                // floor; sunk past MaxRebindDy every later probe
                                // failed and the actor was stranded in the ground.
                                Position = new Vector3(escape.X,
                                    Mesh.SampleYOnTriangle(escapeTri, escape),
                                    escape.Z);
                                CurrentTriangle = escapeTri;
                            }
                            Replan();
                        }
                        if (_stuckRecoveryAttempts >= MaxRecoveryAttempts)
                        {
                            if (DiagnosticLogging)
                            {
                                System.Console.WriteLine(
                                    $"[nav-stuck] pos=({Position.X:F1},{Position.Z:F1}) " +
                                    $"tri={CurrentTriangle} wp[{_waypointIdx}/{_waypoints.Count}] " +
                                    $"target=({Target.X:F1},{Target.Z:F1}) giving up");
                            }
                            PathBlocked = true;
                            _stuckRecoveryAttempts = 0;
                            return;
                        }
                    }
                    if (_path.Count == 0 || _waypoints.Count == 0) return;
                }
            }
            else
            {
                _stuckTicks = 0;
                // SC-NAV-RECOVERY-STICKY — mere epsilon displacement (the
                // 0.4u escape hop, wall-slide jitter) is not progress. Clear
                // the escalation only once the walker has left the recovery
                // neighborhood for real; otherwise attempts keep climbing to
                // the give-up (PathBlocked) and the brain re-rolls the leg.
                if (_stuckRecoveryAttempts > 0)
                {
                    float rdx = Position.X - _recoveryAnchor.X;
                    float rdz = Position.Z - _recoveryAnchor.Z;
                    if (rdx * rdx + rdz * rdz >
                        RecoveryProgressDist * RecoveryProgressDist)
                        _stuckRecoveryAttempts = 0;
                }
            }
        }
        _hasLastTickPos = true;

        float remaining = Speed * dt;
        // Chase the current funnel waypoint. When close enough, advance the waypoint
        // index; on the last waypoint (which is always the goal), declare arrival.
        while (remaining > 0f)
        {
            Vector3 waypoint = _waypoints[_waypointIdx];
            float dx = waypoint.X - Position.X;
            float dz = waypoint.Z - Position.Z;
            float distXZ = MathF.Sqrt(dx * dx + dz * dz);

            if (distXZ <= GoalRadius)
            {
                if (_waypointIdx + 1 >= _waypoints.Count)
                {
                    // Standing on the goal triangle and close to the target: done.
                    Position = new Vector3(Target.X, Mesh.SampleYOnTriangle(_path[^1], Target), Target.Z);
                    CurrentTriangle = _path[^1];
                    ReachedGoal = true;
                    return;
                }
                _waypointIdx++;
                continue;
            }

            float step = MathF.Min(remaining, distXZ);
            float nx = Position.X + dx / distXZ * step;
            float nz = Position.Z + dz / distXZ * step;
            // Resample Y on whichever triangle the new XZ position lands on. We trust
            // the hit only when it's the current path triangle or the next one — funnel
            // smoothing means we may walk in a straight line that briefly clips the
            // INSIDE corner of an off-path neighbor, but we still need to land Y on the
            // legitimate path tile. We sweep _pathIdx forward to whichever path[k] the
            // hit matches (a single straight segment can span several triangles when
            // the funnel pulls a long line through a corridor).
            int standing = CurrentTriangle;
            bool advanced = false;
            // SC-FADE-WALKABLE — standing resolution includes fade-hidden
            // ground (it's physical; skipping it re-glued the walker to the
            // wrong layer whenever a path crossed a faded area).
            // SC-NAV-VERTICAL-REBIND — the probe is Y-gated; a hit on a
            // vertically unrelated overlapping surface counts as off-mesh
            // and flows into the swept clamp below instead of being walked.
            bool onMesh = TryFindStandTriangle(new Vector3(nx, Position.Y, nz), out var hit);
            if (onMesh)
            {
                if (hit == _path[_pathIdx])
                {
                    standing = hit;
                    advanced = true;
                }
                else
                {
                    // Look ahead through the path for this triangle — funnel waypoints
                    // can outrun pathIdx by more than one tile per step.
                    var ahead = -1;
                    for (var k = _pathIdx + 1; k < _path.Count; k++)
                    {
                        if (_path[k] == hit) { ahead = k; break; }
                    }
                    if (ahead >= 0)
                    {
                        _pathIdx = ahead;
                        standing = hit;
                        advanced = true;
                    }
                    else
                    {
                        // SC-NAV-FOLLOWER-DRIFT — the hit is NOT on the path.
                        // Check if it's an adjacency-neighbor of the current
                        // path tri (a legal one-step deviation, e.g. the
                        // funnel cut a corner past a tile boundary into an
                        // adjacent walkable tile). If yes, treat THIS hit as
                        // the new standing triangle so the actor stays glued
                        // to the surface they're visibly on, instead of
                        // pinning Y to the stale _path[_pathIdx] and
                        // wedging visually. Doesn't reroute the path —
                        // next tick the funnel pulls back toward the
                        // current waypoint, and if the actor keeps
                        // drifting off-path, stuck-detection escalates.
                        // Pure-look fix; no A* change.
                        int cur = _path[_pathIdx];
                        if (cur >= 0)
                        {
                            for (int s = 0; s < 3; s++)
                            {
                                if (Mesh.Neighbors[3 * cur + s] == hit)
                                {
                                    standing = hit;
                                    advanced = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            if (!onMesh && TrySeamHop(nx, nz, out var hopTri, out var hopPathIdx, out var hopX, out var hopZ))
            {
                // SC-NAV-SEAM-HOP — the step left the mesh because the
                // corridor crosses a stitched door seam's coverage gap;
                // land on the far triangle instead of clamping at the edge.
                nx = hopX;
                nz = hopZ;
                standing = hopTri;
                _pathIdx = hopPathIdx;
                advanced = true;
                if (DiagnosticLogging)
                    System.Console.WriteLine(
                        $"[nav-seam-hop] tri {CurrentTriangle}->{hopTri} " +
                        $"to ({nx:F1},{nz:F1}) at ({Position.X:F1},{Position.Z:F1})");
            }
            else if (!onMesh)
            {
                // SC-NAV-CONTAIN — the step would leave the walkable floor
                // (a wall, a building side, the world edge). NEVER move off
                // the mesh: binary-search back toward the last on-floor point
                // for the farthest fraction of the step still on a triangle,
                // so the actor hugs the wall instead of clipping through it
                // (the old "advance anyway" put the player inside buildings)
                // or freezing dead on an edge (the reverted hard-clamp froze
                // NPCs). lo stays 0 only when we're already hard against the
                // boundary — then we hold position and the funnel retries
                // next tick / stuck-detection replans. This is the hard
                // out-of-bounds barrier: nothing walks off the nav mesh.
                float lo = 0f, hi = 1f;
                for (int it = 0; it < 7; it++)
                {
                    float mid = (lo + hi) * 0.5f;
                    float tx = Position.X + (nx - Position.X) * mid;
                    float tz = Position.Z + (nz - Position.Z) * mid;
                    if (TryFindStandTriangle(new Vector3(tx, Position.Y, tz), out var thit))
                    { lo = mid; standing = thit; }
                    else hi = mid;
                }
                nx = Position.X + (nx - Position.X) * lo;
                nz = Position.Z + (nz - Position.Z) * lo;
            }
            else if (!advanced && standing < 0)
            {
                // On the mesh somewhere but no bound triangle resolved —
                // hold position so the next tick / replan re-binds instead
                // of Y-sampling on garbage.
                nx = Position.X;
                nz = Position.Z;
            }
            float ny = standing >= 0 ? Mesh.SampleYOnTriangle(standing, new Vector3(nx, 0f, nz)) : Position.Y;
            // SC-NAV-STEP-DOWN-GATE — a walker descends ramps/stairs a few
            // tenths of a unit per sub-step; a DROP of more than ~1u in one
            // step means the stand probe fell through a coverage seam onto
            // the layer BELOW (bridge edge: deck Y8.9 → water sheet 7.5 →
            // stream bed 4.7 — the "player dips into the ground + grey
            // flash" report; each layer gap fit the ±2u rebind gate, so the
            // walker slid down the whole stack). Refuse the drop: hold
            // position + triangle this sub-step; the funnel retries next
            // tick and, if the seam persists, stuck-recovery replans — whose
            // rebind chain provably lands on the correct bank ground without
            // the visible plunge. Same-triangle drops stay allowed (a single
            // steep tri is legitimate slope, not a layer flip).
            if (standing >= 0 && CurrentTriangle >= 0 && standing != CurrentTriangle
                && ny < Position.Y - MaxStepDownDy)
            {
                if (DiagnosticLogging)
                    System.Console.WriteLine(
                        $"[nav-step-gate] refused {Position.Y - ny:F1}u drop " +
                        $"tri {CurrentTriangle}->{standing} at ({nx:F1},{nz:F1}) — holding");
                nx = Position.X;
                nz = Position.Z;
                standing = CurrentTriangle;
                ny = Position.Y;
            }
            Position = new Vector3(nx, ny, nz);
            CurrentTriangle = standing;
            remaining -= step;
        }
        _lastTickPos = tickStartPos;
    }

    /// <summary>SC-NAV-SEAM-HOP — when a forward sub-step's stand probe fails
    /// (landing point over a seam's coverage gap), find the next triangle on
    /// the active path that is a direct neighbor of the standing triangle and
    /// return a bounded landing point on it. Only path triangles qualify —
    /// A* already vetted them for traversal/blocking — and only within the
    /// vertical gates that keep this from ever re-introducing the cross-layer
    /// teleport (SC-NAV-VERTICAL-REBIND).</summary>
    private bool TrySeamHop(float nx, float nz, out int hopTri, out int hopPathIdx, out float hopX, out float hopZ)
    {
        hopTri = -1;
        hopPathIdx = -1;
        hopX = nx;
        hopZ = nz;
        if (CurrentTriangle < 0 || _path.Count == 0) return false;
        int maxK = Math.Min(_pathIdx + 2, _path.Count - 1);
        for (int k = _pathIdx; k <= maxK; k++)
        {
            int far = _path[k];
            if (far < 0 || far == CurrentTriangle) continue;
            bool linked = false;
            for (int slot = 0; slot < 3; slot++)
            {
                if (Mesh.Neighbors[3 * CurrentTriangle + slot] == far) { linked = true; break; }
            }
            // SC-NAV-SEAM-OVERFLOW — door-authored adjacency carried outside
            // the 3-slot array (see NavMesh.ExtraLinks) is hoppable too.
            if (!linked && Mesh.ExtraLinks is not null
                && Mesh.ExtraLinks.TryGetValue(CurrentTriangle, out var extra)
                && Array.IndexOf(extra, far) >= 0)
                linked = true;
            if (!linked) continue;
            var landing = Mesh.NearestPointInTriangleXZ(far, new Vector3(nx, Position.Y, nz));
            float dx = landing.X - Position.X;
            float dz = landing.Z - Position.Z;
            if (dx * dx + dz * dz > MaxSeamHopDist * MaxSeamHopDist) continue;
            if (MathF.Abs(landing.Y - Position.Y) > MaxRebindDy) continue;
            if (landing.Y < Position.Y - MaxStepDownDy) continue;
            // SC-NAV-SEAM-HOP-FORWARD — the hop must make PROGRESS. The
            // nearest point of the far triangle can sit BEHIND the walker
            // (its near edge lies back along the corridor); hopping there,
            // walking forward, and falling off at the same spot loops
            // forever — the chase-sim's PINNED orbit (83u walked for a 9u
            // trip; in-game: a chaser jogging in place). Reject a landing
            // that isn't strictly closer to the active waypoint; the
            // containment clamp then holds the edge and stuck-recovery
            // replans honestly.
            var wpGoal = _waypointIdx < _waypoints.Count ? _waypoints[_waypointIdx] : Target;
            float curDx = wpGoal.X - Position.X, curDz = wpGoal.Z - Position.Z;
            float landDx = wpGoal.X - landing.X, landDz = wpGoal.Z - landing.Z;
            if (landDx * landDx + landDz * landDz >= curDx * curDx + curDz * curDz - 1e-3f) continue;
            hopTri = far;
            hopPathIdx = k;
            hopX = landing.X;
            hopZ = landing.Z;
            return true;
        }
        return false;
    }
}
