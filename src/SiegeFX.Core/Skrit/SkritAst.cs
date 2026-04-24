namespace SiegeFX.Core.Skrit;

/// <summary>Root AST node types for Skrit. Every node carries a (Line, Column) pair from
/// its opening token so downstream stages (binder, VM diagnostics) can point into the
/// source. Records are immutable; the parser builds them in a single pass.</summary>
public abstract record SkritNode(int Line, int Column);

// ----- script + top level ---------------------------------------------------------------

public sealed record SkritScript(IReadOnlyList<SkritTopLevel> TopLevels)
    : SkritNode(1, 1);

public abstract record SkritTopLevel(int Line, int Column) : SkritNode(Line, Column);

/// <summary>`#include "x.skrit"` or `#only( game )`. Captured verbatim; the binder decides
/// whether to act on include-resolution. Parser doesn't splice includes.</summary>
public sealed record SkritPreprocessorDecl(string Directive, int Line, int Column)
    : SkritTopLevel(Line, Column);

/// <summary>`property <type> <name>$ [= <init>] [doc = "..."];`</summary>
public sealed record SkritPropertyDecl(
    string Type, string Name, SkritExpr? Initializer, string? Doc,
    int Line, int Column) : SkritTopLevel(Line, Column);

/// <summary>Top-level `owner = <Ident>;` declaration. Binds the script's `owner` symbol to
/// a host component type (Aspect, GoSkritComponent, etc.).</summary>
public sealed record SkritOwnerDecl(string Owner, int Line, int Column)
    : SkritTopLevel(Line, Column);

/// <summary>Top-level bare field: `int m_BG$;` or `float time$ = 0;`.</summary>
public sealed record SkritFieldDecl(
    string Type, string Name, SkritExpr? Initializer, int Line, int Column)
    : SkritTopLevel(Line, Column);

/// <summary>Top-level function: `[returnType] Name$(params) { body }`. Return type is null
/// when the declaration has no leading type keyword (e.g. `UpdateDuration$(float dt$) { ... }`).
/// </summary>
public sealed record SkritFunctionDecl(
    string? ReturnType, string Name, IReadOnlyList<SkritParam> Params, SkritBlock Body,
    int Line, int Column) : SkritTopLevel(Line, Column);

/// <summary>`[startup] state Name$ [trigger On...(...)] { state-members }`. When a trigger
/// header is present the body is a bare statement list (the trigger body) rather than a
/// list of state members; the parser normalises that by storing it in <see cref="HeaderTriggerBody"/>
/// and leaving <see cref="Body"/> empty.</summary>
public sealed record SkritStateDecl(
    bool IsStartup, string Name, SkritTriggerHeader? HeaderTrigger,
    IReadOnlyList<SkritStateMember> Body, SkritBlock? HeaderTriggerBody,
    int Line, int Column) : SkritTopLevel(Line, Column);

/// <summary>Top-level `event OnX$([params]) { body }` — seen in shipped auto-skrits
/// (jipper, log, mp, rotatex) as floating handlers outside any state.</summary>
public sealed record SkritTopLevelEvent(
    string Name, IReadOnlyList<SkritParam> Params, SkritBlock Body,
    int Line, int Column) : SkritTopLevel(Line, Column);

/// <summary>Top-level `trigger OnX$([args]) { body }` — rare but legal at script scope.</summary>
public sealed record SkritTopLevelTrigger(
    string Name, IReadOnlyList<SkritExpr> Args, SkritBlock Body,
    int Line, int Column) : SkritTopLevel(Line, Column);

public sealed record SkritParam(string Type, string Name);

/// <summary>Inline trigger on a state header; the body is flattened into the state's
/// startup/entry behavior by the binder. When only args are given (no body), the parser
/// still records this so the binder can wire the receiver.</summary>
public sealed record SkritTriggerHeader(
    string EventName, IReadOnlyList<SkritExpr> Args, SkritBlock? Body);

// ----- state members --------------------------------------------------------------------

public abstract record SkritStateMember(int Line, int Column) : SkritNode(Line, Column);

public sealed record SkritStateField(
    string Type, string Name, SkritExpr? Initializer, int Line, int Column)
    : SkritStateMember(Line, Column);

public sealed record SkritEventHandler(
    string Name, IReadOnlyList<SkritParam> Params, SkritBlock Body, int Line, int Column)
    : SkritStateMember(Line, Column);

public sealed record SkritTriggerHandler(
    string Name, IReadOnlyList<SkritExpr> Args, SkritBlock Body, int Line, int Column)
    : SkritStateMember(Line, Column);

/// <summary>`transition -> StateName$ : EventName$(args);` — declarative state edge.</summary>
public sealed record SkritTransition(
    string TargetState, string EventName, IReadOnlyList<SkritExpr> EventArgs,
    int Line, int Column) : SkritStateMember(Line, Column);

/// <summary>`ChoreName$ at ( count frames|seconds ) { body }` — delayed coroutine.</summary>
public sealed record SkritScheduledBlock(
    string Name, SkritExpr CountExpr, string Unit, SkritBlock Body,
    int Line, int Column) : SkritStateMember(Line, Column);

// ----- statements -----------------------------------------------------------------------

public abstract record SkritStmt(int Line, int Column) : SkritNode(Line, Column);

public sealed record SkritBlock(
    IReadOnlyList<SkritStmt> Statements, int Line, int Column) : SkritStmt(Line, Column);

public sealed record SkritIfStmt(
    SkritExpr Cond, SkritStmt Then, SkritStmt? Else, int Line, int Column)
    : SkritStmt(Line, Column);

public sealed record SkritWhileStmt(
    SkritExpr Cond, SkritStmt Body, int Line, int Column) : SkritStmt(Line, Column);

public sealed record SkritForStmt(
    SkritStmt? Init, SkritExpr? Cond, SkritExpr? Update, SkritStmt Body,
    int Line, int Column) : SkritStmt(Line, Column);

public sealed record SkritReturnStmt(
    SkritExpr? Value, int Line, int Column) : SkritStmt(Line, Column);

public sealed record SkritSetStateStmt(
    string StateName, int Line, int Column) : SkritStmt(Line, Column);

public sealed record SkritLocalDecl(
    string Type, string Name, SkritExpr? Initializer, int Line, int Column)
    : SkritStmt(Line, Column);

public sealed record SkritExprStmt(
    SkritExpr Expr, int Line, int Column) : SkritStmt(Line, Column);

// ----- expressions ----------------------------------------------------------------------

public abstract record SkritExpr(int Line, int Column) : SkritNode(Line, Column);

public sealed record SkritIntLit(long Value, int Line, int Column) : SkritExpr(Line, Column);
public sealed record SkritFloatLit(double Value, int Line, int Column) : SkritExpr(Line, Column);
public sealed record SkritStringLit(string Value, int Line, int Column) : SkritExpr(Line, Column);
public sealed record SkritFourCharLit(uint Packed, string Text, int Line, int Column)
    : SkritExpr(Line, Column);
public sealed record SkritBoolLit(bool Value, int Line, int Column) : SkritExpr(Line, Column);
public sealed record SkritNullLit(int Line, int Column) : SkritExpr(Line, Column);

public sealed record SkritIdentExpr(string Name, int Line, int Column) : SkritExpr(Line, Column);
public sealed record SkritMemberExpr(
    SkritExpr Target, string Member, int Line, int Column) : SkritExpr(Line, Column);
public sealed record SkritCallExpr(
    SkritExpr Callee, IReadOnlyList<SkritExpr> Args, int Line, int Column)
    : SkritExpr(Line, Column);

public sealed record SkritUnaryExpr(
    string Op, SkritExpr Operand, int Line, int Column) : SkritExpr(Line, Column);
public sealed record SkritBinaryExpr(
    string Op, SkritExpr Left, SkritExpr Right, int Line, int Column)
    : SkritExpr(Line, Column);
public sealed record SkritAssignExpr(
    string Op, SkritExpr Target, SkritExpr Value, int Line, int Column)
    : SkritExpr(Line, Column);
public sealed record SkritTernaryExpr(
    SkritExpr Cond, SkritExpr Then, SkritExpr Else, int Line, int Column)
    : SkritExpr(Line, Column);
