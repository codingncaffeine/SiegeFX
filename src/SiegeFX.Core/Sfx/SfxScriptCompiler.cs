using System.Text;

namespace SiegeFX.Core.Sfx;

/// <summary>Phase 17-SC-F — text → <see cref="SfxProgram"/> compiler.
/// The DSL is line-oriented but with explicit ';' terminators and
/// brace-grouped <c>if/else</c> bodies; comments are <c>//</c> to EOL
/// and <c>/* … */</c> blocks. The minimal interpreter doesn't yet handle
/// conditional bodies or <c>waitfor</c> / <c>get</c> shapes — those land
/// as later splinters fold them in. Everything the parser doesn't know
/// surfaces as <see cref="StatementKind.Raw"/> with the original tokens
/// preserved, so the VM can log + skip rather than blowing up.</summary>
public static class SfxScriptCompiler
{
    public static SfxProgram Compile(string name, string body)
    {
        // The body comes off SfxScriptStore as the raw attribute value
        // GasDocument captured for `script = [[ ... ]];` — i.e. it still
        // wraps the DSL in literal `[[` ... `]]` markers (DS1's GAS quoting
        // for multi-line script literals). Strip both ends if present so
        // the tokenizer doesn't see them as their own tokens.
        var trimmed = body.Trim();
        if (trimmed.StartsWith("[[")) trimmed = trimmed.Substring(2);
        if (trimmed.EndsWith("]]"))   trimmed = trimmed.Substring(0, trimmed.Length - 2);

        var stripped = StripComments(trimmed);
        var tokens   = Tokenize(stripped);
        var stmts    = new List<SfxStatement>();

        int i = 0;
        while (i < tokens.Count)
        {
            // Empty statement (stray ;).
            if (tokens[i] == ";") { i++; continue; }

            // if/else brace blocks. Phase 21-SC-SCROLL fold-fix: instead of
            // dropping the body (which hid `position_at` calls inside
            // fireball_base's `if ([1]==3) {...} else {...}` and made every
            // trackball start at #TARGET = arrived-immediately = pruned),
            // we recursively compile the body statements and inline them
            // into the parent. The if/else head is still logged once as a
            // Raw stmt so the diagnostic CLI can see what conditions exist.
            //
            // Net effect: both branches of every if/else execute. For DS1
            // scripts that author parallel "branch A: position_at @kill_bone
            // source; branch B: position_at @weapon_bone source" the last
            // write wins (and our handler ignores bone names anyway, so
            // the result is the same in practice). For the rarer case of
            // truly-divergent branches (different visual decorations per
            // condition) we'll get a slight over-render — fixable later
            // by a real condition evaluator. Today's bar is "fireball
            // shoots a projectile" and that requires the body to run.
            if (string.Equals(tokens[i], "if", StringComparison.OrdinalIgnoreCase))
            {
                i = InlineBracedBody(tokens, i, stmts);
                continue;
            }
            if (string.Equals(tokens[i], "else", StringComparison.OrdinalIgnoreCase))
            {
                i = InlineBracedBody(tokens, i, stmts);
                continue;
            }

            // Slurp tokens up to the next ';'.
            var stmtTokens = new List<string>();
            while (i < tokens.Count && tokens[i] != ";")
            {
                stmtTokens.Add(tokens[i]);
                i++;
            }
            if (i < tokens.Count) i++; // consume the ';'

            if (stmtTokens.Count == 0) continue;

            stmts.Add(BuildStatement(stmtTokens));
        }

        return new SfxProgram(name, stmts);
    }

    static int InlineBracedBody(List<string> tokens, int i, List<SfxStatement> stmts)
    {
        // Skip the head tokens (verb + condition) up to the opening brace.
        // The original implementation emitted a Raw stmt with the head for
        // diagnostics, but that surfaced as a fake `unhandled verb 'if'` /
        // `'else'` in `siegefx sfx run` output even though both branches
        // already inline-compile and execute. Pragmatic-merge semantics =
        // the verb is a no-op at runtime; emitting a logging shadow is a
        // lie. Preserve the body, drop the head Raw record.
        i++;
        while (i < tokens.Count && tokens[i] != "{") i++;
        if (i >= tokens.Count) return i;
        // Find matching `}` and slice body tokens out for nested compile.
        int bodyStart = i + 1;
        int depth = 1;
        i++;
        while (i < tokens.Count && depth > 0)
        {
            if      (tokens[i] == "{") depth++;
            else if (tokens[i] == "}") depth--;
            if (depth == 0) break;
            i++;
        }
        int bodyEnd = i; // exclusive — points at the closing `}`
        if (i < tokens.Count) i++; // consume `}`

        // Recursively parse the body via a sub-scanner that mirrors the
        // outer Compile loop. Sharing logic by reconstructing a token slice
        // and running CompileTokenRange over it.
        var body = tokens.GetRange(bodyStart, bodyEnd - bodyStart);
        CompileTokenRange(body, stmts);
        return i;
    }

    /// <summary>Statement loop extracted so the if/else inliner can call it.
    /// Mirrors the outer <see cref="Compile"/> body once stripped of the
    /// `[[ ]]` wrappers and comment-removal — those preprocessing steps
    /// happen at the top level only.</summary>
    static void CompileTokenRange(List<string> tokens, List<SfxStatement> stmts)
    {
        int i = 0;
        while (i < tokens.Count)
        {
            if (tokens[i] == ";") { i++; continue; }
            if (string.Equals(tokens[i], "if", StringComparison.OrdinalIgnoreCase))
            { i = InlineBracedBody(tokens, i, stmts); continue; }
            if (string.Equals(tokens[i], "else", StringComparison.OrdinalIgnoreCase))
            { i = InlineBracedBody(tokens, i, stmts); continue; }
            var stmtTokens = new List<string>();
            while (i < tokens.Count && tokens[i] != ";")
            {
                stmtTokens.Add(tokens[i]);
                i++;
            }
            if (i < tokens.Count) i++;
            if (stmtTokens.Count == 0) continue;
            stmts.Add(BuildStatement(stmtTokens));
        }
    }

    static SfxStatement BuildStatement(List<string> toks)
    {
        var verb = toks[0];

        // sfx <subverb> [args...]
        if (string.Equals(verb, "sfx", StringComparison.OrdinalIgnoreCase) && toks.Count >= 2)
        {
            var sub = toks[1].ToLowerInvariant();
            // sfx friendly <target|party> $x  → both are gameplay-side
            // friendliness flags with no visual side-effect. Folded into
            // SfxFriendlyTarget so they're silently consumed instead of
            // surfacing as `unhandled verb 'sfx friendly'` in audit output.
            if (sub == "friendly" && toks.Count >= 3 &&
                (string.Equals(toks[2], "target", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(toks[2], "party",  StringComparison.OrdinalIgnoreCase)))
            {
                var args = toks.GetRange(3, toks.Count - 3);
                return new SfxStatement(StatementKind.SfxFriendlyTarget, "sfx friendly " + toks[2].ToLowerInvariant(), args, null);
            }
            var rest = toks.GetRange(2, toks.Count - 2);
            return sub switch
            {
                "create"       => MakeCreate(rest),
                "start"        => new SfxStatement(StatementKind.SfxStart,        "sfx start",        rest, null),
                "destroy"      => new SfxStatement(StatementKind.SfxDestroy,      "sfx destroy",      rest, null),
                "finish"       => new SfxStatement(StatementKind.SfxFinish,       "sfx finish",       rest, null),
                "target"       => new SfxStatement(StatementKind.SfxTarget,       "sfx target",       rest, null),
                "attach"       => new SfxStatement(StatementKind.SfxAttach,       "sfx attach",       rest, null),
                "attach_point" => new SfxStatement(StatementKind.SfxAttachPoint,  "sfx attach_point", rest, null),
                "position_at"  => new SfxStatement(StatementKind.SfxPositionAt,   "sfx position_at",  rest, null),
                "offset"       => new SfxStatement(StatementKind.SfxOffset,       "sfx offset",       rest, null),
                "offset_bone"  => new SfxStatement(StatementKind.SfxOffset,       "sfx offset_bone",  rest, null),
                "rat"          => new SfxStatement(StatementKind.SfxRat,          "sfx rat",          rest, null),
                "direction"    => new SfxStatement(StatementKind.SfxDirection,    "sfx direction",    rest, null),
                // freeze_targets / snap_to_ground — gameplay-side flags that
                // don't drive the visual primitive. We map them onto
                // SfxFriendlyTarget which already consumes #POP/#PEEK
                // correctly so script stack discipline holds.
                "freeze_targets"  => new SfxStatement(StatementKind.SfxFriendlyTarget, "sfx freeze_targets",  rest, null),
                "snap_to_ground"  => new SfxStatement(StatementKind.SfxFriendlyTarget, "sfx snap_to_ground",  rest, null),
                _              => new SfxStatement(StatementKind.Raw,             "sfx " + sub,       toks, null),
            };
        }

        // sound <play|stop> ...
        if (string.Equals(verb, "sound", StringComparison.OrdinalIgnoreCase) && toks.Count >= 2)
        {
            var sub = toks[1].ToLowerInvariant();
            var rest = toks.GetRange(2, toks.Count - 2);
            return sub switch
            {
                "play" => new SfxStatement(StatementKind.SoundPlay, "sound play", rest, null),
                "stop" => new SfxStatement(StatementKind.SoundStop, "sound stop", rest, null),
                _      => new SfxStatement(StatementKind.Raw,       "sound " + sub, toks, null),
            };
        }

        if (string.Equals(verb, "set",   StringComparison.OrdinalIgnoreCase))
            return new SfxStatement(StatementKind.Set,   "set",   toks.GetRange(1, toks.Count - 1), null);
        if (string.Equals(verb, "pause", StringComparison.OrdinalIgnoreCase))
            return new SfxStatement(StatementKind.Pause, "pause", toks.GetRange(1, toks.Count - 1), null);
        if (string.Equals(verb, "call",  StringComparison.OrdinalIgnoreCase))
            return new SfxStatement(StatementKind.Call,  "call",  toks.GetRange(1, toks.Count - 1), null);

        return new SfxStatement(StatementKind.Raw, verb, toks, null);
    }

    static SfxStatement MakeCreate(List<string> rest)
    {
        // Layout: <kind> <target-token> ["param-string"]
        // The param-string is always the last token if any (quoted in
        // source). Tokenize() preserved its quoted form by stripping the
        // quotes and storing the inner text — so we look for a token
        // whose first character was tagged with a leading STRING marker.
        // We use a leading sentinel of '\u0001' below.
        string? param = null;
        var tokens = new List<string>();
        foreach (var t in rest)
        {
            if (t.Length > 0 && t[0] == '\u0001') param = t.Substring(1);
            else tokens.Add(t);
        }
        return new SfxStatement(StatementKind.SfxCreate, "sfx create", tokens, param);
    }

    // --- lexing --------------------------------------------------------

    static string StripComments(string s)
    {
        var sb = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            if (i + 1 < s.Length && s[i] == '/' && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
            }
            else if (i + 1 < s.Length && s[i] == '/' && s[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                if (i + 1 < s.Length) i += 2;
            }
            else
            {
                sb.Append(s[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            // Punctuation single-char tokens.
            if (c == ';' || c == '{' || c == '}' || c == '(' || c == ')')
            {
                tokens.Add(c.ToString()); i++; continue;
            }

            // Quoted string: extract verbatim, prefix with \u0001 marker
            // so MakeCreate can pull it out of the token list.
            if (c == '"')
            {
                int start = ++i;
                var inner = new StringBuilder();
                while (i < s.Length && s[i] != '"')
                {
                    inner.Append(s[i]);
                    i++;
                }
                if (i < s.Length) i++; // closing "
                // Collapse any whitespace runs to single spaces — DS1
                // wraps long param strings across multiple lines.
                var collapsed = CollapseWs(inner.ToString());
                tokens.Add("\u0001" + collapsed);
                continue;
            }

            // v<x y z> vector literal — collapse the whole thing into
            // one token so parameters that take a 3-vec stay together.
            if (c == 'v' && i + 1 < s.Length && s[i + 1] == '<')
            {
                int start = i;
                while (i < s.Length && s[i] != '>') i++;
                if (i < s.Length) i++;
                tokens.Add(s.Substring(start, i - start));
                continue;
            }

            // <angle-quoted-arg> e.g. call fireball <offset(0,0,-.3)>;
            if (c == '<')
            {
                int start = i;
                int depth = 0;
                while (i < s.Length)
                {
                    if (s[i] == '<') depth++;
                    else if (s[i] == '>') { depth--; if (depth == 0) { i++; break; } }
                    i++;
                }
                tokens.Add(s.Substring(start, i - start));
                continue;
            }

            // Otherwise: word token — gobble until whitespace or break char.
            int wordStart = i;
            while (i < s.Length)
            {
                char d = s[i];
                if (char.IsWhiteSpace(d)) break;
                if (d == ';' || d == '{' || d == '}' || d == '"' || d == '<') break;
                // Phase 21-SC-SPELL-VISUAL-F — also break on `(` and `)`
                // so DS1's `if(` / `if (` / `else (` author shapes both
                // tokenize the same way. The `radius(0)` and similar
                // parenthesized argument forms only appear INSIDE the
                // sfx_create param string, which is lexed separately as
                // a quoted region above — splitting parens here doesn't
                // touch them.
                if (d == '(' || d == ')') break;
                // Keep []{} together inside a word so [0,0,0] / [N] stay glued.
                i++;
            }
            if (i > wordStart) tokens.Add(s.Substring(wordStart, i - wordStart));
        }
        return tokens;
    }

    static string CollapseWs(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool prevWs = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevWs) sb.Append(' ');
                prevWs = true;
            }
            else
            {
                sb.Append(ch);
                prevWs = false;
            }
        }
        return sb.ToString().Trim();
    }
}
