using System;
using System.Globalization;
using System.Text;

namespace SiegeFX.Core.Skrit;

/// <summary>Pretty-prints a compiled <see cref="SkritCodeChunk"/> for debugging the
/// compiler's bytecode output. Used by <c>siegefx skrit compile &lt;file&gt;</c>.</summary>
public static class SkritDisassembler
{
    public static string Dump(SkritCodeChunk c)
    {
        var sb = new StringBuilder();
        sb.Append("chunk ").Append(c.Name)
          .Append("  params=").Append(c.ParamCount)
          .Append("  locals=").Append(c.LocalCount)
          .Append("  bytes=").Append(c.Bytecode.Length)
          .AppendLine();

        var bc = c.Bytecode;
        int ip = 0;
        while (ip < bc.Length)
        {
            int start = ip;
            var op = (SkritOpcode)bc[ip++];
            sb.Append("  ").Append(start.ToString("D4", CultureInfo.InvariantCulture)).Append("  ").Append(op);

            switch (op)
            {
                case SkritOpcode.PushInt:
                    sb.Append("  ").Append(c.IntConstants[ReadU16(bc, ref ip)]);
                    break;
                case SkritOpcode.PushFloat:
                    sb.Append("  ").Append(c.FloatConstants[ReadU16(bc, ref ip)].ToString(CultureInfo.InvariantCulture));
                    break;
                case SkritOpcode.PushString:
                    sb.Append("  \"").Append(c.StringConstants[ReadU16(bc, ref ip)]).Append('"');
                    break;
                case SkritOpcode.LoadLocal:
                case SkritOpcode.StoreLocal:
                    sb.Append("  #").Append(ReadU16(bc, ref ip));
                    break;
                case SkritOpcode.LoadGlobal:
                case SkritOpcode.StoreGlobal:
                case SkritOpcode.LoadExtern:
                case SkritOpcode.StoreExtern:
                case SkritOpcode.LoadMember:
                case SkritOpcode.StoreMember:
                case SkritOpcode.SetState:
                    sb.Append("  ").Append(c.Names[ReadU16(bc, ref ip)]);
                    break;
                case SkritOpcode.Call:
                case SkritOpcode.CallMember:
                    sb.Append("  ").Append(c.Names[ReadU16(bc, ref ip)]).Append(" #").Append(bc[ip++]);
                    break;
                case SkritOpcode.Jump:
                case SkritOpcode.JumpIfFalse:
                case SkritOpcode.JumpIfTrue:
                {
                    int delta = ReadI16(bc, ref ip);
                    sb.Append("  -> ").Append((ip + delta).ToString("D4", CultureInfo.InvariantCulture));
                    break;
                }
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    static int ReadU16(byte[] bc, ref int ip) { int v = bc[ip] | (bc[ip + 1] << 8); ip += 2; return v; }
    static int ReadI16(byte[] bc, ref int ip) { int v = bc[ip] | (bc[ip + 1] << 8); ip += 2; return (short)v; }
}
