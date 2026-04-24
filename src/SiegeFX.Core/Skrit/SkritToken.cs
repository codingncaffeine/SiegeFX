namespace SiegeFX.Core.Skrit;

/// <summary>Categorises a lexed Skrit token. Keywords are case-insensitive in shipped
/// data (<c>SetState</c> and <c>setstate</c> both occur), so the lexer normalises them
/// before tagging. Identifiers keep the trailing <c>$</c> because in Skrit it's part of
/// the name, not a sigil — stripping it collides <c>Looper$</c> with a hypothetical
/// C-side <c>Looper</c>.</summary>
public enum SkritTokenKind
{
    EndOfFile,

    // Literals
    IntLiteral,
    FloatLiteral,
    StringLiteral,
    FourCharLiteral,
    Identifier,

    // Keywords
    KwProperty, KwState, KwStartup, KwEvent, KwTrigger,
    KwIf, KwElse, KwWhile, KwFor, KwReturn, KwSetState,
    KwTrue, KwFalse, KwNull,
    KwAt, KwFrames, KwSeconds, KwDoc,
    KwInt, KwFloat, KwBool, KwString, KwVoid,
    KwTransition,

    // Punctuation
    LBrace, RBrace, LParen, RParen, LBracket, RBracket,
    Semicolon, Comma, Dot, Colon, Question,
    Arrow,            // `->`  state transition
    PreprocessorDirective, // `#include "..."` etc., whole line captured in Text

    // Operators
    Assign, EqEq, NotEq, TildeEq, Lt, LtEq, Gt, GtEq,
    Plus, Minus, Star, Slash, Percent,
    PlusAssign, MinusAssign, StarAssign, SlashAssign,
    AndAnd, OrOr, Bang,
    Pipe, Ampersand, Caret, Tilde,
    PipeAssign, AmpAssign, CaretAssign,
    LeftShift, RightShift,
}

/// <summary>Lexed token + source span. <see cref="Text"/> is the verbatim slice
/// (keyword casing preserved for diagnostics). <see cref="Line"/> and <see cref="Column"/>
/// are 1-based for human-readable errors.</summary>
public readonly record struct SkritToken(
    SkritTokenKind Kind,
    string Text,
    int Line,
    int Column)
{
    public override string ToString() => $"{Line}:{Column} {Kind} '{Text}'";
}
