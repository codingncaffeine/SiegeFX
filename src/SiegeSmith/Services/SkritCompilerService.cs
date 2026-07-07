using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SiegeFX.Core.Skrit;

namespace SiegeSmith.Services;

/// <summary>Runs Skrit source through the engine's full front-end — lex → parse → bind → compile
/// — and reports the outcome: a friendly status line, parser/binder diagnostics, the catalogue
/// of unresolved host symbols (externs), and a bytecode disassembly. Mirrors exactly what the
/// game's own toolchain would accept.</summary>
public static class SkritCompilerService
{
    public static SkritCompileResult Compile(string source)
    {
        try
        {
            var script = SkritParser.Parse(source);            // throws on a parse error
            var bind = new SkritBinder(script).Bind();
            var program = new SkritCompiler(script, bind).Compile();

            var diags = bind.Diagnostics.Select(d => d.ToString()).ToList();

            var sb = new StringBuilder();
            foreach (var chunk in program.Chunks)
            {
                sb.Append(SkritDisassembler.Dump(chunk));
                sb.AppendLine();
            }

            var ok = !bind.HasErrors;
            var status = ok
                ? $"Compiled — {program.Chunks.Count} chunk(s), {program.Externs.Count} extern(s)"
                : $"Bound with {bind.Diagnostics.Count} diagnostic(s)";
            return new SkritCompileResult(ok, status, diags, program.Externs.ToList(), sb.ToString());
        }
        catch (Exception ex)
        {
            return new SkritCompileResult(false, "Parse error", new[] { ex.Message }, Array.Empty<string>(), "");
        }
    }
}

/// <summary>Outcome of compiling Skrit source for display in the editor.</summary>
public sealed record SkritCompileResult(
    bool Ok,
    string Status,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> Externs,
    string Disassembly);
