using System.Collections.Generic;

namespace SiegeFX.Core.Skrit;

/// <summary>Two-pass binder over a parsed <see cref="SkritScript"/>. Pass 1 gathers globals
/// and state declarations; pass 2 walks each function / handler / state-member body with a
/// scope stack, resolving identifiers and validating state-transition targets. Anything the
/// binder can't resolve locally becomes an <c>Extern</c> — the catalogue of host-API
/// identifiers that Phase 8c's VM wires to the runtime.
///
/// The binder is deliberately forgiving: shipped DS1 skrits reference host symbols the
/// binder has no manifest for (Report, WorldState, MSG_*, WS_*). Unknown roots don't
/// error — they just get listed. Only *structural* mistakes (duplicate state, unknown
/// transition target) raise diagnostics.</summary>
public sealed class SkritBinder
{
    readonly SkritScript _script;
    readonly Dictionary<string, SkritSymbol> _globals = new(System.StringComparer.Ordinal);
    readonly Dictionary<string, SkritSymbol> _states = new(System.StringComparer.Ordinal);
    readonly HashSet<string> _externs = new(System.StringComparer.Ordinal);
    readonly List<SkritDiagnostic> _diags = new();

    // Identifiers that are always resolved, never flagged as extern. Skrit is case-
    // insensitive for keywords and predefined names (shipped code uses both `owner` and
    // `Owner`, `NULL` and `null`), so lookup ignores case here too.
    static readonly HashSet<string> PredefinedRoots = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "owner", "self", "this", "NULL",
    };

    public SkritBinder(SkritScript script) => _script = script;

    public SkritBindResult Bind()
    {
        CollectGlobals();
        foreach (var top in _script.TopLevels)
            WalkTopLevel(top);

        var externsList = new List<string>(_externs);
        externsList.Sort(System.StringComparer.Ordinal);
        return new SkritBindResult
        {
            Script = _script,
            Globals = _globals,
            States = _states,
            Externs = externsList,
            Diagnostics = _diags,
        };
    }

    // ---- Pass 1: gather top-level declarations ----------------------------------------

    void CollectGlobals()
    {
        foreach (var top in _script.TopLevels)
        {
            switch (top)
            {
                case SkritPropertyDecl p:
                    AddGlobal(new SkritSymbol(p.Name, SkritSymbolKind.Property, p.Type, p), p.Line, p.Column);
                    break;
                case SkritFieldDecl f:
                    AddGlobal(new SkritSymbol(f.Name, SkritSymbolKind.Field, f.Type, f), f.Line, f.Column);
                    break;
                case SkritFunctionDecl fn:
                    AddGlobal(new SkritSymbol(fn.Name, SkritSymbolKind.Function, fn.ReturnType, fn), fn.Line, fn.Column);
                    break;
                case SkritOwnerDecl o:
                    AddGlobal(new SkritSymbol("owner", SkritSymbolKind.Owner, o.Owner, o), o.Line, o.Column);
                    break;
                case SkritStateDecl s:
                    AddState(s);
                    break;
                case SkritTopLevelEvent ev:
                    AddGlobal(new SkritSymbol(ev.Name, SkritSymbolKind.EventHandler, null, ev), ev.Line, ev.Column);
                    break;
                case SkritTopLevelTrigger tg:
                    AddGlobal(new SkritSymbol(tg.Name, SkritSymbolKind.TriggerHandler, null, tg), tg.Line, tg.Column);
                    break;
                // Preprocessor directives and splice markers are ignored.
            }
        }
    }

    void AddGlobal(SkritSymbol sym, int line, int col)
    {
        if (_globals.ContainsKey(sym.Name))
        {
            _diags.Add(new SkritDiagnostic(
                $"duplicate global declaration '{sym.Name}'", line, col));
            return;
        }
        _globals[sym.Name] = sym;
    }

    void AddState(SkritStateDecl s)
    {
        if (_states.ContainsKey(s.Name))
        {
            _diags.Add(new SkritDiagnostic(
                $"duplicate state '{s.Name}'", s.Line, s.Column));
            return;
        }
        _states[s.Name] = new SkritSymbol(s.Name, SkritSymbolKind.State, null, s);
    }

    // ---- Pass 2: walk bodies with a scope stack --------------------------------------

    readonly Stack<Dictionary<string, SkritSymbol>> _scopes = new();

    void PushScope() => _scopes.Push(new Dictionary<string, SkritSymbol>(System.StringComparer.Ordinal));
    void PopScope() => _scopes.Pop();

    void DeclareLocal(string name, string? type, SkritSymbolKind kind, SkritNode decl, int line, int col)
    {
        // Shipped skrits comment out param names with /* ... */, producing empty-name params
        // (e.g. `event OnStartChore$( int /*subanim$*/, int /*flags$*/ )`). Accept silently —
        // they're positional slots the caller passes through but the script doesn't bind.
        if (string.IsNullOrEmpty(name)) return;
        var top = _scopes.Peek();
        if (top.ContainsKey(name))
        {
            _diags.Add(new SkritDiagnostic(
                $"duplicate local '{name}' in this scope", line, col));
            return;
        }
        top[name] = new SkritSymbol(name, kind, type, decl);
    }

    bool TryResolve(string name, out SkritSymbol sym)
    {
        foreach (var scope in _scopes)
        {
            if (scope.TryGetValue(name, out var found)) { sym = found; return true; }
        }
        if (_globals.TryGetValue(name, out sym!)) return true;
        if (_states.TryGetValue(name, out sym!)) return true;
        sym = null!;
        return false;
    }

    void WalkTopLevel(SkritTopLevel top)
    {
        switch (top)
        {
            case SkritPropertyDecl p:
                if (p.Initializer is not null)
                {
                    PushScope(); WalkExpr(p.Initializer); PopScope();
                }
                break;
            case SkritFieldDecl f:
                if (f.Initializer is not null)
                {
                    PushScope(); WalkExpr(f.Initializer); PopScope();
                }
                break;
            case SkritFunctionDecl fn:
                WalkFunction(fn);
                break;
            case SkritStateDecl s:
                WalkState(s);
                break;
            case SkritTopLevelEvent ev:
                WalkEventHandler(ev.Params, ev.Body);
                break;
            case SkritTopLevelTrigger tg:
                PushScope();
                foreach (var arg in tg.Args) WalkExpr(arg);
                WalkBlock(tg.Body);
                PopScope();
                break;
        }
    }

    void WalkFunction(SkritFunctionDecl fn)
    {
        PushScope();
        foreach (var p in fn.Params)
            DeclareLocal(p.Name, p.Type, SkritSymbolKind.Parameter, fn, fn.Line, fn.Column);
        WalkBlock(fn.Body);
        PopScope();
    }

    void WalkEventHandler(IReadOnlyList<SkritParam> ps, SkritBlock body)
    {
        PushScope();
        foreach (var p in ps)
            DeclareLocal(p.Name, p.Type, SkritSymbolKind.Parameter, body, body.Line, body.Column);
        WalkBlock(body);
        PopScope();
    }

    void WalkState(SkritStateDecl s)
    {
        // State-scope: collect state-level fields as symbols visible to handlers/scheduled blocks.
        PushScope();
        foreach (var m in s.Body)
        {
            if (m is SkritStateField sf)
                DeclareLocal(sf.Name, sf.Type, SkritSymbolKind.StateField, sf, sf.Line, sf.Column);
        }

        // Header trigger (if any): walk args + body under the state scope.
        if (s.HeaderTrigger is not null)
        {
            PushScope();
            foreach (var arg in s.HeaderTrigger.Args) WalkExpr(arg);
            if (s.HeaderTrigger.Body is not null) WalkBlock(s.HeaderTrigger.Body);
            PopScope();
        }
        if (s.HeaderTriggerBody is not null) WalkBlock(s.HeaderTriggerBody);

        foreach (var m in s.Body) WalkStateMember(m);
        PopScope();
    }

    void WalkStateMember(SkritStateMember m)
    {
        switch (m)
        {
            case SkritStateField sf:
                if (sf.Initializer is not null)
                {
                    PushScope(); WalkExpr(sf.Initializer); PopScope();
                }
                break;
            case SkritEventHandler eh:
                WalkEventHandler(eh.Params, eh.Body);
                break;
            case SkritTriggerHandler th:
                PushScope();
                foreach (var a in th.Args) WalkExpr(a);
                WalkBlock(th.Body);
                PopScope();
                break;
            case SkritTransition tr:
                CheckStateTarget(tr.TargetState, tr.Line, tr.Column);
                PushScope();
                foreach (var a in tr.EventArgs) WalkExpr(a);
                if (tr.Body is not null) WalkBlock(tr.Body);
                PopScope();
                break;
            case SkritScheduledBlock sb:
                PushScope();
                WalkExpr(sb.CountExpr);
                WalkBlock(sb.Body);
                PopScope();
                break;
        }
    }

    void CheckStateTarget(string name, int line, int col)
    {
        if (!_states.ContainsKey(name))
            _diags.Add(new SkritDiagnostic(
                $"unknown state '{name}' in transition target", line, col));
    }

    // ---- Statements / expressions -----------------------------------------------------

    void WalkBlock(SkritBlock b)
    {
        PushScope();
        foreach (var s in b.Statements) WalkStmt(s);
        PopScope();
    }

    void WalkStmt(SkritStmt s)
    {
        switch (s)
        {
            case SkritBlock b: WalkBlock(b); break;
            case SkritIfStmt i:
                WalkExpr(i.Cond);
                WalkStmt(i.Then);
                if (i.Else is not null) WalkStmt(i.Else);
                break;
            case SkritWhileStmt w:
                WalkExpr(w.Cond);
                WalkStmt(w.Body);
                break;
            case SkritForStmt f:
                PushScope();
                if (f.Init is not null) WalkStmt(f.Init);
                if (f.Cond is not null) WalkExpr(f.Cond);
                if (f.Update is not null) WalkExpr(f.Update);
                WalkStmt(f.Body);
                PopScope();
                break;
            case SkritReturnStmt r:
                if (r.Value is not null) WalkExpr(r.Value);
                break;
            case SkritSetStateStmt ss:
                CheckStateTarget(ss.StateName, ss.Line, ss.Column);
                break;
            case SkritLocalDecl ld:
                if (ld.Initializer is not null) WalkExpr(ld.Initializer);
                DeclareLocal(ld.Name, ld.Type, SkritSymbolKind.Local, ld, ld.Line, ld.Column);
                break;
            case SkritExprStmt es:
                WalkExpr(es.Expr);
                break;
        }
    }

    void WalkExpr(SkritExpr e)
    {
        switch (e)
        {
            case SkritIdentExpr id:
                ResolveRoot(id.Name);
                break;
            case SkritMemberExpr m:
                WalkExpr(m.Target);
                break;
            case SkritCallExpr c:
                WalkExpr(c.Callee);
                foreach (var a in c.Args) WalkExpr(a);
                break;
            case SkritUnaryExpr u:
                WalkExpr(u.Operand);
                break;
            case SkritBinaryExpr b:
                WalkExpr(b.Left);
                WalkExpr(b.Right);
                break;
            case SkritAssignExpr a:
                WalkExpr(a.Target);
                WalkExpr(a.Value);
                break;
            case SkritTernaryExpr t:
                WalkExpr(t.Cond);
                WalkExpr(t.Then);
                WalkExpr(t.Else);
                break;
            // Literals: nothing to resolve.
        }
    }

    void ResolveRoot(string name)
    {
        if (PredefinedRoots.Contains(name)) return;
        if (TryResolve(name, out _)) return;
        _externs.Add(name);
    }
}
