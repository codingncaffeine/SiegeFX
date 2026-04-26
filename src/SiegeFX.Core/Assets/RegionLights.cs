using System.Globalization;
using System.Numerics;
using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

public enum RegionLightKind { Directional, Point }

/// <summary>One light entry from a region's <c>lights/lights.gas</c>. Color is
/// already decoded to 0..1 RGB (DS1 stores it as <c>0xAARRGGBB</c>). For
/// directional lights, <see cref="DirectionOrPosition"/> is a unit-ish
/// direction in node-local space and <see cref="NodeGuid"/> is the anchor SNO;
/// for point lights, the same field is a node-local position.</summary>
public sealed record RegionLight(
    RegionLightKind Kind,
    string Name,
    Vector3 Color,
    float Intensity,
    bool DrawShadow,
    bool AffectsActors,
    bool AffectsItems,
    bool AffectsTerrain,
    Vector3 DirectionOrPosition,
    uint NodeGuid,
    float InnerRadius,
    float OuterRadius);

/// <summary>Parses <c>{region}/lights/lights.gas</c> into a list of
/// <see cref="RegionLight"/>. Outdoor regions ship 1-2 directional sources
/// (key/fill) plus a swarm of warm point lights for torches/braziers; the
/// renderer can pick directionals for global shading and reserve points for
/// later passes. Quietly returns an empty list if the file isn't present
/// (some interior-only regions skip it).</summary>
public static class RegionLights
{
    public static (IReadOnlyList<RegionLight> Lights, IReadOnlyList<string> Diagnostics) Load(
        TankReader tank, string regionPath)
    {
        var diags = new List<string>();
        var path = regionPath.TrimEnd('/') + "/lights/lights.gas";
        if (!tank.TryGetFile(path, out _))
            return (Array.Empty<RegionLight>(), diags);

        byte[] bytes;
        try { bytes = tank.ExtractToMemory(path); }
        catch (Exception ex) { diags.Add($"{path}: extract failed: {ex.Message}"); return (Array.Empty<RegionLight>(), diags); }

        GasDocument doc;
        try { doc = GasDocument.Load(bytes); }
        catch (Exception ex) { diags.Add($"{path}: parse failed: {ex.Message}"); return (Array.Empty<RegionLight>(), diags); }

        if (doc.Roots.Count == 0) return (Array.Empty<RegionLight>(), diags);

        // The whole file is wrapped in a single [lights] block.
        var root = doc.Roots[0];
        if (!string.Equals(root.Header, "lights", StringComparison.OrdinalIgnoreCase))
            return (Array.Empty<RegionLight>(), diags);

        var list = new List<RegionLight>(root.Children.Count);
        foreach (var child in root.Children)
        {
            // Header looks like "t:directional,n:light_0xNNNN" — same shape as
            // ActorInstance, so reuse its parser.
            if (!TemplateStore.TryParseHeader(child.Header, out var typeName, out var nameField))
                continue;
            RegionLightKind kind;
            if (string.Equals(typeName, "directional", StringComparison.OrdinalIgnoreCase)) kind = RegionLightKind.Directional;
            else if (string.Equals(typeName, "point", StringComparison.OrdinalIgnoreCase)) kind = RegionLightKind.Point;
            else continue;

            var color    = DecodeArgb(FindAttr(child, "color"));
            var intensity = ParseFloat(FindAttr(child, "intensity")) ?? 1f;
            var drawShadow = ParseBool(FindAttr(child, "draw_shadow")) ?? false;
            var affectsActors = ParseBool(FindAttr(child, "affects_actors")) ?? true;
            var affectsItems = ParseBool(FindAttr(child, "affects_items")) ?? true;
            var affectsTerrain = ParseBool(FindAttr(child, "affects_terrain")) ?? true;
            var inner   = ParseFloat(FindAttr(child, "inner_radius")) ?? 0f;
            var outer   = ParseFloat(FindAttr(child, "outer_radius")) ?? 0f;

            // Direction (directional) or position (point) live in a child block.
            var dirOrPos = Vector3.Zero;
            uint nodeGuid = 0;
            var dirChild = FindChild(child, kind == RegionLightKind.Directional ? "direction" : "position");
            if (dirChild is not null)
            {
                var x = ParseFloat(FindAttr(dirChild, "x")) ?? 0f;
                var y = ParseFloat(FindAttr(dirChild, "y")) ?? 0f;
                var z = ParseFloat(FindAttr(dirChild, "z")) ?? 0f;
                dirOrPos = new Vector3(x, y, z);
                ParseHex(FindAttr(dirChild, "node"), out nodeGuid);
            }

            list.Add(new RegionLight(
                kind, nameField, color, intensity, drawShadow,
                affectsActors, affectsItems, affectsTerrain,
                dirOrPos, nodeGuid, inner, outer));
        }
        return (list, diags);
    }

    static string? FindAttr(GasNode n, string name)
    {
        for (int i = 0; i < n.Attributes.Count; i++)
            if (string.Equals(n.Attributes[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return n.Attributes[i].Value;
        return null;
    }

    static GasNode? FindChild(GasNode n, string header)
    {
        for (int i = 0; i < n.Children.Count; i++)
            if (string.Equals(n.Children[i].Header, header, StringComparison.OrdinalIgnoreCase))
                return n.Children[i];
        return null;
    }

    /// <summary>DS1 colors are <c>0xAARRGGBB</c>. Returns RGB only; alpha
    /// would need a separate accessor if a future use needs it.</summary>
    static Vector3 DecodeArgb(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return Vector3.One;
        var s = hex.Trim();
        if (s.Length >= 2 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X')) s = s[2..];
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return Vector3.One;
        var r = ((v >> 16) & 0xff) / 255f;
        var g = ((v >>  8) & 0xff) / 255f;
        var b = ( v        & 0xff) / 255f;
        return new Vector3(r, g, b);
    }

    static float? ParseFloat(string? s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;

    static bool? ParseBool(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var t = s.Trim();
        if (string.Equals(t, "true",  StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(t, "false", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    static bool ParseHex(string? s, out uint value)
    {
        value = 0;
        if (string.IsNullOrEmpty(s)) return false;
        var span = s.AsSpan().Trim();
        if (span.Length >= 2 && span[0] == '0' && (span[1] == 'x' || span[1] == 'X')) span = span[2..];
        return uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}
