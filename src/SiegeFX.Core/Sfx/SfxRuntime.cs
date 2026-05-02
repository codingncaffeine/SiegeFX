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
///   <c>MaintainSteam</c> at the emitter's anchored world position. fh_r1's
///   farmhouse fire / smoke columns / waterfall froth.</item>
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
/// <para>Verbs the VM still doesn't recognize (orbiter / sphere / charge /
/// lightsource / fireb / spawn / waitfor / get / worldmsg) log once and
/// continue — same Phase 17-SC-F policy as the parser's
/// <see cref="StatementKind.Raw"/> fallthrough. Keeps every region's
/// emitters running rather than freezing on the first un-modeled verb.</para></summary>
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
            StepUntilYield(rs);
            if (rs.Done) _scripts.RemoveAt(i);
        }

        // Phase 21-SC-SPELL-VFX-MOTION-HANDLE — advance live motion
        // handles before the emitter maintain pass so emitters that
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
                if (!_motionHandles.TryGetValue(e.TargetMotionId, out var motion) || motion.Done)
                {
                    _emitters.RemoveAt(i);
                    continue;
                }
                e.Position = motion.Position;
            }
            // Self-duration expiry (DS1's `dur(N)` on a lightsource etc.).
            if (e.Duration > 0f)
            {
                e.AgeSec += dt;
                if (e.AgeSec >= e.Duration)
                {
                    _emitters.RemoveAt(i);
                    continue;
                }
            }
            switch (e.Mode)
            {
                case EmitterMode.Fire:
                    e.Carry = _particles.MaintainFire(e.Position, e.Color, e.Scale, dt, e.Rate, e.Carry);
                    break;
                case EmitterMode.Smoke:
                    e.Carry = _particles.MaintainSmoke(e.Position, e.Color, e.Scale, dt, e.Rate, e.Carry);
                    break;
                case EmitterMode.Steam:
                    e.Carry = _particles.MaintainSteam(e.Position, e.Color, e.Scale, dt, e.Rate, e.Carry);
                    break;
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
                    // Phi/Theta tick + radius growth. Position = anchor +
                    // unit-circle * radius. Theta tilts the circle out of
                    // the XZ plane (DS1's flame_blades has small theta);
                    // when itheta is 0 we keep the circle horizontal.
                    m.Phi    += m.PhiRate * dt;
                    m.Radius += m.RadiusInc * dt;
                    {
                        float r = m.Radius;
                        float cx = MathF.Cos(m.Phi) * r;
                        float cz = MathF.Sin(m.Phi) * r;
                        // Tilt: rotate the (cx, 0, cz) vector around the X
                        // axis by Theta so the orbit can be inclined.
                        float cy = MathF.Sin(m.Theta) * r * 0.25f;
                        m.Position = m.Anchor + new Vector3(cx, cy, cz);
                    }
                    break;

                case "trackball":
                    // Homing toward Target at fixed Speed. Done when within
                    // 0.5u of Target — collision-aware impact gating
                    // (waitfor) is a future SC slice; for now the trackball
                    // simply expires on arrival and child emitters get
                    // dropped via the Done flag.
                    {
                        var to = m.Target - m.Position;
                        float distSq = to.LengthSquared();
                        if (distSq <= 0.25f)
                        {
                            m.Position = m.Target;
                            m.Done = true;
                        }
                        else
                        {
                            float step = m.Speed * dt;
                            float dist = MathF.Sqrt(distSq);
                            m.Position += to * (MathF.Min(step, dist) / dist);
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
                    // Quadratic Bezier from Anchor → Target with a control
                    // point above the midpoint (default arc). t advances
                    // along Duration. When t >= 1, Done.
                    {
                        if (m.Duration <= 0.001f) m.Duration = 1.0f;
                        float t = MathF.Min(1f, m.Elapsed / m.Duration);
                        var mid = (m.Anchor + m.Target) * 0.5f + new Vector3(0f, 1.0f, 0f);
                        float u = 1f - t;
                        m.Position = (u * u) * m.Anchor + (2f * u * t) * mid + (t * t) * m.Target;
                        if (t >= 1f) m.Done = true;
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
                    if (TryParseFloat(stmt.Tokens, 0, out var sec) && sec > 0f)
                    {
                        rs.PauseRemaining = sec;
                        return; // yield
                    }
                    break;
                case StatementKind.Call:
                    ExecCall(rs, stmt);
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
                case StatementKind.SfxOffset:
                case StatementKind.SfxRat:
                case StatementKind.Raw:
                    LogUnhandledOnce(stmt.Verb);
                    // Some of these (sfx attach $h #POP) reference the stack —
                    // best-effort consume so subsequent #POPs target the right
                    // slot. Cheap and forgiving; the worst case is a stale
                    // handle reference no one looks at.
                    ConsumeStackOperand(rs, stmt.Tokens);
                    break;
            }
        }
        rs.Done = true;
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

        // ParamString may carry [N] back-references to caller args. Substitute
        // before keyword extraction so flamesize([0]) etc. resolves cleanly.
        var raw = SubstituteCallerArgs(stmt.ParamString, rs.CallerArgs);

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
            var motion = new MotionState
            {
                Id           = id,
                Kind         = kind,
                Anchor       = anchor,
                Target       = rs.Ctx.TargetPos,
                Position     = anchor,
                Phi          = handle.OrbitPhi,
                Theta        = handle.OrbitTheta,
                Radius       = handle.OrbitRadius,
                RadiusInc    = handle.OrbitRadiusInc,
                // Default rotation rate for orbiter — DS1 doesn't author
                // a per-spell phi-rate field; visual sweep covers tuning.
                PhiRate      = handle.Mode == EmitterMode.MotionOrbiter ? 4.0f : 0f,
                Speed        = handle.Velocity > 0f ? handle.Velocity : 8.0f,
                Duration     = handle.Duration > 0.1f ? handle.Duration : 0f,
                Elapsed      = 0f,
                ParentMotionId = 0,
            };
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

        switch (h.Mode)
        {
            case EmitterMode.OneShotLightning:
            {
                // Phase 21-SC-SPELL-VFX-2 — render the actual hand-to-target
                // beam. OtherEnd was set by `sfx target $bolt source` /
                // `sfx attach_point $bolt @weapon_bone source`; if neither
                // ran (script never re-anchored) it stays equal to Anchor
                // and SpawnLightning collapses to a zero-length flash.
                _particles.SpawnLightning(h.OtherEnd, h.Anchor, h.Color, h.Duration, h.Displace);
                break;
            }
            case EmitterMode.OneShotExplosion:
            case EmitterMode.OneShotSparkles:
            {
                // DS1's `sfx create explosion #TARGET_KB "..."` describes a
                // burst of textured sparkles around the target. We approximate
                // with a one-shot SpawnSpark (count + color from the param
                // string) — no textured atlas yet, but the colored sparkle
                // pop reads correctly at gameplay distance.
                int count = h.BurstCount > 0 ? h.BurstCount : 16;
                _particles.SpawnSpark(h.Anchor, h.Color,
                    MathF.Max(0.20f, h.Scale * 1.2f),
                    MathF.Max(0.30f, h.Duration),
                    count);
                break;
            }
            case EmitterMode.OneShotFlurry:
            {
                // Phase 21-SC-SPELL-VFX-3f — `flurry` is a popcorn-style
                // particle burst with `count`/`dur`/`color0`/`grow_params`/
                // `tin`/`tout` params. First-pass: SpawnSpark sized by the
                // grow_params middle (peak scale). Doesn't yet animate the
                // grow curve — visible test list will flag if it's needed.
                int count = h.BurstCount > 0 ? h.BurstCount : 30;
                _particles.SpawnSpark(h.Anchor, h.Color,
                    MathF.Max(0.30f, h.Scale * 1.5f),
                    MathF.Max(0.30f, h.Duration),
                    count);
                break;
            }
            case EmitterMode.Fireb:
            {
                // Phase 21-SC-SPELL-VFX-3g — `fireb` is a directional fire
                // emitter (dragon_fire's flame cones, spell_flame's burst).
                // First-pass: SpawnFire one-shot at the anchor, sized by
                // flamesize, with count from the param string. Real DS1
                // shape (forward-emitting cone with velocity/accel) needs
                // a directional-emitter primitive — flagged as a tweak
                // candidate.
                int count = h.BurstCount > 0 ? h.BurstCount : 30;
                _particles.SpawnFire(h.Anchor, h.Color,
                    MathF.Max(0.40f, h.Scale * 1.3f),
                    MathF.Max(0.50f, h.Duration),
                    count);
                break;
            }
            case EmitterMode.OneShotCylinder:
            {
                // Phase 21-SC-SPELL-VFX-3i — `cylinder` is a textured beam
                // between hp0 and hp1 (head positions) with rp0/rp1 radii,
                // tin/tout fade, dur, color0, spin, segments. First-pass:
                // straight beam (displace=0) via SpawnLightning between the
                // anchor and OtherEnd. The "tube" effect is approximated by
                // the lightning bolt rendering. Tube-textured rendering
                // would need a new primitive — listed in the test sweep as
                // "expect a colored beam, not necessarily a tube."
                _particles.SpawnLightning(h.OtherEnd, h.Anchor, h.Color,
                    MathF.Max(0.20f, h.Duration),
                    displace: 0f);
                break;
            }
            case EmitterMode.OneShotSray:
            {
                // Phase 21-SC-SPELL-VFX-3h — `sray` is a directional ray
                // (sun ray, death blast streamer) with theta/phi/lmin/lmax
                // and offset params. First-pass: dense SpawnSpark burst at
                // the anchor sized by `radius`. Directional bias (the actual
                // ray shape) needs a streak/billboard primitive — tweak list.
                int count = h.BurstCount > 0 ? h.BurstCount : 32;
                _particles.SpawnSpark(h.Anchor, h.Color,
                    MathF.Max(0.25f, h.Scale * 1.4f),
                    MathF.Max(0.25f, h.Duration),
                    count);
                break;
            }
            case EmitterMode.Unsupported:
                return;
            case EmitterMode.MotionOrbiter:
            case EmitterMode.MotionCurve:
                // Pure motion handles — no visible emitter of their own.
                // Their MotionState lives in _motionHandles (allocated at
                // ExecCreate) and child emitters that targeted them via
                // `sfx target $emitter $orbiter` follow Position each tick.
                // Nothing to register here; Tick advances the motion.
                break;
            case EmitterMode.MotionTrackball:
            case EmitterMode.LightSource:
            {
                // Visible motion handles — render a continuous glow at the
                // motion's live position. SelfMotionId binds the emitter to
                // its own motion; TargetMotionId is the lookup key the Tick
                // pump uses to refresh Position. Trackball uses Fire (warm
                // streak); LightSource uses Steam (color-preserving glow).
                var glowMode = h.Mode == EmitterMode.MotionTrackball
                    ? EmitterMode.Fire
                    : EmitterMode.Steam;
                _emitters.Add(new PersistentEmitter
                {
                    Mode     = glowMode,
                    Position = h.Anchor,
                    Color    = h.Color,
                    Scale    = MathF.Max(0.30f, h.Scale * 0.80f),
                    Rate     = h.Mode == EmitterMode.MotionTrackball ? 60f : 30f,
                    TargetMotionId = h.MotionId,
                    SelfMotionId   = h.MotionId,
                    Duration = h.Duration > 0.10f ? h.Duration : 0f,
                });
                break;
            }
            default:
                _emitters.Add(new PersistentEmitter
                {
                    Mode     = h.Mode,
                    Position = h.Anchor,
                    Color    = h.Color,
                    Scale    = h.Scale,
                    Rate     = h.Rate,
                    TargetMotionId = h.TargetMotionId,
                });
                break;
        }
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
            h.OtherEnd = named.Anchor;     // useful initial value for one-shots
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
        // We don't have skeletal bone resolution yet; treat the trailing
        // source/target word as an override of the bolt's far-end. For DS1's
        // shipped zap that's `... @weapon_bone source`, which lands the bolt
        // at the caster's hand-area (we pass WeaponBonePos in SfxContext for
        // exactly this case). Phase 21+ will replace the static position with
        // a live skeletal lookup.
        if (stmt.Tokens.Count < 3) return;
        if (!TryResolveHandleOperand(rs, stmt.Tokens[0], pop: false, out var h))
            return;

        // Trailing token: source / target. Bone token (tokens[1]) is parsed
        // when we wire skeletal resolution; for now we always use WeaponBonePos
        // when the trailing word is `source` and TargetPos when `target`.
        var trailing = stmt.Tokens[stmt.Tokens.Count - 1];
        if (trailing.StartsWith("source", StringComparison.OrdinalIgnoreCase))
            h.OtherEnd = rs.Ctx.WeaponBonePos;
        else if (trailing.StartsWith("target", StringComparison.OrdinalIgnoreCase))
            h.OtherEnd = rs.Ctx.TargetPos;

        StoreMutatedHandle(rs, stmt.Tokens[0], h);
    }

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
                rs.NamedHandles[name] = h;
            return;
        }
        // Shape B: `set $name = expr` (verbatim string capture; the VM
        // doesn't yet evaluate expressions but stores them so future
        // diagnostics can replay the script).
        if (stmt.Tokens.Count >= 3 && stmt.Tokens[1] == "=")
            rs.Vars[name] = stmt.Tokens[2];
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
        var prog = SfxScriptCompiler.Compile(name, script.Body);
        var sub = new RunningScript(prog, rs.Ctx, rs.CallerArgs);
        StepUntilYield(sub);
        if (!sub.Done) _scripts.Add(sub);
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
        bool hasSmoke = ContainsKeyword(raw, "texture") &&
                        raw.IndexOf("b_sfx_smoke", StringComparison.OrdinalIgnoreCase) >= 0;
        bool isDark   = ContainsKeyword(raw, "dark");
        switch (kind)
        {
            case "fire":      return (hasSmoke || isDark) ? EmitterMode.Smoke : EmitterMode.Fire;
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
            case "orbiter":     return EmitterMode.MotionOrbiter;
            case "trackball":   return EmitterMode.MotionTrackball;
            case "lightsource": return EmitterMode.LightSource;
            case "curve":       return EmitterMode.MotionCurve;
            default:          return EmitterMode.Unsupported;
        }
    }

    static Vector4 DefaultColor(string kind) => kind switch
    {
        "fire"      => new Vector4(1.00f, 0.55f, 0.20f, 1f),
        "smoke"     => new Vector4(0.35f, 0.35f, 0.38f, 0.55f),
        "steam"     => new Vector4(0.85f, 0.92f, 1.00f, 0.65f),
        "lightning" => new Vector4(0.7f, 0.85f, 1.0f, 1f),
        "explosion" => new Vector4(1f, 0.9f, 0.5f, 1f),
        "sparkles"  => new Vector4(0.9f, 0.9f, 1f, 1f),
        _           => new Vector4(1f, 1f, 1f, 1f),
    };

    static void ApplyParamString(ref Handle h, string raw)
    {
        if (string.IsNullOrEmpty(raw)) return;

        // Scale knobs — different param names per effect type, all map to
        // our single Scale field.
        if (TryReadFloat(raw, "flamesize",  out var fs))   h.Scale = MathF.Max(0.05f, fs);
        else if (TryReadFloat(raw, "radius", out var rad)) h.Scale = MathF.Max(0.05f, rad);
        else if (TryReadFloat(raw, "max_radius", out var mr)) h.Scale = MathF.Max(0.05f, mr);
        // explosion uses scale_range(min,base,max) — pull the middle.
        if (TryReadFloat(raw, "scale_range", out var sr, argIndex: 1)) h.Scale = MathF.Max(0.05f, sr);
        // lightning uses scale(N) for line thickness.
        if (TryReadFloat(raw, "scale", out var sc)) h.Scale = MathF.Max(0.05f, sc);

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
    }

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

    static bool ContainsKeyword(string raw, string keyword)
    {
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
        OneShotCylinder,  // 3i — straight beam between hp0/hp1, ≈SpawnLightning displace=0
        OneShotSray,      // 3h — directional ray, ≈SpawnSpark dense + slight bias
        // Phase 21-SC-SPELL-VFX-MOTION-HANDLE — motion handles that other
        // emitters can target via `sfx target $emitter $motion`. Each gets
        // a slot in _motionHandles whose Position advances every tick;
        // PersistentEmitters with TargetMotionId>0 follow that position
        // each maintain pass.
        MotionOrbiter,    // orbital motion anchor — invisible, drives child emitters
        MotionTrackball,  // homing projectile — visible glow trail + drives child emitters
        LightSource,      // persistent glow billboard at position
        MotionCurve,      // splined-path motion handle — invisible
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

        public RunningScript(SfxProgram prog, in SfxContext ctx, IReadOnlyList<string>? args)
        {
            Program    = prog;
            Ctx        = ctx;
            CallerArgs = args;
        }
    }
}
