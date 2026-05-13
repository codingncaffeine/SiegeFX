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
    private const float StuckEpsilon = 0.05f;
    private const int StuckMaxTicks = 8;

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

        if (!Mesh.TryFindTriangle(Position, out var startTri))
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

        // Stuck detection — if the follower didn't move appreciably
        // since the last Tick despite having an open path, replan.
        // This is the backstop for the boundary-respect logic below:
        // when the candidate XZ would leave the walkable mesh we
        // clamp instead of penetrating, but a pathfinder edge case
        // can still pin an actor in a corner. Replan resamples the
        // waypoint sequence from the current position and usually
        // recovers; if it can't, PathBlocked latches.
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
                    Replan();
                    if (_path.Count == 0 || _waypoints.Count == 0) return;
                }
            }
            else
            {
                _stuckTicks = 0;
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
            if (Mesh.TryFindTriangle(new Vector3(nx, Position.Y, nz), out var hit))
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
                }
            }
            if (!advanced)
            {
                // Phase 24-NAV fold (post-test) — RESPECT BOUNDARIES.
                // The candidate XZ would leave the walkable mesh (off-
                // path, no adjacent forward tile). DS1 doesn't let the
                // actor walk off the surface here; previous SiegeFX
                // code DID move the position to (nx,nz) anyway and
                // resampled Y on the old triangle, which visually
                // pushed the actor into terrain. Clamp the candidate
                // back onto the current triangle's nearest interior
                // point so the actor stops at the boundary. Stuck-
                // detection above triggers a replan if the actor stays
                // pinned for too many ticks.
                if (standing >= 0)
                {
                    var clamped = Mesh.ClampPointToTriangleXZ(standing,
                        new Vector3(nx, Position.Y, nz));
                    nx = clamped.X;
                    nz = clamped.Z;
                }
                else
                {
                    // No current triangle either — don't advance at all
                    // this tick; let the next Replan recover.
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
