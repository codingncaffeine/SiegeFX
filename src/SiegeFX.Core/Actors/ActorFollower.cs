using System.Numerics;
using SiegeFX.Core.Nav;

namespace SiegeFX.Core.Actors;

/// <summary>
/// Per-actor wander driver. Wraps a <see cref="NavFollower"/> and feeds it occasional
/// short strolls sampled around the actor's spawn <b>anchor</b>, with a random idle
/// dwell between them (SC-MOB-ROAM), so a spawn holds near its authored post and mostly
/// stands — DS1's idle model — instead of random-walking away across the region. Facing
/// is derived from the tick's XZ delta so the render layer can rotate the actor to match
/// its direction of travel. A brain's Chase/Attack states drive the underlying follower
/// directly; when they release back to wander, the anchor pulls the mob home.
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

    // SC-MOB-ROAM — the spawn pose the actor leashes to. DS1 idle mobs hold near
    // their authored post and take occasional short strolls, returning afterward,
    // rather than random-walking away across the region. Wander picks sample around
    // THIS anchor (not the live position), so a mob drifts back toward its post and,
    // after a chase ends, walks home instead of milling wherever the fight stopped.
    Vector3 _anchor;

    // SC-MOB-ROAM — idle dwell between strolls. DS1 mobs stand most of the time; a
    // completed stroll parks the actor for a random beat (~2-7s at 20 Hz) before the
    // next pick, so a spawn reads as "milling at its post", not "endlessly pacing".
    public int IdleMinTicks { get; set; } = 45;
    public int IdleMaxTicks { get; set; } = 150;

    // After a failed target pick or a blocked path, wait this many ticks before
    // retrying. At 20 Hz that's ~1 second of idling — enough that a permanently
    // pinned actor (e.g., chicken in a fenced pen with no reachable samples) spends
    // its budget on standing still rather than chewing the rng every tick.
    int _idleTicksRemaining;
    const int IdleAfterFail = 20;

    // SC-MOB-LEG-TIMEOUT — watchdog on a wander leg. A follower can end up
    // pinned mid-leg without EVER reporting PathBlocked or ReachedGoal (the
    // recovery-oscillation freeze), and this driver only re-rolls on those
    // two flags — so one bad leg froze the mob for the whole session. Budget
    // each pick at 3× the straight-line walk time + 2s slack; an expired leg
    // is abandoned (idle a beat, then re-roll) exactly like a blocked one.
    // Armed ONLY for legs picked here: Chase/Flee re-target the follower
    // directly every tick, and the target-match check disarms the watchdog
    // the moment the leg is no longer ours.
    float _legTimeLeft;
    Vector3 _legTarget;
    bool _legArmed;

    /// <summary>SC-MOB-ROAM-AUDIT — computer-gated amphibious policy for actors
    /// AUTHORED on water (Dark Mire mucosa, pond fish). Their placement is the
    /// capability statement: with the land-only Computer policy their spawn tri
    /// was an illegal path START, every SetTarget failed, and they stood frozen
    /// from tick 0 (roam-sim: 12 actors blocked 1800/1800 ticks).</summary>
    static readonly NavTraversal ComputerAmphibious = new()
    {
        WaterCostMultiplier = 4f,
        Actor = Assets.LogicalFlagsStore.ActorClass.ComputerPlayer,
    };

    public ActorFollower(NavMesh mesh, Vector3 startPos, float speed, int rngSeed, Vector3 initialFacing)
    {
        // SC-MOB-ROAM-AUDIT — island escape. A spawn that resolves onto a tiny
        // disconnected component (sd_r2's 4-triangle trap-pit floors sit 3u
        // under the dungeon floor and catch best-Y spawn snaps) can never path
        // anywhere: the actor freezes for the whole session. Re-snap to the
        // nearest triangle of a real component before wiring the follower.
        startPos = EscapeTinyIsland(mesh, startPos);
        // Phase 24-NAV-LOGICAL-FLAGS — NPC / brain actors respect the
        // lf_computer_player gate so they stay out of human-only zones.
        // SC-MOB-ROAM-AUDIT — actors placed on a Water triangle get the
        // amphibious variant; everyone else stays land-only per DS1 stock.
        var traversal = NavTraversal.Computer;
        if (mesh.TryFindTriangle(startPos, out var spawnTri, includeFadeHidden: true) &&
            mesh.Kinds[spawnTri] == Assets.SnoModel.FloorKind.Water)
            traversal = ComputerAmphibious;
        Follower = new NavFollower(mesh, startPos, speed)
        {
            Traversal = traversal,
        };
        _rng = new Random(rngSeed);
        // Collapse to XZ unit vector; degenerate input (Y-only or zero) falls back to +Z.
        var flat = new Vector3(initialFacing.X, 0f, initialFacing.Z);
        float len = flat.Length();
        _facing = len > 1e-4f ? flat / len : Vector3.UnitZ;
        _anchor = startPos;
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

        // SC-MOB-LEG-TIMEOUT — abandon a leg that has overrun its walk
        // budget without arriving. Only for legs this driver picked; an
        // external retarget (chase/flee steering the follower directly)
        // disarms the watchdog until the next PickNewTarget.
        if (_legArmed)
        {
            if (Follower.Target != _legTarget)
            {
                _legArmed = false;
            }
            else if (!Follower.ReachedGoal && !Follower.PathBlocked)
            {
                _legTimeLeft -= dt;
                if (_legTimeLeft <= 0f)
                {
                    _legArmed = false;
                    _idleTicksRemaining = IdleAfterFail;
                    return;
                }
            }
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

        // SC-MOB-ROAM — a finished stroll parks the actor at its post for a random
        // dwell instead of immediately re-rolling, so idle mobs mostly stand and
        // fidget. SC-MOB-ROAM-AUDIT — a blocked path re-routes after a short
        // idle, NOT immediately: an actor whose every pick fails (hemmed in, or
        // its stand-bind rejected) used to re-run target selection + A* at the
        // full 20 Hz forever — a frozen mob that also burned a pathfinder's
        // worth of CPU each tick.
        if (Follower.PathBlocked)
            _idleTicksRemaining = IdleAfterFail;
        else if (Follower.ReachedGoal)
            _idleTicksRemaining = _rng.Next(IdleMinTicks, IdleMaxTicks + 1);
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

    /// <summary>True when <paramref name="startTri"/>'s connected component holds
    /// fewer than <paramref name="limit"/> triangles — a nav island too small to
    /// live on (trap covers, rubble slabs). Bounded flood fill; cost is O(limit).</summary>
    static bool IsTinyComponent(NavMesh mesh, int startTri, int limit)
    {
        var seen = new HashSet<int> { startTri };
        var stack = new Stack<int>();
        stack.Push(startTri);
        while (stack.Count > 0)
        {
            int t = stack.Pop();
            for (int s = 0; s < 3; s++)
            {
                int n = mesh.Neighbors[3 * t + s];
                if (n < 0 || !seen.Add(n)) continue;
                if (seen.Count >= limit) return false;
                stack.Push(n);
            }
        }
        return true;
    }

    /// <summary>If <paramref name="pos"/> resolves onto a tiny nav island, returns
    /// the nearest surrounding point on a real component (ring probe, out to 4u);
    /// otherwise returns <paramref name="pos"/> unchanged.</summary>
    static Vector3 EscapeTinyIsland(NavMesh mesh, Vector3 pos)
    {
        const int MinComponentTris = 12;
        if (!mesh.TryFindTriangle(pos, out var tri, includeFadeHidden: true)) return pos;
        if (!IsTinyComponent(mesh, tri, MinComponentTris)) return pos;
        for (float r = 0.75f; r <= 4.01f; r += 0.75f)
        {
            for (int i = 0; i < 12; i++)
            {
                float a = i * MathF.PI * 2f / 12f;
                var probe = new Vector3(pos.X + MathF.Cos(a) * r, pos.Y, pos.Z + MathF.Sin(a) * r);
                if (!mesh.TryFindTriangle(probe, out var t2, includeFadeHidden: true)) continue;
                if (IsTinyComponent(mesh, t2, MinComponentTris)) continue;
                return probe with { Y = mesh.SampleYOnTriangle(t2, probe) };
            }
        }
        return pos;
    }

    void PickNewTarget()
    {
        // SC-MOB-ROAM — sample around the spawn anchor, not the live position, so
        // strolls stay leashed to the post and a disengaged mob heads home rather
        // than random-walking ever further from where it was authored.
        var origin = _anchor;
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
                if (!mesh.TryFindTriangle(candidate, out var tri)) continue;
                // SC-MOB-ROAM-AUDIT — the sample must land on the actor's OWN
                // layer and on ground its policy can enter. The picker resolves
                // best-Y at the XZ, so on a switchback path or above a valley an
                // un-gated sample happily targeted a leg 10-30u below — mobs
                // marched off toward cross-layer picks and piled against the
                // corner where the route pinched (the field report's krug pile
                // at (-26.5,32,151), reproduced 1:1 by `region roam-sim`).
                float triY = mesh.SampleYOnTriangle(tri, candidate);
                if (MathF.Abs(triY - origin.Y) > 3f) continue;
                if (!Follower.Traversal.CanEnter(mesh.Kinds[tri])) continue;
                Follower.SetTarget(candidate);
                // SC-MOB-LEG-TIMEOUT — arm the watchdog for this leg.
                float ldx = candidate.X - Follower.Position.X;
                float ldz = candidate.Z - Follower.Position.Z;
                float gait = Follower.Speed > 0.5f ? Follower.Speed : 4f;
                _legTimeLeft = MathF.Sqrt(ldx * ldx + ldz * ldz) / gait * 3f + 2f;
                _legTarget = Follower.Target;
                _legArmed = true;
                return;
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
        {
            Follower.SetTarget(origin);
            // SC-MOB-LEG-TIMEOUT — the walk-home fallback leg can be long
            // (a mob returning to its anchor after a chase); watchdog it
            // like any picked leg so a pin on the way home can't freeze.
            float hdx = origin.X - Follower.Position.X;
            float hdz = origin.Z - Follower.Position.Z;
            float gait = Follower.Speed > 0.5f ? Follower.Speed : 4f;
            _legTimeLeft = MathF.Sqrt(hdx * hdx + hdz * hdz) / gait * 3f + 2f;
            _legTarget = Follower.Target;
            _legArmed = true;
        }
        _idleTicksRemaining = IdleAfterFail;
    }
}
