namespace SiegeFX.Core.Sfx;

/// <summary>The kind of "thing" each statement does. Phase 17-SC-F's
/// minimal interpreter handles every kind below; the rest of DS1's verbs
/// (waitfor, get, worldmsg, conditional logic, math expression rhs) get
/// folded in as later splinters call for them. Unknown verbs surface as
/// <see cref="StatementKind.Raw"/> so the VM can log + continue without
/// dropping the whole script.</summary>
public enum StatementKind
{
    /// <summary>set $name = value (or #POP)</summary>
    Set,

    /// <summary>sfx create &lt;kind&gt; &lt;target-token&gt; "param-string"</summary>
    SfxCreate,

    /// <summary>sfx start &lt;handle-token&gt; (#POP, #PEEK, $var, $var [N])</summary>
    SfxStart,

    /// <summary>sfx destroy &lt;handle-token&gt;</summary>
    SfxDestroy,

    /// <summary>sfx finish &lt;handle-token&gt;</summary>
    SfxFinish,

    /// <summary>sfx target &lt;handle-token&gt; &lt;target-token&gt;</summary>
    SfxTarget,

    /// <summary>sfx attach &lt;parent-token&gt; &lt;child-token&gt;</summary>
    SfxAttach,

    /// <summary>sfx attach_point &lt;handle-token&gt; &lt;bone&gt; &lt;target/source&gt;</summary>
    SfxAttachPoint,

    /// <summary>sfx position_at &lt;handle-token&gt; &lt;@bone&gt; &lt;target/source&gt;</summary>
    SfxPositionAt,

    /// <summary>sfx offset &lt;handle-token&gt; v&lt;x y z&gt; &lt;source/target&gt;</summary>
    SfxOffset,

    /// <summary>sfx rat &lt;handle-token&gt; — random angle theta (rotation jitter)</summary>
    SfxRat,

    /// <summary>sfx direction &lt;handle-token&gt; &lt;where&gt; — aim vector toward
    /// the resolved position (Phase 21-SC-SPELL-VISUAL-E).</summary>
    SfxDirection,

    /// <summary>sfx friendly target &lt;handle&gt;</summary>
    SfxFriendlyTarget,

    /// <summary>sound play &lt;name&gt; [loop|at #POS dist N M]</summary>
    SoundPlay,

    /// <summary>sound stop &lt;handle&gt;</summary>
    SoundStop,

    /// <summary>pause &lt;seconds&gt;</summary>
    Pause,

    /// <summary>call &lt;script-name&gt; [&lt;arg-list&gt;]</summary>
    Call,

    /// <summary>Phase 21-SC-SPELL-VISUAL-G — coroutine gate. <c>waitfor
    /// collision &lt;handle&gt; &lt;timeout&gt;</c> suspends the script until
    /// the named handle's motion finishes (collision) or the timeout
    /// elapses; pushes <c>#OBJECT_COLLISION</c>, <c>#TERRAIN_COLLISION</c>,
    /// or <c>#NO_COLLISION</c> onto the stack on resume. <c>waitfor sig
    /// &lt;name&gt; &lt;timeout&gt;</c> waits on a named signal — currently
    /// resolved to NO_COLLISION on timeout (no signal bus yet).</summary>
    Waitfor,

    /// <summary>Phase 21-SC-SPELL-VISUAL-G — query verbs. <c>get
    /// target_position &lt;handle&gt; [source|target]</c> pushes the
    /// handle's anchor or target as a position handle. <c>get collision
    /// point &lt;handle&gt;</c> pushes the live impact point. <c>get
    /// collision direction &lt;handle&gt;</c> pushes a unit direction.</summary>
    Get,

    /// <summary>Phase 21-SC-SPELL-VISUAL-G — runtime conditional begin.
    /// Tokens carry the parenthesized condition expression.</summary>
    IfBegin,

    /// <summary>Phase 21-SC-SPELL-VISUAL-G — closes the matching
    /// <see cref="IfBegin"/> body. Optional <see cref="ElseBegin"/> may
    /// immediately follow.</summary>
    IfEnd,

    /// <summary>Phase 21-SC-SPELL-VISUAL-G — opens an else body that pairs
    /// with the most-recently-closed <see cref="IfEnd"/>.</summary>
    ElseBegin,

    /// <summary>Phase 21-SC-SPELL-VISUAL-G — closes the matching
    /// <see cref="ElseBegin"/> body.</summary>
    ElseEnd,

    /// <summary>Unrecognized verb captured as raw tokens. The VM logs
    /// once and continues. Lets us land SC-F without freezing every
    /// script body that uses `if`, `waitfor`, `get`, `worldmsg`.</summary>
    Raw,
}

/// <summary>Single statement in a parsed sfx script. <see cref="Tokens"/>
/// is the raw token sequence (verb stripped) so the VM can match shapes
/// per kind without re-parsing strings. <see cref="ParamString"/> is the
/// quoted "param-string" payload for create-style verbs (post param
/// substitution); the parser extracts it from the token stream so the VM
/// just hands it to the host.</summary>
public sealed class SfxStatement
{
    public StatementKind Kind { get; }
    public string Verb { get; }
    public IReadOnlyList<string> Tokens { get; }
    public string? ParamString { get; }

    public SfxStatement(StatementKind kind, string verb,
                        IReadOnlyList<string> tokens, string? paramString)
    {
        Kind = kind;
        Verb = verb;
        Tokens = tokens;
        ParamString = paramString;
    }
}

/// <summary>The compiled form of one <c>script=[[ ... ]]</c> body.
/// Non-mutable; reused by every VM instance running the same script.</summary>
public sealed class SfxProgram
{
    public string Name { get; }
    public IReadOnlyList<SfxStatement> Statements { get; }

    public SfxProgram(string name, IReadOnlyList<SfxStatement> statements)
    {
        Name = name;
        Statements = statements;
    }
}
