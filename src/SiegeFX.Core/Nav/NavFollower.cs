using System.Numerics;

namespace SiegeFX.Core.Nav;

/// <summary>
/// Walks a point along a <see cref="NavMesh"/> toward a target, one tick at a time.
/// The follower is stateful: it caches a path computed from the current position to the
/// target and advances through the triangle sequence as the point enters each triangle.
///
/// Movement is purely XZ-planar in region-space; Y is resampled from whatever triangle
/// the follower is currently standing on so it sticks to the terrain. No funnel
/// smoothing yet — the follower walks toward the centroid of the next triangle in the
/// path, which produces a visibly lumpy route across wide corridors. Good enough to
/// prove the wiring works before the first actor integration; replace with a funnel
/// when lumpy paths start showing up in the viewer.
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
    private readonly NavPathfinder.Workspace _workspace = new();
    private int _pathIdx;

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

    private void Replan()
    {
        _path.Clear();
        _pathIdx = 0;

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
        if (!NavPathfinder.TryFindPath(Mesh, startTri, goalTri, _path, _workspace))
        {
            PathBlocked = true;
            return;
        }

        CurrentTriangle = startTri;
        // Snap Y to the starting triangle's surface so the first tick doesn't jump.
        Position = new Vector3(Position.X, Mesh.SampleYOnTriangle(startTri, Position), Position.Z);
    }

    /// <summary>Advances the follower by one simulation tick. No-op if the follower has
    /// already reached the goal or is blocked.</summary>
    public void Tick(float dt)
    {
        if (ReachedGoal || PathBlocked || _path.Count == 0) return;
        if (dt <= 0f) return;

        float remaining = Speed * dt;
        // The follower chases one waypoint at a time; waypoint N is the centroid of
        // path[N+1] while we're still walking the corridor, and the actual Target once
        // we're on the last triangle. Switching at GoalRadius gives smooth handoffs
        // without zig-zag on long tiles.
        while (remaining > 0f)
        {
            Vector3 waypoint = NextWaypoint();
            float dx = waypoint.X - Position.X;
            float dz = waypoint.Z - Position.Z;
            float distXZ = MathF.Sqrt(dx * dx + dz * dz);

            if (distXZ <= GoalRadius)
            {
                if (_pathIdx + 1 >= _path.Count)
                {
                    // Standing on the goal triangle and close to the target: done.
                    Position = new Vector3(Target.X, Mesh.SampleYOnTriangle(_path[^1], Target), Target.Z);
                    CurrentTriangle = _path[^1];
                    ReachedGoal = true;
                    return;
                }
                _pathIdx++;
                CurrentTriangle = _path[_pathIdx];
                continue;
            }

            float step = MathF.Min(remaining, distXZ);
            float nx = Position.X + dx / distXZ * step;
            float nz = Position.Z + dz / distXZ * step;
            // Resample Y on whichever triangle the new XZ position lands on; falls back
            // to the current triangle when the step stays inside it.
            int standing = CurrentTriangle;
            if (Mesh.TryFindTriangle(new Vector3(nx, Position.Y, nz), out var hit))
                standing = hit;
            float ny = standing >= 0 ? Mesh.SampleYOnTriangle(standing, new Vector3(nx, 0f, nz)) : Position.Y;
            Position = new Vector3(nx, ny, nz);
            CurrentTriangle = standing;
            remaining -= step;
        }
    }

    private Vector3 NextWaypoint()
    {
        // When the follower is on the final triangle of the path, chase the target
        // directly so it lands right on it. Otherwise chase the centroid of the next
        // triangle, which funnels the walk through the corridor.
        if (_pathIdx + 1 >= _path.Count) return Target;
        return Mesh.Centroids[_path[_pathIdx + 1]];
    }
}
