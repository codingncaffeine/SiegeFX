using System;
using System.Collections.Generic;

namespace SiegeFX.Core.Skrit;

/// <summary>Compiles a bound <see cref="SkritScript"/> into a <see cref="SkritProgram"/> of
/// stack-based bytecode chunks. One chunk per top-level function, top-level event/trigger,
/// state entry handler, state-level event/trigger handler, scheduled block, and transition
/// body. Chunks are keyed by a canonical path (e.g. <c>State$/event/OnEnterState$</c>)
/// so the runtime can find them by state + event name.
///
/// The compiler consults the bind result to pick opcodes: a bare identifier becomes
/// <c>LoadLocal</c> (if in scope), <c>LoadGlobal</c> (if a property/field), <c>LoadExtern</c>
/// (otherwise). State names are resolved at compile time for <c>SetState</c>.</summary>
public sealed class SkritCompiler
{
    readonly SkritScript _script;
    readonly SkritBindResult _bind;

    public SkritCompiler(SkritScript script, SkritBindResult bind)
    {
        _script = script;
        _bind = bind;
    }

    public SkritProgram Compile()
    {
        var chunks = new List<SkritCodeChunk>();
        var byName = new Dictionary<string, SkritCodeChunk>(StringComparer.Ordinal);

        void Emit(SkritCodeChunk c)
        {
            chunks.Add(c);
            byName[c.Name] = c; // later duplicates shadow earlier; shipped scripts sometimes have two same-named handlers on different states, so we prefix names with the state path.
        }

        // Synthesise an @__init__ chunk that assigns property/field initialisers into
        // globals. The VM runs it once on construction so globals hold their declared
        // default values before any event / handler fires.
        var initStmts = new List<SkritStmt>();
        foreach (var top in _script.TopLevels)
        {
            switch (top)
            {
                case SkritPropertyDecl p when p.Initializer is not null:
                    initStmts.Add(new SkritExprStmt(
                        new SkritAssignExpr("=",
                            new SkritIdentExpr(p.Name, p.Line, p.Column),
                            p.Initializer, p.Line, p.Column),
                        p.Line, p.Column));
                    break;
                case SkritFieldDecl f when f.Initializer is not null:
                    initStmts.Add(new SkritExprStmt(
                        new SkritAssignExpr("=",
                            new SkritIdentExpr(f.Name, f.Line, f.Column),
                            f.Initializer, f.Line, f.Column),
                        f.Line, f.Column));
                    break;
            }
        }
        if (initStmts.Count > 0)
            Emit(CompileFunction("@__init__", Array.Empty<SkritParam>(),
                new SkritBlock(initStmts, 1, 1)));

        foreach (var top in _script.TopLevels)
        {
            switch (top)
            {
                case SkritFunctionDecl fn:
                    Emit(CompileFunction($"@{fn.Name}", fn.Params, fn.Body));
                    break;
                case SkritTopLevelEvent ev:
                    Emit(CompileFunction($"@event/{ev.Name}", ev.Params, ev.Body));
                    break;
                case SkritTopLevelTrigger tg:
                    Emit(CompileFunction($"@trigger/{tg.Name}", Array.Empty<SkritParam>(), tg.Body));
                    break;
                case SkritStateDecl s:
                    CompileState(s, Emit);
                    break;
            }
        }

        return new SkritProgram
        {
            Script = _script,
            Globals = _bind.Globals,
            States = _bind.States,
            Externs = _bind.Externs,
            Chunks = chunks,
            ChunksByName = byName,
        };
    }

    void CompileState(SkritStateDecl s, Action<SkritCodeChunk> emit)
    {
        var prefix = s.Name;

        if (s.HeaderTrigger is not null && s.HeaderTrigger.Body is not null)
            emit(CompileFunction($"{prefix}/trigger/{s.HeaderTrigger.EventName}", Array.Empty<SkritParam>(), s.HeaderTrigger.Body));
        if (s.HeaderTriggerBody is not null)
            emit(CompileFunction($"{prefix}/header", Array.Empty<SkritParam>(), s.HeaderTriggerBody));

        foreach (var m in s.Body)
        {
            switch (m)
            {
                case SkritEventHandler eh:
                    emit(CompileFunction($"{prefix}/event/{eh.Name}", eh.Params, eh.Body));
                    break;
                case SkritTriggerHandler th:
                    emit(CompileFunction($"{prefix}/trigger/{th.Name}", Array.Empty<SkritParam>(), th.Body));
                    break;
                case SkritScheduledBlock sb:
                    emit(CompileFunction($"{prefix}/sched/{sb.Name}", Array.Empty<SkritParam>(), sb.Body));
                    break;
                case SkritTransition tr when tr.Body is not null:
                    // Edge-fire bodies compile into their own chunk. Multiple transitions
                    // can target the same state, so the event name disambiguates them —
                    // `OnBotHandleMessage$` + line-number tag keeps names unique when the
                    // same (target, event) pair appears twice in a state.
                    emit(CompileFunction(
                        $"{prefix}/trans/{tr.TargetState}/{tr.EventName}@{tr.Line}",
                        Array.Empty<SkritParam>(), tr.Body));
                    break;
                // State fields have no bytecode (initialisers are handled elsewhere).
                // Transitions without bodies are declarative only — no chunk to emit.
            }
        }
    }

    SkritCodeChunk CompileFunction(string name, IReadOnlyList<SkritParam> ps, SkritBlock body)
    {
        var emitter = new FunctionEmitter(_bind);
        foreach (var p in ps)
            if (!string.IsNullOrEmpty(p.Name))
                emitter.DeclareLocal(p.Name);
        int paramCount = emitter.LocalCount;
        emitter.EmitBlockBody(body);
        emitter.EmitOp(SkritOpcode.ReturnVoid);

        return new SkritCodeChunk
        {
            Name = name,
            Bytecode = emitter.FinalizeBytecode(),
            IntConstants = emitter.IntConstants,
            FloatConstants = emitter.FloatConstants,
            StringConstants = emitter.StringConstants,
            Names = emitter.Names,
            LocalCount = emitter.LocalCount,
            ParamCount = paramCount,
        };
    }

    // ---------- nested emitter -----------------------------------------------------

    sealed class FunctionEmitter
    {
        readonly SkritBindResult _bind;
        readonly List<byte> _bc = new(256);
        readonly List<long> _ints = new();
        readonly List<double> _floats = new();
        readonly List<string> _strings = new();
        readonly List<string> _names = new();
        readonly Dictionary<long, int> _intIdx = new();
        readonly Dictionary<double, int> _floatIdx = new();
        readonly Dictionary<string, int> _stringIdx = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _nameIdx = new(StringComparer.Ordinal);

        // Scope stack of local-slot maps. Slots are function-wide but names re-bind per block.
        readonly Stack<Dictionary<string, int>> _scopes = new();
        public int LocalCount { get; private set; }

        public IReadOnlyList<long>   IntConstants    => _ints;
        public IReadOnlyList<double> FloatConstants  => _floats;
        public IReadOnlyList<string> StringConstants => _strings;
        public IReadOnlyList<string> Names           => _names;

        public FunctionEmitter(SkritBindResult bind)
        {
            _bind = bind;
            _scopes.Push(new Dictionary<string, int>(StringComparer.Ordinal));
        }

        public byte[] FinalizeBytecode() => _bc.ToArray();

        // ----- constant/name interning
        int AddInt(long v) { if (_intIdx.TryGetValue(v, out var i)) return i; _ints.Add(v); _intIdx[v] = _ints.Count - 1; return _ints.Count - 1; }
        int AddFloat(double v) { if (_floatIdx.TryGetValue(v, out var i)) return i; _floats.Add(v); _floatIdx[v] = _floats.Count - 1; return _floats.Count - 1; }
        int AddString(string v) { if (_stringIdx.TryGetValue(v, out var i)) return i; _strings.Add(v); _stringIdx[v] = _strings.Count - 1; return _strings.Count - 1; }
        int AddName(string v) { if (_nameIdx.TryGetValue(v, out var i)) return i; _names.Add(v); _nameIdx[v] = _names.Count - 1; return _names.Count - 1; }

        // ----- emit helpers
        public void EmitOp(SkritOpcode op) => _bc.Add((byte)op);
        void EmitU16(int v) { _bc.Add((byte)(v & 0xFF)); _bc.Add((byte)((v >> 8) & 0xFF)); }
        void EmitU8(int v) => _bc.Add((byte)(v & 0xFF));
        int EmitJump(SkritOpcode op) { EmitOp(op); EmitU16(0); return _bc.Count - 2; }
        void PatchJump(int operandPos)
        {
            int from = operandPos + 2; // ip after operand
            int to = _bc.Count;
            int delta = to - from;
            if (delta < short.MinValue || delta > short.MaxValue)
                throw new InvalidOperationException("jump offset out of range");
            _bc[operandPos] = (byte)(delta & 0xFF);
            _bc[operandPos + 1] = (byte)((delta >> 8) & 0xFF);
        }
        int Here() => _bc.Count;
        void EmitJumpTo(SkritOpcode op, int targetIp)
        {
            EmitOp(op);
            int from = _bc.Count + 2;
            int delta = targetIp - from;
            if (delta < short.MinValue || delta > short.MaxValue)
                throw new InvalidOperationException("jump offset out of range");
            EmitU16(delta & 0xFFFF);
        }

        // ----- scope
        public int DeclareLocal(string name)
        {
            var top = _scopes.Peek();
            if (top.TryGetValue(name, out var existing)) return existing;
            int slot = LocalCount++;
            top[name] = slot;
            return slot;
        }
        bool TryFindLocal(string name, out int slot)
        {
            foreach (var s in _scopes)
                if (s.TryGetValue(name, out slot)) return true;
            slot = 0; return false;
        }
        void PushScope() => _scopes.Push(new Dictionary<string, int>(StringComparer.Ordinal));
        void PopScope() => _scopes.Pop();

        // ----- blocks / statements

        public void EmitBlockBody(SkritBlock b)
        {
            PushScope();
            foreach (var s in b.Statements) EmitStmt(s);
            PopScope();
        }

        void EmitStmt(SkritStmt s)
        {
            switch (s)
            {
                case SkritBlock b: EmitBlockBody(b); break;
                case SkritIfStmt i: EmitIf(i); break;
                case SkritWhileStmt w: EmitWhile(w); break;
                case SkritForStmt f: EmitFor(f); break;
                case SkritReturnStmt r:
                    if (r.Value is not null) { EmitExpr(r.Value); EmitOp(SkritOpcode.Return); }
                    else EmitOp(SkritOpcode.ReturnVoid);
                    break;
                case SkritSetStateStmt ss:
                    EmitOp(SkritOpcode.SetState); EmitU16(AddName(ss.StateName));
                    break;
                case SkritLocalDecl ld:
                    int slot = DeclareLocal(ld.Name);
                    if (ld.Initializer is not null)
                    {
                        EmitExpr(ld.Initializer);
                        EmitOp(SkritOpcode.StoreLocal); EmitU16(slot);
                    }
                    break;
                case SkritExprStmt es:
                    EmitExpr(es.Expr);
                    // Pop the expression result that's now on the stack (unless it was an
                    // assignment, which leaves nothing — we handle that inside EmitExpr).
                    if (es.Expr is not SkritAssignExpr) EmitOp(SkritOpcode.Pop);
                    break;
            }
        }

        void EmitIf(SkritIfStmt i)
        {
            EmitExpr(i.Cond);
            int jfalse = EmitJump(SkritOpcode.JumpIfFalse);
            EmitStmt(i.Then);
            if (i.Else is not null)
            {
                int jend = EmitJump(SkritOpcode.Jump);
                PatchJump(jfalse);
                EmitStmt(i.Else);
                PatchJump(jend);
            }
            else
            {
                PatchJump(jfalse);
            }
        }

        void EmitWhile(SkritWhileStmt w)
        {
            int top = Here();
            EmitExpr(w.Cond);
            int jfalse = EmitJump(SkritOpcode.JumpIfFalse);
            EmitStmt(w.Body);
            EmitJumpTo(SkritOpcode.Jump, top);
            PatchJump(jfalse);
        }

        void EmitFor(SkritForStmt f)
        {
            PushScope();
            if (f.Init is not null) EmitStmt(f.Init);
            int top = Here();
            int jfalse = -1;
            if (f.Cond is not null)
            {
                EmitExpr(f.Cond);
                jfalse = EmitJump(SkritOpcode.JumpIfFalse);
            }
            EmitStmt(f.Body);
            if (f.Update is not null) { EmitExpr(f.Update); EmitOp(SkritOpcode.Pop); }
            EmitJumpTo(SkritOpcode.Jump, top);
            if (jfalse >= 0) PatchJump(jfalse);
            PopScope();
        }

        // ----- expressions (leaves exactly one value on the stack, EXCEPT assignments which leave zero)

        void EmitExpr(SkritExpr e)
        {
            switch (e)
            {
                case SkritIntLit i:    EmitOp(SkritOpcode.PushInt);    EmitU16(AddInt(i.Value)); break;
                case SkritFloatLit f:  EmitOp(SkritOpcode.PushFloat);  EmitU16(AddFloat(f.Value)); break;
                case SkritStringLit s: EmitOp(SkritOpcode.PushString); EmitU16(AddString(s.Value)); break;
                case SkritFourCharLit fc: EmitOp(SkritOpcode.PushInt); EmitU16(AddInt(fc.Packed)); break;
                case SkritBoolLit b:   EmitOp(b.Value ? SkritOpcode.PushTrue : SkritOpcode.PushFalse); break;
                case SkritNullLit:     EmitOp(SkritOpcode.PushNull); break;
                case SkritIdentExpr id: EmitLoadIdent(id.Name); break;
                case SkritMemberExpr m:
                    EmitExpr(m.Target);
                    EmitOp(SkritOpcode.LoadMember); EmitU16(AddName(m.Member));
                    break;
                case SkritCallExpr c: EmitCall(c); break;
                case SkritUnaryExpr u: EmitUnary(u); break;
                case SkritBinaryExpr b: EmitBinary(b); break;
                case SkritAssignExpr a: EmitAssign(a); break;
                case SkritTernaryExpr t: EmitTernary(t); break;
                default: throw new InvalidOperationException($"unhandled expr: {e.GetType().Name}");
            }
        }

        void EmitLoadIdent(string name)
        {
            if (TryFindLocal(name, out var slot))
            {
                EmitOp(SkritOpcode.LoadLocal); EmitU16(slot); return;
            }
            if (_bind.Globals.ContainsKey(name))
            {
                EmitOp(SkritOpcode.LoadGlobal); EmitU16(AddName(name)); return;
            }
            EmitOp(SkritOpcode.LoadExtern); EmitU16(AddName(name));
        }

        void EmitStoreIdent(string name)
        {
            if (TryFindLocal(name, out var slot))
            {
                EmitOp(SkritOpcode.StoreLocal); EmitU16(slot); return;
            }
            if (_bind.Globals.ContainsKey(name))
            {
                EmitOp(SkritOpcode.StoreGlobal); EmitU16(AddName(name)); return;
            }
            EmitOp(SkritOpcode.StoreExtern); EmitU16(AddName(name));
        }

        void EmitCall(SkritCallExpr c)
        {
            if (c.Args.Count > 255) throw new InvalidOperationException("too many args");

            // Receiver.Method(args) → CallMember
            if (c.Callee is SkritMemberExpr mem)
            {
                EmitExpr(mem.Target);
                foreach (var a in c.Args) EmitExpr(a);
                EmitOp(SkritOpcode.CallMember);
                EmitU16(AddName(mem.Member));
                EmitU8(c.Args.Count);
                return;
            }
            // Name(args) — free-call (script function or host free-call)
            if (c.Callee is SkritIdentExpr id)
            {
                foreach (var a in c.Args) EmitExpr(a);
                EmitOp(SkritOpcode.Call);
                EmitU16(AddName(id.Name));
                EmitU8(c.Args.Count);
                return;
            }
            throw new InvalidOperationException("unsupported callee form");
        }

        void EmitUnary(SkritUnaryExpr u)
        {
            EmitExpr(u.Operand);
            switch (u.Op)
            {
                case "-": EmitOp(SkritOpcode.Neg); break;
                case "!": EmitOp(SkritOpcode.Not); break;
                case "~": EmitOp(SkritOpcode.BitNot); break;
                case "+": break; // no-op
                default: throw new InvalidOperationException($"unknown unary op: {u.Op}");
            }
        }

        void EmitBinary(SkritBinaryExpr b)
        {
            // Short-circuit && and || — evaluate left, test, skip right if decided.
            if (b.Op == "&&")
            {
                EmitExpr(b.Left);
                EmitOp(SkritOpcode.Dup);
                int jshort = EmitJump(SkritOpcode.JumpIfFalse);
                EmitOp(SkritOpcode.Pop);
                EmitExpr(b.Right);
                PatchJump(jshort);
                return;
            }
            if (b.Op == "||")
            {
                EmitExpr(b.Left);
                EmitOp(SkritOpcode.Dup);
                int jshort = EmitJump(SkritOpcode.JumpIfTrue);
                EmitOp(SkritOpcode.Pop);
                EmitExpr(b.Right);
                PatchJump(jshort);
                return;
            }

            EmitExpr(b.Left);
            EmitExpr(b.Right);
            EmitOp(b.Op switch
            {
                "+" => SkritOpcode.Add,
                "-" => SkritOpcode.Sub,
                "*" => SkritOpcode.Mul,
                "/" => SkritOpcode.Div,
                "%" => SkritOpcode.Mod,
                "**" => SkritOpcode.Pow,
                "==" => SkritOpcode.Eq,
                "!=" => SkritOpcode.NotEq,
                "~=" => SkritOpcode.TildeEq,
                "<" => SkritOpcode.Lt,
                "<=" => SkritOpcode.LtEq,
                ">" => SkritOpcode.Gt,
                ">=" => SkritOpcode.GtEq,
                "&" => SkritOpcode.BitAnd,
                "|" => SkritOpcode.BitOr,
                "^" => SkritOpcode.BitXor,
                "<<" => SkritOpcode.Shl,
                ">>" => SkritOpcode.Shr,
                _ => throw new InvalidOperationException($"unknown binary op: {b.Op}"),
            });
        }

        void EmitAssign(SkritAssignExpr a)
        {
            // Compound assignments: expand to load-op-store.
            if (a.Op != "=")
            {
                string binOp = a.Op switch
                {
                    "+=" => "+", "-=" => "-", "*=" => "*", "/=" => "/",
                    "|=" => "|", "&=" => "&", "^=" => "^",
                    _ => throw new InvalidOperationException($"unknown compound assign: {a.Op}"),
                };
                var synthetic = new SkritBinaryExpr(binOp, a.Target, a.Value, a.Line, a.Column);
                EmitStoreTarget(a.Target, synthetic);
                return;
            }
            EmitStoreTarget(a.Target, a.Value);
        }

        void EmitStoreTarget(SkritExpr target, SkritExpr value)
        {
            switch (target)
            {
                case SkritIdentExpr id:
                    EmitExpr(value);
                    EmitStoreIdent(id.Name);
                    break;
                case SkritMemberExpr m:
                    EmitExpr(m.Target);
                    EmitExpr(value);
                    EmitOp(SkritOpcode.StoreMember);
                    EmitU16(AddName(m.Member));
                    break;
                default:
                    throw new InvalidOperationException("assignment target must be identifier or member");
            }
        }

        void EmitTernary(SkritTernaryExpr t)
        {
            EmitExpr(t.Cond);
            int jfalse = EmitJump(SkritOpcode.JumpIfFalse);
            EmitExpr(t.Then);
            int jend = EmitJump(SkritOpcode.Jump);
            PatchJump(jfalse);
            EmitExpr(t.Else);
            PatchJump(jend);
        }
    }
}
