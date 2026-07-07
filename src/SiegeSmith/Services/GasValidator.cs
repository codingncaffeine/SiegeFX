using System;
using System.Collections.Generic;
using SiegeFX.Core.Assets;

namespace SiegeSmith.Services;

/// <summary>Validates GAS source by running it through the engine's own parser, so the editor's
/// verdict matches exactly what the game would accept. Returns a friendly ok/error message the
/// editor surfaces live as the user types.</summary>
public static class GasValidator
{
    public static (bool Ok, string Message) Validate(string text)
    {
        try
        {
            var doc = GasDocument.Parse(text);
            int blocks = 0, attrs = 0;
            Count(doc.Roots, ref blocks, ref attrs);
            return (true, $"Valid — {blocks} block(s), {attrs} attribute(s)");
        }
        catch (Exception ex)
        {
            // GasDocument throws InvalidDataException with a "line N (pos P): message" prefix.
            return (false, ex.Message);
        }
    }

    private static void Count(IReadOnlyList<GasNode> nodes, ref int blocks, ref int attrs)
    {
        foreach (var n in nodes)
        {
            blocks++;
            attrs += n.Attributes.Count;
            Count(n.Children, ref blocks, ref attrs);
        }
    }
}
