using System.Collections.Generic;

namespace SiegeFX.Core.Skrit;

/// <summary>A compiled block of Skrit bytecode — one per function, event/trigger handler,
/// scheduled chore, transition body, or state entry. <see cref="Names"/> holds string
/// identifiers (locals, globals, externs, members); the other pools hold literal constants
/// referenced by <see cref="SkritOpcode.PushInt"/> / <c>PushFloat</c> / <c>PushString</c>.
/// <see cref="LocalCount"/> is the number of local slots the VM must allocate on entry.</summary>
public sealed class SkritCodeChunk
{
    public required string Name { get; init; }
    public required byte[] Bytecode { get; init; }
    public required IReadOnlyList<long> IntConstants { get; init; }
    public required IReadOnlyList<double> FloatConstants { get; init; }
    public required IReadOnlyList<string> StringConstants { get; init; }
    public required IReadOnlyList<string> Names { get; init; }
    public required int LocalCount { get; init; }
    public required int ParamCount { get; init; }
}

/// <summary>The fully-compiled view of a Skrit source file. States, handlers, and script
/// functions are keyed by their source names. The binder's extern list is propagated so
/// the host can pre-resolve callables; everything else is resolved lazily at runtime.</summary>
public sealed class SkritProgram
{
    public required SkritScript Script { get; init; }
    public required IReadOnlyDictionary<string, SkritSymbol> Globals { get; init; }
    public required IReadOnlyDictionary<string, SkritSymbol> States { get; init; }
    public required IReadOnlyList<string> Externs { get; init; }
    public required IReadOnlyList<SkritCodeChunk> Chunks { get; init; }
    public required IReadOnlyDictionary<string, SkritCodeChunk> ChunksByName { get; init; }
}
