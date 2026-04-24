namespace SiegeFX.Core.Assets;

/// <summary>A parsed DS1 template. The source <see cref="Node"/> keeps the full block tree
/// around for nested lookups (<c>aspect.model</c>, <c>body.chore_dictionary</c>, ...).
/// Inheritance is resolved after the whole store loads: <see cref="Specializes"/> points
/// at the parent template in the store, or null for roots like <c>actor_evil</c> whose
/// parent is itself a template and the chain bottoms out naturally.</summary>
public sealed class Template
{
    public string Name { get; }
    public string TypeTag { get; }                    // usually "template" — but category files use "category" etc.
    public string? SpecializesName { get; }
    public GasNode Node { get; }
    public string? SourcePath { get; }                // tank path we loaded this from; informational only

    public Template? Specializes { get; internal set; }

    internal Template(string name, string typeTag, string? specializesName, GasNode node, string? sourcePath)
    {
        Name = name;
        TypeTag = typeTag;
        SpecializesName = specializesName;
        Node = node;
        SourcePath = sourcePath;
    }
}
