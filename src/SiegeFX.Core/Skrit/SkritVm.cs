using System;
using System.Collections.Generic;
using System.Globalization;

namespace SiegeFX.Core.Skrit;

/// <summary>Stack-based interpreter for <see cref="SkritCodeChunk"/> bytecode. One instance
/// owns the per-script globals dictionary; host-facing lookups are routed through the
/// injected <see cref="IHostBridge"/>. Execute individual chunks via <see cref="Run"/> —
/// the VM doesn't schedule state transitions or chores; Phase 8d's actor runtime does.
///
/// Arithmetic is type-promoting: int op int = int; mixing any float promotes to float.
/// String + anything (and anything + string) concatenates. Comparison mirrors C#:
/// <c>==</c> and <c>!=</c> are strict, <c>~=</c> is case-insensitive string compare,
/// and ordered comparisons coerce to float.</summary>
public sealed class SkritVm
{
    readonly SkritProgram _program;
    readonly IHostBridge _host;
    readonly Dictionary<string, SkritValue> _globals = new(StringComparer.Ordinal);

    public SkritVm(SkritProgram program, IHostBridge host)
    {
        _program = program;
        _host = host;
        // Seed globals as null, then run the compiler-synthesised @__init__ chunk so
        // properties / fields with declared initialisers hold their defaults before
        // any user-level handler runs.
        foreach (var g in program.Globals)
            _globals[g.Key] = SkritValue.Null;
        if (program.ChunksByName.TryGetValue("@__init__", out var init))
            Run(init, Array.Empty<SkritValue>());
    }

    public IReadOnlyDictionary<string, SkritValue> Globals => _globals;
    public void SetGlobal(string name, SkritValue v) => _globals[name] = v;
    public SkritValue GetGlobal(string name) => _globals.TryGetValue(name, out var v) ? v : SkritValue.Null;

    public SkritValue Run(string chunkName, params SkritValue[] args)
    {
        if (!_program.ChunksByName.TryGetValue(chunkName, out var chunk))
            throw new InvalidOperationException($"no chunk named '{chunkName}'");
        return Run(chunk, args);
    }

    public SkritValue Run(SkritCodeChunk chunk, SkritValue[] args)
    {
        var locals = new SkritValue[chunk.LocalCount];
        for (int i = 0; i < args.Length && i < chunk.ParamCount; i++) locals[i] = args[i];

        var stack = new Stack<SkritValue>(32);
        var bc = chunk.Bytecode;
        int ip = 0;

        while (ip < bc.Length)
        {
            var op = (SkritOpcode)bc[ip++];
            switch (op)
            {
                case SkritOpcode.PushNull:  stack.Push(SkritValue.Null); break;
                case SkritOpcode.PushTrue:  stack.Push(SkritValue.True); break;
                case SkritOpcode.PushFalse: stack.Push(SkritValue.False); break;
                case SkritOpcode.PushInt:   stack.Push(SkritValue.FromInt(chunk.IntConstants[ReadU16(bc, ref ip)])); break;
                case SkritOpcode.PushFloat: stack.Push(SkritValue.FromFloat(chunk.FloatConstants[ReadU16(bc, ref ip)])); break;
                case SkritOpcode.PushString:stack.Push(SkritValue.FromString(chunk.StringConstants[ReadU16(bc, ref ip)])); break;
                case SkritOpcode.Pop:       stack.Pop(); break;
                case SkritOpcode.Dup:       stack.Push(stack.Peek()); break;

                case SkritOpcode.LoadLocal: stack.Push(locals[ReadU16(bc, ref ip)]); break;
                case SkritOpcode.StoreLocal: locals[ReadU16(bc, ref ip)] = stack.Pop(); break;
                case SkritOpcode.LoadGlobal: stack.Push(_globals[chunk.Names[ReadU16(bc, ref ip)]]); break;
                case SkritOpcode.StoreGlobal: _globals[chunk.Names[ReadU16(bc, ref ip)]] = stack.Pop(); break;
                case SkritOpcode.LoadExtern: stack.Push(_host.GetExtern(chunk.Names[ReadU16(bc, ref ip)])); break;
                case SkritOpcode.StoreExtern: _host.SetExtern(chunk.Names[ReadU16(bc, ref ip)], stack.Pop()); break;

                case SkritOpcode.LoadMember:
                {
                    var name = chunk.Names[ReadU16(bc, ref ip)];
                    var recv = stack.Pop();
                    stack.Push(_host.GetMember(recv, name));
                    break;
                }
                case SkritOpcode.StoreMember:
                {
                    var name = chunk.Names[ReadU16(bc, ref ip)];
                    var value = stack.Pop();
                    var recv = stack.Pop();
                    _host.SetMember(recv, name, value);
                    break;
                }
                case SkritOpcode.Call:
                {
                    var name = chunk.Names[ReadU16(bc, ref ip)];
                    int argc = bc[ip++];
                    var ar = PopArgs(stack, argc);
                    // Prefer a compiled script function with the matching '@name' key.
                    if (_program.ChunksByName.TryGetValue("@" + name, out var target))
                        stack.Push(Run(target, ar));
                    else
                        stack.Push(_host.CallExtern(name, ar));
                    break;
                }
                case SkritOpcode.CallMember:
                {
                    var name = chunk.Names[ReadU16(bc, ref ip)];
                    int argc = bc[ip++];
                    var ar = PopArgs(stack, argc);
                    var recv = stack.Pop();
                    stack.Push(_host.CallMember(recv, name, ar));
                    break;
                }

                case SkritOpcode.Add: BinArith(stack, op); break;
                case SkritOpcode.Sub: BinArith(stack, op); break;
                case SkritOpcode.Mul: BinArith(stack, op); break;
                case SkritOpcode.Div: BinArith(stack, op); break;
                case SkritOpcode.Mod: BinArith(stack, op); break;
                case SkritOpcode.Pow: BinArith(stack, op); break;
                case SkritOpcode.Neg: { var v = stack.Pop(); stack.Push(v.Tag == SkritValueTag.Float ? SkritValue.FromFloat(-v.AsFloat) : SkritValue.FromInt(-v.AsInt)); break; }
                case SkritOpcode.Not: { var v = stack.Pop(); stack.Push(SkritValue.FromBool(!v.AsBool)); break; }
                case SkritOpcode.Eq: { var r = stack.Pop(); var l = stack.Pop(); stack.Push(SkritValue.FromBool(l.Equals(r))); break; }
                case SkritOpcode.NotEq: { var r = stack.Pop(); var l = stack.Pop(); stack.Push(SkritValue.FromBool(!l.Equals(r))); break; }
                case SkritOpcode.TildeEq:
                {
                    var r = stack.Pop(); var l = stack.Pop();
                    stack.Push(SkritValue.FromBool(string.Equals(l.AsString, r.AsString, StringComparison.OrdinalIgnoreCase)));
                    break;
                }
                case SkritOpcode.Lt:   { var r = stack.Pop(); var l = stack.Pop(); stack.Push(SkritValue.FromBool(l.AsFloat <  r.AsFloat)); break; }
                case SkritOpcode.LtEq: { var r = stack.Pop(); var l = stack.Pop(); stack.Push(SkritValue.FromBool(l.AsFloat <= r.AsFloat)); break; }
                case SkritOpcode.Gt:   { var r = stack.Pop(); var l = stack.Pop(); stack.Push(SkritValue.FromBool(l.AsFloat >  r.AsFloat)); break; }
                case SkritOpcode.GtEq: { var r = stack.Pop(); var l = stack.Pop(); stack.Push(SkritValue.FromBool(l.AsFloat >= r.AsFloat)); break; }

                case SkritOpcode.BitAnd: { var r = stack.Pop(); var l = stack.Pop(); stack.Push(SkritValue.FromInt(l.AsInt & r.AsInt)); break; }
                case SkritOpcode.BitOr:  { var r = stack.Pop(); var l = stack.Pop(); stack.Push(SkritValue.FromInt(l.AsInt | r.AsInt)); break; }
                case SkritOpcode.BitXor: { var r = stack.Pop(); var l = stack.Pop(); stack.Push(SkritValue.FromInt(l.AsInt ^ r.AsInt)); break; }
                case SkritOpcode.BitNot: { var v = stack.Pop(); stack.Push(SkritValue.FromInt(~v.AsInt)); break; }
                case SkritOpcode.Shl:    { var r = stack.Pop(); var l = stack.Pop(); stack.Push(SkritValue.FromInt(l.AsInt << (int)r.AsInt)); break; }
                case SkritOpcode.Shr:    { var r = stack.Pop(); var l = stack.Pop(); stack.Push(SkritValue.FromInt(l.AsInt >> (int)r.AsInt)); break; }

                case SkritOpcode.Jump:        ip += ReadI16(bc, ref ip); break;
                case SkritOpcode.JumpIfFalse:
                {
                    int delta = ReadI16(bc, ref ip);
                    if (!stack.Pop().AsBool) ip += delta;
                    break;
                }
                case SkritOpcode.JumpIfTrue:
                {
                    int delta = ReadI16(bc, ref ip);
                    if (stack.Pop().AsBool) ip += delta;
                    break;
                }
                case SkritOpcode.Return:     return stack.Pop();
                case SkritOpcode.ReturnVoid: return SkritValue.Null;

                case SkritOpcode.SetState: _host.SetState(chunk.Names[ReadU16(bc, ref ip)]); break;
                case SkritOpcode.Halt: return stack.Count > 0 ? stack.Pop() : SkritValue.Null;

                default: throw new InvalidOperationException($"unknown opcode {op} at ip={ip - 1}");
            }
        }
        return SkritValue.Null;
    }

    static void BinArith(Stack<SkritValue> stack, SkritOpcode op)
    {
        var r = stack.Pop(); var l = stack.Pop();

        // String concatenation for Add with any string operand.
        if (op == SkritOpcode.Add && (l.Tag == SkritValueTag.String || r.Tag == SkritValueTag.String))
        {
            stack.Push(SkritValue.FromString(l.AsString + r.AsString));
            return;
        }

        bool asFloat = l.Tag == SkritValueTag.Float || r.Tag == SkritValueTag.Float || op == SkritOpcode.Div || op == SkritOpcode.Pow;
        if (asFloat)
        {
            double dl = l.AsFloat, dr = r.AsFloat;
            double result = op switch
            {
                SkritOpcode.Add => dl + dr,
                SkritOpcode.Sub => dl - dr,
                SkritOpcode.Mul => dl * dr,
                SkritOpcode.Div => dl / dr,
                SkritOpcode.Mod => dl % dr,
                SkritOpcode.Pow => Math.Pow(dl, dr),
                _ => 0,
            };
            stack.Push(SkritValue.FromFloat(result));
            return;
        }

        long il = l.AsInt, ir = r.AsInt;
        long iresult = op switch
        {
            SkritOpcode.Add => il + ir,
            SkritOpcode.Sub => il - ir,
            SkritOpcode.Mul => il * ir,
            SkritOpcode.Mod => ir == 0 ? 0 : il % ir,
            _ => 0,
        };
        stack.Push(SkritValue.FromInt(iresult));
    }

    static SkritValue[] PopArgs(Stack<SkritValue> stack, int n)
    {
        var a = new SkritValue[n];
        for (int i = n - 1; i >= 0; i--) a[i] = stack.Pop();
        return a;
    }

    static int ReadU16(byte[] bc, ref int ip)
    {
        int v = bc[ip] | (bc[ip + 1] << 8); ip += 2; return v;
    }
    static int ReadI16(byte[] bc, ref int ip)
    {
        int v = bc[ip] | (bc[ip + 1] << 8); ip += 2; return (short)v;
    }
}
