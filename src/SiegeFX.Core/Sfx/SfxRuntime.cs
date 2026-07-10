using System.Globalization;
using System.Numerics;
using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Sfx;

/// <summary>Phase 17-SC-F-2 / Phase 21-SC-SPELL-VFX-2 — stack-based interpreter
/// for the <c>script=[[ ... ]]</c> DSL parsed by <see cref="SfxScriptCompiler"/>.
///
/// Two state buckets:
/// <list type="bullet">
///   <item><b>Persistent emitters</b> — one entry per <c>sfx create … sfx start</c>
///   pair whose script ended without a matching <c>finish/destroy</c>. Each tick
///   we call <see cref="IParticleSink.MaintainFire"/> / <c>MaintainSmoke</c> /
///   <c>MaintainSteam</c> / <c>MaintainGlow</c> at the emitter's anchored world
///   position. fh_r1's farmhouse fire / smoke columns / waterfall froth, and
///   spell lightsource halos that follow a motion handle.</item>
///   <item><b>Coroutine scripts</b> — script bodies with <c>pause N</c> are
///   stepped statement-by-statement on each tick, yielding when they hit a
///   pause budget; one-shot spawns (sparks, lightning, brief fire bursts) live
///   and die inside the particle backend.</item>
/// </list>
///
/// <para>Phase 21-SC-SPELL-VFX-2 made the runtime context-aware so DS1 spell
/// scripts (zap, fireball, ...) render natively. Callers pass an
/// <see cref="SfxContext"/> with caster/target world positions; the VM resolves
/// <c>#SOURCE</c>, <c>#SOURCE_POSITION</c>, <c>#TARGET</c>, <c>#TARGET_KB</c>
/// macros against it and honours <c>sfx target $h source</c> /
/// <c>sfx attach_point $h @bone source</c> by mutating the bolt's far-end
/// position. Lightning emitters track two endpoints (Anchor + OtherEnd) so the
/// shipped zap renders as a hand-to-target beam rather than a 1-unit vertical
/// stub at the impact point.</para>
///
/// <para>Verbs the VM still doesn't recognize (spawn / worldmsg / frandrange /
/// randrange / camerashake) log once and continue — same Phase 17-SC-F policy
/// as the parser's <see cref="StatementKind.Raw"/> fallthrough. Keeps every
/// region's emitters running rather than freezing on the first un-modeled
/// verb. Phase 21-SC-SPELL-VISUAL slices A–H landed cylinder / sray / fireb /
/// sphere / orbiter / trackball / lightsource / curve / additive Glow / sfx
/// attach / rat / offset_bone / direction / waitfor + collision gate / get
/// target_position + collision point/direction/target / minimal if-evaluator /
/// per-script texture honoring on cylinders / $name+$[N] param-string
/// substitution — 56 of 61 offensive spells now flip COVERED in the visual-
/// audit; the remaining 5 are DS1 author stubs (sound-only, no script body).</para></summary>
public sealed class SfxRuntime
{
    readonly SfxScriptStore _store;
    readonly IParticleSink _particles;
    readonly List<PersistentEmitter> _emitters = new();
    readonly List<RunningScript> _scripts = new();
    readonly HashSet<string> _unhandledVerbsLogged = new(StringComparer.OrdinalIgnoreCase);
    // Phase 21-SC-SPELL-VFX-MOTION-HANDLE — live motion handles keyed by
    // monotonically-increasing id. Cleared on Clear(); per-handle pruning
    // happens inside Tick when Done && no emitter still references it.
    readonly Dictionary<int, MotionState> _motionHandles = new();
    int _nextMotionId = 1;

    public int LivePersistentCount => _emitters.Count;
    public int LiveCoroutineCount  => _scripts.Count;
    public IReadOnlyCollection<string> UnhandledVerbs => _unhandledVerbsLogged;

    /// <summary>Phase 21-SC-SPELL-VFX-3c — the set of <c>sfx create &lt;kind&gt;</c>
    /// kinds the runtime knows how to render (mirrors the cases in
    /// <see cref="MapMode"/>). Callers (the spell-cast site, the visual-audit
    /// CLI) consult this set to decide whether a script is fully runnable
    /// or whether to route it to a placeholder visual instead of letting
    /// the VM spawn stranded emitters bound to unhandled handles. If
    /// <c>MapMode</c> grows new branches, add them here too — keep them
    /// adjacent in PRs.</summary>
    public static IReadOnlyCollection<string> SupportedCreateKinds { get; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "fire", "smoke", "steam", "lightning", "explosion", "sparkles",
            // Phase 21-SC-SPELL-VFX-3f/g/h/i — first-pass primitives.
            // Visual quality not yet DS1-pixel-faithful; the per-primitive
            // test list at session-end will surface where tuning is needed.
            "flurry", "fireb", "cylinder", "sray",
            // Phase 21-SC-SPELL-VFX-3q — 1-spell outliers mapped onto
            // existing primitives (charge -> sparkles, polygonalexplosion
            // -> explosion, spe -> flurry). Bespoke implementations would
            // cost more than the leverage; tweak list will say which to
            // promote to native if the user objects to the visual.
            "charge", "polygonalexplosion", "spe",
            // Phase 21-SC-SPELL-VFX-MOTION-HANDLE — orbital / homing /
            // glow / spline motion handles. Child emitters following
            // them via `sfx target $emitter $motion` get per-frame
            // position updates from the runtime's motion-handle pump.
            "orbiter", "trackball", "lightsource", "curve",
            // Phase 21-SC-SPELL-VISUAL-H — sphere primitive (firebomb /
            // bombard / dave_shield / energy_globe / explosion_volume etc.).
            // First-pass renders as omni-directional SpawnSpark scaled by
            // the authored radius.
            "sphere",
            // Phase 23-fold — monster-corpus kinds surfaced by the widened
            // catalog: linetracer renders as a fading tracer ribbon;
            // spawn (model-frag bursts: lava/rock beast blasts) renders as
            // poly shards until the frag-mesh hook lands (FLAGGED
            // DEVIATION alongside model() orbits).
            "linetracer", "spawn",
        };

    /// <summary>True iff every <c>sfx create &lt;kind&gt;</c> reachable from
    /// the supplied script — including transitively through <c>call
    /// &lt;subscript&gt;</c> — is in <see cref="SupportedCreateKinds"/>.
    /// Use this at a cast site to decide between "let the VM run the native
    /// script" and "the script asks for an unmodeled primitive (orbiter,
    /// trackball, cylinder, …); spawn a placeholder visual instead so the
    /// partial run doesn't leave stranded emitters at the caster" — see
    /// fireball's `sfx target $fire $trackball` pattern, which without this
    /// gate anchors the fire emitters at #SOURCE permanently when trackball
    /// is unimplemented (they're targeting a handle that was never actually
    /// resolved).
    ///
    /// <para>Recurses through <c>call</c> with a name-based visited-set
    /// cycle guard — fireball's top-level script has no <c>sfx create</c> at
    /// all, just a <c>call fireball_base</c>, and fireball_base is where
    /// the trackball + fire creates live. The non-recursive form (the first
    /// pass shipped at d99af1e) reported fireball as fully-covered and let
    /// the VM run, reproducing the static-fire-at-caster bug.</para></summary>
    public static bool IsScriptFullyCovered(SfxScript script, SfxScriptStore store)
    {
        if (script is null) return false;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return WalkScriptForCoverage(script, store, visited);
    }

    static bool WalkScriptForCoverage(SfxScript script, SfxScriptStore store, HashSet<string> visited)
    {
        if (!visited.Add(script.Name)) return true; // already walked — cycle / diamond
        SfxProgram prog;
        try { prog = SfxScriptCompiler.Compile(script.Name, script.Body); }
        catch { return false; } // malformed — treat as not-covered, force placeholder
        foreach (var stmt in prog.Statements)
        {
            if (stmt.Kind == StatementKind.SfxCreate)
            {
                if (stmt.Tokens.Count == 0) continue;
                // ToLowerInvariant before the lookup: MapMode at runtime
                // (ExecCreate calls .ToLowerInvariant() before the switch)
                // is case-sensitive on lowercase, so "Fire" would slip past
                // a case-insensitive HashSet check here only to land on
                // EmitterMode.Unsupported during execution. DS1 ships
                // lowercase so this is defense-in-depth, not a current-data
                // bug — but keeps the invariant tight.
                if (!SupportedCreateKinds.Contains(stmt.Tokens[0].ToLowerInvariant()))
                    return false;
            }
            else if (stmt.Kind == StatementKind.Call)
            {
                if (stmt.Tokens.Count == 0) continue;
                var callName = stmt.Tokens[0].Trim().Trim('"');
                if (string.IsNullOrEmpty(callName)) continue;
                // Subscript not in store = treat as unknown unmodeled work.
                // Forces placeholder rather than running a script that may
                // call into anything.
                if (!store.TryGet(callName, out var sub)) return false;
                if (!WalkScriptForCoverage(sub, store, visited)) return false;
            }
        }
        return true;
    }

    /// <summary>Program-only overload kept for callers that already hold a
    /// compiled <see cref="SfxProgram"/> and don't need <c>call</c>-recursion
    /// (e.g. unit tests that hand-author a single program). Production cast
    /// sites should use the script+store overload above so fireball-class
    /// recipes don't slip through the gate via a top-level <c>call</c>.</summary>
    public static bool IsScriptFullyCovered(SfxProgram program)
    {
        if (program is null) return false;
        foreach (var stmt in program.Statements)
        {
            if (stmt.Kind != StatementKind.SfxCreate) continue;
            if (stmt.Tokens.Count == 0) continue;
            if (!SupportedCreateKinds.Contains(stmt.Tokens[0].ToLowerInvariant()))
                return false;
        }
        return true;
    }

    public SfxRuntime(SfxScriptStore store, IParticleSink particles)
    {
        _store     = store;
        _particles = particles;
    }

    // Phase 23b — the VM's only RNG use (`sfx rat` orientation kick).
    // Random.Shared by default; the timeline-golden CLI injects a fixed
    // seed so headless runs are byte-for-byte reproducible.
    Random _rng = Random.Shared;

    /// <summary>Phase 23b — make every subsequent VM roll deterministic.
    /// Used by <c>siegefx sfx timeline</c> golden generation; the live
    /// renderer never calls this.</summary>
    public void SetDeterministicSeed(int seed) => _rng = new Random(seed);

    /// <summary>Legacy entry-point for region emitters / non-targeted scripts.
    /// Source = target = origin (no #SOURCE vs #TARGET distinction needed).</summary>
    public bool Spawn(string scriptName, Vector3 origin, IReadOnlyList<string>? callerArgs = null)
        => Spawn(scriptName, SfxContext.At(origin), callerArgs);

    /// <summary>Phase 21-SC-SPELL-VFX-2 — context-aware spawn for spell casts.
    /// The VM resolves DS1's <c>#SOURCE</c>/<c>#TARGET</c> macros and bone
    /// translators against <paramref name="ctx"/> so the rendered effect
    /// fires from caster to target.</summary>
    public bool Spawn(string scriptName, in SfxContext ctx, IReadOnlyList<string>? callerArgs = null)
    {
        if (!_store.TryGet(scriptName, out var script)) return false;

        // Compile lazily; SfxProgram is non-mutating so a future cache here is
        // free, but the price-of-freshness is negligible (~1ms for the longest
        // shipped body) and keeps the data path simple.
        var prog = SfxScriptCompiler.Compile(scriptName, script.Body);
        var rs = new RunningScript(prog, ctx, callerArgs);
        StepUntilYield(rs);
        if (!rs.Done) _scripts.Add(rs);
        return true;
    }

    public void Tick(float dt)
    {
        // Advance coroutines first — a pause that just expired might queue more
        // persistent emitters before the maintain pass runs this frame.
        for (int i = _scripts.Count - 1; i >= 0; i--)
        {
            var rs = _scripts[i];
            rs.PauseRemaining -= dt;
            if (rs.PauseRemaining > 0f) continue;

            // Phase 21-SC-SPELL-VISUAL-G — waitfor coroutine gate.
            // When the script asked to wait on a motion handle's collision,
            // poll the motion's Done flag here (set when the trackball
            // reaches its target in AdvanceMotionHandles). Two resume paths:
            //   1. Motion went Done → push the appropriate #*_COLLISION
            //      handle (Kind=collision_type tag) and step.
            //   2. Timeout exhausted → push #NO_COLLISION and step.
            // The collision-type tag is picked heuristically: any reachable
            // motion arriving at its TargetPos counts as OBJECT_COLLISION
            // (we don't yet distinguish actor vs world hits — both branches
            // in shipped DS1 fireball scripts paint impact at the same
            // world point).
            // Phase 21-SC-SPELL-VISUAL-G fold — pending resolution from
            // the previous tick's AdvanceMotionHandles pass takes
            // priority. AdvanceMotionHandles tagged the script with the
            // proper collision_type before pruning the motion entry, so
            // the !found-defaults-to-object-collision fallback below
            // only fires for never-resolved waits.
            if (!string.IsNullOrEmpty(rs.PendingCollisionTag))
            {
                rs.Stack.Push(new Handle
                {
                    Kind = rs.PendingCollisionTag,
                    Anchor = rs.PendingCollisionPos,
                    OtherEnd = rs.PendingCollisionPos
                });
                rs.PendingCollisionTag = null;
                rs.PendingCollisionPos = Vector3.Zero;
            }
            else if (rs.WaitMotionId > 0)
            {
                rs.WaitTimeout -= dt;
                bool resumed = false;
                string collisionTag = "no_collision";
                Vector3 resolvedPos = rs.Ctx.TargetPos;
                bool found = _motionHandles.TryGetValue(rs.WaitMotionId, out var watched);
                // Same-tick resume paths (rare — the typical resolution
                // happens via PendingCollisionTag above):
                //   1. Motion just went Done in THIS tick before we got
                //      back to the coroutine pass — read DoneByCollision
                //      to pick the right tag.
                //   2. Motion is GONE without a pending tag (race or a
                //      script that hit waitfor on an already-vanished
                //      handle) — default to object_collision.
                //   3. WaitTimeout expired with the motion still live —
                //      no_collision; the trackball never reached its
                //      target within the script's window.
                if (found && (watched.Done || watched.Arrived))
                {
                    // Phase 23d-2a — Arrived = collided but lingering through
                    // afterlife(); waitfor resolves at the collision edge.
                    collisionTag = watched.DoneByCollision ? "object_collision" : "no_collision";
                    resolvedPos  = watched.Position;
                    resumed = true;
                }
                else if (!found)
                {
                    collisionTag = "object_collision";
                    resumed = true; // resolvedPos stays at Ctx.TargetPos fallback
                }
                else if (rs.WaitTimeout <= 0f)
                {
                    collisionTag = "no_collision";
                    resolvedPos  = watched.Position;
                    resumed = true;
                }
                if (!resumed) continue;
                rs.Stack.Push(new Handle { Kind = collisionTag, Anchor = resolvedPos, OtherEnd = resolvedPos });
                rs.WaitMotionId = 0;
                rs.WaitTimeout  = 0f;
            }

            StepUntilYield(rs);
            if (rs.Done) _scripts.RemoveAt(i);
        }

        // Phase 21-SC-SPELL-VFX-MOTION-HANDLE — advance live motion
        // handles before the emitter maintain pass so emitters that
        // Phase 23d-2a — mature delay(n)-queued starts before motion
        // advances so a start landing this tick behaves as if it fired at
        // the top of the frame.
        for (int i = _delayed.Count - 1; i >= 0; i--)
        {
            var d = _delayed[i];
            d.Remaining -= dt;
            if (d.Remaining > 0f) { _delayed[i] = d; continue; }
            _delayed.RemoveAt(i);
            DispatchStart(d.H, d.SelfName);
        }

        // follow a motion read the just-updated Position.
        AdvanceMotionHandles(dt);

        // Run continuous spawn budgets for every persistent emitter.
        for (int i = _emitters.Count - 1; i >= 0; i--)
        {
            var e = _emitters[i];

            // Phase 21-SC-SPELL-VFX-MOTION-HANDLE — refresh Position from
            // the motion handle each tick; if the motion is gone or done,
            // drop the emitter so e.g. a fire emitter targeting a
            // departed trackball doesn't render at a stale point forever.
            if (e.TargetMotionId > 0)
            {
                // `|| motion.Done` is defensive: AdvanceMotionHandles prunes
                // Done entries before we get here, so the dict miss is the
                // expected drop path. Keep the second clause in case future
                // refactoring reorders the pump and a Done entry survives
                // into the emitter pass — costs one branch per emitter.
                if (!_motionHandles.TryGetValue(e.TargetMotionId, out var motion) || motion.Done)
                {
                    _emitters.RemoveAt(i);
                    continue;
                }
                e.Position = motion.Position;
            }
            // Age always (flicker phase needs it); expire on dur(N).
            e.AgeSec += dt;
            if (e.Duration > 0f && e.AgeSec >= e.Duration)
            {
                _emitters.RemoveAt(i);
                continue;
            }
            switch (e.Mode)
            {
                case EmitterMode.Fire:
                    e.Carry = e.HasSpec
                        ? _particles.MaintainPlume(in e.Spec, e.Position, e.AgeSec, dt, e.Carry)
                        : _particles.MaintainFire(e.Position, e.Color, e.Scale, dt, e.Rate, e.Carry);
                    break;
                case EmitterMode.Smoke:
                    e.Carry = e.HasSpec
                        ? _particles.MaintainPlume(in e.Spec, e.Position, e.AgeSec, dt, e.Carry)
                        : _particles.MaintainSmoke(e.Position, e.Color, e.Scale, dt, e.Rate, e.Carry);
                    break;
                case EmitterMode.Steam:
                    e.Carry = e.HasSpec
                        ? _particles.MaintainPlume(in e.Spec, e.Position, e.AgeSec, dt, e.Carry)
                        : _particles.MaintainSteam(e.Position, e.Color, e.Scale, dt, e.Rate, e.Carry);
                    break;
                case EmitterMode.Glow:
                {
                    // Scale is overloaded as the halo radius for Glow mode
                    // (the motion-handle dispatch arm sets it from h.Scale,
                    // bounded above 0.30 so a 0-scale script still glows).
                    // Phase 23d-2d — frequency(f) flicker modulates the
                    // halo's alpha at the authored period.
                    var gc = e.Color;
                    if (e.Flicker > 0.001f)
                        gc.W *= 0.70f + 0.30f * MathF.Sin(e.AgeSec / e.Flicker);
                    e.Carry = _particles.MaintainGlow(e.Position, gc, e.Scale, dt, e.Rate, e.Carry);
                    break;
                }
            }
            _emitters[i] = e;
        }
    }

    /// <summary>Drop every running script + persistent emitter. Called on
    /// region teardown so a relaunch doesn't accumulate stale ghosts.</summary>
    public void Clear()
    {
        _emitters.Clear();
        _scripts.Clear();
        _motionHandles.Clear();
        _delayed.Clear();
        _modelOrbits.Clear();
        _nextMotionId = 1;
    }

    /// <summary>Phase 21-SC-SPELL-VFX-MOTION-HANDLE — per-tick advance for
    /// every live motion handle. Orbiter / trackball / lightsource / curve
    /// each compute their next Position from their Anchor + per-handle
    /// state. Done handles stay in the dict for the rest of the tick (so
    /// the emitter pass can detect them and prune followers cleanly) and
    /// get pruned at the bottom.</summary>
    void AdvanceMotionHandles(float dt)
    {
        if (_motionHandles.Count == 0) return;
        // Materialize keys so we can rewrite values without enumerator
        // invalidation. ints are tiny, list reuse not worth it at this scale.
        var ids = new List<int>(_motionHandles.Keys);
        foreach (var id in ids)
        {
            var m = _motionHandles[id];
            // Phase 23-fold F6 — delay(n) hold: frozen until the queued
            // start matures.
            if (m.HoldSec > 0f)
            {
                m.HoldSec -= dt;
                _motionHandles[id] = m;
                continue;
            }
            // Refresh Anchor from parent if this motion follows another.
            if (m.ParentMotionId > 0 && _motionHandles.TryGetValue(m.ParentMotionId, out var parent))
            {
                if (parent.Done)
                {
                    m.Done = true;
                    _motionHandles[id] = m;
                    continue;
                }
                m.Anchor = parent.Position;
            }

            switch (m.Kind.ToLowerInvariant())
            {
                case "orbiter":
                    // Phase 23d-2a — true spherical polar orbit per SU 212:
                    // phi is LATITUDE (iphi default 2.0 — the default
                    // orbiter sweeps a vertical circle), theta is
                    // LONGITUDE (itheta default 0), radiusi spirals, and
                    // vdisplace drifts the whole orbit vertically per
                    // second (pillar_fire's rising column).
                    m.Phi    += m.PhiRate * dt;
                    m.Theta  += m.ThetaRate * dt;
                    m.Radius += m.RadiusInc * dt;
                    m.VOffset += m.VDisplace * dt;
                    {
                        float r = m.Radius;
                        m.Position = m.Anchor + new Vector3(
                            MathF.Sin(m.Phi) * MathF.Cos(m.Theta) * r,
                            MathF.Cos(m.Phi) * r,
                            MathF.Sin(m.Phi) * MathF.Sin(m.Theta) * r)
                            + new Vector3(0f, m.VOffset, 0f);
                    }
                    break;

                case "trackball":
                    // Phase 23d-2a — SU-212 trackball motion: velocity ramps
                    // by accel(f)/sec toward max_velocity; homing() re-aims
                    // at the (live) target while non-homing locks the launch
                    // direction; radius/spin add a spiral interference
                    // around the flight line; afterlife(f) keeps the handle
                    // (and its child emitters) alive at the impact point
                    // after collision while waitfor resumes immediately.
                    {
                        if (m.Arrived)
                        {
                            m.AfterlifeSec -= dt;
                            if (m.AfterlifeSec <= 0f) m.Done = true;
                            break;
                        }
                        var to = m.Target - m.Position;
                        float distSq = to.LengthSquared();
                        if (distSq <= 0.25f)
                        {
                            m.Position = m.Target;
                            m.DoneByCollision = true;
                            if (m.AfterlifeSec > 0f) m.Arrived = true;
                            else m.Done = true;
                        }
                        else
                        {
                            float dist = MathF.Sqrt(distSq);
                            var aim = to / dist;
                            if (!m.Homing)
                            {
                                if (!m.DirLocked) { m.LockedDir = aim; m.DirLocked = true; }
                                aim = m.LockedDir;
                            }
                            m.Speed = MathF.Min(m.MaxSpeed > 0f ? m.MaxSpeed : 80f,
                                                m.Speed + m.AccelRate * dt);
                            float step = MathF.Min(m.Speed * dt, dist);
                            m.Position += aim * step;
                            if (m.SpiralRadius > 0.001f)
                            {
                                float a1 = m.SpiralAngle;
                                m.SpiralAngle += (m.SpiralRate != 0f ? m.SpiralRate : MathF.Tau) * dt;
                                var side = Vector3.Cross(Vector3.UnitY, aim);
                                side = side.LengthSquared() > 0.01f ? Vector3.Normalize(side) : Vector3.UnitX;
                                var up = Vector3.Cross(aim, side);
                                m.Position += side * (MathF.Cos(m.SpiralAngle) - MathF.Cos(a1)) * m.SpiralRadius
                                            + up   * (MathF.Sin(m.SpiralAngle) - MathF.Sin(a1)) * m.SpiralRadius;
                            }
                        }
                    }
                    break;

                case "lightsource":
                    // Static at Anchor unless it has a parent (in which
                    // case Anchor was just refreshed above). Position
                    // tracks anchor.
                    m.Position = m.Anchor;
                    break;

                case "curve":
                    // Phase 23d-2b — quadratic Bezier from Anchor → Target,
                    // drawn at velocity(f) world-units/sec (doc default 30)
                    // with the control point lifted by curvature(f)
                    // (doc: "the higher the less straight", default 1).
                    {
                        float len = MathF.Max(0.05f, (m.Target - m.Anchor).Length());
                        float speed = m.Speed > 0.01f ? m.Speed : 30f;
                        float t = MathF.Min(1f, m.Elapsed * speed / len);
                        float lift = m.Curvature > 0f ? m.Curvature : 1f;
                        var mid = (m.Anchor + m.Target) * 0.5f + new Vector3(0f, lift, 0f);
                        float u = 1f - t;
                        m.Position = (u * u) * m.Anchor + (2f * u * t) * mid + (t * t) * m.Target;
                        // The curve stays alive to the end of dur (trail
                        // afterglow); Done at whichever is later — path
                        // completion or authored duration.
                        if (t >= 1f && (m.Duration <= 0.001f || m.Elapsed >= m.Duration))
                            m.Done = true;
                    }
                    break;
            }

            // Lifetime expiry — applies to every kind that has a positive
            // Duration (e.g. orbiter dur(6) in flame_blades, lightsource
            // dur(2) for short pulses).
            m.Elapsed += dt;
            if (m.Duration > 0f && m.Elapsed >= m.Duration) m.Done = true;
            _motionHandles[id] = m;
        }

        // Phase 21-SC-SPELL-VISUAL-G fold — resolve any waiters BEFORE we
        // prune Done motion handles, so the post-collision branch reads
        // the right collision_type. Pre-fold the prune ran first and the
        // Tick coroutine's `!found` path defaulted to object_collision,
        // which mis-tagged Duration-driven trackball expiries as if they
        // had hit something. After this pass each waiting RunningScript
        // either picks up a tagged PendingCollisionTag (consumed at the
        // next coroutine pass) or stays waiting if its motion is still
        // live.
        for (int s = 0; s < _scripts.Count; s++)
        {
            var rs = _scripts[s];
            if (rs.WaitMotionId == 0) continue;
            if (!_motionHandles.TryGetValue(rs.WaitMotionId, out var watched)) continue;
            if (!watched.Done && !watched.Arrived) continue;
            rs.PendingCollisionTag = watched.DoneByCollision ? "object_collision" : "no_collision";
            rs.PendingCollisionPos = watched.Position;
            rs.WaitMotionId = 0;
            rs.WaitTimeout  = 0f;
        }

        // Cleanup: prune Done motion handles. Doing this AFTER the emitter
        // maintain pass would let dead motion handles accumulate for a
        // frame; prune now so live followers see a stable view.
        // Build a removal list rather than mutating during enumeration.
        List<int>? toRemove = null;
        foreach (var kv in _motionHandles)
        {
            if (kv.Value.Done)
            {
                toRemove ??= new List<int>();
                toRemove.Add(kv.Key);
            }
        }
        if (toRemove is not null)
            foreach (var id in toRemove) _motionHandles.Remove(id);
    }

    /// <summary>Phase 17-SC-J — register a continuous fire/smoke/steam column
    /// without going through an sfx_script. fh_r1's burning farmhouse uses
    /// legacy <c>emt_particle</c> placements that ship a raw
    /// <c>[particle_emitter]</c> block on the *instance* (count, red/green/blue,
    /// growth, yacc, zacc, zvel, fade, dark, …) instead of calling a named
    /// effect script. RenderHost parses those blocks and maps them onto this
    /// API; modern sfx_script-driven emitters still go through Spawn().</summary>
    public void AddPersistentEmitter(ParticleKind kind, Vector3 position, Vector4 color, float scale, float rate)
    {
        _emitters.Add(new PersistentEmitter
        {
            Mode     = kind switch
            {
                ParticleKind.Fire  => EmitterMode.Fire,
                ParticleKind.Smoke => EmitterMode.Smoke,
                ParticleKind.Steam => EmitterMode.Steam,
                _ => EmitterMode.Smoke,
            },
            Position = position,
            Color    = color,
            Scale    = scale,
            Rate     = rate,
        });
    }

    // ---- step engine ---------------------------------------------------

    void StepUntilYield(RunningScript rs)
    {
        while (rs.Ip < rs.Program.Statements.Count)
        {
            var stmt = rs.Program.Statements[rs.Ip];
            rs.Ip++;
            switch (stmt.Kind)
            {
                case StatementKind.SfxCreate:
                    ExecCreate(rs, stmt);
                    break;
                case StatementKind.SfxStart:
                    ExecStart(rs, stmt);
                    break;
                case StatementKind.SfxDestroy:
                case StatementKind.SfxFinish:
                    ExecFinish(rs, stmt);
                    break;
                case StatementKind.SfxTarget:
                    ExecSfxTarget(rs, stmt);
                    break;
                case StatementKind.SfxAttachPoint:
                case StatementKind.SfxPositionAt:
                    ExecAttachPoint(rs, stmt);
                    break;
                case StatementKind.SfxFriendlyTarget:
                    // freeze_targets / friendly target — no visual side-effect
                    // in our renderer; consume the relevant stack item if the
                    // operand is #POP so the script's stack discipline holds.
                    ConsumeStackOperand(rs, stmt.Tokens);
                    break;
                case StatementKind.Set:
                    ExecSet(rs, stmt);
                    break;
                case StatementKind.Pause:
                {
                    // Phase 23-fold F2 — `pause #POP` pops a rolled value
                    // (frandrange residue) and uses it as the duration.
                    // exp_test's `frandrange .2 1; pause #POP; call
                    // exp_test;` loop relies on this yield — without it the
                    // synchronous self-call recursed to StackOverflow.
                    float sec;
                    bool have = TryParseFloat(stmt.Tokens, 0, out sec);
                    if (!have && stmt.Tokens.Count > 0
                        && stmt.Tokens[0].StartsWith("#", StringComparison.Ordinal)
                        && rs.Stack.Count > 0)
                    {
                        var top = stmt.Tokens[0].Equals("#PEEK", StringComparison.OrdinalIgnoreCase)
                            ? rs.Stack.Peek()
                            : rs.Stack.Pop();
                        have = float.TryParse(top.Kind, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out sec);
                        // Non-numeric pops (a real sfx handle) still yield a
                        // frame — DS1 treats an unresolvable pause as the
                        // minimum wait, and yielding preserves loop safety.
                        if (!have) { sec = 0.05f; have = true; }
                    }
                    if (have && sec > 0f)
                    {
                        rs.PauseRemaining = sec;
                        return; // yield
                    }
                    break;
                }
                case StatementKind.Call:
                    ExecCall(rs, stmt);
                    break;
                case StatementKind.Waitfor:
                    if (ExecWaitfor(rs, stmt)) return; // yielded
                    break;
                case StatementKind.Get:
                    ExecGet(rs, stmt);
                    break;
                case StatementKind.IfBegin:
                {
                    // Phase 21-SC-SPELL-VISUAL-G — evaluate the
                    // parenthesized condition; on false, skip past the
                    // matching IfEnd. LastIfTaken records the branch
                    // outcome so a following ElseBegin can decide whether
                    // to run its body. Nested if/else inside the body is
                    // handled by the same dispatch when StepUntilYield
                    // recurses through it.
                    bool cond = EvalCondition(rs, stmt.Tokens);
                    rs.LastIfTaken = cond;
                    if (!cond) SkipToMatching(rs, StatementKind.IfBegin, StatementKind.IfEnd);
                    break;
                }
                case StatementKind.ElseBegin:
                    // Run the else body only if the matching if-body did
                    // NOT run. LastIfTaken is preserved across IfEnd so
                    // we can read it here.
                    if (rs.LastIfTaken)
                        SkipToMatching(rs, StatementKind.ElseBegin, StatementKind.ElseEnd);
                    break;
                case StatementKind.IfEnd:
                case StatementKind.ElseEnd:
                    // Markers — no runtime side effect. Nesting is handled
                    // by SkipToMatching's depth counter, not by these
                    // markers themselves.
                    break;
                case StatementKind.SoundPlay:
                case StatementKind.SoundStop:
                    // Audio is wired separately (see SiegeAudioRouter); the
                    // sfx_script side just declares "make a sound here" and
                    // we silently honor it as a no-op rather than surfacing
                    // every cast as `unhandled verb`. When we wire SED-driven
                    // audio into the VM this becomes a real dispatch.
                    break;
                case StatementKind.SfxAttach:
                    ExecAttach(rs, stmt);
                    break;
                case StatementKind.SfxOffset:
                    ExecOffset(rs, stmt);
                    break;
                case StatementKind.SfxRat:
                    ExecRat(rs, stmt);
                    break;
                case StatementKind.SfxDirection:
                    ExecDirection(rs, stmt);
                    break;
                case StatementKind.Raw:
                    if (TryExecRawVerb(rs, stmt)) break;
                    LogUnhandledOnce(stmt.Verb);
                    ConsumeStackOperand(rs, stmt.Tokens);
                    break;
            }
        }
        rs.Done = true;
    }

    // ---- Phase 23d-2e — model() orbits ---------------------------------
    struct ModelOrbitSpec
    {
        public string  Template;
        public float   ScaleIn, ScaleOut, ScaleMax; // model_sin/sout/smax
        public Vector3 RotInc;                       // rot_inc deg/sec
        public Vector3 RotBase;                      // rotate(x,y,z) deg
    }
    readonly Dictionary<int, ModelOrbitSpec> _modelOrbits = new();

    /// <summary>Phase 23d-2e — a live model(<template>) orbit for the host
    /// to render at the motion's position with the authored scale
    /// animation (model_sin ramp in, model_smax peak, model_sout ramp
    /// out) and rotate + rot_inc orientation.</summary>
    public readonly record struct ModelOrbitView(
        string Template, Vector3 Position, float Scale, Vector3 RotationDeg);

    /// <summary>Phase 23d-2e — enumerate live model orbits each frame.
    /// The RenderHost mesh hook consumes this; headless sinks ignore it.</summary>
    public void CollectModelOrbits(List<ModelOrbitView> into)
    {
        into.Clear();
        if (_modelOrbits.Count == 0) return;
        List<int>? dead = null;
        foreach (var (id, spec) in _modelOrbits)
        {
            if (!_motionHandles.TryGetValue(id, out var m) || m.Done)
            {
                (dead ??= new List<int>()).Add(id);
                continue;
            }
            float scale = spec.ScaleMax;
            if (spec.ScaleIn > 0.001f && m.Elapsed < spec.ScaleIn)
                scale *= m.Elapsed / spec.ScaleIn;
            if (m.Duration > 0f && spec.ScaleOut > 0.001f)
            {
                float outStart = m.Duration - spec.ScaleOut;
                if (m.Elapsed > outStart)
                    scale *= MathF.Max(0f, 1f - (m.Elapsed - outStart) / spec.ScaleOut);
            }
            into.Add(new ModelOrbitView(
                spec.Template, m.Position, scale,
                spec.RotBase + spec.RotInc * m.Elapsed));
        }
        if (dead is not null)
            foreach (var id in dead) _modelOrbits.Remove(id);
    }

    /// <summary>Phase 23d-2e — camerashake hook: (amplitude, duration).
    /// The host (RenderHost chase cam) wires this; headless runs leave it
    /// null.</summary>
    public Action<float, float>? CameraShakeHook;

    /// <summary>Phase 23d-2e — worldmsg hook: (WE_* event name, cast
    /// context). Gameplay-side consumers (spell damage sync) live on the
    /// host; the visual VM just forwards.</summary>
    public Action<string, SfxContext>? WorldMsgHook;

    /// <summary>Phase 23d-2e — raw verbs the VM now executes (the audit
    /// consults this so its unhandled-verb table stays truthful).</summary>
    public static readonly IReadOnlySet<string> HandledRawVerbs =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "randrange", "frandrange", "camerashake", "worldmsg", "exit" };

    // Phase 23d-2e — real dispatch for the formerly-soft raw verbs.
    bool TryExecRawVerb(RunningScript rs, SfxStatement stmt)
    {
        var v = stmt.Verb;
        if (v.Equals("randrange", StringComparison.OrdinalIgnoreCase)
         || v.Equals("frandrange", StringComparison.OrdinalIgnoreCase))
        {
            // Phase 23-fold F3 — the int form is INCLUSIVE of both bounds:
            // giant_spider_chunks branches `randrange 1 8` across eight
            // frag templates ($frag == 1 .. == 8), so branch 8 must be
            // reachable. frandrange stays continuous over [a, b).
            // Deterministic under SetDeterministicSeed.
            float a = 0f, b = 1f;
            if (stmt.Tokens.Count >= 1) TryParseF(stmt.Tokens[0], out a);
            if (stmt.Tokens.Count >= 2) TryParseF(stmt.Tokens[1], out b);
            string val;
            if (char.ToLowerInvariant(v[0]) == 'f')
            {
                float roll = a + (float)_rng.NextDouble() * (b - a);
                val = roll.ToString("0.###", CultureInfo.InvariantCulture);
            }
            else
            {
                int lo = (int)MathF.Floor(MathF.Min(a, b));
                int hi = (int)MathF.Floor(MathF.Max(a, b));
                val = _rng.Next(lo, hi + 1).ToString(CultureInfo.InvariantCulture);
            }
            // The value rides the stack as a tagged handle; `set $x #POP`
            // bridges Kind into rs.Vars so if-conditions compare it.
            rs.Stack.Push(new Handle { Kind = val, Anchor = rs.Origin, OtherEnd = rs.Origin });
            return true;
        }
        if (v.Equals("camerashake", StringComparison.OrdinalIgnoreCase))
        {
            // Phase 23-fold F4 — shipped calls are `camerashake
            // camera_stomp s<frequency=…&magnitude_y=…&duration=…&…>`
            // (a name + property list), not two bare floats. Amplitude =
            // the largest magnitude_* (or `magnitude`); duration from the
            // list. Bare-float form kept as a fallback.
            float amp = 0.5f, dur = 0.5f;
            bool parsed = false;
            foreach (var tok in stmt.Tokens)
            {
                int lt = tok.IndexOf("s<", StringComparison.OrdinalIgnoreCase);
                if (lt < 0 || !tok.EndsWith(">", StringComparison.Ordinal)) continue;
                var body = tok[(lt + 2)..^1];
                float mag = 0f;
                foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = pair[..eq].Trim();
                    if (!TryParseF(pair[(eq + 1)..].Trim(), out var val)) continue;
                    if (key.StartsWith("magnitude", StringComparison.OrdinalIgnoreCase))
                        mag = MathF.Max(mag, MathF.Abs(val));
                    else if (key.Equals("duration", StringComparison.OrdinalIgnoreCase))
                        dur = val;
                }
                if (mag > 0f) amp = mag;
                parsed = true;
                break;
            }
            if (!parsed)
            {
                if (stmt.Tokens.Count >= 1) TryParseF(stmt.Tokens[0], out amp);
                if (stmt.Tokens.Count >= 2) TryParseF(stmt.Tokens[1], out dur);
            }
            CameraShakeHook?.Invoke(amp, dur);
            return true;
        }
        if (v.Equals("worldmsg", StringComparison.OrdinalIgnoreCase))
        {
            WorldMsgHook?.Invoke(stmt.Tokens.Count > 0 ? stmt.Tokens[0] : "", rs.Ctx);
            ConsumeStackOperand(rs, stmt.Tokens);
            return true;
        }
        if (v.Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            // Phase 23-fold — `exit` terminates the script immediately
            // (mana_chant's early-out branch).
            rs.Ip = rs.Program.Statements.Count;
            return true;
        }
        return false;
    }

    // sfx freeze_targets #PEEK — the #PEEK case must NOT pop, so we only
    // pop when the caller wrote #POP. Centralized so call sites stay tiny.
    static void ConsumeStackOperand(RunningScript rs, IReadOnlyList<string> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            if (string.Equals(tokens[i], "#POP", StringComparison.OrdinalIgnoreCase))
            {
                if (rs.Stack.Count > 0) rs.Stack.Pop();
                return;
            }
        }
    }

    void ExecCreate(RunningScript rs, SfxStatement stmt)
    {
        // tokens[0] = kind (fire / smoke / steam / lightning / explosion / sphere / …)
        // tokens[1] = target macro (#TARGET_KB / #SOURCE_POSITION / etc.)
        if (stmt.Tokens.Count == 0) return;
        var kind = stmt.Tokens[0].ToLowerInvariant();

        // ParamString may carry [N] back-references to caller args and
        // $name back-references to script-level Vars. Substitute both
        // BEFORE keyword extraction so `flamesize([0])`, `scale($scale)`,
        // etc. resolve cleanly. Phase 21-SC-SPELL-VISUAL-H+sphere fold:
        // the $name pass landed alongside slice H so firebomb_base /
        // bombard_base's `set $scale .7; sfx create sphere ...
        // "scale($scale)..."` actually applies the authored radius
        // instead of falling back to Handle.Scale = 0.6.
        var raw = SubstituteCallerArgs(stmt.ParamString, rs.CallerArgs);
        raw     = SubstituteVars(raw, rs.Vars);
        raw     = SubstitutePops(raw, rs);

        // Resolve the target-token (where the effect anchors).
        var anchor = rs.Origin;
        if (stmt.Tokens.Count >= 2)
            anchor = ResolveAnchor(stmt.Tokens[1], rs.Ctx);

        var handle = new Handle
        {
            Kind     = kind,
            Anchor   = anchor,
            OtherEnd = anchor, // overridden by sfx target / attach_point
            Position = anchor, // legacy alias for Anchor (persistent emitters use it)
            Color    = DefaultColor(kind),
            Scale    = 0.6f,
            Rate     = 18f,
            Duration = 0.35f,
            Mode     = MapMode(kind, raw),
            BurstCount = 0,
        };
        ApplyParamString(ref handle, raw);

        // Phase 21-SC-SPELL-VFX-MOTION-HANDLE — for motion kinds, allocate
        // a slot in _motionHandles so child emitters that target this
        // handle can follow its Position frame-by-frame. The Handle.MotionId
        // is the linkage; ExecSfxTarget propagates it to the child's
        // TargetMotionId.
        if (handle.Mode == EmitterMode.MotionOrbiter
            || handle.Mode == EmitterMode.MotionTrackball
            || handle.Mode == EmitterMode.LightSource
            || handle.Mode == EmitterMode.MotionCurve)
        {
            int id = _nextMotionId++;
            handle.MotionId = id;
            // For orbiter, anchor is the orbital center. For trackball,
            // anchor is the start point and the script's #TARGET (Ctx.TargetPos)
            // is where it homes to. Lightsource is static at anchor (until a
            // sfx target rebinds parent). Curve uses anchor as start, target as end.
            // Phase 23d-2a — SU-212-faithful motion init. Orbiter: phi is
            // latitude start (phi(f)), iphi its rate (doc default 2.0);
            // theta longitude start, itheta rate (default 0). Trackball:
            // velocity(f) default 5.0, accel(f) ramp default 2.5,
            // max_velocity default 80, spiral from radius/spin, theta
            // spiral-start "*random" per the doc when unauthored.
            bool isOrbiter   = handle.Mode == EmitterMode.MotionOrbiter;
            bool isTrackball = handle.Mode == EmitterMode.MotionTrackball;
            var motion = new MotionState
            {
                Id           = id,
                Kind         = kind,
                Anchor       = anchor,
                Target       = rs.Ctx.TargetPos,
                Position     = anchor,
                Phi          = handle.HasPhi ? handle.PhiStart : 0f,
                Theta        = handle.HasTheta ? handle.ThetaStart : 0f,
                Radius       = handle.HasRadius ? handle.OrbitRadius
                               : (isOrbiter ? 1.0f : 0f),
                RadiusInc    = handle.OrbitRadiusInc,
                PhiRate      = isOrbiter
                               ? (handle.HasIPhi ? handle.OrbitPhi : 2.0f)
                               : 0f,
                ThetaRate    = isOrbiter ? handle.OrbitTheta : 0f,
                VDisplace    = handle.VDisplace,
                Speed        = handle.HasVel ? handle.Velocity
                               : (isTrackball ? 5.0f : 8.0f),
                AccelRate    = isTrackball
                               ? (handle.HasAccelScalar ? handle.AccelScalar : 2.5f)
                               : 0f,
                MaxSpeed     = handle.HasMaxVelocity ? handle.MaxVelocity : 80f,
                SpiralRadius = isTrackball && handle.HasRadius ? handle.OrbitRadius : 0f,
                SpiralRate   = isTrackball ? handle.SpinRate : 0f,
                SpiralAngle  = handle.HasTheta ? handle.ThetaStart
                               : (float)_rng.NextDouble() * MathF.Tau,
                Homing       = handle.Homing,
                AfterlifeSec = handle.Afterlife,
                Curvature    = handle.HasCurvature ? handle.Curvature : 1f,
                Duration     = handle.Duration > 0.1f ? handle.Duration : 0f,
                Elapsed      = 0f,
                ParentMotionId = 0,
            };
            // Curve draws at velocity(f) — doc default 30 u/s.
            if (handle.Mode == EmitterMode.MotionCurve && !handle.HasVel)
                motion.Speed = 30f;
            // rvel_min/rvel_max — random launch-speed variance (trackball).
            if (isTrackball && (handle.RvelMax > 0f || handle.RvelMin != 0f))
                motion.Speed += handle.RvelMin
                    + (float)_rng.NextDouble() * MathF.Max(0f, handle.RvelMax - handle.RvelMin);
            _motionHandles[id] = motion;
        }
        rs.Stack.Push(handle);
    }

    void ExecStart(RunningScript rs, SfxStatement stmt)
    {
        // sfx start <handle-token> — supports #POP, #PEEK, $name.
        if (stmt.Tokens.Count == 0) return;
        if (!TryResolveHandleOperand(rs, stmt.Tokens[0], pop: true, out var h))
            return;
        string? selfName = stmt.Tokens[0].StartsWith("$") ? stmt.Tokens[0] : null;

        // Phase 23d-2a — offset(x,y,z): positional offset relative to the
        // model (SU 212 global params). Applied in the caster's facing
        // frame (forward = source→target on the XZ plane) which matches
        // the model's orientation mid-cast; x=right, y=up, z=forward.
        if (h.HasOffset)
        {
            var world = CasterFrameOffset(rs.Ctx, h.OffsetVec);
            h.Anchor   += world;
            h.OtherEnd += world;
            h.Position  = h.Anchor;
            if (h.MotionId > 0 && _motionHandles.TryGetValue(h.MotionId, out var mm))
            {
                mm.Anchor   += world;
                mm.Position += world;
                _motionHandles[h.MotionId] = mm;
            }
        }

        // Phase 23d-2a — delay(n): pause before the effect starts. The
        // start is queued and Tick dispatches it when it matures, so
        // layered creates (nova_strike's staggered rings etc.) sequence
        // exactly as authored. Phase 23-fold F6: the handle's motion
        // state must hold too — otherwise a delayed trackball flies (and
        // can arrive, burn afterlife, and be pruned) before its visual
        // ever dispatches.
        if (h.DelaySec > 0.0001f)
        {
            if (h.MotionId > 0 && _motionHandles.TryGetValue(h.MotionId, out var held))
            {
                held.HoldSec = h.DelaySec;
                _motionHandles[h.MotionId] = held;
            }
            _delayed.Add(new DelayedStart { Remaining = h.DelaySec, H = h, SelfName = selfName });
            return;
        }
        DispatchStart(h, selfName);
    }

    struct DelayedStart
    {
        public float   Remaining;
        public Handle  H;
        public string? SelfName;
    }
    readonly List<DelayedStart> _delayed = new();

    /// <summary>Phase 23d-2a — build the caster-frame world offset for
    /// `offset(x,y,z)`: forward = source→target flattened to XZ (a caster
    /// faces its target during a cast), up = world Y, right = their cross.</summary>
    static Vector3 CasterFrameOffset(in SfxContext ctx, Vector3 off)
    {
        var fwd = ctx.TargetPos - ctx.SourcePos;
        fwd.Y = 0f;
        fwd = fwd.LengthSquared() > 0.0001f ? Vector3.Normalize(fwd) : Vector3.UnitZ;
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, fwd));
        return right * off.X + Vector3.UnitY * off.Y + fwd * off.Z;
    }

    void DispatchStart(Handle h, string? selfName)
    {
        switch (h.Mode)
        {
            case EmitterMode.OneShotLightning:
            {
                // Phase 21-SC-SPELL-VFX-2 — render the actual hand-to-target
                // beam. Phase 23d-2a passes the full SU-212 bolt shape:
                // signed [mindisplace, maxdisplace] stray range plus
                // subd/minsubd subdivision density.
                _particles.SpawnLightning(h.OtherEnd, h.Anchor, h.Color, h.Duration,
                    h.HasMinDisplace ? h.MinDisplaceY : -h.Displace,
                    h.Displace,
                    h.HasSubd    ? h.SubdLevel : 0f,
                    h.HasMinSubd ? h.MinSubd   : 0f);
                break;
            }
            case EmitterMode.OneShotLineTracer:
            {
                // Phase 23-fold — SU-212 LineTracer: textureless tracer
                // between the handle's two ends fading at fade_rate.
                var c1t = h.ColorTail.W > 0.001f ? h.ColorTail : h.Color;
                _particles.SpawnLineTracer(h.OtherEnd, h.Anchor, h.Color, c1t,
                    h.HasFadeRate ? h.FadeRate : 0.10f,
                    h.HasTin  ? h.FadeIn  : 0.5f,
                    h.HasTout ? h.FadeOut : 0.5f);
                break;
            }
            case EmitterMode.OneShotExplosion:
            {
                // Phase 23-fold — the `spawn` kind (model-frag bursts:
                // lava/rock beast blasts) approximates as poly shards
                // flying at the authored lvel magnitude until the
                // frag-mesh hook lands (FLAGGED DEVIATION).
                if (string.Equals(h.Kind, "spawn", StringComparison.OrdinalIgnoreCase))
                {
                    float mag = h.HasLVel ? MathF.Max(0.4f, h.LVel.Length() / 4f) : 1.0f;
                    var frag = new PolyExplosionSpec
                    {
                        Anchor    = h.Anchor,
                        Color     = h.HasColor2 ? h.Color2 : new Vector4(0.85f, 0.80f, 0.70f, 1f), // bone-frag tint
                        PolySides = 6,
                        Count     = h.BurstCount > 0 ? h.BurstCount : 16,
                        Radius    = h.HasRadius ? MathF.Max(0.05f, h.OrbitRadius) : 0.5f,
                        Mag       = mag,
                        RotRange  = new Vector3(200f, 200f, 200f),
                        Displace  = Vector3.Zero,
                        FadeStart = 0.6f,
                        FadeEnd   = 1.0f,
                        Duration  = h.Duration > 0.05f ? h.Duration : 1.5f,
                    };
                    _particles.SpawnPolyExplosion(in frag);
                    break;
                }
                // Phase 23d-2e — polygonalexplosion routes through MapMode
                // as OneShotExplosion; give it its documented shard burst.
                if (string.Equals(h.Kind, "polygonalexplosion", StringComparison.OrdinalIgnoreCase))
                {
                    var pxspec = new PolyExplosionSpec
                    {
                        Anchor    = h.Anchor,
                        Color     = h.Color,
                        PolySides = h.HasPolySides ? h.PolySides : 9,
                        Count     = h.BurstCount > 0 ? h.BurstCount : 200,
                        Radius    = h.OrbitRadius > 0f ? h.OrbitRadius : 0.75f,
                        Mag       = h.HasMag ? h.Mag : 1.0f,
                        RotRange  = h.RotRangeVec.LengthSquared() > 0.001f
                                    ? h.RotRangeVec : new Vector3(200f, 200f, 200f),
                        Displace  = h.DisplaceVec,
                        FadeStart = h.HasFadeRange ? h.FadeStart : 0.5f,
                        FadeEnd   = h.HasFadeRange ? h.FadeEnd   : 1.0f,
                        Duration  = h.Duration > 0.05f ? h.Duration : 1.0f,
                    };
                    _particles.SpawnPolyExplosion(in pxspec);
                    break;
                }
                // Phase 23d-2a — authored-parameter explosion per SU 212:
                // spawn radius, velocity model (vmin/vmax scalars + ivel
                // base + rvel random), omni_dir vs directional, fade
                // window, srate spawn spread. Defaults are the documented
                // table defaults.
                var spec = new ExplosionSpec
                {
                    Anchor    = h.Anchor,
                    Color     = h.Color,
                    Radius    = h.HasRadius ? MathF.Max(0.005f, h.OrbitRadius) : 0.5f,
                    Count     = h.BurstCount > 0 ? h.BurstCount : 32,
                    ScaleMin  = h.HasScaleRange ? h.ScaleRangeMin : 0.2f,
                    ScaleMax  = h.HasScaleRange ? h.ScaleRangeMax : 0.7f,
                    VMin      = h.HasVMin ? h.VMin : 3.0f,
                    VMax      = h.HasVMax ? h.VMax : 6.5f,
                    IVel      = h.HasIVel ? h.IVelVec : Vector3.Zero,
                    RVel      = h.HasRVel ? h.RVelVec : new Vector3(0.25f, 0.25f, 0.25f),
                    OmniDir   = h.OmniDir,
                    FadeStart = h.HasFadeRange ? h.FadeStart : 0.5f,
                    FadeEnd   = h.HasFadeRange ? h.FadeEnd   : 1.0f,
                    Duration  = h.Duration > 0.05f ? h.Duration : 1.0f,
                    SpawnOver = h.SpawnOverSec,
                    TexSlot   = TextureNameToSlot(h.TextureName, 2),
                    // Phase 23-fold — per-particle variance color (doc
                    // color1), ground bounce with the authored rebound
                    // elasticity, and splat() stick-where-landed.
                    ColorVar    = h.ColorTail,
                    HasColorVar = h.ColorTail.W > 0.001f,
                    Rebound     = h.HasRebound ? h.Rebound : 0.85f,
                    Bounce      = h.CollideFlag || h.Splat,
                    Splat       = h.Splat,
                    GroundY     = h.Anchor.Y,
                };
                _particles.SpawnExplosion(in spec);
                break;
            }
            case EmitterMode.OneShotSparkles:
            {
                // Phase 23d-2d — charge routes through MapMode as
                // OneShotSparkles; give it its documented inward-coalesce.
                if (string.Equals(h.Kind, "charge", StringComparison.OrdinalIgnoreCase))
                {
                    var chspec = new ChargeSpec
                    {
                        Anchor     = h.Anchor,
                        Color      = h.Color,
                        Radius     = h.HasRadius ? MathF.Max(0.01f, h.OrbitRadius) : 1.0f,
                        Count      = h.BurstCount > 0 ? h.BurstCount : 16,
                        Tout       = h.HasTout ? h.FadeOut : 1.0f,
                        Speed0     = h.HasChargeSpeed ? h.ChargeSpeed : 1.0f,
                        CenterSize = h.CenterSize > 0f ? h.CenterSize : 0.75f,
                        IAlpha     = h.IAlpha > 0f ? h.IAlpha : 4.0f,
                        Duration   = h.Duration > 0.05f ? h.Duration : 1.0f,
                        TexSlot    = TextureNameToSlot(h.TextureName, 2),
                    };
                    _particles.SpawnCharge(in chspec);
                    break;
                }
                // Phase 23d-2d — SU-212 sparkles: static alpha-in/out
                // particles that never leave their spawn point.
                var spkspec = new SparklesSpec
                {
                    Anchor   = h.Anchor,
                    Color    = h.Color,
                    Radius   = h.HasRadius ? MathF.Max(0.01f, h.OrbitRadius) : 1.0f,
                    Count    = h.BurstCount > 0 ? h.BurstCount : 60,
                    PSize    = h.PSize > 0f ? h.PSize : 1.0f,
                    YVel     = h.YVel,
                    Duration = h.Duration > 0.05f ? h.Duration : 1.0f,
                    TexSlot  = TextureNameToSlot(h.TextureName, 2),
                };
                _particles.SpawnSparkles(in spkspec);
                break;
            }
            case EmitterMode.OneShotFlurry:
            {
                // Phase 23d-2d — SPE routes through MapMode as
                // OneShotFlurry; run the exact documented parametric model
                // when its triples were authored.
                if (string.Equals(h.Kind, "spe", StringComparison.OrdinalIgnoreCase) && h.HasSpe)
                {
                    var spespec = new SpeSpec
                    {
                        Anchor   = h.Anchor,
                        Color    = h.Color,
                        Radius   = h.OrbitRadius > 0f ? h.OrbitRadius : 1.0f,
                        Scale    = h.Scale > 0.01f && h.Scale != 0.6f ? h.Scale : 0.12f,
                        Index0   = h.SpeIndex0, Index1 = h.SpeIndex1,
                        Speed0   = h.SpeSpeed0, Speed1 = h.SpeSpeed1,
                        Space0   = h.SpeSpace0, Space1 = h.SpeSpace1,
                        Count    = h.BurstCount > 0 ? h.BurstCount : 64,
                        FadeIn   = h.HasTin  ? h.FadeIn  : 1.0f,
                        FadeOut  = h.HasTout ? h.FadeOut : 1.0f,
                        Duration = h.Duration > 0.05f ? h.Duration : 1.0f,
                        TexSlot  = TextureNameToSlot(h.TextureName, 2),
                    };
                    _particles.SpawnSpe(in spespec);
                    break;
                }
                // Phase 23d-2b — real SU-212 flurry: spherical polar swarm
                // with sinusoidal radial interference, grow envelope and
                // tin/tout alpha shaping. Doc defaults where unauthored.
                var fspec = new FlurrySpec
                {
                    Anchor    = h.Anchor,
                    Color     = h.Color,
                    Radius    = h.HasRadius ? MathF.Max(0.01f, h.OrbitRadius) : 1.0f,
                    Count     = h.BurstCount > 0 ? h.BurstCount : 50,
                    IPhi      = h.HasIPhi   ? h.OrbitPhi   : 1.0f,
                    ITheta    = h.HasITheta ? h.OrbitTheta : 1.0f,
                    IAmp      = h.HasIAmp ? h.IAmp : 1.0f,
                    Amplitude = h.HasAmplitude ? h.Amplitude : 1.0f,
                    GrowStart = h.HasGrow ? h.GrowStart : 1f,
                    GrowMid   = h.GrowMid > 0.05f ? h.GrowMid : 1f,
                    GrowEnd   = h.HasGrow ? h.GrowEnd : 1f,
                    FadeIn    = h.HasTin  ? h.FadeIn  : 1.0f,
                    FadeOut   = h.HasTout ? h.FadeOut : 1.0f,
                    Duration  = h.Duration > 0.05f ? h.Duration : 1.0f,
                    TexSlot   = TextureNameToSlot(h.TextureName, 2),
                };
                _particles.SpawnFlurry(in fspec);
                break;
            }
            case EmitterMode.Fireb:
            {
                // Phase 21-SC-SPELL-VISUAL-C — DS1 fireb = directional fire
                // cone. Per the agent inventory of 5 fireb sites
                // (dragon_fire / flame / inferno / pestilence / pestilence_
                // cloud), particles emit in `velocity(x,y,z)` direction,
                // scattered laterally within a cone defined by lower_r0/r1
                // and upper_r0/r1. Layered flame+inferno scripts stack 4
                // creates with different velocities to build a flamethrower.
                // accel applies (e.g. -10 Z for arc-fall on dragon's
                // lateral cones). max_displace = per-particle turbulence.
                int count = h.BurstCount > 0 ? h.BurstCount : 30;
                float life = h.AlphaFade > 0.10f ? h.AlphaFade : 1.0f;
                Vector3 vel = h.VelocityVec.LengthSquared() > 0.001f
                    ? h.VelocityVec : new Vector3(0f, 0f, 10f);
                _particles.SpawnFireb(
                    anchor:       h.Anchor,
                    color:        h.Color,
                    velocity:     vel,
                    accel:        h.AccelVec,
                    lifetime:     life,
                    maxDisplace:  h.MaxDisplace > 0f ? h.MaxDisplace : 0.5f,
                    lowerRadius:  h.LowerRadius,
                    upperRadius:  h.UpperRadius != 0f ? h.UpperRadius : 1.0f,
                    count:        count,
                    flameSize:    h.FlameSize > 0.05f ? h.FlameSize : 1.0f);
                // Phase 23-fold — light_spawn(): mix lightsources into the
                // breath cone (dragon breath). One glow per light_freq
                // particles at light_radius, seeded along the cone axis.
                if (h.LightSpawn)
                {
                    int glows = Math.Max(1, count / (int)MathF.Max(1f, h.LightFreq > 0f ? h.LightFreq : 50f));
                    var dirN = Vector3.Normalize(vel.LengthSquared() > 0.001f ? vel : Vector3.UnitZ);
                    for (int g = 0; g < glows; g++)
                        _particles.SpawnSphere(
                            h.Anchor + dirN * (0.8f + 0.8f * g),
                            new Vector4(1f, 0.75f, 0.35f, 0.9f),
                            h.LightRadius > 0f ? h.LightRadius : 1f,
                            MathF.Max(0.3f, life * 0.6f), 6);
                }
                break;
            }
            case EmitterMode.OneShotCylinder:
            {
                // Phase 21-SC-SPELL-VISUAL-A — DS1 cylinder is a flat
                // textured impact ring at one anchor, NOT a beam between
                // two points. 19 of 19 shipped cylinder scripts confirm
                // this dominant pattern (per the SC-SPELL-VISUAL-A agent
                // inventory). Outer radius from rp0 mid-value (3-float
                // profile authored by every shipped script); donut
                // thickness via the renderer's default. Spin animates the
                // texture circumferentially.
                // Phase 23d-2b — SU-212 tube between two animated rings.
                // rp0/rp1/hp0/hp1 are (start, end, increment) triples; doc
                // defaults rp 0.5/0.5/0, hp0 2.5/2.5/0, hp1 0/0/0. Scripts
                // that only authored the legacy mid-value keep working via
                // the RpMid fallback on both radius profiles.
                float fallbackR = h.RpMid > 0.05f ? h.RpMid : MathF.Max(0.5f, h.Scale);
                var cspec = new CylinderSpec
                {
                    Anchor   = h.Anchor,
                    Color    = h.Color,
                    Rp0      = h.HasRp0 ? h.Rp0Triple : new Vector3(fallbackR, fallbackR, 0f),
                    Rp1      = h.HasRp1 ? h.Rp1Triple
                               : (h.HasRp0 ? h.Rp0Triple : new Vector3(fallbackR, fallbackR, 0f)),
                    Hp0      = h.HasHp0 ? h.Hp0Triple : new Vector3(2.5f, 2.5f, 0f),
                    Hp1      = h.HasHp1 ? h.Hp1Triple : Vector3.Zero,
                    Alpha    = h.HasAlpha ? h.AlphaStart : 0.5f,
                    Spin     = h.SpinRate,
                    FadeIn   = h.HasTin  ? h.FadeIn  : 0.5f,
                    FadeOut  = h.HasTout ? h.FadeOut : 0.5f,
                    Duration = h.Duration > 0.10f ? h.Duration : 1.0f,
                    Rotate   = h.HasRotate  ? h.RotateVec  : Vector3.Zero,
                    IRotate  = h.HasIRotate ? h.IRotateVec : Vector3.Zero,
                    TexSlot  = TextureNameToSlot(h.TextureName, 11),
                    Segments = (byte)Math.Min(96, h.Segments >= 4 ? h.Segments : 16),
                };
                _particles.SpawnCylinderTube(in cspec);
                break;
            }
            case EmitterMode.OneShotSray:
            {
                // Phase 21-SC-SPELL-VISUAL-B — DS1 sray = a tapered streak
                // emitted radially from anchor. Per the agent inventory of
                // 7 sray sites: untextured, lmin/lmax length range,
                // wsmin/wsmax start width, wemin/wemax end width, count
                // for fan size. Color gradient color0→color1 (typically
                // black→gold). theta/phi always 0 — direction is purely
                // radial-azimuth or straight up for count=1.
                // Phase 23d-2b — SU-212 spinning rays: timed spawn (srate
                // period), per-ray polar spin rates from the theta/phi
                // triples, per-ray alpha fade from the alpha triple.
                float lmin = h.LengthMin > 0.05f ? h.LengthMin
                            : MathF.Max(0.5f, h.Scale);
                float lmax = h.LengthMax > lmin ? h.LengthMax : lmin;
                var c1 = h.ColorTail.W > 0.001f ? h.ColorTail
                        : new Vector4(h.Color.X, h.Color.Y * 0.5f, h.Color.Z * 0.2f, h.Color.W);
                var sspec = new SraySpec
                {
                    Anchor      = h.Anchor,
                    Color0      = h.Color,
                    Color1      = c1,
                    Radius      = h.HasRadius ? MathF.Max(0f, h.OrbitRadius) : 0.0005f,
                    Count       = h.BurstCount > 0 ? h.BurstCount : 16,
                    LMin        = lmin,
                    LMax        = lmax,
                    WsMin       = h.WidthStart > 0.01f ? h.WidthStart : 0.15f,
                    WsMax       = h.WidthStart > 0.01f ? h.WidthStart : 0.15f,
                    WeMin       = h.WidthEnd   > 0.01f ? h.WidthEnd   : 0.15f,
                    WeMax       = h.WidthEnd   > 0.01f ? h.WidthEnd   : 0.15f,
                    Theta       = h.HasSrayTheta ? h.ThetaTriple : new Vector3(0f, 1f, 3f),
                    Phi         = h.HasSrayPhi   ? h.PhiTriple   : new Vector3(0f, 1f, -3f),
                    Alpha       = h.HasSrayAlpha ? h.AlphaTriple : new Vector3(1f, 0.5f, 0.5f),
                    SpawnPeriod = h.SpawnOverSec > 0.001f ? h.SpawnOverSec : 0.015f,
                    Duration    = h.Duration > 0.05f ? h.Duration : 0.40f,
                };
                _particles.SpawnSrayTimed(in sspec);
                break;
            }
            case EmitterMode.OneShotSphere:
            {
                // Phase 21-SC-SPELL-VISUAL-H + sphere fold — DS1 sphere
                // is an omni-directional expanding shell. Routed to the
                // dedicated SpawnSphere primitive (uniform unit-vector
                // direction + color-preserving fade) instead of the
                // pre-fold SpawnSpark misuse, which was Y-biased and
                // warm-faded.
                //
                // grow_params(start, mid, end) authors a size envelope
                // over the shell's lifetime; we honor the MID value
                // as the peak radius scaler (start and end act as the
                // birth/death taper, which the renderer's own Scale0/
                // Scale1 fall-off already approximates). When grow is
                // absent, h.Scale alone defines the radius.
                // Phase 23d-2e — DS1's sphere is a textureless TESSELLATED
                // translucent mesh, not a particle shell. grow_params
                // supplies the birth→peak→death radius envelope;
                // subd/sides set tessellation; rotate/irotate orient it.
                var smspec = new SphereMeshSpec
                {
                    Anchor    = h.Anchor,
                    Color     = h.Color,
                    Radius    = h.Scale > 0.05f ? h.Scale : 1.0f,
                    Sides     = h.SphereSides > 3 ? h.SphereSides : 20,
                    Subd      = h.SphereSubd > 0 ? h.SphereSubd : 1,
                    GrowStart = h.HasGrow ? h.GrowStart : 1f,
                    GrowMid   = h.GrowMid > 0.05f ? h.GrowMid : 1f,
                    GrowEnd   = h.HasGrow ? h.GrowEnd : 1f,
                    Rotate    = h.HasRotate  ? h.RotateVec  : Vector3.Zero,
                    IRotate   = h.HasIRotate ? h.IRotateVec : Vector3.Zero,
                    FadeIn    = h.HasTin  ? h.FadeIn  : 1.0f,
                    FadeOut   = h.HasTout ? h.FadeOut : 1.0f,
                    Duration  = h.Duration > 0.05f ? h.Duration : 0.50f,
                };
                _particles.SpawnSphereMesh(in smspec);
                break;
            }
            case EmitterMode.Unsupported:
                return;
            case EmitterMode.MotionOrbiter:
                // Pure motion handle — no visible emitter of its own,
                // unless the script authored model(<template>): the
                // orbiter then carries a mesh (fireshot's 1-in-10 skull,
                // tremor's rocks). The host renders the mesh via
                // CollectModelOrbits; until that hook lands in RenderHost
                // a compact glow core marks the orbit position so the
                // authored visual is never silently absent (FLAGGED
                // DEVIATION for the DS1 side-by-side pass).
                if (h.ModelName is not null && h.MotionId > 0)
                {
                    _modelOrbits[h.MotionId] = new ModelOrbitSpec
                    {
                        Template = h.ModelName,
                        ScaleIn  = h.ModelSin  > 0f ? h.ModelSin  : 1.0f,
                        ScaleOut = h.ModelSout > 0f ? h.ModelSout : 1.0f,
                        ScaleMax = h.ModelSmax > 0f ? h.ModelSmax : 1.0f,
                        RotInc   = h.RotIncVec,
                        RotBase  = h.HasRotate ? h.RotateVec : Vector3.Zero,
                    };
                    if (!h.Invisible)
                        _emitters.Add(new PersistentEmitter
                        {
                            Mode     = EmitterMode.Glow,
                            Position = h.Anchor,
                            Color    = h.Color,
                            Scale    = MathF.Max(0.25f, h.Scale * 0.5f),
                            Rate     = 50f,
                            TargetMotionId = h.MotionId,
                            SelfMotionId   = h.MotionId,
                            Duration = h.Duration > 0.10f ? h.Duration : 0f,
                            SelfName = selfName,
                        });
                }
                break;
            case EmitterMode.MotionCurve:
            {
                // Phase 23d-2b — the curve itself is VISIBLE in DS1: a
                // trail of particles of size(f) spaced spacing(f) apart,
                // drawn as the head advances (healing_wind's swirl).
                // invisible() keeps it a pure motion handle.
                if (h.Invisible) break;
                float spacing = h.HasSpacing ? MathF.Max(0.2f, h.Spacing) : 3.0f;
                float speed   = h.Velocity > 0f ? h.Velocity : 30f;
                float size    = h.CurveSize > 0.01f ? h.CurveSize : 0.125f;
                _emitters.Add(new PersistentEmitter
                {
                    Mode     = EmitterMode.Glow,
                    Position = h.Anchor,
                    Color    = h.Color,
                    Scale    = size * 2.5f,          // glow halo ≈ particle footprint
                    Rate     = speed / spacing,      // particles per second along the path
                    TargetMotionId = h.MotionId,
                    SelfMotionId   = h.MotionId,
                    Duration = h.Duration > 0.10f ? h.Duration : 0f,
                    SelfName = selfName,
                });
                break;
            }
            case EmitterMode.MotionTrackball:
            case EmitterMode.LightSource:
            {
                // Visible motion handles — render a continuous glow at the
                // motion's live position. SelfMotionId binds the emitter to
                // its own motion; TargetMotionId is the lookup key the Tick
                // pump uses to refresh Position. Trackball uses Fire (warm
                // streak); LightSource uses Glow (additive halo, color-
                // preserving — Phase 21-SC-SPELL-VISUAL-D). Steam was the
                // pre-D placeholder and read as a smoke wisp.
                // Phase 23d-2a — invisible(): the motion handle exists (and
                // children follow it) but draws nothing of its own. DS1's
                // acid_cloud / blaster trackballs author this — the visible
                // body is the child emitters, not a glow core.
                if (h.Invisible) break;
                bool isLight = h.Mode == EmitterMode.LightSource;
                var glowMode = isLight ? EmitterMode.Glow : EmitterMode.Fire;
                _emitters.Add(new PersistentEmitter
                {
                    Mode     = glowMode,
                    Position = h.Anchor,
                    Color    = h.Color,
                    // For Glow, Scale is the halo radius; trackball keeps
                    // its 0.80x particle-scale shrink to read as a streak.
                    Scale    = isLight
                        ? MathF.Max(0.30f, h.Scale)
                        : MathF.Max(0.30f, h.Scale * 0.80f),
                    Rate     = isLight ? 80f : 60f,
                    TargetMotionId = h.MotionId,
                    SelfMotionId   = h.MotionId,
                    Duration = h.Duration > 0.10f ? h.Duration : 0f,
                    SelfName = selfName,
                    Flicker  = isLight && h.HasFlicker ? h.Flicker : 0f,
                });
                break;
            }
            default:
            {
                // Phase 23d-2c — fire/smoke/steam pumps carry the full
                // authored plume spec; instant() pre-fills the volume.
                bool isPlume = h.Mode is EmitterMode.Fire or EmitterMode.Smoke or EmitterMode.Steam;
                var emitter = new PersistentEmitter
                {
                    Mode     = h.Mode,
                    Position = h.Anchor,
                    Color    = h.Color,
                    Scale    = h.Scale,
                    Rate     = h.Rate,
                    TargetMotionId = h.TargetMotionId,
                    Duration = h.Duration > 0.10f ? h.Duration : 0f,
                    SelfName = selfName,
                };
                if (isPlume)
                {
                    emitter.Spec = BuildPlumeSpec(h);
                    emitter.HasSpec = true;
                    if (emitter.Spec.Instant)
                        _particles.BurstPlume(in emitter.Spec, h.Anchor, emitter.Spec.Count);
                }
                _emitters.Add(emitter);
                break;
            }
        }
    }

    /// <summary>Phase 23d-2c — Handle → authored plume spec with SU-212
    /// table defaults. The doc's raw max_radius default (4.0) is NOT
    /// applied when unauthored — spells author their own radius knobs and
    /// the raw default describes bonfire-scale ambient emitters; the
    /// legacy Scale-derived footprint is kept for that case (flagged for
    /// the DS1 side-by-side pass).</summary>
    static PlumeSpec BuildPlumeSpec(in Handle h)
    {
        bool steam = h.Mode == EmitterMode.Steam;
        // Honor an AUTHORED velocity even when it's (0,0,0): fireshot's fire
        // rides the trackball and authors velocity(0,0,0) so the flames stay
        // ON the ball. Treating zero as "unauthored" substituted an upward
        // drift that made the fireball smear upward into a spray. Only truly
        // unauthored plumes get the gentle rise default.
        var vel = h.HasVelocity
            ? h.VelocityVec
            : (steam ? new Vector3(0f, 5.75f, 0f) : new Vector3(0f, 8f, 0f)) * 0.25f;
            // 0.25x: DS1's doc velocities are authored against its raw
            // effect scale; unauthored spell plumes at full 8 u/s read as
            // geysers at our world scale. Authored velocities pass as-is.
        var acc = h.HasAccel ? h.AccelVec : Vector3.Zero;
        return new PlumeSpec
        {
            Kind        = h.Mode == EmitterMode.Fire ? (byte)0 : steam ? (byte)2 : (byte)1,
            Color       = h.Color,
            Velocity    = vel,
            Accel       = acc,
            FlameSize   = h.FlameSize > 0.05f ? h.FlameSize : (steam ? 2.25f : 1.75f) * 0.25f,
            Fctrl       = h.FctrlVec,
            HasFctrl    = h.HasFctrl,
            AlphaFade   = h.AlphaFade > 0.10f ? h.AlphaFade : (steam ? 0.55f : 0.85f),
            Count       = h.BurstCount > 0 ? h.BurstCount
                          : h.MinCount > 0 ? h.MinCount
                          : (steam ? 96 : 30),
            MinRadius   = h.MinRadius,
            MaxRadius   = h.HasMaxRadius ? h.MaxRadius
                          : MathF.Max(0.10f, h.Scale * 0.5f),
            MinDisplace = h.HasMinDisplace ? h.MinDisplaceY : 0f,
            MaxDisplace = h.MaxDisplace > 0f ? h.MaxDisplace : 0f,
            Instant     = h.Instant,
            Line        = h.LineMode,
            LineEnd     = h.OtherEnd,
            TexSlot     = TextureNameToSlot(h.TextureName, h.Mode == EmitterMode.Fire ? (byte)0 : (byte)1),
            // Phase 23-fold — line-position animation (gom_icesnake's fire
            // walks the line at linespeed) and the burn_body sine wobble.
            LinePos     = h.LinePos,
            LineSpeed   = h.LineSpeed,
            HasLineAnim = h.HasLineAnim,
            SinPos      = h.SinPos,
            SinSpeed    = h.SinSpeed,
            RadiusRMax  = h.RadiusRMax,
            HasSinAnim  = h.HasSinAnim,
        };
    }

    void ExecFinish(RunningScript rs, SfxStatement stmt)
    {
        // No live-handle teardown yet — finish/destroy just consumes the
        // operand so subsequent stack ops don't re-resolve to a dead slot.
        if (stmt.Tokens.Count > 0)
            TryResolveHandleOperand(rs, stmt.Tokens[0], pop: true, out _);
    }

    void ExecSfxTarget(RunningScript rs, SfxStatement stmt)
    {
        // `sfx target <handle-tok> <where>` — set the bolt's far end.
        // <where> is `source` / `target` / `#SOURCE` / `#TARGET` / `$name`.
        if (stmt.Tokens.Count < 2) return;
        if (!TryResolveHandleOperand(rs, stmt.Tokens[0], pop: false, out var h))
            return;

        // Phase 21-SC-SPELL-VFX-MOTION-HANDLE — when the second arg is a
        // $name handle that has a MotionId, we don't just snapshot the
        // current position into OtherEnd; we wire the source emitter to
        // FOLLOW that motion handle's live Position each tick. This is
        // what makes `sfx target $fire $trackball` track the moving ball
        // instead of stranding $fire at the trackball's spawn point.
        var targetTok = stmt.Tokens[1];
        if (targetTok.StartsWith("$") &&
            rs.NamedHandles.TryGetValue(targetTok, out var named) &&
            named.MotionId > 0)
        {
            h.TargetMotionId = named.MotionId;
            // Use the motion's CURRENT Position (not the spawn-time Anchor)
            // for the initial OtherEnd snapshot — nested orbitals can have
            // already advanced the parent before sfx target runs, and a
            // stale Anchor reads as a frame-zero ghost on one-shot two-
            // ended primitives (cylinder/sray) that read OtherEnd at start.
            // Falls back to Anchor if the motion entry is somehow gone.
            h.OtherEnd = _motionHandles.TryGetValue(named.MotionId, out var liveMotion)
                ? liveMotion.Position
                : named.Anchor;
            // If THIS handle is also a motion (orbiter targeting another
            // motion = nested orbital), record the parent linkage so its
            // own motion advance reads the parent's per-tick Position.
            if (h.MotionId > 0 && _motionHandles.TryGetValue(h.MotionId, out var selfMotion))
            {
                selfMotion.ParentMotionId = named.MotionId;
                _motionHandles[h.MotionId] = selfMotion;
            }
        }
        else
        {
            h.OtherEnd = ResolveAnchor(targetTok, rs.Ctx);
        }
        StoreMutatedHandle(rs, stmt.Tokens[0], h);
    }

    void ExecAttachPoint(RunningScript rs, SfxStatement stmt)
    {
        // `sfx attach_point <handle-tok> <@bone> <source|target>`
        // `sfx position_at <handle-tok> <@bone> <source|target>`
        // We don't have skeletal bone resolution yet; treat the trailing
        // source/target word as an override of the bolt's far-end. For DS1's
        // shipped zap that's `... @weapon_bone source`, which lands the bolt
        // at the caster's hand-area (we pass WeaponBonePos in SfxContext for
        // exactly this case). Phase 21+ will replace the static position with
        // a live skeletal lookup.
        if (stmt.Tokens.Count < 3) return;
        if (!TryResolveHandleOperand(rs, stmt.Tokens[0], pop: false, out var h))
            return;

        // Trailing token: source / target. The middle token is the @bone
        // name DS1 ships (Phase 21-SC-SPELL-VISUAL-F): for `source` we
        // resolve THAT bone on the caster's skeleton via SfxContext.
        // ResolveBone (falls back to WeaponBonePos if the resolver doesn't
        // know the bone). For `target` we land on the target position;
        // bone-on-target lookup isn't a thing in any shipped script.
        var boneTok  = stmt.Tokens[1];
        var trailing = stmt.Tokens[stmt.Tokens.Count - 1];
        Vector3 resolved;
        if (trailing.StartsWith("source", StringComparison.OrdinalIgnoreCase))
            resolved = rs.Ctx.ResolveBone(boneTok);
        else if (trailing.StartsWith("target", StringComparison.OrdinalIgnoreCase))
            resolved = rs.Ctx.TargetPos;
        else
            resolved = h.OtherEnd;
        h.OtherEnd = resolved;

        // Phase 21-SC-SPELL-VFX-MOTION-HANDLE — when this handle backs a
        // live motion (trackball / orbiter / lightsource / curve), the
        // sfx_script's `position_at $X @bone source` is meant to RESET the
        // motion's start point. fireball_base ships
        //     sfx create trackball #TARGET_KB "..."
        //     sfx position_at $trackball @weapon_bone source;
        // — without this update, the trackball's MotionState.Position
        // stays at the create-time anchor (#TARGET_KB → TargetPos), which
        // equals MotionState.Target, so the trackball arrives instantly
        // (distSq=0), Done flips true, gets pruned, and every fire emitter
        // following it gets pruned too. Net: fireball renders nothing on
        // cast. This bug shipped in 0b9bfc3 and was caught when SC-SCROLL
        // testing surfaced "fireball no longer shoots a projectile."
        if (h.MotionId > 0 && _motionHandles.TryGetValue(h.MotionId, out var motion))
        {
            motion.Position = resolved;
            // For orbiter the source token sets the ORBITAL CENTER (anchor),
            // not the orbiter's instantaneous position — the orbital math
            // still computes Position from Anchor + circle each tick.
            if (motion.Kind.Equals("orbiter", StringComparison.OrdinalIgnoreCase))
                motion.Anchor = resolved;
            _motionHandles[h.MotionId] = motion;
        }

        StoreMutatedHandle(rs, stmt.Tokens[0], h);
    }

    // Phase 21-SC-SPELL-VISUAL-E — additional sfx_script verbs that previously
    // logged as `unhandled verb`. Implementing them turns ~30 PARTIAL spells
    // from "fires the right primitives at the wrong location" into "fires at
    // the location DS1 authored." The underlying infrastructure (motion
    // handles, named handle dict, persistent emitters with TargetMotionId)
    // is already in place; these methods just route the verb's mutation onto
    // it.

    void ExecAttach(RunningScript rs, SfxStatement stmt)
    {
        // `sfx attach <parent> <child>` — pin <child>'s emitter so its live
        // Position is driven by <parent>'s motion handle each tick. Different
        // from `sfx target` (which mutates a handle's far-end) by argument
        // order: target's <handle> is the source/owner; attach's <child> is
        // the dependent. fireball_base ships the redundant pair
        //     sfx target $fire $trackball
        //     sfx attach $trackball $fire
        // so failing the verb leaves a duplicate-but-harmless wire-up. Other
        // scripts use attach as the SOLE wire (un_summon's `sfx attach
        // $master #POP` × 3 groups three cylinders under one parent) so
        // dropping it strands children at #SOURCE.
        if (stmt.Tokens.Count < 2) return;
        if (!TryResolveHandleOperand(rs, stmt.Tokens[0], pop: false, out var parent)) return;

        var childTok = stmt.Tokens[1];

        // For #POP/#PEEK children we resolve through the stack; otherwise
        // we need a $name to key NamedHandles + emitter SelfName by. The
        // stack-handle path matches un_summon's grouping pattern; we still
        // can't re-target an anonymous emitter (#POP without a prior `set
        // $name`) so the named lookup below is short-circuited there.
        bool popped = false;
        if (string.Equals(childTok, "#POP", StringComparison.OrdinalIgnoreCase)
         || string.Equals(childTok, "#PEEK", StringComparison.OrdinalIgnoreCase))
        {
            popped = string.Equals(childTok, "#POP", StringComparison.OrdinalIgnoreCase);
            if (rs.Stack.Count == 0) return;
            // Consume so the stack stays balanced. We don't have a back-
            // reference to the named alias of an anonymous stack handle,
            // so the rest of the function is a no-op for #POP/#PEEK
            // children — the practical effect is "verb consumed, stack
            // discipline preserved." For un_summon's cylinders this is
            // correct: a flat ring at #TARGET reads identically with or
            // without the parent grouping.
            if (popped) rs.Stack.Pop(); else rs.Stack.Peek();
            return;
        }

        if (parent.MotionId <= 0) return; // attach to non-motion is a no-op

        // Mutate the named child handle so any not-yet-started emitter picks
        // up the new TargetMotionId on its eventual `sfx start`.
        if (childTok.StartsWith("$") &&
            rs.NamedHandles.TryGetValue(childTok, out var child))
        {
            child.TargetMotionId = parent.MotionId;
            rs.NamedHandles[childTok] = child;
        }

        // Re-target any already-started persistent emitter whose SelfName
        // matches the child token. Covers the fireball_base ordering where
        // `sfx start $fire` precedes `sfx attach $trackball $fire`.
        for (int i = 0; i < _emitters.Count; i++)
        {
            if (_emitters[i].SelfName == childTok)
            {
                var e = _emitters[i];
                e.TargetMotionId = parent.MotionId;
                _emitters[i] = e;
            }
        }
        // Phase 23-fold F6 — delay(n)-queued starts are struct copies in
        // _delayed; an attach issued after `sfx start` must reach them too
        // or the child strands at its spawn anchor when it matures.
        for (int i = 0; i < _delayed.Count; i++)
        {
            if (_delayed[i].SelfName == childTok)
            {
                var d = _delayed[i];
                d.H.TargetMotionId = parent.MotionId;
                _delayed[i] = d;
            }
        }
    }

    void ExecRat(RunningScript rs, SfxStatement stmt)
    {
        // `sfx rat <handle>` — random angle theta. DS1 uses this as a per-
        // emitter rotation jitter so stacked emitters at the same anchor
        // (fireball's three layered fire creates) don't render as a single
        // axis-aligned plume. Concrete effect: roll a uniform Y-axis-only
        // rotation and apply to the handle's OtherEnd around Anchor (a no-
        // op when OtherEnd == Anchor, the typical pre-target state), plus
        // stash it on OrbitPhi so orbiter handles get a randomized starting
        // azimuth instead of always firing east. The "this adds some
        // variance" comment in fireball_base annotates the NEXT line
        // (`sfx offset_bone`), not this verb — `sfx rat` is purely an
        // orientation kick.
        if (stmt.Tokens.Count == 0) return;
        if (!TryResolveHandleOperand(rs, stmt.Tokens[0], pop: false, out var h)) return;

        float angle = (float)_rng.NextDouble() * MathF.Tau;
        var rel = h.OtherEnd - h.Anchor;
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        h.OtherEnd = h.Anchor + new Vector3(rel.X * c - rel.Z * s, rel.Y, rel.X * s + rel.Z * c);
        if (h.MotionId > 0 && _motionHandles.TryGetValue(h.MotionId, out var motion))
        {
            // Phase 23d-2a — under the spherical-polar orbit model the
            // azimuth is Theta (longitude), so the orientation kick
            // rotates that; the spiral angle gets the same kick so
            // stacked trackballs decorrelate.
            motion.Theta = (motion.Theta + angle) % MathF.Tau;
            motion.SpiralAngle = (motion.SpiralAngle + angle) % MathF.Tau;
            _motionHandles[h.MotionId] = motion;
        }
        StoreMutatedHandle(rs, stmt.Tokens[0], h);
    }

    void ExecOffset(RunningScript rs, SfxStatement stmt)
    {
        // `sfx offset_bone <handle> <offset> <source|target>` — re-anchor a
        // handle to a point near the resolved bone position with a per-axis
        // offset. DS1 ships TWO literal forms: angle-bracketed `v<x y z>`
        // (space-separated, the dominant shape — arrow_tracer / bolt_tracer
        // / wraith_tracer all author `v<0 .05 0>` etc.) and square-bracketed
        // `[0]` zero-pad (fireball_base authors that as a no-op marker
        // paired with a prior `sfx rat`). Trailing `source` resolves to
        // WeaponBonePos, not feet — matches ExecAttachPoint's resolution
        // for `... @bone source` so the same author convention reads the
        // same coordinates here.
        if (stmt.Tokens.Count < 3) return;
        if (!TryResolveHandleOperand(rs, stmt.Tokens[0], pop: false, out var h)) return;

        var offsetTok = stmt.Tokens[1];
        var trailing  = stmt.Tokens[stmt.Tokens.Count - 1];

        // Phase 21-SC-SPELL-VISUAL-F — `source` resolves to the caster's
        // weapon_bone via the live skeletal resolver when one is wired
        // (drops back to the static WeaponBonePos field on a resolver-
        // less SfxContext). DS1 author convention: bare `source` in
        // offset_bone means "the caster's default attach bone," which
        // shipped templates (heroes.gas's bone_translator) map to
        // weapon_grip. `target` lands on the live target position
        // unchanged.
        Vector3 baseAnchor = trailing.StartsWith("source", StringComparison.OrdinalIgnoreCase)
            ? rs.Ctx.ResolveBone("weapon_bone")
            : rs.Ctx.TargetPos;
        Vector3 offset = ParseOffsetLiteral(offsetTok);
        var resolved = baseAnchor + offset;

        h.OtherEnd = resolved;
        // For motion-backed handles (trackball aim-point) we'd normally
        // update MotionState.Target. SKIP when offset is zero and the
        // base resolves to motion.Position — the `[0]` zero-pad form is
        // a re-anchor marker, NOT a destination change, and writing
        // motion.Target = motion.Position collapses distSq to 0, flips
        // Done true on the next tick, and prunes every emitter following
        // the trackball. Same hazard fixed for sfx position_at in
        // 0b9bfc3; this is the offset_bone twin.
        if (h.MotionId > 0 && _motionHandles.TryGetValue(h.MotionId, out var motion))
        {
            bool wouldCollapse = offset.LengthSquared() < 1e-6f
                              && Vector3.DistanceSquared(motion.Position, resolved) < 1e-4f;
            if (!wouldCollapse)
            {
                motion.Target = resolved;
                _motionHandles[h.MotionId] = motion;
            }
        }
        StoreMutatedHandle(rs, stmt.Tokens[0], h);
    }

    void ExecDirection(RunningScript rs, SfxStatement stmt)
    {
        // `sfx direction <handle> <where>` — set the handle's aim vector. For
        // primitives that read a directional field (fireb's velocity cone,
        // sray's emit ray) this turns "default forward" into "toward the
        // resolved position." When the renderer ignores VelocityVec (Fire/
        // Smoke), the field is harmlessly stashed.
        if (stmt.Tokens.Count < 2) return;
        if (!TryResolveHandleOperand(rs, stmt.Tokens[0], pop: false, out var h)) return;

        Vector3 to;
        var whereTok = stmt.Tokens[1];
        if (whereTok.StartsWith("$") && rs.NamedHandles.TryGetValue(whereTok, out var named))
            to = named.Anchor;
        else if (string.Equals(whereTok, "#POP", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(whereTok, "#PEEK", StringComparison.OrdinalIgnoreCase))
        {
            bool pop = string.Equals(whereTok, "#POP", StringComparison.OrdinalIgnoreCase);
            if (rs.Stack.Count == 0) return;
            var stk = pop ? rs.Stack.Pop() : rs.Stack.Peek();
            to = stk.Anchor;
        }
        else
            to = ResolveAnchor(whereTok, rs.Ctx);

        var dir = to - h.Anchor;
        float lenSq = dir.LengthSquared();
        if (lenSq > 1e-6f)
        {
            dir /= MathF.Sqrt(lenSq);
            // Preserve magnitude when caller already authored a velocity
            // (fireb scripts ship velocity(0,0,N) and want the direction
            // override to keep the speed); otherwise default to 10 u/s
            // matching SpawnFireb's fallback.
            float mag = h.VelocityVec.Length();
            if (mag < 0.01f) mag = 10.0f;
            h.VelocityVec = dir * mag;
        }
        StoreMutatedHandle(rs, stmt.Tokens[0], h);
    }

    static Vector3 ParseOffsetLiteral(string tok)
    {
        // Two literal forms ship in DS1:
        //   `v<x y z>`  — angle-bracket, space-separated. Dominant shape
        //                 (arrow_tracer, bolt_tracer, wraith_tracer,
        //                 wraith_hands_base, braak_iceblast_base, …).
        //                 The tokenizer collapses the whole thing into one
        //                 token at SfxScriptCompiler.Tokenize() lines
        //                 287-296.
        //   `[0]` / `[a,b,c]` — square-bracket, comma-separated. Zero-pad
        //                 marker (fireball_base ships `[0]` as a no-op
        //                 next to `sfx rat`); 3-component form would be
        //                 a real offset but no shipped script ships it.
        // Single-element brackets (`[N]`) read as zero offset matching DS1
        // author intent; 2-component readings extend the missing Z axis
        // to 0 instead of silently dropping the whole literal.
        if (tok.Length < 3) return Vector3.Zero;
        if (tok[0] == 'v' && tok[1] == '<' && tok[tok.Length - 1] == '>')
        {
            var inner = tok.Substring(2, tok.Length - 3);
            var parts = inner.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
                return new Vector3(ParseF(parts[0]), ParseF(parts[1]), ParseF(parts[2]));
            if (parts.Length == 2)
                return new Vector3(ParseF(parts[0]), ParseF(parts[1]), 0f);
            if (parts.Length == 1)
                return new Vector3(ParseF(parts[0]), 0f, 0f);
            return Vector3.Zero;
        }
        if (tok[0] == '[' && tok[tok.Length - 1] == ']')
        {
            var inner = tok.Substring(1, tok.Length - 2);
            var parts = inner.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return Vector3.Zero;          // `[0]` zero-pad marker
            if (parts.Length >= 3)
                return new Vector3(ParseF(parts[0]), ParseF(parts[1]), ParseF(parts[2]));
            if (parts.Length == 2)
                return new Vector3(ParseF(parts[0]), ParseF(parts[1]), 0f);
        }
        return Vector3.Zero;
    }

    static float ParseF(string s)
        => float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : 0f;

    void ExecSet(RunningScript rs, SfxStatement stmt)
    {
        // Two shapes ship in DS1 sfx scripts:
        //   set $bolt #POP;       (most common — name a stack handle)
        //   set $name = expr;     (rare — used by emitter prelude)
        if (stmt.Tokens.Count == 0) return;
        var name = stmt.Tokens[0];
        if (!name.StartsWith("$")) return;

        // Shape A: `set $name #POP|#PEEK`
        if (stmt.Tokens.Count >= 2 && stmt.Tokens[1].StartsWith("#"))
        {
            if (TryResolveHandleOperand(rs, stmt.Tokens[1], pop: true, out var h))
            {
                rs.NamedHandles[name] = h;
                // Phase 21-SC-SPELL-VISUAL-G — bridge tagged handles
                // (collision_type, etc.) into the string Vars dict so the
                // if-condition evaluator can compare $collision_type against
                // #OBJECT_COLLISION / #TERRAIN_COLLISION / #NO_COLLISION
                // without a separate handle-tag lookup table.
                if (!string.IsNullOrEmpty(h.Kind))
                    rs.Vars[name] = h.Kind;
            }
            return;
        }
        // Shape B: `set $name = expr` (verbatim string capture; the VM
        // doesn't yet evaluate expressions but stores them so future
        // diagnostics can replay the script).
        if (stmt.Tokens.Count >= 3 && stmt.Tokens[1] == "=")
        {
            rs.Vars[name] = stmt.Tokens[2];
            return;
        }
        // Shape C: `set $name LITERAL` — bare literal, no `=`. fireball_
        // base / fireshot_base / firebomb_base author this for scalar
        // tweaks like `set $scale .7; set $detail 20` that later
        // interpolate into param strings as `scale($scale)`. Capture the
        // literal so future param-string $name interpolation (slice H or
        // its own splinter) can read it; today the value sits unused
        // until the substitution pass lands, but stack-balance and Vars
        // bookkeeping are correct now.
        if (stmt.Tokens.Count >= 2 && !stmt.Tokens[1].StartsWith("$"))
            rs.Vars[name] = stmt.Tokens[1];
    }

    // Phase 21-SC-SPELL-VISUAL-G — coroutine gate. Returns true when the
    // script should yield (waitfor still pending); false when the verb
    // doesn't suspend (unrecognized form, missing handle, zero timeout)
    // and execution should continue to the next statement.
    bool ExecWaitfor(RunningScript rs, SfxStatement stmt)
    {
        // `waitfor collision <handle> <timeout>` — common shape (16 spells
        // gate impact bursts on it). `waitfor sig <name> <timeout>` —
        // signal wait used by line tracers; we don't have a signal bus
        // yet so it timeout-resolves immediately.
        if (stmt.Tokens.Count < 2) return false;
        var verb = stmt.Tokens[0];
        if (string.Equals(verb, "collision", StringComparison.OrdinalIgnoreCase) && stmt.Tokens.Count >= 3)
        {
            if (!TryResolveHandleOperand(rs, stmt.Tokens[1], pop: false, out var h)) return false;
            if (h.MotionId <= 0) return false; // can't wait on a non-motion handle
            float timeout = ResolveTimeout(stmt.Tokens[2]);
            // If the motion is already Done at the moment we hit waitfor
            // (rare, e.g. instantaneous-arrival trackball), resume in
            // place — push collision_type and let the next statement run.
            if (_motionHandles.TryGetValue(h.MotionId, out var motion) && motion.Done)
            {
                rs.Stack.Push(new Handle { Kind = "object_collision", Anchor = motion.Position, OtherEnd = motion.Position });
                return false;
            }
            rs.WaitMotionId = h.MotionId;
            rs.WaitTimeout  = timeout;
            return true; // yield
        }
        // `waitfor sig <name> <timeout>` — no signal bus yet. Push
        // no_collision so any following condition reads as false and the
        // script unwinds to a no-op rather than hanging forever.
        if (string.Equals(verb, "sig", StringComparison.OrdinalIgnoreCase))
        {
            rs.Stack.Push(new Handle { Kind = "no_collision", Anchor = rs.Ctx.TargetPos, OtherEnd = rs.Ctx.TargetPos });
            return false;
        }
        // `waitfor script <handle> <timeout>` — wrapper scripts (fireball,
        // fireshot, firestorm, braak_iceblast wrappers) push a subscript
        // handle from `call <name>` and waitfor on its completion before
        // firing `worldmsg WE_SPELL_SYNC_END`. Subscript-completion
        // tracking is its own splinter — for now we POP the operand to
        // keep the stack balanced and resolve immediately. Visual side
        // is unaffected (the subscript already ran inside ExecCall);
        // gameplay-sync timing slips earlier than DS1's design, but no
        // shipped logic depends on that ordering today.
        if (string.Equals(verb, "script", StringComparison.OrdinalIgnoreCase))
        {
            if (stmt.Tokens.Count >= 2)
                ConsumeStackOperand(rs, new[] { stmt.Tokens[1] });
            return false;
        }
        return false;
    }

    static float ResolveTimeout(string tok)
    {
        // DS1 ships `#DEFAULT_TIMEOUT` as the canonical timeout macro — no
        // shipped script names a different number. Treat it as 30s; any
        // numeric value parses through the normal float reader.
        if (string.Equals(tok, "#DEFAULT_TIMEOUT", StringComparison.OrdinalIgnoreCase))
            return 30f;
        return ParseF(tok);
    }

    void ExecGet(RunningScript rs, SfxStatement stmt)
    {
        // Verb shapes shipped in DS1:
        //   get target_position <handle> [source|target]   tokens[1] = handle
        //   get collision point <handle> [source|target]   tokens[2] = handle
        //   get collision direction <handle>               tokens[2] = handle
        // The handle index DEPENDS on the sub-verb: "target_position" puts
        // the handle at tokens[1], whereas "collision" reserves tokens[1]
        // for "point" / "direction" and the handle moves to tokens[2].
        // Resolving tokens[1] unconditionally up-front (the pre-fold bug)
        // returned false on the literal "point" / "direction" string and
        // silently dropped the entire collision dispatch, so every
        // `get collision point $h; sfx create explosion #POP` chain
        // popped a stale stack handle and anchored the explosion at the
        // wrong world position.
        if (stmt.Tokens.Count < 2) return;
        var sub = stmt.Tokens[0].ToLowerInvariant();

        Vector3 result;
        if (sub == "target_position")
        {
            if (!TryResolveHandleOperand(rs, stmt.Tokens[1], pop: false, out var h)) return;
            // Trailing source/target qualifier picks which endpoint of
            // the motion handle to read; default to target so callers that
            // omit it still land on the impact point.
            string? trailing = stmt.Tokens.Count >= 3 ? stmt.Tokens[2] : null;
            if (trailing is not null && trailing.StartsWith("source", StringComparison.OrdinalIgnoreCase))
                result = h.Anchor;
            else if (h.MotionId > 0 && _motionHandles.TryGetValue(h.MotionId, out var motion))
                result = motion.Target;
            else
                result = h.OtherEnd;
        }
        else if (sub == "collision")
        {
            if (stmt.Tokens.Count < 3) return;
            if (!TryResolveHandleOperand(rs, stmt.Tokens[2], pop: false, out var h)) return;
            var what = stmt.Tokens[1].ToLowerInvariant();
            if (what == "direction")
            {
                // Unit vector from motion start to motion end. fireball_base
                // pipes this into `sfx direction $explosion #POP` so the
                // explosion blows in the trackball's flight direction.
                var src = (h.MotionId > 0 && _motionHandles.TryGetValue(h.MotionId, out var motion))
                    ? motion.Anchor : h.Anchor;
                var dst = (h.MotionId > 0 && _motionHandles.TryGetValue(h.MotionId, out motion))
                    ? motion.Target : h.OtherEnd;
                var dir = dst - src;
                float len = dir.Length();
                result = len > 1e-4f ? dir / len : new Vector3(0f, 0f, 1f);
            }
            else if (what == "target")
            {
                // `get collision target $h target` — fireshot_base and
                // fireball_explosive_trap author this. Push the motion's
                // authored target position so a follow-up `sfx create
                // explosion #POP` anchors at the intended landing spot.
                // Pre-fold this fell into the else and pushed
                // motion.Position, leaking a stale handle on the stack.
                result = (h.MotionId > 0 && _motionHandles.TryGetValue(h.MotionId, out var motion))
                    ? motion.Target : h.OtherEnd;
            }
            else
            {
                // Default to "point" — the live impact world position
                // from the motion's current Position; falls back to the
                // handle's OtherEnd when the motion entry has been
                // pruned (Done-and-cleaned-up).
                result = (h.MotionId > 0 && _motionHandles.TryGetValue(h.MotionId, out var motion))
                    ? motion.Position : h.OtherEnd;
            }
        }
        else
        {
            return; // unrecognized get-form; quiet no-op so future shapes don't crash
        }
        rs.Stack.Push(new Handle { Anchor = result, OtherEnd = result });
    }

    // Phase 21-SC-SPELL-VISUAL-G — conditional evaluator. Recognizes the
    // `$name == #MACRO` / `$name != #MACRO` shape that gates impact
    // bursts on collision_type, plus `||` and `&&` between such clauses.
    // When the expression is anything else, returns true so the body
    // executes (preserves the pre-G "always run both branches"
    // pragmatism for unmodeled syntax).
    bool EvalCondition(RunningScript rs, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0) return true;
        // Strip surrounding parens; keep going while we see balanced wrap.
        int lo = 0, hi = tokens.Count - 1;
        while (lo < hi && tokens[lo] == "(" && tokens[hi] == ")")
        {
            // Verify the outer parens match — otherwise we'd strip
            // unbalanced wrappers like `(a) || (b)` into `a) || (b`.
            int depth = 0; bool outer = true;
            for (int j = lo; j <= hi; j++)
            {
                if (tokens[j] == "(") depth++;
                else if (tokens[j] == ")") depth--;
                if (depth == 0 && j < hi) { outer = false; break; }
            }
            if (!outer) break;
            lo++; hi--;
        }
        if (lo > hi) return true;

        // Split on top-level `||` and `&&` operators. Walk left to right
        // tracking paren depth; at depth 0 we know an operator splits the
        // expression.
        var parts = new List<(int start, int end, string? op)>();
        int partStart = lo;
        int parenDepth = 0;
        for (int j = lo; j <= hi; j++)
        {
            if (tokens[j] == "(") parenDepth++;
            else if (tokens[j] == ")") parenDepth--;
            if (parenDepth == 0 && (tokens[j] == "||" || tokens[j] == "&&"))
            {
                parts.Add((partStart, j - 1, tokens[j]));
                partStart = j + 1;
            }
        }
        parts.Add((partStart, hi, null));

        if (parts.Count == 1)
            return EvalLeafCondition(rs, tokens, parts[0].start, parts[0].end);

        // Evaluate left-to-right; honor each part's PRECEDING op. Left-
        // fold gives `A || B && C` = `(A || B) && C` rather than the
        // standard `A || (B && C)`; that's wrong for unparenthesized
        // mixed-operator expressions but DS1 only ships explicitly
        // parenthesized forms (`(A) || (B) || (C)` etc.) so the wrong
        // precedence doesn't fire on any shipped script. Real precedence
        // climbing would be its own splinter — flagged in the audit
        // fold, deferred until a script trips it.
        bool acc = EvalLeafCondition(rs, tokens, parts[0].start, parts[0].end);
        for (int p = 1; p < parts.Count; p++)
        {
            bool next = EvalLeafCondition(rs, tokens, parts[p].start, parts[p].end);
            if (parts[p - 1].op == "||") acc = acc || next;
            else                          acc = acc && next;
        }
        return acc;
    }

    bool EvalLeafCondition(RunningScript rs, IReadOnlyList<string> tokens, int lo, int hi)
    {
        // Trim outer parens at the leaf level too — e.g. `(($x == #MACRO))`.
        while (lo < hi && tokens[lo] == "(" && tokens[hi] == ")")
        {
            int depth = 0; bool outer = true;
            for (int j = lo; j <= hi; j++)
            {
                if (tokens[j] == "(") depth++;
                else if (tokens[j] == ")") depth--;
                if (depth == 0 && j < hi) { outer = false; break; }
            }
            if (!outer) break;
            lo++; hi--;
        }
        // Recognized leaf shape: <var> <op> <macro> with op in {==, !=}.
        // Anything else evaluates to true so the body runs (preserves
        // pre-G "always run both branches" pragmatism for unmodeled
        // condition shapes).
        if (hi - lo + 1 != 3) return true;
        var lhs = tokens[lo];
        var op  = tokens[lo + 1];
        var rhs = tokens[lo + 2];
        if (op != "==" && op != "!=") return true;
        var lhsVal = ResolveCondOperand(rs, lhs);
        var rhsVal = ResolveCondOperand(rs, rhs);
        bool eq = string.Equals(lhsVal, rhsVal, StringComparison.OrdinalIgnoreCase);
        return op == "==" ? eq : !eq;
    }

    static string ResolveCondOperand(RunningScript rs, string token)
    {
        // $name → string Vars lookup (collision_type tag)
        if (token.StartsWith("$"))
            return rs.Vars.TryGetValue(token, out var v) ? v : "";
        // Phase 23-fold F1 — `#POP`/`#PEEK` as a condition operand consume
        // the rolled value (gib_blood's `if (#POP > #LODFI)`); leaving it
        // stacked desynchronized every later `sfx start #POP`.
        if (token.Equals("#POP", StringComparison.OrdinalIgnoreCase))
            return rs.Stack.Count > 0 ? rs.Stack.Pop().Kind.ToLowerInvariant() : "";
        if (token.Equals("#PEEK", StringComparison.OrdinalIgnoreCase))
            return rs.Stack.Count > 0 ? rs.Stack.Peek().Kind.ToLowerInvariant() : "";
        // #MACRO → strip leading # and lowercase, matching the tags
        // pushed by waitfor and stored by ExecSet.
        if (token.StartsWith("#"))
            return token.Substring(1).ToLowerInvariant();
        // [N] → caller-arg index lookup. fireball_base ships
        // `if ( [1] == 3 )` to gate the firestorm-only branch and
        // `if ( [1] == 1 )` for the plain-fireball-only sound + scale.
        // Pre-fold both tested false because [N] was treated as a
        // string literal, so plain fireball shipped with rain-hit
        // sound + scale=0.4 instead of normal-hit + scale=0.7.
        if (token.Length >= 3 && token[0] == '[' && token[token.Length - 1] == ']'
            && rs.CallerArgs is not null)
        {
            var inner = token.Substring(1, token.Length - 2);
            if (int.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)
                && idx >= 1 && idx <= rs.CallerArgs.Count)
                return rs.CallerArgs[idx - 1].ToLowerInvariant();
            return ""; // out of bounds → empty (compares unequal to anything)
        }
        return token.ToLowerInvariant();
    }

    static void SkipToMatching(RunningScript rs, StatementKind opener, StatementKind closer)
    {
        // Skip statements until we land past the matching closer. Nested
        // openers of the same kind (nested if-blocks inside a skipped
        // outer if) bump depth. Stops at the END of the matching closer
        // so the outer dispatch resumes at the next statement.
        int depth = 1;
        var stmts = rs.Program.Statements;
        while (rs.Ip < stmts.Count && depth > 0)
        {
            var k = stmts[rs.Ip].Kind;
            rs.Ip++;
            if      (k == opener) depth++;
            else if (k == closer) depth--;
        }
    }

    void ExecCall(RunningScript rs, SfxStatement stmt)
    {
        // call <script> [<arg-list>] — recurse with the named script. Args
        // pass through verbatim; param substitution happens inside the
        // callee. Recursion is bounded because shipped DS1 sfx scripts call
        // a small fixed library of named primitives.
        if (stmt.Tokens.Count == 0) return;
        var name = stmt.Tokens[0];
        if (!_store.TryGet(name, out var script)) return;
        // Caller-args are tokens AFTER the script name. The compiler pre-
        // splits the verb so stmt.Tokens[0] is the script name and the
        // rest are positional args. fireball wrapper authors `call
        // fireball_base v<0 0 0> u<1>;` — args 1 and 2 are the literal
        // strings `v<0 0 0>` and `u<1>` which `[1]`/`[2]` references in
        // fireball_base then read via SubstituteCallerArgs.
        IReadOnlyList<string>? subArgs;
        if (stmt.Tokens.Count > 1)
        {
            var collected = new List<string>(stmt.Tokens.Count - 1);
            for (int t = 1; t < stmt.Tokens.Count; t++) collected.Add(stmt.Tokens[t]);
            subArgs = collected;
        }
        else
        {
            subArgs = rs.CallerArgs;
        }
        var prog = SfxScriptCompiler.Compile(name, script.Body);
        var sub = new RunningScript(prog, rs.Ctx, subArgs);
        StepUntilYield(sub);
        if (!sub.Done) _scripts.Add(sub);
        // Phase 21-SC-SPELL-VISUAL-G fold — push a marker handle so the
        // wrapper's `waitfor script #POP #DEFAULT_TIMEOUT` has something
        // to consume. Tag is "subscript" so a future splinter that
        // wires real subscript-completion tracking can match the kind
        // without changing the call site.
        rs.Stack.Push(new Handle { Kind = "subscript", Anchor = rs.Ctx.TargetPos, OtherEnd = rs.Ctx.TargetPos });
    }

    // ---- handle resolution ---------------------------------------------

    bool TryResolveHandleOperand(RunningScript rs, string token, bool pop, out Handle h)
    {
        h = default;
        if (string.Equals(token, "#POP", StringComparison.OrdinalIgnoreCase))
        {
            if (rs.Stack.Count == 0) return false;
            h = pop ? rs.Stack.Pop() : rs.Stack.Peek();
            return true;
        }
        if (string.Equals(token, "#PEEK", StringComparison.OrdinalIgnoreCase))
        {
            if (rs.Stack.Count == 0) return false;
            h = rs.Stack.Peek();
            return true;
        }
        if (token.StartsWith("$"))
            return rs.NamedHandles.TryGetValue(token, out h);
        return false;
    }

    static void StoreMutatedHandle(RunningScript rs, string token, Handle h)
    {
        if (token.StartsWith("$"))
            rs.NamedHandles[token] = h;
        else if (string.Equals(token, "#PEEK", StringComparison.OrdinalIgnoreCase)
              || string.Equals(token, "#POP",  StringComparison.OrdinalIgnoreCase))
        {
            // Mutating a #PEEK — push the modified handle back on top so the
            // next consumer sees the change.
            if (rs.Stack.Count > 0) rs.Stack.Pop();
            rs.Stack.Push(h);
        }
    }

    static Vector3 ResolveAnchor(string token, in SfxContext ctx)
    {
        // Token forms (case-insensitive):
        //   #SOURCE / #SOURCE_POSITION / #SOURCE_KB / source / @source
        //   #TARGET / #TARGET_KB / target / @target
        //   $var (unsupported here — falls through to TargetPos as a safe
        //     default: the typical caller uses #TARGET_KB)
        var t = token;
        if (t.Length > 0 && (t[0] == '#' || t[0] == '@')) t = t.Substring(1);
        if (t.StartsWith("source", StringComparison.OrdinalIgnoreCase))
        {
            if (t.Equals("source_position", StringComparison.OrdinalIgnoreCase))
                return ctx.SourcePos;
            return ctx.SourcePos;
        }
        if (t.StartsWith("target", StringComparison.OrdinalIgnoreCase))
            return ctx.TargetPos;
        return ctx.TargetPos;
    }

    // ---- param string parser ------------------------------------------

    static EmitterMode MapMode(string kind, string raw)
    {
        // A `sfx create fire` whose texture explicitly names a smoke/mist
        // sprite is meant to render as a color-preserving plume, not a
        // fire-flicker. SpawnFire's Color1 fade is warm-biased (X*0.4,
        // Y*0.2, Z*0.05), which renders any non-warm color (acid green,
        // ice cyan, etc.) as brown over the particle's lifetime — the
        // same family of bug we caught on iceshard's projectile trail.
        // Routing those creates to Smoke uses SpawnSmoke's color-preserving
        // Color1 fade so the script's color0 reads correctly.
        bool hasSmoke = ContainsKeyword(raw, "texture") &&
                        (raw.IndexOf("b_sfx_smoke", StringComparison.OrdinalIgnoreCase) >= 0
                         || raw.IndexOf("b_sfx_mist",  StringComparison.OrdinalIgnoreCase) >= 0);

        // A `sfx create fire` with a clearly non-warm color0 is also a
        // misnomer — DS1 spell authors used `fire` for "any flickering
        // particle column" even when the spell isn't fire-element
        // (acid_cloud, blast_zap, ice_storm, etc. all do this). Detect
        // by R-dominance: an input where Red is the brightest channel
        // wants the warm fade (real fire). When G or B dominates,
        // route to Smoke so SpawnSmoke's color-preserving fade keeps
        // green / blue / purple visible across the particle's
        // lifetime instead of fading to brown. Fixes the "every spell
        // looks like fireball" complaint without per-spell tweaks.
        bool nonWarmColor = false;
        if (TryReadVec4(raw, "color0", out var c0probe))
        {
            // R dominant within ~10% margin keeps the warm path; if G
            // or B exceeds R by more than that, treat as non-warm.
            float r = c0probe.X, g = c0probe.Y, b = c0probe.Z;
            if (g > r + 0.05f || b > r + 0.05f) nonWarmColor = true;
        }
        bool isDark   = ContainsKeyword(raw, "dark");
        switch (kind)
        {
            case "fire":      return (hasSmoke || isDark || nonWarmColor) ? EmitterMode.Smoke : EmitterMode.Fire;
            case "smoke":     return EmitterMode.Smoke;
            case "steam":     return EmitterMode.Steam;
            case "lightning": return EmitterMode.OneShotLightning;
            case "explosion": return EmitterMode.OneShotExplosion;
            case "sparkles":  return EmitterMode.OneShotSparkles;
            // Phase 21-SC-SPELL-VFX-3f/g/h/i first-pass mappings.
            case "flurry":    return EmitterMode.OneShotFlurry;
            case "fireb":     return EmitterMode.Fireb;
            case "cylinder":  return EmitterMode.OneShotCylinder;
            case "sray":      return EmitterMode.OneShotSray;
            case "sphere":    return EmitterMode.OneShotSphere;
            // Phase 21-SC-SPELL-VFX-3q outliers (1 spell each). Mapped onto
            // existing primitives to clear MISS without the bespoke
            // implementation cost — these are unique to one spell, low
            // leverage if we get them wrong.
            case "charge":             return EmitterMode.OneShotSparkles;     // fireskull build-up
            case "polygonalexplosion": return EmitterMode.OneShotExplosion;    // explode_body
            case "spe":                return EmitterMode.OneShotFlurry;       // incinerate
            // Phase 21-SC-SPELL-VFX-MOTION-HANDLE — motion-driven kinds.
            // Each gets a slot in _motionHandles whose Position advances
            // every tick; child emitters following them via TargetMotionId.
            // Phase 23-fold — monster-corpus kinds.
            case "linetracer":  return EmitterMode.OneShotLineTracer;
            case "spawn":       return EmitterMode.OneShotExplosion; // poly-shard approx in dispatch
            case "orbiter":     return EmitterMode.MotionOrbiter;
            case "trackball":   return EmitterMode.MotionTrackball;
            case "lightsource": return EmitterMode.LightSource;
            case "curve":       return EmitterMode.MotionCurve;
            default:          return EmitterMode.Unsupported;
        }
    }

    static Vector4 DefaultColor(string kind) => kind switch
    {
        "fire"               => new Vector4(1.00f, 0.55f, 0.20f, 1f),
        "smoke"              => new Vector4(0.35f, 0.35f, 0.38f, 0.55f),
        "steam"              => new Vector4(0.85f, 0.92f, 1.00f, 0.65f),
        "lightning"          => new Vector4(0.7f, 0.85f, 1.0f, 1f),
        "explosion"          => new Vector4(1f, 0.9f, 0.5f, 1f),
        "sparkles"           => new Vector4(0.9f, 0.9f, 1f, 1f),
        // Phase 21-SC-SPELL-VFX-3f/g/h/i/q/MOTION-HANDLE — defaults for the
        // newer kinds when a script forgets `color0(...)`. Match the
        // dominant in-game element each kind is associated with so a
        // script-side omission doesn't render colorless white.
        "flurry"             => new Vector4(1f, 0.9f, 0.5f, 1f),    // explosion-like burst
        "fireb"              => new Vector4(1.00f, 0.55f, 0.20f, 1f), // fire family
        "cylinder"           => new Vector4(1.00f, 0.55f, 0.20f, 1f), // shock/fire beams typical
        "sray"               => new Vector4(1f, 0.95f, 0.65f, 1f),  // sun-ray warm
        "charge"             => new Vector4(1.00f, 0.55f, 0.20f, 1f), // fireskull
        "polygonalexplosion" => new Vector4(1f, 0.9f, 0.5f, 1f),    // explosion-like
        "spe"                => new Vector4(1.00f, 0.55f, 0.20f, 1f), // incinerate (fire-themed)
        "trackball"          => new Vector4(1.00f, 0.55f, 0.20f, 1f), // fireball-class default
        "orbiter"            => new Vector4(1f, 1f, 1f, 1f),        // typically invisible(); white if not
        "lightsource"        => new Vector4(1.00f, 0.85f, 0.45f, 1f), // warm glow
        "curve"              => new Vector4(1f, 1f, 1f, 1f),        // motion handle, usually invisible
        "sphere"             => new Vector4(1.00f, 0.55f, 0.20f, 1f), // typical authored fireball-orange
        _                    => new Vector4(1f, 1f, 1f, 1f),
    };

    // ---- Phase 23a — param-coverage probe ------------------------------
    //
    // Every authored param key the runtime consumes funnels through
    // ExtractArgs / ContainsKeyword. While s_paramProbe is non-null those
    // helpers record the keyword they were asked for, so one pass of
    // ApplyParamString + MapMode against a dummy string yields the
    // complete consumed-key set straight from the live parse path — the
    // audit CLI can never drift from the parser the way a hand-maintained
    // list would.
    [ThreadStatic] static HashSet<string>? s_paramProbe;

    /// <summary>Phase 23a — enumerate every param keyword the runtime's
    /// param-string parser actually consumes, by probing the live parse
    /// path. Ground truth for <c>siegefx sfx param-audit</c>.</summary>
    public static IReadOnlyCollection<string> CollectConsumedParamKeys()
    {
        var probe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        s_paramProbe = probe;
        try
        {
            var h = new Handle();
            // Non-empty so ApplyParamString doesn't early-return; nothing
            // matches a single space, so every TryRead*/ContainsKeyword
            // call — including the else-branches of first-match chains —
            // executes and registers its keyword.
            ApplyParamString(ref h, " ");
            MapMode("fire", " "); // texture/color0/dark routing probes
        }
        finally { s_paramProbe = null; }
        return probe;
    }

    /// <summary>Phase 23a — param keys that are gameplay/damage payloads,
    /// not visuals (Siege University SiegeFX reference: trackball/lightning
    /// <c>damage(min,max,fit)</c>, fire <c>fdamage(...)</c> +
    /// <c>ignite(...)</c>). The param audit reports these separately
    /// instead of counting them as rendering gaps.</summary>
    public static readonly IReadOnlySet<string> GameplayParamKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "damage", "fdamage", "ignite" };

    static void ApplyParamString(ref Handle h, string raw)
    {
        if (string.IsNullOrEmpty(raw)) return;

        // Phase 21-SC-SPELL-VISUAL-H — read authored `texture(NAME)` so
        // dispatch arms can pick a renderer slot from the actual DS1
        // texture name instead of falling back to a hardcoded default.
        if (TryReadIdentifier(raw, "texture", out var texName))
            h.TextureName = texName;

        // Phase 21-SC-SPELL-VISUAL-H+sphere fold — `grow_params(start,
        // mid, end)` middle. firebomb_base authors `(.1, 1.5, 3)` — the
        // shell blows out to 1.5x base radius mid-life. Birth (start)
        // and death (end) tapers fall out of the renderer's existing
        // Scale0 → Scale1 fall-off. Default 1.0 = no scaling.
        if (TryReadFloat(raw, "grow_params", out var growMid, argIndex: 1) && growMid > 0.05f)
            h.GrowMid = growMid;

        // Scale knobs — different param names per effect type, all map to
        // our single Scale field.
        if (TryReadFloat(raw, "flamesize",  out var fs))   h.Scale = MathF.Max(0.05f, fs);
        else if (TryReadFloat(raw, "radius", out var rad)) h.Scale = MathF.Max(0.05f, rad);
        else if (TryReadFloat(raw, "max_radius", out var mr)) h.Scale = MathF.Max(0.05f, mr);
        // explosion uses scale_range(min,base,max) — pull the middle.
        if (TryReadFloat(raw, "scale_range", out var sr, argIndex: 1)) h.Scale = MathF.Max(0.05f, sr);
        // lightning uses scale(N) for line thickness.
        if (TryReadFloat(raw, "scale", out var sc)) h.Scale = MathF.Max(0.05f, sc);

        // Plume particle SIZE (fire/smoke/steam) is the authored scale()/
        // scale_range(), NOT radius/max_radius (those are the spawn footprint,
        // which also writes h.Scale above). Capture it into FlameSize so
        // BuildPlumeSpec sizes flames per-emitter — fireshot layers a big
        // scale(1.1) core over a scale(.5) aura; collapsing both to a fixed
        // fallback is what made the fireball a string of tiny embers.
        // flamesize() is the most explicit knob and still wins (applied below).
        if (TryReadFloat(raw, "scale_range", out var srFlame, argIndex: 1)) h.FlameSize = MathF.Max(0.05f, srFlame);
        else if (TryReadFloat(raw, "scale", out var scFlame)) h.FlameSize = MathF.Max(0.05f, scFlame);

        // Phase 21-SC-SPELL-VFX-2 — duration/lifetime params.
        // bolt_life(N) for lightning, dur(N) for explosion/sparkles.
        if (TryReadFloat(raw, "bolt_life", out var bl)) h.Duration = MathF.Max(0.10f, bl);
        if (TryReadFloat(raw, "dur",       out var du)) h.Duration = MathF.Max(0.10f, du);

        // maxdisplace(N) → bolt jitter amplitude.
        if (TryReadFloat(raw, "maxdisplace", out var md))
            h.Displace = MathF.Max(0f, MathF.Abs(md));

        // count(N) for explosion/sparkles burst size.
        if (TryReadFloat(raw, "count", out var cn) && cn > 0f)
            h.BurstCount = (int)MathF.Min(96f, cn);

        // ts(N) — DS1's "time scale" / particle lifetime. Turn into a
        // particles-per-second budget for persistent emitters.
        if (TryReadFloat(raw, "ts", out var ts) && ts > 0.001f)
            h.Rate = Math.Clamp(20f / ts, 4f, 120f);

        // color0(R,G,B[,A]) — start tint.
        if (TryReadVec4(raw, "color0", out var c0)) h.Color = c0;
        else if (TryReadVec4(raw, "color", out var c)) h.Color = c;

        // dark() — modifier shifting the tint into smoke-grey.
        if (ContainsKeyword(raw, "dark"))
        {
            var lum = 0.30f + 0.10f * h.Color.X;
            h.Color = new Vector4(lum, lum, lum, 0.55f);
        }

        // Phase 21-SC-SPELL-VFX-MOTION-HANDLE — orbital / homing knobs.
        // Always parsed; non-motion modes ignore them. iphi/itheta/radiusi
        // are unique to orbiter; velocity is unique to trackball. radius is
        // overloaded (Scale for non-orbiter), so OrbitRadius is set
        // separately and only used when EmitterMode is MotionOrbiter.
        if (TryReadFloat(raw, "iphi",     out var iphi))   h.OrbitPhi   = iphi;
        if (TryReadFloat(raw, "itheta",   out var ith))    h.OrbitTheta = ith;
        if (TryReadFloat(raw, "radius",   out var or))     h.OrbitRadius = or;
        if (TryReadFloat(raw, "radiusi",  out var ori))    h.OrbitRadiusInc = ori;
        if (TryReadFloat(raw, "velocity", out var vel))    h.Velocity = MathF.Abs(vel);

        // Phase 21-SC-SPELL-VISUAL-A — cylinder knobs. spin(N), tin/tout
        // fade-in/out, segments(N), and rp0(start,mid,end) where the mid
        // value drives the ring's outer radius (per the agent's inventory
        // of all 19 cylinder spells). rp1 ignored — it's a per-end taper
        // we don't render yet.
        if (TryReadFloat(raw, "spin",     out var sp))     h.SpinRate = sp;
        if (TryReadFloat(raw, "tin",      out var ti))     h.FadeIn   = MathF.Max(0f, ti);
        if (TryReadFloat(raw, "tout",     out var to))     h.FadeOut  = MathF.Max(0f, to);
        // segments(N) — the renderer's SpawnCylinder clamps to >=4 internally,
        // so don't gate here. Letting tiny values through preserves them in
        // diagnostic logs even if the renderer floors them.
        if (TryReadFloat(raw, "segments", out var sg)) h.Segments = (int)sg;
        if (TryReadFloat(raw, "rp0", out var rpMid, argIndex: 1)) h.RpMid = rpMid;

        // Phase 21-SC-SPELL-VISUAL-B — sray knobs. wsmin/wsmax + wemin/wemax
        // come as min/max ranges; average when both present so the value
        // honors the script's authored taper (e.g. wsmin(0) wsmax(0.3)
        // = 0.15 average — a tapered start, not a needle). First-only-wins
        // on a single-side authoring stays sensible.
        if (TryReadFloat(raw, "lmin", out var lmin)) h.LengthMin = lmin;
        if (TryReadFloat(raw, "lmax", out var lmax)) h.LengthMax = lmax;
        bool hasWsmin = TryReadFloat(raw, "wsmin", out var wsmin);
        bool hasWsmax = TryReadFloat(raw, "wsmax", out var wsmax);
        if (hasWsmin && hasWsmax) h.WidthStart = (wsmin + wsmax) * 0.5f;
        else if (hasWsmin)        h.WidthStart = wsmin;
        else if (hasWsmax)        h.WidthStart = wsmax;
        bool hasWemin = TryReadFloat(raw, "wemin", out var wemin);
        bool hasWemax = TryReadFloat(raw, "wemax", out var wemax);
        if (hasWemin && hasWemax) h.WidthEnd = (wemin + wemax) * 0.5f;
        else if (hasWemin)        h.WidthEnd = wemin;
        else if (hasWemax)        h.WidthEnd = wemax;
        if (TryReadVec4(raw, "color1", out var c1tail)) h.ColorTail = c1tail;

        // Phase 21-SC-SPELL-VISUAL-C — fireb knobs. velocity(x,y,z) full
        // 3-vector. accel(x,y,z) full 3-vector. lower_r0/r1 + upper_r0/r1
        // mid values (we use the average start/end for a single cone radius).
        // alphafade(N) = particle lifetime. fctrl(0,0,N) flicker — only
        // the third arg is the rate.
        if (TryReadFloat(raw, "velocity", out var vx, argIndex: 0)
         && TryReadFloat(raw, "velocity", out var vy, argIndex: 1)
         && TryReadFloat(raw, "velocity", out var vz, argIndex: 2))
            { h.VelocityVec = new Vector3(vx, vy, vz); h.HasVelocity = true; }
        if (TryReadFloat(raw, "accel", out var ax, argIndex: 0)
         && TryReadFloat(raw, "accel", out var ay, argIndex: 1)
         && TryReadFloat(raw, "accel", out var az, argIndex: 2))
            { h.AccelVec = new Vector3(ax, ay, az); h.HasAccel = true; }
        if (TryReadFloat(raw, "max_displace", out var maxd))
            h.MaxDisplace = MathF.Abs(maxd);
        if (TryReadFloat(raw, "alphafade", out var afade))
            h.AlphaFade = MathF.Max(0.10f, afade);
        // lower_r0/r1 and upper_r0/r1 likewise come as range pairs — start
        // and end of the cone profile along the axis. Average to a single
        // representative radius when both present.
        bool hasLr0 = TryReadFloat(raw, "lower_r0", out var lr0);
        bool hasLr1 = TryReadFloat(raw, "lower_r1", out var lr1);
        if (hasLr0 && hasLr1) h.LowerRadius = (lr0 + lr1) * 0.5f;
        else if (hasLr0)      h.LowerRadius = lr0;
        else if (hasLr1)      h.LowerRadius = lr1;
        bool hasUr0 = TryReadFloat(raw, "upper_r0", out var ur0);
        bool hasUr1 = TryReadFloat(raw, "upper_r1", out var ur1);
        if (hasUr0 && hasUr1) h.UpperRadius = (ur0 + ur1) * 0.5f;
        else if (hasUr0)      h.UpperRadius = ur0;
        else if (hasUr1)      h.UpperRadius = ur1;
        if (TryReadFloat(raw, "flamesize", out var flsz))
            h.FlameSize = MathF.Max(0.05f, flsz);

        // ---- Phase 23d-1 — full Appendix-A capture ----------------------
        // Everything below is parsed 1:1 from GPG's documented parameter
        // tables (SU 212 Appendix A). Legacy mappings above are untouched;
        // renderer honoring switches over kind-by-kind in 23d-2 so each
        // visual change lands as a reviewable golden-trace diff.

        // Global.
        if (TryReadVec3(raw, "offset", out var off)) { h.OffsetVec = off; h.HasOffset = true; }
        if (TryReadVec3(raw, "dir",    out var dirv)) { h.DirVec = dirv; h.HasDir = true; }
        if (TryReadFloat(raw, "delay", out var dly)) h.DelaySec = MathF.Max(0f, dly);
        if (ContainsKeyword(raw, "abs"))          h.AbsCoords = true;
        if (ContainsKeyword(raw, "sup"))          h.StaticUpdate = true;
        if (ContainsKeyword(raw, "use_wind"))     h.UseWind = true;
        if (ContainsKeyword(raw, "world_orient")) h.WorldOrient = true;
        if (ContainsKeyword(raw, "bone_orient"))  h.BoneOrientFlag = true;

        // Explosion family.
        if (TryReadFloat(raw, "fade_range", out var fs0, argIndex: 0)
         && TryReadFloat(raw, "fade_range", out var fs1, argIndex: 1))
        { h.FadeStart = fs0; h.FadeEnd = fs1; h.HasFadeRange = true; }
        if (TryReadFloat(raw, "vmin", out var vmn)) { h.VMin = vmn; h.HasVMin = true; }
        if (TryReadFloat(raw, "vmax", out var vmx)) { h.VMax = vmx; h.HasVMax = true; }
        if (TryReadVec3(raw, "ivel", out var ivl)) { h.IVelVec = ivl; h.HasIVel = true; }
        if (TryReadVec3(raw, "rvel", out var rvl)) { h.RVelVec = rvl; h.HasRVel = true; }
        if (ContainsKeyword(raw, "omni_dir") || ContainsKeyword(raw, "omnidir")) h.OmniDir = true;
        if (ContainsKeyword(raw, "collide"))    h.CollideFlag = true;
        if (ContainsKeyword(raw, "grey_tex"))   h.GreyTex = true;
        if (ContainsKeyword(raw, "line"))       h.LineMode = true;
        if (ContainsKeyword(raw, "rand_scale")) h.RandScale = true;
        if (TryReadFloat(raw, "srate", out var sr8)) h.SpawnOverSec = MathF.Max(0f, sr8);
        if (TryReadFloat(raw, "min_theta", out var mnt)) { h.MinTheta = mnt; h.HasThetaRange = true; }
        if (TryReadFloat(raw, "max_theta", out var mxt)) { h.MaxTheta = mxt; h.HasThetaRange = true; }
        if (TryReadFloat(raw, "min_phi", out var mnp)) { h.MinPhi = mnp; h.HasPhiRange = true; }
        if (TryReadFloat(raw, "max_phi", out var mxp)) { h.MaxPhi = mxp; h.HasPhiRange = true; }

        // Fire / Steam.
        if (TryReadFloat(raw, "min_radius", out var mnr)) h.MinRadius = MathF.Max(0f, mnr);
        if (TryReadFloat(raw, "max_radius", out var mxr2)) { h.MaxRadius = mxr2; h.HasMaxRadius = true; }
        if (TryReadFloat(raw, "min_displace", out var mnd)) { h.MinDisplaceY = mnd; h.HasMinDisplace = true; }
        else if (TryReadFloat(raw, "mindisplace", out var mnd2)) { h.MinDisplaceY = mnd2; h.HasMinDisplace = true; }
        if (ContainsKeyword(raw, "instant")) h.Instant = true;
        if (TryReadFloat(raw, "fctrl", out var fc0, argIndex: 0)
         && TryReadFloat(raw, "fctrl", out var fc1, argIndex: 1))
        {
            TryReadFloat(raw, "fctrl", out var fc2, argIndex: 2);
            h.FctrlVec = new Vector3(fc0, fc1, fc2); h.HasFctrl = true;
        }
        if (TryReadFloat(raw, "min_count", out var mnc)) h.MinCount = (int)mnc;

        // Lightning.
        if (TryReadFloat(raw, "subd", out var sbd)) { h.SubdLevel = sbd; h.HasSubd = true; }
        if (TryReadFloat(raw, "minsubd", out var msbd)) { h.MinSubd = msbd; h.HasMinSubd = true; }

        // Cylinder ring triples (start, end, increment).
        if (TryReadVec3(raw, "rp0", out var rp0t)) { h.Rp0Triple = rp0t; h.HasRp0 = true; }
        if (TryReadVec3(raw, "rp1", out var rp1t)) { h.Rp1Triple = rp1t; h.HasRp1 = true; }
        if (TryReadVec3(raw, "hp0", out var hp0t)) { h.Hp0Triple = hp0t; h.HasHp0 = true; }
        if (TryReadVec3(raw, "hp1", out var hp1t)) { h.Hp1Triple = hp1t; h.HasHp1 = true; }
        if (TryReadVec3(raw, "rotate",  out var rotv)) { h.RotateVec = rotv; h.HasRotate = true; }
        else if (TryReadFloat(raw, "rotate", out var rots)) { h.RotateVec = new Vector3(0f, rots, 0f); h.HasRotate = true; }
        if (TryReadVec3(raw, "irotate", out var irot)) { h.IRotateVec = irot; h.HasIRotate = true; }

        // alpha — sray ships a (start, fade-min, fade-max) triple; cylinder
        // and decal ship a scalar. Disambiguate by arg count.
        {
            var alphaArgs = ExtractArgs(raw, "alpha");
            if (alphaArgs is { Length: >= 3 }
                && TryParseF(alphaArgs[0], out var a0) && TryParseF(alphaArgs[1], out var a1) && TryParseF(alphaArgs[2], out var a2))
            { h.AlphaTriple = new Vector3(a0, a1, a2); h.HasSrayAlpha = true; }
            else if (alphaArgs is { Length: >= 1 } && TryParseF(alphaArgs[0], out var aS))
            { h.AlphaStart = aS; h.HasAlpha = true; }
        }
        // theta — sray triple (start, min-inc, max-inc) vs trackball/orbiter scalar.
        {
            var thetaArgs = ExtractArgs(raw, "theta");
            if (thetaArgs is { Length: >= 3 }
                && TryParseF(thetaArgs[0], out var t0) && TryParseF(thetaArgs[1], out var t1) && TryParseF(thetaArgs[2], out var t2))
            { h.ThetaTriple = new Vector3(t0, t1, t2); h.HasSrayTheta = true; }
            else if (thetaArgs is { Length: >= 1 } && TryParseF(thetaArgs[0], out var tS))
            { h.ThetaStart = tS; h.HasTheta = true; }
        }
        // phi — sray triple vs orbiter scalar.
        {
            var phiArgs = ExtractArgs(raw, "phi");
            if (phiArgs is { Length: >= 3 }
                && TryParseF(phiArgs[0], out var p0) && TryParseF(phiArgs[1], out var p1) && TryParseF(phiArgs[2], out var p2))
            { h.PhiTriple = new Vector3(p0, p1, p2); h.HasSrayPhi = true; }
            else if (phiArgs is { Length: >= 1 } && TryParseF(phiArgs[0], out var pS))
            { h.PhiStart = pS; h.HasPhi = true; }
        }

        // Trackball.
        if (TryReadFloat(raw, "afterlife", out var aft)) h.Afterlife = MathF.Max(0f, aft);
        if (TryReadFloat(raw, "kdamp", out var kd)) { h.Kdamp = kd; h.HasKdamp = true; }
        if (TryReadFloat(raw, "rvel_min", out var rvmn)) h.RvelMin = rvmn;
        if (TryReadFloat(raw, "rvel_max", out var rvmx)) h.RvelMax = rvmx;
        if (TryReadFloat(raw, "max_velocity", out var mxv)) { h.MaxVelocity = mxv; h.HasMaxVelocity = true; }
        if (ContainsKeyword(raw, "homing"))     h.Homing = true;
        if (ContainsKeyword(raw, "damped"))     h.DampedFlag = true;
        if (ContainsKeyword(raw, "invisible"))  h.Invisible = true;
        if (ContainsKeyword(raw, "no_collide")) h.NoCollide = true;
        if (ContainsKeyword(raw, "hit_target_only"))    h.HitTargetOnly = true;
        if (ContainsKeyword(raw, "no_collide_objects")) h.NoCollideObjects = true;

        // Orbiter.
        if (TryReadFloat(raw, "vdisplace", out var vd)) h.VDisplace = vd;
        if (ContainsKeyword(raw, "free"))   h.FreeOrient = true;
        if (ContainsKeyword(raw, "smooth")) h.Smooth = true;
        if (TryReadIdentifier(raw, "model", out var mdl))
        {
            // Orbiter/spawn author a template name; curve authors a numeric
            // behavior selector 0/1/2. Disambiguate by parseability.
            if (int.TryParse(mdl, out var cm)) h.CurveModel = cm;
            else h.ModelName = mdl;
        }
        if (TryReadFloat(raw, "model_sin",  out var msin)) h.ModelSin = msin;
        if (TryReadFloat(raw, "model_sout", out var msout)) h.ModelSout = msout;
        if (TryReadFloat(raw, "model_smax", out var msmax)) h.ModelSmax = msmax;
        if (TryReadVec3(raw, "rot_inc", out var rinc)) h.RotIncVec = rinc;

        // Curve.
        if (TryReadFloat(raw, "curvature", out var cur)) { h.Curvature = cur; h.HasCurvature = true; }
        if (TryReadFloat(raw, "spacing", out var spc)) { h.Spacing = spc; h.HasSpacing = true; }
        if (TryReadFloat(raw, "tlength", out var tl)) { h.TrailLength = tl; h.HasTrailLength = true; }
        if (TryReadFloat(raw, "size", out var csz)) h.CurveSize = csz;
        if (TryReadFloat(raw, "step", out var cst)) h.CurveStep = cst;

        // Flurry.
        if (TryReadFloat(raw, "amplitude", out var amp)) { h.Amplitude = amp; h.HasAmplitude = true; }
        if (TryReadFloat(raw, "iamp", out var iam)) { h.IAmp = iam; h.HasIAmp = true; }

        // SPE — per-axis (x,y,z) triples.
        {
            bool spe = false;
            if (TryReadVec3(raw, "index0", out var i0)) { h.SpeIndex0 = i0; spe = true; }
            if (TryReadVec3(raw, "index1", out var i1)) { h.SpeIndex1 = i1; spe = true; }
            if (TryReadVec3(raw, "speed0", out var s0)) { h.SpeSpeed0 = s0; spe = true; }
            if (TryReadVec3(raw, "speed1", out var s1)) { h.SpeSpeed1 = s1; spe = true; }
            if (TryReadVec3(raw, "space0", out var sp0)) { h.SpeSpace0 = sp0; spe = true; }
            if (TryReadVec3(raw, "space1", out var sp1)) { h.SpeSpace1 = sp1; spe = true; }
            if (spe) h.HasSpe = true;
        }

        // Sphere.
        if (TryReadFloat(raw, "sides", out var sds)) h.SphereSides = (int)sds;
        if (h.HasSubd) h.SphereSubd = (int)h.SubdLevel; // sphere reuses subd(n)
        if (ContainsKeyword(raw, "lines")) h.SphereLines = true;
        if (ContainsKeyword(raw, "cull"))  h.BackfaceCull = true;

        // PolygonalExplosion.
        if (TryReadFloat(raw, "poly_sides", out var ps)) { h.PolySides = (int)ps; h.HasPolySides = true; }
        if (TryReadFloat(raw, "mag", out var mg)) { h.Mag = mg; h.HasMag = true; }
        if (TryReadVec3(raw, "rotrange", out var rr)) h.RotRangeVec = rr;
        if (TryReadVec3(raw, "displace", out var dsp)) h.DisplaceVec = dsp;

        // Lightsource.
        if (TryReadFloat(raw, "iradius", out var ird)) h.InnerRadius = ird;
        if (TryReadFloat(raw, "frequency", out var frq)) { h.Flicker = frq; h.HasFlicker = true; }

        // FireB.
        if (ContainsKeyword(raw, "cvel")) h.CVelOff = true;
        if (TryReadFloat(raw, "start_rate", out var str8)) { h.StartRate = str8; h.HasStartRate = true; }
        if (TryReadFloat(raw, "end_rate", out var enr8)) { h.EndRate = enr8; h.HasEndRate = true; }
        if (TryReadFloat(raw, "velocity_s", out var vsc)) h.VelocityScalar = vsc;

        // Phase 23d-2d — charge/sparkles scalar knobs. speed0 is shared
        // with SPE's (x,y,z) form — arg count disambiguates.
        {
            var sp0Args = ExtractArgs(raw, "speed0");
            if (sp0Args is { Length: 1 } && TryParseF(sp0Args[0], out var chSp))
            { h.ChargeSpeed = chSp; h.HasChargeSpeed = true; }
        }
        if (TryReadFloat(raw, "centersize", out var ctr)) h.CenterSize = ctr;
        if (TryReadFloat(raw, "ialpha", out var ial)) h.IAlpha = ial;
        if (TryReadFloat(raw, "psize", out var psz)) h.PSize = psz;
        if (TryReadFloat(raw, "yvel", out var yv)) h.YVel = yv;

        // Trackball scalar accel(f) — only when the vector form didn't parse.
        {
            var accelArgs = ExtractArgs(raw, "accel");
            if (accelArgs is { Length: 1 } && TryParseF(accelArgs[0], out var accS))
            { h.AccelScalar = accS; h.HasAccelScalar = true; }
        }
        // Explosion scale_range(min, max, 0) — full range capture.
        if (TryReadFloat(raw, "scale_range", out var srMin, argIndex: 0)
         && TryReadFloat(raw, "scale_range", out var srMax, argIndex: 1))
        { h.ScaleRangeMin = srMin; h.ScaleRangeMax = srMax; h.HasScaleRange = true; }

        // Phase 23-fold — monster-corpus params surfaced by the widened
        // catalog (dragon breath lights, gom_icesnake line animation,
        // burn_body sine wobble, spit physics, tracer fades, frag spawns).
        if (ContainsKeyword(raw, "light_spawn")) h.LightSpawn = true;
        if (TryReadFloat(raw, "light_freq", out var lfq)) h.LightFreq = lfq;
        if (TryReadFloat(raw, "light_radius", out var lrd)) h.LightRadius = lrd;
        if (TryReadFloat(raw, "linepos", out var lps)) { h.LinePos = lps; h.HasLineAnim = true; }
        if (TryReadFloat(raw, "linespeed", out var lsp)) { h.LineSpeed = lsp; h.HasLineAnim = true; }
        if (TryReadFloat(raw, "sinpos", out var snp)) { h.SinPos = snp; h.HasSinAnim = true; }
        if (TryReadFloat(raw, "sinspeed", out var sns)) { h.SinSpeed = sns; h.HasSinAnim = true; }
        if (TryReadFloat(raw, "radius_rmax", out var rrm)) h.RadiusRMax = rrm;
        if (TryReadFloat(raw, "rebound", out var rbn)) { h.Rebound = rbn; h.HasRebound = true; }
        if (ContainsKeyword(raw, "splat")) h.Splat = true;
        if (TryReadVec4(raw, "color2", out var c2)) { h.Color2 = c2; h.HasColor2 = true; }
        if (TryReadFloat(raw, "fade_rate", out var fdr)) { h.FadeRate = fdr; h.HasFadeRate = true; }
        if (TryReadVec3(raw, "lvel", out var lv)) { h.LVel = lv; h.HasLVel = true; }
        if (TryReadVec3(raw, "lvel_var", out var lvv)) h.LVelVar = lvv;
        if (TryReadFloat(raw, "spawn_int", out var spi)) h.SpawnInterval = spi;
        // Engine-level (consumed deliberately): fps = animated-[tsd]
        // texture frame rate (our slots are static stills for now),
        // use_fog = fog-blend hint, lock = linetracer third-target ref,
        // splat_* / stexture / alphastart tune the splat decal we render
        // as stick-in-place particles.
        _ = ContainsKeyword(raw, "fps");
        _ = ContainsKeyword(raw, "use_fog");
        _ = ContainsKeyword(raw, "lock");
        _ = ContainsKeyword(raw, "splat_life");
        _ = ContainsKeyword(raw, "splat_fade");
        _ = ContainsKeyword(raw, "splatscaleup");
        _ = ContainsKeyword(raw, "splatskip");
        _ = ContainsKeyword(raw, "stexture");
        _ = ContainsKeyword(raw, "alphastart");
        _ = ContainsKeyword(raw, "spawn_count");

        // Phase 23-fold F7 — authored-presence flags + grow triple.
        h.HasTin    = ContainsKeyword(raw, "tin");
        h.HasTout   = ContainsKeyword(raw, "tout");
        h.HasIPhi   = ContainsKeyword(raw, "iphi");
        h.HasITheta = ContainsKeyword(raw, "itheta");
        h.HasVel    = ContainsKeyword(raw, "velocity");
        h.HasRadius = ContainsKeyword(raw, "radius");
        if (TryReadFloat(raw, "grow_params", out var gp0, argIndex: 0)) { h.GrowStart = gp0; h.HasGrow = true; }
        if (TryReadFloat(raw, "grow_params", out var gp2, argIndex: 2)) h.GrowEnd = gp2;

        // Engine-level flags with no visual meaning for our runtime —
        // consumed deliberately so the param audit reads them as handled:
        // must_update() asks DS1 to keep re-evaluating a moving anchor,
        // which our persistent-emitter/motion-handle path already does
        // unconditionally; cheap() selects a cheaper motion integrator;
        // check_ground()/yorient() are engine hints. Probed via
        // ContainsKeyword so they register in CollectConsumedParamKeys.
        _ = ContainsKeyword(raw, "must_update");
        _ = ContainsKeyword(raw, "cheap");
        _ = ContainsKeyword(raw, "check_ground");
        _ = ContainsKeyword(raw, "yorient");
        _ = ContainsKeyword(raw, "no_noise");
    }

    /// <summary>Phase 23d-1 — read a 3-component <c>key(x,y,z)</c> vector.
    /// Requires at least 3 parseable args (2-arg and scalar forms are the
    /// caller's business — e.g. rotate() ships both scalar and triple in
    /// DS1 data).</summary>
    static bool TryReadVec3(string raw, string keyword, out Vector3 value)
    {
        value = default;
        var args = ExtractArgs(raw, keyword);
        if (args is null || args.Length < 3) return false;
        if (!TryParseF(args[0], out var x) || !TryParseF(args[1], out var y) || !TryParseF(args[2], out var z))
            return false;
        value = new Vector3(x, y, z);
        return true;
    }

    static bool TryParseF(string s, out float v) =>
        float.TryParse(s.TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    static string SubstituteCallerArgs(string? param, IReadOnlyList<string>? callerArgs)
    {
        if (string.IsNullOrEmpty(param)) return "";
        if (callerArgs is null || callerArgs.Count == 0) return param;
        var sb = new System.Text.StringBuilder(param.Length);
        for (int i = 0; i < param.Length; i++)
        {
            if (param[i] == '[' && i + 2 < param.Length && param[i + 2] == ']' && char.IsDigit(param[i + 1]))
            {
                int idx = param[i + 1] - '0';
                if (idx < callerArgs.Count) sb.Append(callerArgs[idx]);
                i += 2;
                continue;
            }
            sb.Append(param[i]);
        }
        return sb.ToString();
    }

    /// <summary>Phase 21-SC-SPELL-VISUAL-H+sphere fold — substitute
    /// <c>$name</c> tokens in a param string with their values from the
    /// running script's <see cref="RunningScript.Vars"/>. firebomb_base
    /// and similar scripts ship `set $scale .7; sfx create sphere ...
    /// "scale($scale)..."` so this pass runs ahead of TryReadFloat /
    /// TryReadVec4 so the keyword extractors see real numbers.
    /// Unrecognized $names pass through unchanged (matches the
    /// pre-substitute behavior — no spurious hard-fail on a typo).</summary>
    static string SubstituteVars(string raw, Dictionary<string, string> vars)
    {
        if (string.IsNullOrEmpty(raw) || vars.Count == 0) return raw;
        if (raw.IndexOf('$') < 0) return raw;
        var sb = new System.Text.StringBuilder(raw.Length);
        int i = 0;
        while (i < raw.Length)
        {
            char c = raw[i];
            if (c == '$' && i + 1 < raw.Length && (char.IsLetter(raw[i + 1]) || raw[i + 1] == '_'))
            {
                int start = i;
                i++; // skip $
                while (i < raw.Length && (char.IsLetterOrDigit(raw[i]) || raw[i] == '_'))
                    i++;
                var name = raw.Substring(start, i - start);
                if (vars.TryGetValue(name, out var v))
                    sb.Append(v);
                else
                    sb.Append(name); // leave as-is so the keyword reader sees the literal
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    /// <summary>Phase 23-fold F1 — substitute <c>#POP</c>/<c>#PEEK</c>
    /// occurrences inside a create's param string with values consumed
    /// from the script stack (exp_test's <c>color0(#POP,#POP,#POP)</c>
    /// eats three frandrange rolls). Leaving them stacked desynchronized
    /// every later <c>sfx start #POP</c> — explosion #1 never started and
    /// the numeric residue became an immortal invisible plume emitter.</summary>
    static string SubstitutePops(string raw, RunningScript rs)
    {
        if (string.IsNullOrEmpty(raw) || raw.IndexOf('#') < 0) return raw;
        static bool At(string s, int i, string kw) =>
            i + kw.Length <= s.Length
            && string.Compare(s, i, kw, 0, kw.Length, StringComparison.OrdinalIgnoreCase) == 0
            && (i + kw.Length == s.Length || !char.IsLetter(s[i + kw.Length]));
        var sb = new System.Text.StringBuilder(raw.Length);
        int i = 0;
        while (i < raw.Length)
        {
            if (raw[i] == '#')
            {
                if (At(raw, i, "#POP"))
                {
                    sb.Append(rs.Stack.Count > 0 ? rs.Stack.Pop().Kind : "0");
                    i += 4;
                    continue;
                }
                if (At(raw, i, "#PEEK"))
                {
                    sb.Append(rs.Stack.Count > 0 ? rs.Stack.Peek().Kind : "0");
                    i += 5;
                    continue;
                }
            }
            sb.Append(raw[i]);
            i++;
        }
        return sb.ToString();
    }

    static bool ContainsKeyword(string raw, string keyword)
    {
        s_paramProbe?.Add(keyword);
        int idx = 0;
        while ((idx = raw.IndexOf(keyword, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int end = idx + keyword.Length;
            if (end < raw.Length && raw[end] == '(')
            {
                bool leftOk = idx == 0 || (!char.IsLetter(raw[idx - 1]) && raw[idx - 1] != '_');
                if (leftOk) return true;
            }
            idx = end;
        }
        return false;
    }

    static bool TryReadFloat(string raw, string keyword, out float value, int argIndex = 0)
    {
        value = 0f;
        var args = ExtractArgs(raw, keyword);
        if (args is null || args.Length <= argIndex) return false;
        return float.TryParse(args[argIndex].TrimEnd('f', 'F'),
            NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Phase 21-SC-SPELL-VISUAL-H — read a single identifier
    /// (non-numeric) argument from a `keyword(arg)` block. Used for
    /// `texture(b_sfx_xxx)` where the arg is a path-like name rather
    /// than a number; TryReadFloat would silently reject it.</summary>
    static bool TryReadIdentifier(string raw, string keyword, out string value)
    {
        value = "";
        var args = ExtractArgs(raw, keyword);
        if (args is null || args.Length == 0) return false;
        var v = args[0].Trim();
        if (v.Length == 0) return false;
        value = v;
        return true;
    }

    /// <summary>Phase 21-SC-SPELL-VISUAL-H — DS1 texture name to renderer
    /// slot. The renderer pre-loads the most-referenced textures into
    /// fixed slots (see ParticleSystem.LoadTextures); this maps the
    /// authored `texture(NAME)` value to one of those, with sensible
    /// family fallbacks (any sparkle-class name → slot 2, any cyl-class
    /// → 9/10/11, etc.). Returns the supplied default when the name
    /// is unrecognized so dispatch arms still pick a usable slot.</summary>
    public static byte TextureNameToSlot(string? name, byte fallback)
    {
        if (string.IsNullOrEmpty(name)) return fallback;
        // Strip any path / extension that crept into the param.
        var n = name;
        int slash = n.LastIndexOf('/');
        if (slash >= 0) n = n.Substring(slash + 1);
        int dot = n.LastIndexOf('.');
        if (dot >= 0) n = n.Substring(0, dot);
        n = n.ToLowerInvariant();
        return n switch
        {
            "b_sfx_fireball-01"   => 0,
            "b_sfx_fireball-02"   => 0,    // close-enough family fallback
            "b_sfx_smoke"         => 1,
            "b_sfx_snow_01"       => 1,    // smoke-class billboard
            "b_sfx_mist_01"       => 1,
            "b_sfx_sparkle01"     => 2,
            "b_sfx_sparkle_02"    => 2,
            "b_sfx_star_01"       => 2,
            "b_sfx_star_02"       => 2,
            "b_sfx_010"           => 2,    // sparkle-class
            "b_sfx_002"           => 3,
            "b_sfx_033"           => 3,
            "b_sfx_lightray_01"   => 4,
            "b_sfx_lightray_02"   => 5,
            "b_sfx_lightray_04"   => 6,
            "b_sfx_streaks"       => 7,
            "b_sfx_lightray01"    => 8,
            // Phase 21-SC-SPELL-VISUAL-H+sphere fold: blueflare is a
            // ROUND flash, not a streak — was mis-routed to slot 8
            // (lightray01). Slot 0 (b_sfx_fireball-01) is the closest
            // round-flash analog the renderer pre-loads. Same logic
            // for armor_shock (impact burst, not a beam).
            "b_sfx_blueflare_01"  => 0,
            "b_sfx_armor_shock"   => 0,
            "b_sfx_cyl_01"        => 9,
            "b_sfx_cyl_02"        => 10,
            "b_sfx_cyl_03"        => 11,
            "b_sfx_splotches_02"  => 0,    // warm fireball-class
            // Rock chunks are tight bright sprites, not soft smoke —
            // route to the sparkle slot which renders as a bright dot
            // instead of a slow smoke fade.
            "b_sfx_rock_single_01"=> 2,
            _ => fallback,
        };
    }

    static bool TryReadVec4(string raw, string keyword, out Vector4 value)
    {
        value = default;
        var args = ExtractArgs(raw, keyword);
        if (args is null || args.Length < 3) return false;
        if (!float.TryParse(args[0].TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return false;
        if (!float.TryParse(args[1].TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return false;
        if (!float.TryParse(args[2].TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) return false;
        float w = 1f;
        if (args.Length >= 4)
            float.TryParse(args[3].TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture, out w);
        value = new Vector4(x, y, z, w);
        return true;
    }

    static string[]? ExtractArgs(string raw, string keyword)
    {
        s_paramProbe?.Add(keyword);
        int idx = 0;
        while ((idx = raw.IndexOf(keyword, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int end = idx + keyword.Length;
            if (end < raw.Length && raw[end] == '(')
            {
                bool leftOk = idx == 0 || (!char.IsLetter(raw[idx - 1]) && raw[idx - 1] != '_');
                if (leftOk)
                {
                    int depth = 1;
                    int start = end + 1;
                    int j = start;
                    while (j < raw.Length && depth > 0)
                    {
                        if (raw[j] == '(') depth++;
                        else if (raw[j] == ')') depth--;
                        if (depth == 0) break;
                        j++;
                    }
                    if (j > raw.Length) return null;
                    return raw.Substring(start, j - start)
                              .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                }
            }
            idx = end;
        }
        return null;
    }

    static bool TryParseFloat(IReadOnlyList<string> tokens, int index, out float value)
    {
        value = 0f;
        if (index >= tokens.Count) return false;
        return float.TryParse(tokens[index].TrimEnd('f', 'F'),
            NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    void LogUnhandledOnce(string verb)
    {
        if (_unhandledVerbsLogged.Add(verb))
            Console.WriteLine($"[sfx-vm] unhandled verb '{verb}' — logged once, future occurrences silent");
    }

    // ---- types ---------------------------------------------------------

    enum EmitterMode
    {
        Fire, Smoke, Steam,
        OneShotLightning, OneShotExplosion, OneShotSparkles,
        // Phase 21-SC-SPELL-VFX-3f/g/h/i — first-pass implementations
        // mapped to existing particle/bolt primitives. Visual tuning
        // (texture choice, motion shape) is the user's eyes-test job
        // captured in the per-primitive test list at session end.
        OneShotFlurry,    // 3f — particle burst with growth, ≈SpawnSpark+count
        Fireb,            // 3g — fire one-shot with directional puff, ≈SpawnFire
        OneShotCylinder,  // SC-SPELL-VISUAL-A — flat textured ground ring at anchor, rp0-mid radius
        OneShotSray,      // 3h — directional ray, ≈SpawnSpark dense + slight bias
        // Phase 21-SC-SPELL-VISUAL-H — sphere primitive. firebomb_base /
        // bombard_base / dave_shield etc. author this as an expanding
        // particle shell around the anchor. First-pass renders as omni-
        // directional SpawnSpark scaled by the authored radius + grow
        // params; flips the last MISS primitive (2 spells: bombard,
        // firebomb) to OK in the catalog audit.
        OneShotSphere,
        // Phase 21-SC-SPELL-VFX-MOTION-HANDLE — motion handles that other
        // emitters can target via `sfx target $emitter $motion`. Each gets
        // a slot in _motionHandles whose Position advances every tick;
        // PersistentEmitters with TargetMotionId>0 follow that position
        // each maintain pass.
        OneShotLineTracer, // Phase 23-fold — fading tracer ribbon between two points
        MotionOrbiter,    // orbital motion anchor — invisible, drives child emitters
        MotionTrackball,  // homing projectile — visible glow trail + drives child emitters
        LightSource,      // persistent glow billboard at position
        MotionCurve,      // splined-path motion handle — invisible
        // Phase 21-SC-SPELL-VISUAL-D — additive halo cluster used by
        // lightsource handles. Distinct from Steam (smoke wisp) so the
        // motion-driven glow reads as a glowing core, not a puff trail.
        Glow,
        Unsupported,
    }

    struct Handle
    {
        public string      Kind;
        /// <summary>Where `sfx create` anchored the effect (typically
        /// resolved from the second token, e.g. #TARGET_KB).</summary>
        public Vector3     Anchor;
        /// <summary>For two-ended effects (lightning beam) this is the
        /// other endpoint, set by `sfx target` / `sfx attach_point`.
        /// Defaults to <see cref="Anchor"/> until something re-anchors it.</summary>
        public Vector3     OtherEnd;
        /// <summary>Legacy alias of <see cref="Anchor"/> kept so persistent
        /// emitter creation reads the same field name as before.</summary>
        public Vector3     Position;
        public Vector4     Color;
        public float       Scale;
        public float       Rate;
        public float       Duration;     // bolt_life / dur
        public float       Displace;     // maxdisplace amplitude
        public int         BurstCount;   // explosion/sparkles count(N)
        public EmitterMode Mode;
        // Phase 21-SC-SPELL-VISUAL-H — authored texture name from the
        // create's `texture(b_sfx_xxx)` param. null when the script
        // didn't author one; the per-mode dispatch falls back to a
        // sensible default slot. Mapped to a renderer texture-slot
        // via TextureNameToSlot at dispatch time.
        public string?     TextureName;
        // Phase 21-SC-SPELL-VISUAL-H+sphere fold — `grow_params(start,
        // mid, end)` middle value. firebomb_base authors `(.1, 1.5, 3)`
        // — the shell blows out to 1.5x base radius mid-life. The
        // start/end values are the birth/death taper which the
        // renderer's Scale0/Scale1 already approximates. Default 1.0
        // = no scaling.
        public float       GrowMid;
        // Phase 21-SC-SPELL-VISUAL-A — cylinder-specific knobs.
        public float       SpinRate;     // spin(N) — radians/sec around axis
        public float       FadeIn;       // tin(N)  — seconds to ramp alpha 0→1
        public float       FadeOut;      // tout(N) — seconds to ramp alpha 1→0
        public int         Segments;     // segments(N) — ring subdivision
        /// <summary>rp0(start, mid, end) middle value — the dominant radius
        /// value DS1 ships in cylinder profiles. We render a flat ring of
        /// this radius; <see cref="Scale"/> is overloaded as ring outer
        /// radius for the OneShotCylinder dispatch, but RpMid takes
        /// precedence when present so 3-float profiles aren't truncated
        /// by the ApplyParamString radius→Scale shortcut.</summary>
        public float       RpMid;
        // Phase 21-SC-SPELL-VISUAL-B — sray-specific knobs.
        public float       LengthMin;    // lmin
        public float       LengthMax;    // lmax
        public float       WidthStart;   // wsmin..wsmax
        public float       WidthEnd;     // wemin..wemax
        public Vector4     ColorTail;    // color1
        // Phase 21-SC-SPELL-VISUAL-C — fireb-specific knobs.
        public Vector3     VelocityVec;  // velocity(x,y,z) — directional vector
        public bool        HasVelocity;  // velocity() authored (even (0,0,0))
        public Vector3     AccelVec;     // accel(x,y,z)
        public bool        HasAccel;     // accel() authored (even (0,0,0))
        public float       MaxDisplace;  // max_displace
        public float       AlphaFade;    // alphafade — particle lifetime
        public float       LowerRadius;  // lower_r0/r1 average
        public float       UpperRadius;  // upper_r0/r1 average
        public float       FlameSize;    // flamesize
        // Phase 21-SC-SPELL-VFX-MOTION-HANDLE — motion-tracking fields.
        /// <summary>Non-zero when this handle IS a motion source (orbiter,
        /// trackball, lightsource, curve). The id maps into the runtime's
        /// <c>_motionHandles</c> dict; the live <see cref="MotionState"/>
        /// is what advances each tick.</summary>
        public int         MotionId;
        /// <summary>Non-zero when this handle's emitter should FOLLOW a
        /// motion id (set via `sfx target $emitter $motionhandle`). On
        /// `sfx start` the persistent emitter inherits this id and looks
        /// the position up each maintain pass.</summary>
        public int         TargetMotionId;
        // Motion-specific param-string fields (parsed by ApplyParamString,
        // ignored by non-motion modes).
        public float       OrbitPhi;        // iphi(N)   — initial orbital angle
        public float       OrbitTheta;      // itheta(N) — orbital tilt
        public float       OrbitRadius;     // radius(N) for orbiter (overloaded; Scale path stays for non-orbit)
        public float       OrbitRadiusInc;  // radiusi(N) — per-second radius delta
        public float       Velocity;        // velocity(N) — trackball speed

        // ---- Phase 23d-1 — full Appendix-A param capture ---------------
        // Parsed 1:1 from GPG's SiegeFX doc (SU 212 Appendix A, local copy
        // at _ds1refs/su212_siegefx_wikitext.txt). Fields below are the
        // authored values; renderer honoring lands kind-by-kind in 23d-2
        // so each visual change is a controlled golden-trace diff.

        // Global effect parameters.
        public Vector3     OffsetVec;       // offset(x,y,z) — positional offset relative to the model
        public bool        HasOffset;
        public Vector3     DirVec;          // dir(x,y,z) — initial direction relative to the object
        public bool        HasDir;
        public float       DelaySec;        // delay(n) — pause before the effect starts
        public bool        AbsCoords;       // abs() — don't scale effect to target size
        public bool        StaticUpdate;    // sup() — effect never moves
        public bool        UseWind;         // use_wind()
        public bool        WorldOrient;     // world_orient()
        public bool        BoneOrientFlag;  // bone_orient()

        // Explosion.
        public float       FadeStart;       // fade_range(s,e,0) start
        public float       FadeEnd;         //                    end
        public bool        HasFadeRange;
        public float       VMin;            // vmin(f) — min initial particle velocity scalar (doc default 3.0)
        public float       VMax;            // vmax(f) — max initial particle velocity scalar (doc default 6.5)
        public bool        HasVMin, HasVMax;
        public Vector3     IVelVec;         // ivel(x,y,z) — initial velocity vector
        public bool        HasIVel;
        public Vector3     RVelVec;         // rvel(x,y,z) — initial random velocity vector
        public bool        HasRVel;
        public bool        OmniDir;         // omni_dir() / omnidir()
        public bool        CollideFlag;     // collide() — particles bounce off terrain
        public bool        GreyTex;         // grey_tex() — greyscale default texture (default is RED)
        public bool        LineMode;        // line() — spawn along source→target line
        public bool        RandScale;       // rand_scale()
        public float       SpawnOverSec;    // srate — explosion: spawn spread duration; sray: period per ray
        public float       MinTheta, MaxTheta, MinPhi, MaxPhi; // spawn-arc ranges
        public bool        HasThetaRange, HasPhiRange;

        // Fire / Steam.
        public float       MinRadius;       // min_radius — inner spawn radius
        public float       MaxRadius;       // max_radius — outer spawn radius (doc default 4.0)
        public bool        HasMaxRadius;
        public float       MinDisplaceY;    // min_displace / (lightning) mindisplace
        public bool        HasMinDisplace;
        public bool        Instant;         // instant() — full volume immediately
        public Vector3     FctrlVec;        // fctrl(min,max,i) — flame expansion control
        public bool        HasFctrl;
        public int         MinCount;        // min_count(n)

        // Lightning.
        public float       SubdLevel;       // subd(f) — bolt subdivision level (doc default 0.4)
        public float       MinSubd;         // minsubd(f) — minimum subdivision (doc default 2.0)
        public bool        HasSubd, HasMinSubd;

        // Cylinder — rp0/rp1/hp0/hp1 are (start, end, increment) triples:
        // two rings (0 and 1), each with radius profile rpN and height hpN.
        public Vector3     Rp0Triple, Rp1Triple, Hp0Triple, Hp1Triple;
        public bool        HasRp0, HasRp1, HasHp0, HasHp1;
        public float       AlphaStart;      // alpha(f) — starting alpha (cylinder/decal)
        public bool        HasAlpha;
        public Vector3     RotateVec;       // rotate(x,y,z) degrees
        public Vector3     IRotateVec;      // irotate(x,y,z) degrees/increment
        public bool        HasRotate, HasIRotate;

        // SRay — theta/phi/alpha are (start, min-inc, max-inc) /
        // (start, fade-min, fade-max) triples.
        public Vector3     ThetaTriple, PhiTriple, AlphaTriple;
        public bool        HasSrayTheta, HasSrayPhi, HasSrayAlpha;

        // Trackball.
        public float       Afterlife;       // afterlife(f) — seconds to exist after collision
        public float       Kdamp;           // kdamp(f) — damping coefficient 0..1 (doc default 0.45)
        public bool        HasKdamp;
        public float       RvelMin, RvelMax;// rvel_min / rvel_max — random velocity range
        public float       MaxVelocity;     // max_velocity(f) (doc default 80)
        public bool        HasMaxVelocity;
        public float       ThetaStart;      // theta(f) — spiral start angle (trackball/orbiter)
        public bool        HasTheta;
        public bool        Homing;          // homing() — adjusts to hit target
        public bool        DampedFlag;      // damped() — limits turn sharpness
        public bool        Invisible;       // invisible() — motion handle draws nothing
        public bool        NoCollide;       // no_collide()
        public bool        HitTargetOnly;   // hit_target_only()
        public bool        NoCollideObjects;// no_collide_objects()

        // Orbiter.
        public float       PhiStart;        // phi(f) — latitude start
        public bool        HasPhi;
        public float       VDisplace;       // vdisplace(f) — additive vertical displacer
        public bool        FreeOrient;      // free() — own orientation, not target's
        public bool        Smooth;          // smooth() — average recorded positions
        public string?     ModelName;       // model(text) — template of mesh to orbit
        public float       ModelSin, ModelSout, ModelSmax; // model scale in/out/scalar
        public Vector3     RotIncVec;       // rot_inc(x,y,z) — model rotation increments

        // Curve.
        public float       Curvature;       // curvature(f) — higher = less straight
        public float       Spacing;         // spacing(f) — space between particles
        public float       TrailLength;     // tlength(f)
        public float       CurveSize;       // size(f) — single-particle size
        public float       CurveStep;       // step(f) — detail level
        public int         CurveModel;      // model(n) — curve behavior 0/1/2 (numeric form)
        public bool        HasCurvature, HasSpacing, HasTrailLength;

        // Flurry.
        public float       Amplitude;       // amplitude(f) — sinusoidal interference factor
        public float       IAmp;            // iamp(f) — interference amplitude speed
        public bool        HasAmplitude, HasIAmp;

        // SPE — exact parametric model per the doc: per-axis
        // pos = (sin(index0) + sin(index1)) / 2, indices advanced by
        // speed0/speed1, particles offset by space0/space1.
        public Vector3     SpeIndex0, SpeIndex1, SpeSpeed0, SpeSpeed1, SpeSpace0, SpeSpace1;
        public bool        HasSpe;

        // Sphere.
        public int         SphereSides;     // sides(n) (doc default 20)
        public int         SphereSubd;      // subd(n) — tessellation level (doc default 1)
        public bool        SphereLines;     // lines() — draw polygonal lines
        public bool        BackfaceCull;    // cull()

        // PolygonalExplosion.
        public int         PolySides;       // poly_sides(n) (doc default 9)
        public float       Mag;             // mag(f) — explosion magnitude
        public Vector3     RotRangeVec;     // rotrange(x,y,z)
        public Vector3     DisplaceVec;     // displace(x,y,z) — origin displacement range
        public bool        HasMag, HasPolySides;

        // Lightsource.
        public float       InnerRadius;     // iradius(f) — inner falloff radius
        public float       Flicker;         // frequency(f) — flicker frequency
        public bool        HasFlicker;

        // FireB.
        public bool        CVelOff;         // cvel() — turns OFF initial random velocity
        public float       StartRate;       // start_rate(f) — burn ramp-up (doc default 27.5)
        public float       EndRate;         // end_rate(f) — burn ramp-down (doc default 16.5)
        public bool        HasStartRate, HasEndRate;
        public float       VelocityScalar;  // velocity_s(f)

        // Trackball scalar accel(f) — the 3-arg vector form belongs to
        // fire/fireb/steam; trackball ships a single ramp rate.
        public float       AccelScalar;
        public bool        HasAccelScalar;
        // Phase 23-fold F7 — presence flags: an authored ZERO must not
        // collapse into a dispatch default (tin(0) = instant-on, iphi(0)
        // = frozen latitude, radius(0) = point spawn). Plus the full
        // grow_params (start, mid, end) envelope.
        public bool        HasTin, HasTout, HasIPhi, HasITheta, HasVel, HasRadius;
        public float       GrowStart, GrowEnd;
        public bool        HasGrow;
        // Phase 23-fold — monster-corpus knobs.
        public bool        LightSpawn;      // fireb light_spawn()
        public float       LightFreq;       // particles per lightsource (doc 50)
        public float       LightRadius;     // doc 1
        public float       LinePos, LineSpeed;      // fire line() animation
        public bool        HasLineAnim;
        public float       SinPos, SinSpeed, RadiusRMax; // fire sine wobble
        public bool        HasSinAnim;
        public float       Rebound;         // explosion bounce elasticity (doc 0.85)
        public bool        HasRebound;
        public bool        Splat;           // splat() — stick where landing
        public Vector4     Color2;          // splat color
        public bool        HasColor2;
        public float       FadeRate;        // linetracer fade (doc 0.10)
        public bool        HasFadeRate;
        public Vector3     LVel, LVelVar;   // spawn kind linear velocity
        public bool        HasLVel;
        public float       SpawnInterval;   // spawn kind interval (doc 0.5)
        // Phase 23d-2d — charge / sparkles knobs.
        public float       ChargeSpeed;     // charge speed0(f) — random accel factor
        public bool        HasChargeSpeed;
        public float       CenterSize;      // charge centersize(f) (doc default 0.75)
        public float       IAlpha;          // charge ialpha(f) (doc default 4.0)
        public float       PSize;           // sparkles psize(f) (doc default 1.0)
        public float       YVel;            // sparkles yvel(f) (doc default 0)
        // Explosion scale_range(s,e,0) — per-particle random SIZE range
        // (start=min, end=max per SU 212), distinct from the legacy
        // arg-1→Scale shortcut kept above for goldens continuity.
        public float       ScaleRangeMin, ScaleRangeMax;
        public bool        HasScaleRange;
    }

    struct PersistentEmitter
    {
        public EmitterMode Mode;
        public Vector3 Position;
        public Vector4 Color;
        public float   Scale;
        public float   Rate;
        public float   Carry;
        // Phase 21-SC-SPELL-VFX-MOTION-HANDLE — when set, the runtime
        // re-derives `Position` from `_motionHandles[TargetMotionId]`
        // every tick before maintain. When the target motion ends the
        // emitter is dropped on the same pass.
        public int     TargetMotionId;
        // Phase 21-SC-SPELL-VFX-MOTION-HANDLE — when this emitter IS a
        // motion handle (lightsource, trackball with visible trail), the
        // motion id its Position is driven from. Used to clean up the
        // emitter when its own motion expires (`dur` elapsed).
        public int     SelfMotionId;
        public float   AgeSec;        // time since spawn — for `dur` expiry
        public float   Duration;      // 0 = never expire
        // Phase 21-SC-SPELL-VISUAL-E — `$name` of the originating handle when
        // the script bound one with `set $h #POP`. Lets `sfx attach $parent
        // $child` re-target an already-started child by matching this string.
        // null when the operand was anonymous (#POP without a `set $name`).
        public string? SelfName;
        // Phase 23d-2c — authored plume parameters (fire/smoke/steam). When
        // HasSpec the Tick pump routes through MaintainPlume instead of the
        // legacy Maintain* trio.
        public PlumeSpec Spec;
        public bool      HasSpec;
        // Phase 23d-2d — lightsource frequency(f): flicker period in
        // seconds (doc default 0.025). 0 = steady.
        public float     Flicker;
    }

    /// <summary>Phase 21-SC-SPELL-VFX-MOTION-HANDLE — live motion state for
    /// orbiter / trackball / lightsource / curve handles. Advanced once per
    /// <see cref="Tick"/>; <see cref="PersistentEmitter"/>s with a matching
    /// <see cref="PersistentEmitter.TargetMotionId"/> read <c>Position</c>
    /// before their maintain pass so child emitters follow the moving anchor
    /// frame by frame (this is what makes `sfx target $fire $orbiter` work).
    /// </summary>
    struct MotionState
    {
        public int     Id;
        public string  Kind;             // "orbiter" / "trackball" / "lightsource" / "curve"
        /// <summary>The static-frame anchor — orbital center, trackball
        /// start, lightsource emit point, curve start. When
        /// <see cref="ParentMotionId"/> is non-zero, this is re-derived
        /// from the parent's Position each tick before motion advances.
        /// </summary>
        public Vector3 Anchor;
        public Vector3 Target;            // trackball: where to home; curve: end point
        public int     ParentMotionId;    // non-zero = follow another motion handle's Position
        public Vector3 Position;          // computed each tick
        public float   Phi;               // current orbital angle
        public float   Theta;             // current orbital tilt
        public float   Radius;            // current orbital distance
        public float   RadiusInc;         // delta radius per second
        public float   PhiRate;           // delta phi per second (rad/s)
        public float   Speed;             // trackball travel speed
        public float   Duration;          // dur(N) lifetime; 0 = never expire
        public float   Elapsed;           // time since spawn
        public bool    Done;              // true when expired or trackball arrived
        // ---- Phase 23d-2a — SU-212-faithful motion fields ---------------
        public float   ThetaRate;         // orbiter itheta — longitude rate
        public float   VDisplace;         // orbiter vdisplace — Y drift per second
        public float   VOffset;           // accumulated vdisplace
        public float   AccelRate;         // trackball accel(f) — speed ramp/sec (doc default 2.5)
        public float   MaxSpeed;          // trackball max_velocity (doc default 80)
        public float   SpiralRadius;      // trackball radius(f) — spiral interference
        public float   SpiralRate;        // trackball spin(f) — spiral speed
        public float   SpiralAngle;       // current spiral angle (theta start; doc *random)
        public bool    Homing;            // homing() — re-aims at moving target; without it
                                          // the initial aim is locked (kdamp/damped shape the
                                          // turn when a live target moves — wired in-game)
        public bool    DirLocked;
        public Vector3 LockedDir;
        /// <summary>afterlife(f) — trackball lingers at the impact point
        /// for N seconds after collision. <see cref="Arrived"/> is set at
        /// collision (waitfor resumes then); <see cref="Done"/> flips when
        /// the afterlife window closes so child emitters keep pumping at
        /// the impact point exactly as DS1's fireball does.</summary>
        public float   AfterlifeSec;
        public bool    Arrived;
        public float   Curvature;         // curve curvature(f) — control-point lift (doc default 1)
        /// <summary>Phase 23-fold F6 — delay(n) hold: motion doesn't
        /// advance or age until this drains, keeping the handle in step
        /// with its queued visual dispatch.</summary>
        public float   HoldSec;
        // Phase 21-SC-SPELL-VISUAL-G — distinguish "trackball reached its
        // target" (collision) from "Duration expired without arrival"
        // (timeout). The Tick pass needs this to push the right
        // collision_type onto the waiter's stack — pre-G both paths
        // collapsed to object_collision.
        public bool    DoneByCollision;
    }

    sealed class RunningScript
    {
        public SfxProgram Program;
        public SfxContext Ctx;
        public Vector3    Origin => Ctx.TargetPos;
        public IReadOnlyList<string>? CallerArgs;
        public int        Ip;
        public float      PauseRemaining;
        public bool       Done;
        public Stack<Handle>           Stack         = new();
        public Dictionary<string, Handle> NamedHandles = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Vars         = new(StringComparer.OrdinalIgnoreCase);

        // Phase 21-SC-SPELL-VISUAL-G — coroutine wait state. When > 0,
        // StepUntilYield won't advance the IP; Tick decrements WaitTimeout
        // and checks the watched motion handle's Done flag each frame.
        // Resume condition: motion is Done (collision) or timeout reached
        // (no collision). Push of #OBJECT_COLLISION / #TERRAIN_COLLISION
        // / #NO_COLLISION happens at resume.
        public int   WaitMotionId;
        public float WaitTimeout;
        // Phase 21-SC-SPELL-VISUAL-G fold — set by AdvanceMotionHandles
        // when the watched motion went Done, BEFORE the prune step
        // wipes the handle entry. Consumed at the next coroutine pass:
        // we push a tagged Handle and clear the pending state. Empty
        // string ("" or null) means no pending resolution; a non-empty
        // tag triggers the resume.
        public string? PendingCollisionTag;
        public Vector3 PendingCollisionPos;
        // Phase 21-SC-SPELL-VISUAL-G — outcome of the most recent IfBegin
        // so an immediately-following ElseBegin can pick the opposite
        // branch. Nested if/else on the same RunningScript is fine
        // because the inner if/else fully resolves between the outer
        // IfBegin and its matching IfEnd — by the time control returns
        // to the outer level, the inner result has already been consumed.
        // Note: the default value (false) means a stray `else { … }`
        // without a preceding `if { … }` runs unconditionally — that
        // matches the pre-G "always run both branches" pragmatism for
        // un-paired authoring but is technically undefined in DS1's
        // grammar. No shipped script trips it.
        public bool  LastIfTaken;

        public RunningScript(SfxProgram prog, in SfxContext ctx, IReadOnlyList<string>? args)
        {
            Program    = prog;
            Ctx        = ctx;
            CallerArgs = args;
        }
    }
}
