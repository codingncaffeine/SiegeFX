using System.Globalization;
using System.Numerics;
using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Sfx;

/// <summary>Phase 17-SC-F-2 — minimal stack-based interpreter for the
/// <c>script=[[ ... ]]</c> DSL parsed by <see cref="SfxScriptCompiler"/>.
///
/// The runtime owns two state buckets:
/// <list type="bullet">
///   <item><b>Persistent emitters</b> — one entry per <c>sfx create … sfx start</c>
///   pair whose script ended without a matching <c>finish/destroy</c>. Each tick
///   we call <see cref="IParticleSink.MaintainFire"/> / <c>MaintainSmoke</c> /
///   <c>MaintainSteam</c> at the emitter's anchored world position. This is the
///   fh_r1 farmhouse fire / smoke columns / waterfall froth case.</item>
///   <item><b>Coroutine scripts</b> — script bodies with <c>pause N</c> are
///   stepped statement-by-statement on each tick, yielding when they hit a pause
///   budget; one-shot spawns (sparks, lightning, brief fire bursts) live and die
///   inside the particle backend.</item>
/// </list>
///
/// Verbs the VM doesn't recognize (orbiter / sphere / charge / lightsource /
/// fireb / explosion / spawn / waitfor / get / worldmsg) log once and continue
/// — same Phase 17-SC-F policy as the parser's <see cref="StatementKind.Raw"/>
/// fallthrough. The stand-in keeps every region's emitters running rather than
/// freezing on the first un-modeled verb.</summary>
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

    /// <summary>Look up <paramref name="scriptName"/> in the store and start it
    /// at <paramref name="origin"/>. <paramref name="callerArgs"/> fills the
    /// <c>[N]</c> param-substitution slots referenced from the script body
    /// (DS1's authoring pattern: an emitter template stuffs its tunable
    /// payload into the call site, the script body slots it via
    /// <c>"[0]"</c>). Returns false if the script isn't in the store.</summary>
    public bool Spawn(string scriptName, Vector3 origin, IReadOnlyList<string>? callerArgs = null)
    {
        if (!_store.TryGet(scriptName, out var script)) return false;

        // Compile lazily; SfxProgram is non-mutating so a future cache here is
        // free, but the price-of-freshness is negligible (~1ms for the longest
        // shipped body) and keeps the data path simple.
        var prog = SfxScriptCompiler.Compile(scriptName, script.Body);
        var rs = new RunningScript(prog, origin, callerArgs);
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
                case StatementKind.SfxTarget:
                case StatementKind.SfxAttach:
                case StatementKind.SfxAttachPoint:
                case StatementKind.SfxPositionAt:
                case StatementKind.SfxOffset:
                case StatementKind.SfxRat:
                case StatementKind.SfxFriendlyTarget:
                case StatementKind.Raw:
                    LogUnhandledOnce(stmt.Verb);
                    break;
            }
        }
        rs.Done = true;
    }

    void ExecCreate(RunningScript rs, SfxStatement stmt)
    {
        // tokens[0] = kind (fire / smoke / steam / lightning / sphere / …)
        if (stmt.Tokens.Count == 0) return;
        var kind = stmt.Tokens[0].ToLowerInvariant();

        // ParamString may carry [N] back-references to caller args. Substitute
        // before keyword extraction so flamesize([0]) etc. resolves cleanly.
        var raw = SubstituteCallerArgs(stmt.ParamString, rs.CallerArgs);

        var handle = new Handle
        {
            Kind     = kind,
            Position = rs.Origin,
            Color    = DefaultColor(kind),
            Scale    = 0.6f,
            Rate     = 18f,
            Mode     = MapMode(kind, raw),
        };
        ApplyParamString(ref handle, raw);
        rs.Stack.Push(handle);
    }

    void ExecStart(RunningScript rs, SfxStatement stmt)
    {
        // sfx start <handle-token>. We support #POP only — every shipped
        // script in /effects/* uses the pop pattern; #PEEK / $var indexing
        // is rare and lands as a no-op (the create still pushed; dropping
        // start just means the emitter never fires that frame).
        if (rs.Stack.Count == 0) return;
        var h = rs.Stack.Pop();
        if (h.Mode == EmitterMode.OneShotLightning)
        {
            _particles.SpawnLightning(h.Position, h.Position + new Vector3(0f, 1f, 0f), h.Color, 0.35f);
            return;
        }
        if (h.Mode == EmitterMode.Unsupported) return;
        _emitters.Add(new PersistentEmitter
        {
            Mode     = h.Mode,
            Position = h.Position,
            Color    = h.Color,
            Scale    = h.Scale,
            Rate     = h.Rate,
        });
    }

    void ExecFinish(RunningScript rs, SfxStatement stmt)
    {
        // No handle-id mapping yet — finish/destroy with #POP just discards
        // the top of stack so subsequent starts don't resolve to it.
        if (rs.Stack.Count > 0) rs.Stack.Pop();
    }

    void ExecSet(RunningScript rs, SfxStatement stmt)
    {
        // set $name = expr — minimal: store the right-hand verbatim. The VM
        // doesn't yet evaluate expressions (no shipped emitter relies on it).
        if (stmt.Tokens.Count >= 3 && stmt.Tokens[1] == "=")
        {
            rs.Vars[stmt.Tokens[0]] = stmt.Tokens[2];
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
        var prog = SfxScriptCompiler.Compile(name, script.Body);
        var sub = new RunningScript(prog, rs.Origin, rs.CallerArgs);
        StepUntilYield(sub);
        if (!sub.Done) _scripts.Add(sub);
    }

    // ---- param string parser ------------------------------------------

    static EmitterMode MapMode(string kind, string raw)
    {
        // The 'fire' kind is overloaded: with texture(b_sfx_smoke) or dark()
        // it acts as a smoke column, otherwise as flame. 'steam' rides through
        // the steam path (waterfall froth). 'lightning' is a one-shot bolt.
        bool hasSmoke = ContainsKeyword(raw, "texture") &&
                        raw.IndexOf("b_sfx_smoke", StringComparison.OrdinalIgnoreCase) >= 0;
        bool isDark   = ContainsKeyword(raw, "dark");
        switch (kind)
        {
            case "fire":      return (hasSmoke || isDark) ? EmitterMode.Smoke : EmitterMode.Fire;
            case "smoke":     return EmitterMode.Smoke;
            case "steam":     return EmitterMode.Steam;
            case "lightning": return EmitterMode.OneShotLightning;
            default:          return EmitterMode.Unsupported;
        }
    }

    static Vector4 DefaultColor(string kind) => kind switch
    {
        "fire"      => new Vector4(1.00f, 0.55f, 0.20f, 1f),
        "smoke"     => new Vector4(0.35f, 0.35f, 0.38f, 0.55f),
        "steam"     => new Vector4(0.85f, 0.92f, 1.00f, 0.65f),
        "lightning" => new Vector4(0.7f, 0.85f, 1.0f, 1f),
        _           => new Vector4(1f, 1f, 1f, 1f),
    };

    static void ApplyParamString(ref Handle h, string raw)
    {
        if (string.IsNullOrEmpty(raw)) return;

        // flamesize / radius / max_radius — scale knob. DS1 ships values from
        // ~0.15 (drips) up to ~3.0 (waterfall) so the raw float maps fine.
        if (TryReadFloat(raw, "flamesize",  out var fs))   h.Scale = MathF.Max(0.05f, fs);
        else if (TryReadFloat(raw, "radius", out var rad)) h.Scale = MathF.Max(0.05f, rad);
        else if (TryReadFloat(raw, "max_radius", out var mr)) h.Scale = MathF.Max(0.05f, mr);

        // ts(N) — DS1's "time scale" / particle lifetime. We turn it into a
        // particles-per-second budget: shorter ts = denser stream.
        if (TryReadFloat(raw, "ts", out var ts) && ts > 0.001f)
            h.Rate = Math.Clamp(20f / ts, 4f, 120f);

        // color0(R,G,B[,A]) — start tint. The renderer alpha-fades to color1,
        // but we currently ignore color1; using start-tint gets us the right
        // base reading.
        if (TryReadVec4(raw, "color0", out var c0)) h.Color = c0;
        else if (TryReadVec4(raw, "color", out var c)) h.Color = c;

        // dark() — modifier that shifts the whole tint into smoke-grey range.
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
        // Match "keyword(" so substrings like "bedark" don't false-positive
        // against "dark". A left-side word boundary keeps us off "max_radius"
        // when we're scanning for "radius".
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

    static bool TryReadFloat(string raw, string keyword, out float value)
    {
        value = 0f;
        var args = ExtractArgs(raw, keyword);
        if (args is null || args.Length == 0) return false;
        return float.TryParse(args[0].TrimEnd('f', 'F'),
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
        // Locate `keyword(...)` occurrences and split the payload by commas.
        // Word-boundary check on the left edge so "radius" doesn't match the
        // tail of "max_radius".
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

    enum EmitterMode { Fire, Smoke, Steam, OneShotLightning, Unsupported }

    struct Handle
    {
        public string      Kind;
        public Vector3     Position;
        public Vector4     Color;
        public float       Scale;
        public float       Rate;
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
        public Vector3    Origin;
        public IReadOnlyList<string>? CallerArgs;
        public int        Ip;
        public float      PauseRemaining;
        public bool       Done;
        public Stack<Handle>           Stack = new();
        public Dictionary<string, string> Vars = new(StringComparer.OrdinalIgnoreCase);

        public RunningScript(SfxProgram prog, Vector3 origin, IReadOnlyList<string>? args)
        {
            Program    = prog;
            Origin     = origin;
            CallerArgs = args;
        }
    }
}
