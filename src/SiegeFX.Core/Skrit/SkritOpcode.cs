namespace SiegeFX.Core.Skrit;

/// <summary>Stack-based opcode set for the Skrit VM. Operands are encoded inline in the
/// bytecode stream: <c>u16</c> = two bytes little-endian, <c>u8</c> = one byte, <c>i16</c>
/// for jump offsets (relative to the byte after the operand). Constants (ints, floats,
/// strings, names) live in the owning <see cref="SkritCodeChunk"/>'s constant pool and
/// name table — the opcode carries only the pool index.</summary>
public enum SkritOpcode : byte
{
    // Stack primitives
    PushNull,       // ()
    PushTrue,       // ()
    PushFalse,      // ()
    PushInt,        // (u16 constIdx → IntConstants)
    PushFloat,      // (u16 constIdx → FloatConstants)
    PushString,     // (u16 constIdx → StringConstants)
    Pop,            // ()
    Dup,            // ()

    // Locals / globals / externs
    LoadLocal,      // (u16 slot)
    StoreLocal,     // (u16 slot) — stores TOS, pops
    LoadGlobal,     // (u16 nameIdx)
    StoreGlobal,    // (u16 nameIdx)
    LoadExtern,     // (u16 nameIdx) — host lookup
    StoreExtern,    // (u16 nameIdx)

    // Member access (dot)
    LoadMember,     // (u16 nameIdx) — stack: receiver → value
    StoreMember,    // (u16 nameIdx) — stack: receiver, value → (empty)
    Call,           // (u16 nameIdx, u8 argCount) — host free-call or script function
    CallMember,     // (u16 nameIdx, u8 argCount) — stack: receiver, args → result

    // Arithmetic / logical. Division and Pow always promote to float (so int 1/0 yields
    // Infinity, never a crash). Shifts mask the count modulo the operand width — a count
    // >= 64 wraps, matching C#'s long-shift semantics.
    // Note: `&&` / `||` compile to short-circuit Dup + JumpIfFalse / JumpIfTrue; there is
    // no strict boolean `And` / `Or` opcode.
    Add, Sub, Mul, Div, Mod, Pow, Neg,
    Not,
    Eq, NotEq, TildeEq,   // TildeEq = case-insensitive string compare (~= operator)
    Lt, LtEq, Gt, GtEq,
    BitAnd, BitOr, BitXor, BitNot, Shl, Shr,

    // Control flow
    Jump,           // (i16 offset)
    JumpIfFalse,    // (i16 offset)
    JumpIfTrue,     // (i16 offset)
    Return,         // () — returns TOS
    ReturnVoid,     // ()

    // Script
    SetState,       // (u16 nameIdx)
    Halt,
}
