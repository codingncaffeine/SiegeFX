using System.Numerics;
using SiegeFX.Core.Nav;

namespace SiegeFX.Core.Actors;

/// <summary>
/// Per-actor wander driver. Wraps a <see cref="NavFollower"/> and keeps feeding it
/// random nearby nav-mesh points so spawned actors roam their region instead of
/// idling on their authored spawn pose. Facing is derived from the tick's XZ
/// delta so the render layer can rotate the actor to match its direction of travel.
///
/// This is the minimum useful wander AI — no territory markers, no clustering, no
/// fleeing/chasing. Those are Phase 12+ when combat lands and actors need opinions
/// about other actors. For now we just prove the follower plumbing is live per-actor
/// and that the nav mesh holds up under 181 concurrent walkers.
///
/// <b>Not reentrant.</b> The underlying <see cref="NavFollower"/> shares a pathfinder
/// workspace with itself, so calling <see cref="Tick"/> from a skrit callback that
/// fires inside another Tick would stomp the A* scratch. Drive one follower per actor
/// from the main sim loop only.
/// </summary>
public sealed class ActorFollower
{
    public NavFollower Follower { get; }

    /// <summary>Max XZ radius of a wander leg. The actual pick is biased slightly
    /// toward the outer rim so actors don't pace in tiny circles.</summary>
    public float WanderRadius { get; set; } = 8f;

    /// <summary>Number of random XZ samples to try before giving up on a wander
    /// target selection and idling for a beat. DS1 spawn corners (pens, caves) often
    /// have a tiny walkable footprint where ~3-4 samples miss in a row.</summary>
    public int MaxRetries { get; set; } = 6;

    // Initial facing is caller-supplied (from the actor's authored orientation) so a
    // follower whose first pick stalls or whose path is briefly blocked doesn't visibly
    // snap all 181 actors to +Z at spawn. Once movement actually produces a nonzero XZ
    // delta, this gets overwritten from the tick-to-tick direction vector.
    Vector3 _facing;
    readonly Random _rng;

    // After a failed target pick or a blocked path, wait this many ticks before
    // retrying. At 20 Hz that's ~1 second of idling — enough that a permanently
    // pinned actor (e.g., chicken in a fenced pen with no reachable samples) spends
    // its budget on standing still rather than chewing the rng every tick.
    int _idleTicksRemaining;
    const int IdleAfterFail = 20;

    public ActorFollower(NavMesh mesh, Vector3 startPos, float speed, int rngSeed, Vector3 initialFacing)
    {
        Follower = new NavFollower(mesh, startPos, speed)
        {
            // Phase 24-NAV-LOGICAL-FLAGS — NPC / brain actors respect
            // the lf_computer_player gate so they stay out of human-
            // only zones (town building interiors, scripted refuges).
            Traversal = NavTraversal.Computer,
        };
        _rng = new Random(rngSeed);
        // Collapse to XZ unit vector; degenerate input (Y-only or zero) falls back to +Z.
        var flat = new Vector3(initialFacing.X, 0f, initialFacing.Z);
        float len = flat.Length();
        _facing = len > 1e-4f ? flat / len : Vector3.UnitZ;
        PickNewTarget();
    }

    public Vector3 Position => Follower.Position;

    /// <summary>Unit XZ heading. Updated only when the tick produced nonzero
    /// movement so an arrived actor keeps its last-known facing instead of snapping
    /// to +Z.</summary>
    public Vector3 Facing => _facing;

    public void Tick(float dt)
    {
        if (_idleTicksRemaining > 0)
        {
            _idleTicksRemaining--;
            if (_idleTicksRemaining == 0) PickNewTarget();
            return;
        }

        var before = Follower.Position;
        Follower.Tick(dt);
        float dx = Follower.Position.X - before.X;
        float dz = Follower.Position.Z - before.Z;
        float len2 = dx * dx + dz * dz;
        if (len2 > 1e-6f)
        {
            float len = MathF.Sqrt(len2);
            _facing = new Vector3(dx / len, 0f, dz / len);
        }

        if (Follower.ReachedGoal || Follower.PathBlocked)
            PickNewTarget();
    }

    /// <summary>Phase 19b — teleport the underlying nav follower to
    /// <paramref name="pos"/> and force a fresh wander pick on the next tick.
    /// Used by save/load to restore actor positions; the saved facing is
    /// re-applied separately so a teleported actor doesn't snap to +Z.</summary>
    public void Teleport(Vector3 pos)
    {
        Follower.Teleport(pos);
        // Schedule a fresh wander target on the very next tick. PickNewTarget
        // would re-roll right here, but doing it inline can stall if the new
        // position lands off-mesh and we have to fall back to the idle path —
        // letting Tick handle it keeps the timing consistent with regular
        // wander-end transitions.
        _idleTicksRemaining = 1;
    }

    /// <summary>Phase 19b — restore the persistent XZ heading. Pairs with
    /// <see cref="Teleport"/> so a saved-and-restored actor faces the way
    /// it was facing at save, not +Z.</summary>
    public void SetFacing(Vector3 facing)
    {
        var flat = new Vector3(facing.X, 0f, facing.Z);
        float len = flat.Length();
        if (len > 1e-4f) _facing = flat / len;
    }

    void PickNewTarget()
    {
        var origin = Follower.Position;
        var mesh = Follower.Mesh;
        // Three radius scales — full / half / quarter. NPCs on tight
        // islands or near a navmesh seam can fail every sample at the
        // full radius; shrinking the disk keeps the wander selection
        // tractable. SC-MOB-IDLE-FROZEN fold from the user-reported
        // "second flavor" frozen mobs (mobs just standing, no
        // boundary-clamp involved).
        float[] radiusScales = { 1f, 0.5f, 0.25f };
        foreach (var scale in radiusScales)
        {
            for (int i = 0; i < MaxRetries; i++)
            {
                float angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
                float radius = WanderRadius * scale * (0.3f + 0.7f * (float)_rng.NextDouble());
                var candidate = new Vector3(
                    origin.X + MathF.Cos(angle) * radius,
                    origin.Y,
                    origin.Z + MathF.Sin(angle) * radius);
                if (mesh.TryFindTriangle(candidate, out _))
                {
                    Follower.SetTarget(candidate);
                    return;
                }
            }
        }
        // Last-ditch: if EVERY scaled sample missed (mob spawned on a
        // 1-triangle island or near a mesh hole), set the target to
        // the mob's CURRENT position so Follower has a valid goal
        // instead of zero-state. The follower instantly reports
        // ReachedGoal and the next tick re-rolls PickNewTarget — still
        // potentially fruitless, but it visibly "tries" rather than
        // freezing. Idle backoff scales the retry rate down so we
        // don't burn CPU on a permanently-pinned actor.
        if (mesh.TryFindTriangle(origin, out _))
            Follower.SetTarget(origin);
        _idleTicksRemaining = IdleAfterFail;
    }
}
