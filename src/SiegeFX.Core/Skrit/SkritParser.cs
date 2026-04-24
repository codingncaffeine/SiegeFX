using System.Globalization;

namespace SiegeFX.Core.Skrit;

/// <summary>Recursive-descent parser for Skrit. Consumes the token stream produced by
/// <see cref="SkritLexer"/> and builds a <see cref="SkritScript"/> AST. Errors throw
/// <see cref="InvalidDataException"/> with line/column context — file-level fuzz runs
/// either get a clean parse or a single pointed error.</summary>
public sealed class SkritParser
{
    private readonly IReadOnlyList<SkritToken> _toks;
    private int _pos;

    public SkritParser(IReadOnlyList<SkritToken> tokens)
    {
        _toks = tokens;
        _pos = 0;
    }

    public static SkritScript Parse(string source)
    {
        var toks = SkritLexer.Tokenize(source);
        return new SkritParser(toks).ParseScript();
    }

    // ----- token helpers ----------------------------------------------------------------

    private SkritToken Cur => _toks[_pos];
    private SkritToken Peek(int off) => _toks[Math.Min(_pos + off, _toks.Count - 1)];

    private bool Match(SkritTokenKind k)
    {
        if (Cur.Kind == k) { _pos++; return true; }
        return false;
    }

    private SkritToken Expect(SkritTokenKind k)
    {
        if (Cur.Kind != k)
            throw new InvalidDataException(
                $"expected {k} but got {Cur.Kind} '{Cur.Text}' at line {Cur.Line}:{Cur.Column}");
        var t = Cur; _pos++; return t;
    }

    private static bool IsTypeKeyword(SkritTokenKind k) => k is
        SkritTokenKind.KwInt or SkritTokenKind.KwFloat or SkritTokenKind.KwBool or
        SkritTokenKind.KwString or SkritTokenKind.KwVoid;

    private static string TypeTokenToString(SkritToken t) => t.Kind switch
    {
        SkritTokenKind.KwInt    => "int",
        SkritTokenKind.KwFloat  => "float",
        SkritTokenKind.KwBool   => "bool",
        SkritTokenKind.KwString => "string",
        SkritTokenKind.KwVoid   => "void",
        _ => t.Text, // user-defined component types used as arg types (e.g. `Job job$`)
    };

    // ----- top level --------------------------------------------------------------------

    public SkritScript ParseScript()
    {
        var items = new List<SkritTopLevel>();
        while (Cur.Kind != SkritTokenKind.EndOfFile)
        {
            items.AddRange(ParseTopLevel());
        }
        return new SkritScript(items);
    }

    private static List<SkritTopLevel> One(SkritTopLevel x) => new() { x };

    private List<SkritTopLevel> ParseTopLevel()
    {
        var t = Cur;

        if (t.Kind == SkritTokenKind.PreprocessorDirective)
        {
            _pos++;
            return One(new SkritPreprocessorDecl(t.Text, t.Line, t.Column));
        }

        // Top-level `[[ ... ]]` — conditional-compile block wrapping top-level decls. Splice
        // inner items directly into the parent item list.
        if (t.Kind == SkritTokenKind.LBracket && Peek(1).Kind == SkritTokenKind.LBracket)
        {
            _pos += 2;
            var inner = new List<SkritTopLevel>();
            while (!(Cur.Kind == SkritTokenKind.RBracket && Peek(1).Kind == SkritTokenKind.RBracket)
                   && Cur.Kind != SkritTokenKind.EndOfFile)
            {
                inner.AddRange(ParseTopLevel());
            }
            Expect(SkritTokenKind.RBracket);
            Expect(SkritTokenKind.RBracket);
            return inner;
        }

        // Top-level `transition { <entries> }` — free-floating transition block, each entry
        // may take an optional source-state prefix (`Inactive$ -> Active$: ...`). We wrap in
        // a synthetic state with no members so the binder has a single decl to process.
        if (t.Kind == SkritTokenKind.KwTransition)
        {
            _pos++;
            Expect(SkritTokenKind.LBrace);
            var members = new List<SkritStateMember>();
            while (Cur.Kind != SkritTokenKind.RBrace && Cur.Kind != SkritTokenKind.EndOfFile)
            {
                members.Add(ParseTransitionEntry(t));
            }
            Expect(SkritTokenKind.RBrace);
            return One(new SkritStateDecl(
                false, "__top_transitions__", null, members, null, t.Line, t.Column));
        }

        if (t.Kind == SkritTokenKind.KwProperty) return One(ParseProperty());
        if (t.Kind is SkritTokenKind.KwStartup or SkritTokenKind.KwState) return One(ParseState());

        if (t.Kind == SkritTokenKind.KwEvent)
        {
            _pos++;
            var name = Expect(SkritTokenKind.Identifier).Text;
            List<SkritParam> ps;
            if (Match(SkritTokenKind.LParen))
            {
                ps = ParseParamList();
                Expect(SkritTokenKind.RParen);
            }
            else ps = new List<SkritParam>();
            var body = ParseBlock();
            return One(new SkritTopLevelEvent(name, ps, body, t.Line, t.Column));
        }

        if (t.Kind == SkritTokenKind.KwTrigger)
        {
            _pos++;
            var name = Expect(SkritTokenKind.Identifier).Text;
            List<SkritExpr> args;
            if (Match(SkritTokenKind.LParen))
            {
                args = ParseArgList();
                Expect(SkritTokenKind.RParen);
            }
            else args = new List<SkritExpr>();
            var body = ParseBlock();
            return One(new SkritTopLevelTrigger(name, args, body, t.Line, t.Column));
        }

        if (t.Kind == SkritTokenKind.Identifier && string.Equals(t.Text, "owner", StringComparison.OrdinalIgnoreCase)
            && Peek(1).Kind == SkritTokenKind.Assign)
        {
            _pos++;
            Expect(SkritTokenKind.Assign);
            var id = Expect(SkritTokenKind.Identifier);
            Expect(SkritTokenKind.Semicolon);
            return One(new SkritOwnerDecl(id.Text, t.Line, t.Column));
        }

        if (IsTypeKeyword(t.Kind))
        {
            var type = TypeTokenToString(t); _pos++;
            var nameTok = Expect(SkritTokenKind.Identifier);
            if (Cur.Kind == SkritTokenKind.LParen) return One(ParseFunctionDeclTail(type, nameTok.Text, t.Line, t.Column));
            // `bool IsReady$ { ... }` — typed bare-name function (no parens).
            if (Cur.Kind == SkritTokenKind.LBrace)
            {
                var body = ParseBlock();
                return One(new SkritFunctionDecl(type, nameTok.Text, new List<SkritParam>(), body, t.Line, t.Column));
            }
            return ParseFieldDeclListTail(type, nameTok.Text, nameTok.Line, nameTok.Column);
        }

        if (t.Kind == SkritTokenKind.Identifier && Peek(1).Kind == SkritTokenKind.LParen)
        {
            var name = t.Text; _pos++;
            return One(ParseFunctionDeclTail(null, name, t.Line, t.Column));
        }

        if (t.Kind == SkritTokenKind.Identifier && Peek(1).Kind == SkritTokenKind.LBrace)
        {
            var name = t.Text; _pos++;
            var body = ParseBlock();
            return One(new SkritFunctionDecl(null, name, new List<SkritParam>(), body, t.Line, t.Column));
        }

        if (t.Kind == SkritTokenKind.Identifier && Peek(1).Kind == SkritTokenKind.Identifier)
        {
            var type = t.Text; _pos++;
            var nameTok = Expect(SkritTokenKind.Identifier);
            if (Cur.Kind == SkritTokenKind.LParen)
                return One(ParseFunctionDeclTail(type, nameTok.Text, t.Line, t.Column));
            // `ePContentType calc_general_type$ { ... }` — user-typed bare-name function.
            if (Cur.Kind == SkritTokenKind.LBrace)
            {
                var fbody = ParseBlock();
                return One(new SkritFunctionDecl(type, nameTok.Text, new List<SkritParam>(), fbody, t.Line, t.Column));
            }
            return ParseFieldDeclListTail(type, nameTok.Text, nameTok.Line, nameTok.Column);
        }

        throw new InvalidDataException(
            $"unexpected top-level token {t.Kind} '{t.Text}' at line {t.Line}:{t.Column}");
    }

    private SkritPropertyDecl ParseProperty()
    {
        var start = Expect(SkritTokenKind.KwProperty);

        string type;
        if (IsTypeKeyword(Cur.Kind) || Cur.Kind == SkritTokenKind.Identifier)
        {
            type = TypeTokenToString(Cur); _pos++;
        }
        else throw new InvalidDataException(
            $"expected type in property decl at line {Cur.Line}:{Cur.Column}");

        var name = Expect(SkritTokenKind.Identifier).Text;

        SkritExpr? init = null;
        if (Match(SkritTokenKind.Assign)) init = ParseExpression();

        string? doc = null;
        if (Cur.Kind == SkritTokenKind.KwDoc)
        {
            _pos++;
            Expect(SkritTokenKind.Assign);
            doc = Expect(SkritTokenKind.StringLiteral).Text;
        }

        Expect(SkritTokenKind.Semicolon);
        return new SkritPropertyDecl(type, name, init, doc, start.Line, start.Column);
    }

    private List<SkritTopLevel> ParseFieldDeclListTail(string type, string firstName, int line, int col)
    {
        var list = new List<SkritTopLevel>();
        var curName = firstName;
        var curLine = line;
        var curCol = col;
        while (true)
        {
            SkritExpr? init = null;
            if (Match(SkritTokenKind.Assign)) init = ParseExpression();
            list.Add(new SkritFieldDecl(type, curName, init, curLine, curCol));
            if (!Match(SkritTokenKind.Comma)) break;
            var nameTok = Expect(SkritTokenKind.Identifier);
            curName = nameTok.Text;
            curLine = nameTok.Line;
            curCol = nameTok.Column;
        }
        Expect(SkritTokenKind.Semicolon);
        return list;
    }

    private SkritFunctionDecl ParseFunctionDeclTail(string? retType, string name, int line, int col)
    {
        Expect(SkritTokenKind.LParen);
        var ps = ParseParamList();
        Expect(SkritTokenKind.RParen);
        var body = ParseBlock();
        return new SkritFunctionDecl(retType, name, ps, body, line, col);
    }

    private List<SkritParam> ParseParamList()
    {
        var ps = new List<SkritParam>();
        if (Cur.Kind == SkritTokenKind.RParen) return ps;

        while (true)
        {
            // Param type is either a keyword type or a user type (identifier). Identifier-only
            // cases are rare but seen (e.g. `Job job$`). Comments on the name (`/* x$ */`) have
            // already been stripped by the lexer.
            string ptype;
            if (IsTypeKeyword(Cur.Kind) || Cur.Kind == SkritTokenKind.Identifier)
            {
                ptype = TypeTokenToString(Cur); _pos++;
            }
            else throw new InvalidDataException(
                $"expected param type at line {Cur.Line}:{Cur.Column}");

            // Param name might be absent (comment-only marker already stripped by lexer).
            string pname = "";
            if (Cur.Kind == SkritTokenKind.Identifier) { pname = Cur.Text; _pos++; }
            ps.Add(new SkritParam(ptype, pname));

            if (!Match(SkritTokenKind.Comma)) break;
        }
        return ps;
    }

    private SkritStateDecl ParseState()
    {
        var start = Cur;
        bool startup = Match(SkritTokenKind.KwStartup);
        Expect(SkritTokenKind.KwState);
        var name = Expect(SkritTokenKind.Identifier).Text;

        // Empty-body state: `startup state Inactive$;` — decl-only placeholder, transitions
        // are wired separately at top level.
        if (Match(SkritTokenKind.Semicolon))
        {
            return new SkritStateDecl(
                startup, name, null, new List<SkritStateMember>(), null,
                start.Line, start.Column);
        }

        // Optional inline-trigger / inline-event header. When the header is present the braces
        // hold statements (the handler body); when absent the braces hold state members.
        SkritTriggerHeader? header = null;
        if (Cur.Kind == SkritTokenKind.KwTrigger)
        {
            _pos++;
            var evt = Expect(SkritTokenKind.Identifier).Text;
            List<SkritExpr> args;
            if (Match(SkritTokenKind.LParen))
            {
                args = ParseArgList();
                Expect(SkritTokenKind.RParen);
            }
            else args = new List<SkritExpr>();
            header = new SkritTriggerHeader(evt, args, null);
        }
        else if (Cur.Kind == SkritTokenKind.KwEvent)
        {
            _pos++;
            var evt = Expect(SkritTokenKind.Identifier).Text;
            // Event-form header accepts a param list (typed params), then body.
            List<SkritParam> evps;
            if (Match(SkritTokenKind.LParen))
            {
                evps = ParseParamList();
                Expect(SkritTokenKind.RParen);
            }
            else evps = new List<SkritParam>();
            var evBody = ParseBlock();
            // Model the event-header state as a state whose sole member is an event handler.
            return new SkritStateDecl(
                startup, name, null,
                new List<SkritStateMember> { new SkritEventHandler(evt, evps, evBody, start.Line, start.Column) },
                null, start.Line, start.Column);
        }

        // `state Name$ poll ( interval ) { members }` — polling-interval state. The interval
        // gets attached as a synthetic trigger-header marker the binder can read.
        if (Cur.Kind == SkritTokenKind.Identifier
            && string.Equals(Cur.Text, "poll", StringComparison.OrdinalIgnoreCase)
            && Peek(1).Kind == SkritTokenKind.LParen)
        {
            _pos++;
            Expect(SkritTokenKind.LParen);
            var interval = ParseExpression();
            Expect(SkritTokenKind.RParen);
            header = new SkritTriggerHeader("__poll__", new List<SkritExpr> { interval }, null);
        }

        if (header is not null && header.EventName != "__poll__")
        {
            var triggerBody = ParseBlock();
            return new SkritStateDecl(
                startup, name, header, new List<SkritStateMember>(), triggerBody,
                start.Line, start.Column);
        }

        // Inline scheduled-block state: `state Foo$ Sched$ at ( N frames ) { body }`. The
        // whole state is just one scheduled chore; ServerGo$/SG_0$ pattern in jipper/mp.
        if (Cur.Kind == SkritTokenKind.Identifier && Peek(1).Kind == SkritTokenKind.KwAt)
        {
            var schedTok = Cur; _pos++;
            var sched = ParseScheduledBlockTail(schedTok);
            return new SkritStateDecl(
                startup, name, header,
                new List<SkritStateMember> { sched }, null,
                start.Line, start.Column);
        }

        Expect(SkritTokenKind.LBrace);
        var body = new List<SkritStateMember>();
        while (Cur.Kind != SkritTokenKind.RBrace && Cur.Kind != SkritTokenKind.EndOfFile)
        {
            body.AddRange(ParseStateMember());
        }
        Expect(SkritTokenKind.RBrace);

        return new SkritStateDecl(startup, name, header, body, null, start.Line, start.Column);
    }

    private SkritScheduledBlock ParseScheduledBlockTail(SkritToken nameTok)
    {
        Expect(SkritTokenKind.KwAt);
        Expect(SkritTokenKind.LParen);
        var count = ParseExpression();
        string unit = "frames";
        if (Match(SkritTokenKind.KwFrames)) unit = "frames";
        else if (Match(SkritTokenKind.KwSeconds)) unit = "seconds";
        Expect(SkritTokenKind.RParen);
        var body = ParseBlock();
        return new SkritScheduledBlock(nameTok.Text, count, unit, body, nameTok.Line, nameTok.Column);
    }

    private static List<SkritStateMember> OneMember(SkritStateMember m) => new() { m };

    private List<SkritStateMember> ParseStateMember()
    {
        var t = Cur;

        if (t.Kind == SkritTokenKind.PreprocessorDirective)
        {
            _pos++;
            return OneMember(new SkritStateField("__preprocessor__", t.Text, null, t.Line, t.Column));
        }

        // State-body `[[ ... ]]` — conditional-compile block wrapping state members.
        if (t.Kind == SkritTokenKind.LBracket && Peek(1).Kind == SkritTokenKind.LBracket)
        {
            _pos += 2;
            var inner = new List<SkritStateMember>();
            while (!(Cur.Kind == SkritTokenKind.RBracket && Peek(1).Kind == SkritTokenKind.RBracket)
                   && Cur.Kind != SkritTokenKind.EndOfFile)
            {
                inner.AddRange(ParseStateMember());
            }
            Expect(SkritTokenKind.RBracket);
            Expect(SkritTokenKind.RBracket);
            return inner;
        }

        if (t.Kind == SkritTokenKind.KwEvent)
        {
            _pos++;
            var name = Expect(SkritTokenKind.Identifier).Text;
            List<SkritParam> ps;
            if (Match(SkritTokenKind.LParen))
            {
                ps = ParseParamList();
                Expect(SkritTokenKind.RParen);
            }
            else ps = new List<SkritParam>();
            var body = ParseBlock();
            return OneMember(new SkritEventHandler(name, ps, body, t.Line, t.Column));
        }

        if (t.Kind == SkritTokenKind.KwTrigger)
        {
            _pos++;
            var name = Expect(SkritTokenKind.Identifier).Text;
            List<SkritExpr> args;
            if (Match(SkritTokenKind.LParen))
            {
                args = ParseArgList();
                Expect(SkritTokenKind.RParen);
            }
            else args = new List<SkritExpr>();
            var body = ParseBlock();
            return OneMember(new SkritTriggerHandler(name, args, body, t.Line, t.Column));
        }

        if (t.Kind == SkritTokenKind.KwTransition)
        {
            _pos++;
            // Block form: `transition { -> Target$: Evt$(args) [= { body }]; ... }`.
            if (Match(SkritTokenKind.LBrace))
            {
                var list = new List<SkritStateMember>();
                while (Cur.Kind != SkritTokenKind.RBrace && Cur.Kind != SkritTokenKind.EndOfFile)
                {
                    list.Add(ParseTransitionEntry(t));
                }
                Expect(SkritTokenKind.RBrace);
                return list;
            }
            // Single inline form: `transition -> Target$: Evt$(args);`.
            return OneMember(ParseTransitionEntry(t));
        }

        if (IsTypeKeyword(t.Kind))
        {
            var type = TypeTokenToString(t); _pos++;
            var nameTok = Expect(SkritTokenKind.Identifier);
            return ParseStateFieldListTail(type, nameTok);
        }

        if (t.Kind == SkritTokenKind.Identifier && Peek(1).Kind == SkritTokenKind.KwAt)
        {
            var schedTok = Cur; _pos++;
            return OneMember(ParseScheduledBlockTail(schedTok));
        }

        if (t.Kind == SkritTokenKind.Identifier && Peek(1).Kind == SkritTokenKind.Identifier)
        {
            var type = t.Text; _pos++;
            var nameTok = Expect(SkritTokenKind.Identifier);
            return ParseStateFieldListTail(type, nameTok);
        }

        throw new InvalidDataException(
            $"unexpected state-body token {t.Kind} '{t.Text}' at line {t.Line}:{t.Column}");
    }

    private List<SkritStateMember> ParseStateFieldListTail(string type, SkritToken firstName)
    {
        var list = new List<SkritStateMember>();
        var nameTok = firstName;
        while (true)
        {
            SkritExpr? init = null;
            if (Match(SkritTokenKind.Assign)) init = ParseExpression();
            list.Add(new SkritStateField(type, nameTok.Text, init, nameTok.Line, nameTok.Column));
            if (!Match(SkritTokenKind.Comma)) break;
            nameTok = Expect(SkritTokenKind.Identifier);
        }
        Expect(SkritTokenKind.Semicolon);
        return list;
    }

    private SkritTransition ParseTransitionEntry(SkritToken start)
    {
        // Optional leading source-state identifier: `Inactive$ -> Active$: ...`. When the
        // next token is an Identifier followed by Arrow, consume the source and drop it
        // (the AST captures only the target; the binder wires edges from the owning state).
        if (Cur.Kind == SkritTokenKind.Identifier && Peek(1).Kind == SkritTokenKind.Arrow)
        {
            _pos++;
        }
        Expect(SkritTokenKind.Arrow);
        var target = Expect(SkritTokenKind.Identifier).Text;
        Expect(SkritTokenKind.Colon);

        // Event position accepts either an identifier event name or an `if (expr)` guard —
        // jipper/mp use the guard form to branch on bool properties.
        string evtName;
        List<SkritExpr> eargs = new();
        if (Cur.Kind == SkritTokenKind.KwIf)
        {
            _pos++;
            evtName = "__if__";
            Expect(SkritTokenKind.LParen);
            eargs.Add(ParseExpression());
            Expect(SkritTokenKind.RParen);
        }
        else
        {
            evtName = Expect(SkritTokenKind.Identifier).Text;
            if (Match(SkritTokenKind.LParen))
            {
                eargs = ParseArgList();
                Expect(SkritTokenKind.RParen);
            }
        }
        // Optional `= { body }` clause attached to the transition entry. When present the
        // trailing `;` is optional (block form omits it); when absent the `;` is required.
        SkritBlock? body = null;
        if (Match(SkritTokenKind.Assign))
        {
            body = ParseBlock();
        }
        if (body is not null) Match(SkritTokenKind.Semicolon);
        else Expect(SkritTokenKind.Semicolon);
        return new SkritTransition(target, evtName, eargs, body, start.Line, start.Column);
    }

    // ----- statements -------------------------------------------------------------------

    private SkritBlock ParseBlock()
    {
        var lb = Expect(SkritTokenKind.LBrace);
        var stmts = new List<SkritStmt>();
        while (Cur.Kind != SkritTokenKind.RBrace && Cur.Kind != SkritTokenKind.EndOfFile)
        {
            AppendStatement(stmts);
        }
        Expect(SkritTokenKind.RBrace);
        return new SkritBlock(stmts, lb.Line, lb.Column);
    }

    private void AppendStatement(List<SkritStmt> stmts)
    {
        var t = Cur;

        // Local variable decl — may be comma-separated: `Go m_Go$, temp$;`.
        bool isLocalDecl =
            (IsTypeKeyword(t.Kind) && Peek(1).Kind == SkritTokenKind.Identifier) ||
            (t.Kind == SkritTokenKind.Identifier && Peek(1).Kind == SkritTokenKind.Identifier
             && !string.Equals(t.Text, "owner", StringComparison.OrdinalIgnoreCase));

        if (isLocalDecl)
        {
            var type = TypeTokenToString(t); _pos++;
            var nameTok = Expect(SkritTokenKind.Identifier);
            while (true)
            {
                SkritExpr? init = null;
                if (Match(SkritTokenKind.Assign)) init = ParseExpression();
                stmts.Add(new SkritLocalDecl(type, nameTok.Text, init, nameTok.Line, nameTok.Column));
                if (!Match(SkritTokenKind.Comma)) break;
                nameTok = Expect(SkritTokenKind.Identifier);
            }
            Expect(SkritTokenKind.Semicolon);
            return;
        }

        stmts.Add(ParseStatement());
    }

    private SkritStmt ParseStatement()
    {
        var t = Cur;

        if (t.Kind == SkritTokenKind.PreprocessorDirective)
        {
            _pos++;
            // Treat a preprocessor line as a no-op expr-stmt so statement-flow is preserved.
            // The directive text is preserved as a string literal payload.
            return new SkritExprStmt(
                new SkritStringLit(t.Text, t.Line, t.Column), t.Line, t.Column);
        }

        if (t.Kind == SkritTokenKind.LBrace) return ParseBlock();

        // `[[ ... ]]` — conditional-compile block from #only()/#version()-style directives.
        // Treat as a bare statement block.
        if (t.Kind == SkritTokenKind.LBracket && Peek(1).Kind == SkritTokenKind.LBracket)
        {
            var open = t; _pos += 2;
            var stmts = new List<SkritStmt>();
            while (!(Cur.Kind == SkritTokenKind.RBracket && Peek(1).Kind == SkritTokenKind.RBracket)
                   && Cur.Kind != SkritTokenKind.EndOfFile)
            {
                AppendStatement(stmts);
            }
            Expect(SkritTokenKind.RBracket);
            Expect(SkritTokenKind.RBracket);
            return new SkritBlock(stmts, open.Line, open.Column);
        }

        if (t.Kind == SkritTokenKind.KwIf)
        {
            _pos++;
            // No mandatory paren consumption — a leading `(` is absorbed by ParseExpression
            // as a grouping primary. This also tolerates shipped paren-imbalance bugs like
            // `if (A) && (B)` where the outer paren closes too early.
            var cond = ParseExpression();
            // Shipped jornus uses `if (A)) || (B))` with a stray trailing `)`; swallow any
            // stray close parens that appear between the condition and the body.
            while (Cur.Kind == SkritTokenKind.RParen) _pos++;
            var thenBr = ParseStatement();
            SkritStmt? elseBr = null;
            if (Match(SkritTokenKind.KwElse)) elseBr = ParseStatement();
            return new SkritIfStmt(cond, thenBr, elseBr, t.Line, t.Column);
        }

        if (t.Kind == SkritTokenKind.KwWhile)
        {
            _pos++;
            var cond = ParseExpression();
            var body = ParseStatement();
            return new SkritWhileStmt(cond, body, t.Line, t.Column);
        }

        // `forever { body }` — infinite loop; lowered to `while(true)`.
        if (t.Kind == SkritTokenKind.Identifier
            && string.Equals(t.Text, "forever", StringComparison.OrdinalIgnoreCase)
            && Peek(1).Kind == SkritTokenKind.LBrace)
        {
            _pos++;
            var body = ParseBlock();
            return new SkritWhileStmt(
                new SkritBoolLit(true, t.Line, t.Column), body, t.Line, t.Column);
        }

        // Empty statement `;` — accommodates shipped double-semicolons (`;;`).
        if (t.Kind == SkritTokenKind.Semicolon)
        {
            _pos++;
            return new SkritBlock(new List<SkritStmt>(), t.Line, t.Column);
        }

        if (t.Kind == SkritTokenKind.KwFor)
        {
            _pos++;
            Expect(SkritTokenKind.LParen);
            SkritStmt? init = null;
            if (!Match(SkritTokenKind.Semicolon))
            {
                init = ParseStatement(); // consumes trailing ';'
            }
            SkritExpr? cond = null;
            if (Cur.Kind != SkritTokenKind.Semicolon) cond = ParseExpression();
            Expect(SkritTokenKind.Semicolon);
            SkritExpr? update = null;
            if (Cur.Kind != SkritTokenKind.RParen) update = ParseExpression();
            Expect(SkritTokenKind.RParen);
            var body = ParseStatement();
            return new SkritForStmt(init, cond, update, body, t.Line, t.Column);
        }

        if (t.Kind == SkritTokenKind.KwReturn)
        {
            _pos++;
            SkritExpr? v = null;
            if (Cur.Kind != SkritTokenKind.Semicolon) v = ParseExpression();
            Expect(SkritTokenKind.Semicolon);
            return new SkritReturnStmt(v, t.Line, t.Column);
        }

        if (t.Kind == SkritTokenKind.KwSetState)
        {
            _pos++;
            // SetState accepts either `SetState Name$;` or `SetState (Name$);`.
            bool paren = Match(SkritTokenKind.LParen);
            var name = Expect(SkritTokenKind.Identifier).Text;
            if (paren) Expect(SkritTokenKind.RParen);
            Expect(SkritTokenKind.Semicolon);
            return new SkritSetStateStmt(name, t.Line, t.Column);
        }

        // Local variable decl: `int name [= expr] [, name2 [= expr2]]*;` / `Job x [= expr];`.
        bool isLocalDecl =
            (IsTypeKeyword(t.Kind) && Peek(1).Kind == SkritTokenKind.Identifier) ||
            (t.Kind == SkritTokenKind.Identifier && Peek(1).Kind == SkritTokenKind.Identifier);
        if (isLocalDecl)
        {
            var type = TypeTokenToString(t); _pos++;
            var nameTok = Expect(SkritTokenKind.Identifier);
            var decls = new List<SkritStmt>();
            while (true)
            {
                SkritExpr? init = null;
                if (Match(SkritTokenKind.Assign)) init = ParseExpression();
                decls.Add(new SkritLocalDecl(type, nameTok.Text, init, nameTok.Line, nameTok.Column));
                if (!Match(SkritTokenKind.Comma)) break;
                nameTok = Expect(SkritTokenKind.Identifier);
            }
            Expect(SkritTokenKind.Semicolon);
            if (decls.Count == 1) return decls[0];
            return new SkritBlock(decls, t.Line, t.Column);
        }

        // Otherwise expression statement.
        var expr = ParseExpression();
        Expect(SkritTokenKind.Semicolon);
        return new SkritExprStmt(expr, t.Line, t.Column);
    }

    // ----- expressions ------------------------------------------------------------------

    public SkritExpr ParseExpression() => ParseAssignment();

    private SkritExpr ParseAssignment()
    {
        var lhs = ParseTernary();
        if (Cur.Kind is SkritTokenKind.Assign or SkritTokenKind.PlusAssign or
            SkritTokenKind.MinusAssign or SkritTokenKind.StarAssign or SkritTokenKind.SlashAssign or
            SkritTokenKind.PipeAssign or SkritTokenKind.AmpAssign or SkritTokenKind.CaretAssign)
        {
            var op = Cur; _pos++;
            var rhs = ParseAssignment(); // right-associative
            return new SkritAssignExpr(op.Text, lhs, rhs, op.Line, op.Column);
        }
        return lhs;
    }

    private SkritExpr ParseTernary()
    {
        var cond = ParseLogicalOr();
        if (Cur.Kind == SkritTokenKind.Question)
        {
            var q = Cur; _pos++;
            var thenE = ParseAssignment();
            Expect(SkritTokenKind.Colon);
            var elseE = ParseAssignment();
            return new SkritTernaryExpr(cond, thenE, elseE, q.Line, q.Column);
        }
        return cond;
    }

    private SkritExpr ParseLogicalOr()
    {
        var l = ParseLogicalAnd();
        while (Cur.Kind == SkritTokenKind.OrOr)
        {
            var op = Cur; _pos++;
            var r = ParseLogicalAnd();
            l = new SkritBinaryExpr(op.Text, l, r, op.Line, op.Column);
        }
        return l;
    }

    private SkritExpr ParseLogicalAnd()
    {
        var l = ParseBitOr();
        while (Cur.Kind == SkritTokenKind.AndAnd)
        {
            var op = Cur; _pos++;
            var r = ParseBitOr();
            l = new SkritBinaryExpr(op.Text, l, r, op.Line, op.Column);
        }
        return l;
    }

    private SkritExpr ParseBitOr()
    {
        var l = ParseBitXor();
        while (Cur.Kind == SkritTokenKind.Pipe)
        {
            var op = Cur; _pos++;
            var r = ParseBitXor();
            l = new SkritBinaryExpr(op.Text, l, r, op.Line, op.Column);
        }
        return l;
    }

    private SkritExpr ParseBitXor()
    {
        var l = ParseBitAnd();
        while (Cur.Kind == SkritTokenKind.Caret)
        {
            var op = Cur; _pos++;
            var r = ParseBitAnd();
            l = new SkritBinaryExpr(op.Text, l, r, op.Line, op.Column);
        }
        return l;
    }

    private SkritExpr ParseBitAnd()
    {
        var l = ParseEquality();
        while (Cur.Kind == SkritTokenKind.Ampersand)
        {
            var op = Cur; _pos++;
            var r = ParseEquality();
            l = new SkritBinaryExpr(op.Text, l, r, op.Line, op.Column);
        }
        return l;
    }

    private SkritExpr ParseEquality()
    {
        var l = ParseRelational();
        while (Cur.Kind is SkritTokenKind.EqEq or SkritTokenKind.NotEq or SkritTokenKind.TildeEq)
        {
            var op = Cur; _pos++;
            var r = ParseRelational();
            l = new SkritBinaryExpr(op.Text, l, r, op.Line, op.Column);
        }
        return l;
    }

    private SkritExpr ParseRelational()
    {
        var l = ParseShift();
        while (Cur.Kind is SkritTokenKind.Lt or SkritTokenKind.LtEq or SkritTokenKind.Gt or SkritTokenKind.GtEq)
        {
            var op = Cur; _pos++;
            var r = ParseShift();
            l = new SkritBinaryExpr(op.Text, l, r, op.Line, op.Column);
        }
        return l;
    }

    private SkritExpr ParseShift()
    {
        var l = ParseAdditive();
        while (Cur.Kind is SkritTokenKind.LeftShift or SkritTokenKind.RightShift)
        {
            var op = Cur; _pos++;
            var r = ParseAdditive();
            l = new SkritBinaryExpr(op.Text, l, r, op.Line, op.Column);
        }
        return l;
    }

    private SkritExpr ParseAdditive()
    {
        var l = ParseMultiplicative();
        while (Cur.Kind is SkritTokenKind.Plus or SkritTokenKind.Minus)
        {
            var op = Cur; _pos++;
            var r = ParseMultiplicative();
            l = new SkritBinaryExpr(op.Text, l, r, op.Line, op.Column);
        }
        return l;
    }

    private SkritExpr ParseMultiplicative()
    {
        var l = ParsePower();
        while (Cur.Kind is SkritTokenKind.Star or SkritTokenKind.Slash or SkritTokenKind.Percent)
        {
            var op = Cur; _pos++;
            var r = ParsePower();
            l = new SkritBinaryExpr(op.Text, l, r, op.Line, op.Column);
        }
        return l;
    }

    private SkritExpr ParsePower()
    {
        var l = ParseUnary();
        // `**` is right-associative and higher precedence than `*` `/` `%`.
        if (Cur.Kind == SkritTokenKind.StarStar)
        {
            var op = Cur; _pos++;
            var r = ParsePower();
            return new SkritBinaryExpr(op.Text, l, r, op.Line, op.Column);
        }
        return l;
    }

    private SkritExpr ParseUnary()
    {
        if (Cur.Kind is SkritTokenKind.Bang or SkritTokenKind.Minus or
            SkritTokenKind.Plus or SkritTokenKind.Tilde)
        {
            var op = Cur; _pos++;
            var operand = ParseUnary();
            return new SkritUnaryExpr(op.Text, operand, op.Line, op.Column);
        }
        return ParsePostfix();
    }

    private SkritExpr ParsePostfix()
    {
        var e = ParsePrimary();
        while (true)
        {
            if (Cur.Kind == SkritTokenKind.Dot)
            {
                _pos++;
                var m = Expect(SkritTokenKind.Identifier);
                e = new SkritMemberExpr(e, m.Text, m.Line, m.Column);
            }
            else if (Cur.Kind == SkritTokenKind.LParen)
            {
                var lp = Cur; _pos++;
                var args = ParseArgList();
                Expect(SkritTokenKind.RParen);
                e = new SkritCallExpr(e, args, lp.Line, lp.Column);
            }
            else break;
        }
        return e;
    }

    private SkritExpr ParsePrimary()
    {
        var t = Cur;
        switch (t.Kind)
        {
            case SkritTokenKind.IntLiteral:
                _pos++;
                return new SkritIntLit(ParseIntLiteral(t.Text), t.Line, t.Column);

            case SkritTokenKind.FloatLiteral:
                _pos++;
                return new SkritFloatLit(
                    double.Parse(t.Text, CultureInfo.InvariantCulture), t.Line, t.Column);

            case SkritTokenKind.StringLiteral:
                _pos++;
                return new SkritStringLit(t.Text, t.Line, t.Column);

            case SkritTokenKind.FourCharLiteral:
                _pos++;
                uint packed = t.Text.Length == 4 ? SkritLexer.PackFourCharLiteral(t.Text) : 0u;
                return new SkritFourCharLit(packed, t.Text, t.Line, t.Column);

            case SkritTokenKind.KwTrue:
                _pos++;
                return new SkritBoolLit(true, t.Line, t.Column);

            case SkritTokenKind.KwFalse:
                _pos++;
                return new SkritBoolLit(false, t.Line, t.Column);

            case SkritTokenKind.KwNull:
                _pos++;
                return new SkritNullLit(t.Line, t.Column);

            case SkritTokenKind.Identifier:
                _pos++;
                return new SkritIdentExpr(t.Text, t.Line, t.Column);

            case SkritTokenKind.KwInt:
            case SkritTokenKind.KwFloat:
            case SkritTokenKind.KwBool:
            case SkritTokenKind.KwString:
                // Type-keyword-as-cast: `int ( expr )` / `float (expr)`. Surface as a callable
                // identifier so ParsePostfix wraps the (expr) into a CallExpr the binder can
                // lower to a conversion.
                _pos++;
                return new SkritIdentExpr(TypeTokenToString(t), t.Line, t.Column);

            case SkritTokenKind.LParen:
                _pos++;
                var inner = ParseExpression();
                Expect(SkritTokenKind.RParen);
                return inner;

            default:
                throw new InvalidDataException(
                    $"expected expression but got {t.Kind} '{t.Text}' at line {t.Line}:{t.Column}");
        }
    }

    private List<SkritExpr> ParseArgList()
    {
        var args = new List<SkritExpr>();
        if (Cur.Kind == SkritTokenKind.RParen) return args;
        while (true)
        {
            args.Add(ParseExpression());
            if (!Match(SkritTokenKind.Comma)) break;
        }
        return args;
    }

    private static long ParseIntLiteral(string text)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.Parse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return long.Parse(text, CultureInfo.InvariantCulture);
    }
}
