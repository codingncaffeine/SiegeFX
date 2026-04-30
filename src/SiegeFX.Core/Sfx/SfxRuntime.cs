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

    public int LivePersistentCount => _emitters.Count;
    public int LiveCoroutineCount  => _scripts.Count;
    public IReadOnlyCollection<string> UnhandledVerbs => _unhandledVerbsLogged;

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

        // Run continuous spawn budgets for every persistent emitter.
        for (int i = 0; i < _emitters.Count; i++)
        {
            var e = _emitters[i];
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
            case EmitterMode.Unsupported:
                return;
            default:
                _emitters.Add(new PersistentEmitter
                {
                    Mode     = h.Mode,
                    Position = h.Anchor,
                    Color    = h.Color,
                    Scale    = h.Scale,
                    Rate     = h.Rate,
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
        // <where> is `source` / `target` / `#SOURCE` / `#TARGET` etc.
        if (stmt.Tokens.Count < 2) return;
        if (!TryResolveHandleOperand(rs, stmt.Tokens[0], pop: false, out var h))
            return;
        h.OtherEnd = ResolveAnchor(stmt.Tokens[1], rs.Ctx);
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
    }

    struct PersistentEmitter
    {
        public EmitterMode Mode;
        public Vector3 Position;
        public Vector4 Color;
        public float   Scale;
        public float   Rate;
        public float   Carry;
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
