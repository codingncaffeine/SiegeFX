namespace SiegeFX.Core.Skrit;

/// <summary>Kinds of symbol the binder recognises.</summary>
public enum SkritSymbolKind
{
    Property, Field, StateField, Local, Parameter,
    Function, EventHandler, TriggerHandler, State, ScheduledChore,
    Owner, Self,
    Extern, // unresolved / host-API identifier catalogued for downstream resolution
}

/// <summary>A bound declaration. <see cref="Type"/> is the declared type string (e.g. "int",
/// "Go", "eAnimChore") or null when untyped (e.g. a state or handler). External symbols
/// carry <c>"?"</c> to mean "unknown, resolve at host-API binding time".</summary>
public sealed record SkritSymbol(
    string Name,
    SkritSymbolKind Kind,
    string? Type,
    SkritNode? Declaration)
{
    public override string ToString() =>
        $"{Kind} {Name}{(Type is null ? "" : " : " + Type)}";
}

/// <summary>Diagnostic emitted by the binder. Severity is implicit: non-empty list = errors.</summary>
public sealed record SkritDiagnostic(string Message, int Line, int Column)
{
    public override string ToString() => $"line {Line}:{Column}: {Message}";
}

/// <summary>Result of binding a single script. <see cref="Externs"/> contains unique names
/// (identifier roots) the binder couldn't resolve locally — candidates for the host-API
/// catalogue in Phase 8c's VM wiring.</summary>
public sealed class SkritBindResult
{
    public required SkritScript Script { get; init; }
    public required IReadOnlyDictionary<string, SkritSymbol> Globals { get; init; }
    public required IReadOnlyDictionary<string, SkritSymbol> States { get; init; }
    public required IReadOnlyList<string> Externs { get; init; }
    public required IReadOnlyList<SkritDiagnostic> Diagnostics { get; init; }

    public bool HasErrors => Diagnostics.Count > 0;
}
