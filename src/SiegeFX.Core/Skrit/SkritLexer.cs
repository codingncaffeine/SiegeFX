using System.Globalization;
using System.Text;

namespace SiegeFX.Core.Skrit;

/// <summary>Tokenises Skrit source. Handles DS1's authoring conventions directly:
/// trailing <c>$</c> on identifiers, case-insensitive keywords, C-style block/line
/// comments, four-char <c>'abcd'</c> int literals, and the usual C-family numeric/
/// string/operator grammar.
///
/// Not a parser — produces a flat token stream with source positions. Parser lives in
/// Phase 8a parser slice; whitespace and comments are stripped here so the parser never
/// sees them.</summary>
public static class SkritLexer
{
    private static readonly Dictionary<string, SkritTokenKind> Keywords =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["property"] = SkritTokenKind.KwProperty,
        ["state"]    = SkritTokenKind.KwState,
        ["startup"]  = SkritTokenKind.KwStartup,
        ["event"]    = SkritTokenKind.KwEvent,
        ["trigger"]  = SkritTokenKind.KwTrigger,
        ["if"]       = SkritTokenKind.KwIf,
        ["else"]     = SkritTokenKind.KwElse,
        ["while"]    = SkritTokenKind.KwWhile,
        ["for"]      = SkritTokenKind.KwFor,
        ["return"]   = SkritTokenKind.KwReturn,
        ["setstate"] = SkritTokenKind.KwSetState,
        ["true"]     = SkritTokenKind.KwTrue,
        ["false"]    = SkritTokenKind.KwFalse,
        ["null"]     = SkritTokenKind.KwNull,
        ["transition"] = SkritTokenKind.KwTransition,
        ["at"]       = SkritTokenKind.KwAt,
        ["frames"]   = SkritTokenKind.KwFrames,
        ["seconds"]  = SkritTokenKind.KwSeconds,
        ["doc"]      = SkritTokenKind.KwDoc,
        ["int"]      = SkritTokenKind.KwInt,
        ["float"]    = SkritTokenKind.KwFloat,
        ["bool"]     = SkritTokenKind.KwBool,
        ["string"]   = SkritTokenKind.KwString,
        ["void"]     = SkritTokenKind.KwVoid,
    };

    public static IReadOnlyList<SkritToken> Tokenize(string source)
    {
        var tokens = new List<SkritToken>();
        int i = 0, line = 1, col = 1;
        int n = source.Length;

        while (i < n)
        {
            char c = source[i];

            // Line endings — accept LF, CR, CRLF.
            if (c == '\r')
            {
                i++;
                if (i < n && source[i] == '\n') i++;
                line++; col = 1;
                continue;
            }
            if (c == '\n')
            {
                i++; line++; col = 1;
                continue;
            }

            // Whitespace.
            if (c == ' ' || c == '\t')
            {
                i++; col++;
                continue;
            }

            // Preprocessor directive: `#...` to end of line. Common forms: `#include "x.skrit"`,
            // `#only( game )`. Shipped data has these both at col 1 and indented inside blocks
            // (NPC dialogue skrits embed `#only( game ) [[ ... ]]` inside event bodies), so
            // col doesn't gate the rule — any `#` starts a directive.
            if (c == '#')
            {
                int start = i;
                int startLine2 = line, startCol2 = col;
                while (i < n && source[i] != '\n' && source[i] != '\r') { i++; col++; }
                tokens.Add(new SkritToken(
                    SkritTokenKind.PreprocessorDirective,
                    source.Substring(start, i - start),
                    startLine2, startCol2));
                continue;
            }

            // Line comment // ... \n
            if (c == '/' && i + 1 < n && source[i + 1] == '/')
            {
                while (i < n && source[i] != '\n' && source[i] != '\r') { i++; col++; }
                continue;
            }

            // Block comment /* ... */ (no nesting — verify during fuzz).
            if (c == '/' && i + 1 < n && source[i + 1] == '*')
            {
                i += 2; col += 2;
                while (i + 1 < n && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    if (source[i] == '\n') { line++; col = 1; }
                    else if (source[i] == '\r')
                    {
                        if (i + 1 < n && source[i + 1] == '\n') { i++; }
                        line++; col = 1;
                    }
                    else col++;
                    i++;
                }
                if (i + 1 >= n) throw new InvalidDataException($"unterminated block comment at line {line}");
                i += 2; col += 2;
                continue;
            }

            int startLine = line, startCol = col;

            // Identifier / keyword. Letters + underscore to start; letters, digits, underscore inside.
            // Trailing $ is consumed if present; it's part of the identifier in Skrit.
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                {
                    i++; col++;
                }
                // Absorb a run of `$` chars. Normally one (Skrit's scope sigil); shipped data has
                // a `job$$.MarkForDeletion(...)` typo the DS1 parser tolerated — we mirror that.
                while (i < n && source[i] == '$') { i++; col++; }
                var text = source.Substring(start, i - start);
                var kind = Keywords.TryGetValue(text, out var kw) ? kw : SkritTokenKind.Identifier;
                tokens.Add(new SkritToken(kind, text, startLine, startCol));
                continue;
            }

            // Numeric literal. Supports int (`123`, `0x1A`) and float (`1.5`, `.25`, `1.5e-3`).
            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(source[i + 1])))
            {
                int start = i;
                bool isFloat = false;
                bool isHex = false;

                if (c == '0' && i + 1 < n && (source[i + 1] == 'x' || source[i + 1] == 'X'))
                {
                    isHex = true;
                    i += 2; col += 2;
                    while (i < n && IsHexDigit(source[i])) { i++; col++; }
                }
                else
                {
                    while (i < n && char.IsDigit(source[i])) { i++; col++; }
                    if (i < n && source[i] == '.')
                    {
                        isFloat = true;
                        i++; col++;
                        while (i < n && char.IsDigit(source[i])) { i++; col++; }
                    }
                    if (i < n && (source[i] == 'e' || source[i] == 'E'))
                    {
                        isFloat = true;
                        i++; col++;
                        if (i < n && (source[i] == '+' || source[i] == '-')) { i++; col++; }
                        while (i < n && char.IsDigit(source[i])) { i++; col++; }
                    }
                }

                var text = source.Substring(start, i - start);
                tokens.Add(new SkritToken(
                    isFloat ? SkritTokenKind.FloatLiteral : SkritTokenKind.IntLiteral,
                    text, startLine, startCol));

                // Suppress unused-var warning when isHex is only a decode-helper today.
                _ = isHex;
                continue;
            }

            // String literal "..." — C-style escapes (\", \\, \n, \t). Shipped data uses \"
            // routinely for embedded-quote report messages like report.Warning("say \"hi\"").
            // Unknown escapes are passed through as the raw following char to avoid rejecting
            // on novel sequences; the parser gets the decoded text.
            if (c == '"')
            {
                i++; col++;
                var sb = new StringBuilder();
                while (i < n && source[i] != '"')
                {
                    char ch = source[i];
                    if (ch == '\\' && i + 1 < n)
                    {
                        char esc = source[i + 1];
                        switch (esc)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case 'r': sb.Append('\r'); break;
                            case '\\': sb.Append('\\'); break;
                            case '"': sb.Append('"'); break;
                            default:  sb.Append(esc); break;
                        }
                        i += 2; col += 2;
                        continue;
                    }
                    if (ch == '\n') { line++; col = 1; }
                    else col++;
                    sb.Append(ch);
                    i++;
                }
                if (i >= n) throw new InvalidDataException($"unterminated string at line {startLine}");
                i++; col++; // closing quote
                tokens.Add(new SkritToken(SkritTokenKind.StringLiteral, sb.ToString(), startLine, startCol));
                continue;
            }

            // Four-char literal 'abcd' — always exactly 4 chars, packs to int. DS1 emits these for
            // message discriminators like 'lfdn' (left-foot-down). Single-char / shorter forms do
            // not appear in shipped data; if one shows up, the fuzz run will flag it.
            if (c == '\'')
            {
                i++; col++;
                var sb = new StringBuilder();
                while (i < n && source[i] != '\'' && source[i] != '\n')
                {
                    sb.Append(source[i]);
                    i++; col++;
                }
                if (i >= n || source[i] != '\'')
                    throw new InvalidDataException($"unterminated char literal at line {startLine}");
                i++; col++;
                tokens.Add(new SkritToken(SkritTokenKind.FourCharLiteral, sb.ToString(), startLine, startCol));
                continue;
            }

            // Punctuation / operators. Multi-char forms checked before single-char.
            SkritTokenKind kindP;
            int span = 1;
            switch (c)
            {
                case '{': kindP = SkritTokenKind.LBrace; break;
                case '}': kindP = SkritTokenKind.RBrace; break;
                case '(': kindP = SkritTokenKind.LParen; break;
                case ')': kindP = SkritTokenKind.RParen; break;
                case '[': kindP = SkritTokenKind.LBracket; break;
                case ']': kindP = SkritTokenKind.RBracket; break;
                case ';': kindP = SkritTokenKind.Semicolon; break;
                case ',': kindP = SkritTokenKind.Comma; break;
                case '.': kindP = SkritTokenKind.Dot; break;
                case ':': kindP = SkritTokenKind.Colon; break;
                case '?': kindP = SkritTokenKind.Question; break;

                case '~':
                    // `~=` is DS1's string / case-insensitive equality operator; bare `~` is
                    // bitwise NOT (not observed yet but the token is cheap to provide).
                    if (i + 1 < n && source[i + 1] == '=') { kindP = SkritTokenKind.TildeEq; span = 2; }
                    else kindP = SkritTokenKind.Tilde;
                    break;

                case '=':
                    if (i + 1 < n && source[i + 1] == '=') { kindP = SkritTokenKind.EqEq; span = 2; }
                    else kindP = SkritTokenKind.Assign;
                    break;
                case '!':
                    if (i + 1 < n && source[i + 1] == '=') { kindP = SkritTokenKind.NotEq; span = 2; }
                    else kindP = SkritTokenKind.Bang;
                    break;
                case '<':
                    if (i + 1 < n && source[i + 1] == '<') { kindP = SkritTokenKind.LeftShift; span = 2; }
                    else if (i + 1 < n && source[i + 1] == '=') { kindP = SkritTokenKind.LtEq; span = 2; }
                    else kindP = SkritTokenKind.Lt;
                    break;
                case '>':
                    if (i + 1 < n && source[i + 1] == '>') { kindP = SkritTokenKind.RightShift; span = 2; }
                    else if (i + 1 < n && source[i + 1] == '=') { kindP = SkritTokenKind.GtEq; span = 2; }
                    else kindP = SkritTokenKind.Gt;
                    break;
                case '+':
                    if (i + 1 < n && source[i + 1] == '=') { kindP = SkritTokenKind.PlusAssign; span = 2; }
                    else kindP = SkritTokenKind.Plus;
                    break;
                case '-':
                    if (i + 1 < n && source[i + 1] == '=') { kindP = SkritTokenKind.MinusAssign; span = 2; }
                    else if (i + 1 < n && source[i + 1] == '>') { kindP = SkritTokenKind.Arrow; span = 2; }
                    else kindP = SkritTokenKind.Minus;
                    break;
                case '*':
                    if (i + 1 < n && source[i + 1] == '=') { kindP = SkritTokenKind.StarAssign; span = 2; }
                    else kindP = SkritTokenKind.Star;
                    break;
                case '/':
                    if (i + 1 < n && source[i + 1] == '=') { kindP = SkritTokenKind.SlashAssign; span = 2; }
                    else kindP = SkritTokenKind.Slash;
                    break;
                case '%': kindP = SkritTokenKind.Percent; break;
                case '&':
                    if (i + 1 < n && source[i + 1] == '&') { kindP = SkritTokenKind.AndAnd; span = 2; }
                    else if (i + 1 < n && source[i + 1] == '=') { kindP = SkritTokenKind.AmpAssign; span = 2; }
                    else kindP = SkritTokenKind.Ampersand;
                    break;
                case '|':
                    if (i + 1 < n && source[i + 1] == '|') { kindP = SkritTokenKind.OrOr; span = 2; }
                    else if (i + 1 < n && source[i + 1] == '=') { kindP = SkritTokenKind.PipeAssign; span = 2; }
                    else kindP = SkritTokenKind.Pipe;
                    break;
                case '^':
                    if (i + 1 < n && source[i + 1] == '=') { kindP = SkritTokenKind.CaretAssign; span = 2; }
                    else kindP = SkritTokenKind.Caret;
                    break;

                default:
                    throw new InvalidDataException(
                        $"unexpected character '{c}' (U+{(int)c:X4}) at line {startLine}:{startCol}");
            }

            tokens.Add(new SkritToken(kindP, source.Substring(i, span), startLine, startCol));
            i += span; col += span;
        }

        tokens.Add(new SkritToken(SkritTokenKind.EndOfFile, "", line, col));
        return tokens;
    }

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    /// <summary>Packs a four-char literal body to the same <c>uint</c> DS1 emits at runtime.
    /// Byte order: first char in high byte (big-endian pack). Verified empirically when fuzz
    /// runs; unit-test target if anything shifts.</summary>
    public static uint PackFourCharLiteral(string body)
    {
        if (body.Length != 4)
            throw new ArgumentException($"four-char literal body must be exactly 4 chars, got {body.Length}", nameof(body));
        return (uint)(((byte)body[0] << 24) | ((byte)body[1] << 16) | ((byte)body[2] << 8) | (byte)body[3]);
    }
}
