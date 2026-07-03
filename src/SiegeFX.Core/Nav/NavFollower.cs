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

    private void Replan()
    {
        _path.Clear();
        _waypoints.Clear();
        _pathIdx = 0;
        _waypointIdx = 0;

        // SC-FADE-WALKABLE — the tile we're STANDING on is physical ground
        // even if a fade just hid it (a cutaway firing mid-walk must not
        // strand the walker); include hidden tris in the start lookup.
        if (!Mesh.TryFindTriangle(Position, out var startTri, includeFadeHidden: true))
        {
            PathBlocked = true;
            return;
        }
        if (!Mesh.TryFindTriangle(Target, out var goalTri))
        {
            PathBlocked = true;
            return;
        }
        if (!NavPathfinder.TryFindPath(Mesh, startTri, goalTri, _path, _workspace, Traversal))
        {
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
                            if (Mesh.TryFindTriangle(escape, out _))
                            {
                                if (DiagnosticLogging)
                                {
                                    System.Console.WriteLine(
                                        $"[nav-stuck] perpendicular escape attempt {_stuckRecoveryAttempts} " +
                                        $"({(ccw ? "CCW" : "CW")}) at pos=({Position.X:F1},{Position.Z:F1})");
                                }
                                Position = new Vector3(escape.X,
                                    Mesh.SampleYOnTriangle(_path[_pathIdx], escape),
                                    escape.Z);
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
                _stuckRecoveryAttempts = 0;
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
            if (Mesh.TryFindTriangle(new Vector3(nx, Position.Y, nz), out var hit, includeFadeHidden: true))
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
            if (!advanced)
            {
                // Phase 24-NAV fold (post-test #2) — earlier boundary-
                // clamp was too aggressive and froze NPCs whose funnel
                // waypoint happened to sit right on a triangle edge.
                // Reverted to "advance anyway"; stuck-detection above
                // (8-tick replan) catches genuinely-pinned cases. The
                // true fix needs BSP-accelerated point-in-triangle so
                // edge-on funnel waypoints tie-break consistently —
                // splinter SC-NAV-BSP-LOOKUP carries that.
                if (standing < 0)
                {
                    // No current triangle: leave position frozen so the
                    // next tick / replan can re-bind; advancing here
                    // would Y-sample on garbage.
                    nx = Position.X;
                    nz = Position.Z;
                }
            }
            float ny = standing >= 0 ? Mesh.SampleYOnTriangle(standing, new Vector3(nx, 0f, nz)) : Position.Y;
            Position = new Vector3(nx, ny, nz);
            CurrentTriangle = standing;
            remaining -= step;
        }
        _lastTickPos = tickStartPos;
    }
}
