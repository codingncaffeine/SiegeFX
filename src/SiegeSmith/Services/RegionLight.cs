using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace SiegeSmith.Services;

public enum AuthoredLightKind { Directional, Point }

/// <summary>A region light authored in the World Builder. Only directional lights with
/// <see cref="AffectsActors"/> reach the SiegeFX renderer (the engine caps them at 4); point lights
/// ship into lights.gas for retail compatibility but render no illumination in the engine.</summary>
public sealed class AuthoredLight
{
    public uint Scid;
    public AuthoredLightKind Kind = AuthoredLightKind.Directional;
    public uint Color = 0xFFFFFFFF;                    // 0xAARRGGBB — alpha ignored by the engine
    public float Intensity = 1f;
    public bool DrawShadow;
    public bool AffectsActors = true;
    public bool AffectsItems = true;
    public bool AffectsTerrain = true;
    public Vector3 Direction = new(0.3f, 0.3f, -0.9f); // world-space (directional light); node anchor vestigial
    public Vector3 Position;                            // node-local position (point light)
    public uint NodeGuid;                               // anchor node for a point light
    public float InnerRadius = 1f;
    public float OuterRadius = 12f;

    public string Label => Kind == AuthoredLightKind.Point ? "Point light (author-only)" : "Directional light";
    public string Detail => $"0x{Color:X8} · int {Intensity.ToString("0.0#", CultureInfo.InvariantCulture)}";

    public AuthoredLight Clone() => (AuthoredLight)MemberwiseClone();
}

/// <summary>Writes the region's <c>lights/lights.gas</c>: one <c>[lights]</c> root with a
/// <c>[t:directional]</c> / <c>[t:point]</c> child per light. Shapes follow the engine's LightStore.</summary>
public static class LightsGasWriter
{
    public static string Write(IReadOnlyList<AuthoredLight> lights)
    {
        var sb = new StringBuilder();
        sb.Append("[lights]\r\n{\r\n");
        foreach (var l in lights)
        {
            string kind = l.Kind == AuthoredLightKind.Point ? "point" : "directional";
            sb.Append($"\t[t:{kind},n:0x{l.Scid:X8}]\r\n\t{{\r\n");
            sb.Append($"\t\tcolor = 0x{l.Color:X8};\r\n");
            sb.Append($"\t\tintensity = {F(l.Intensity)};\r\n");
            sb.Append("\t\tactive = true;\r\n");
            sb.Append($"\t\tdraw_shadow = {B(l.DrawShadow)};\r\n");
            sb.Append($"\t\taffects_actors = {B(l.AffectsActors)};\r\n");
            sb.Append($"\t\taffects_items = {B(l.AffectsItems)};\r\n");
            sb.Append($"\t\taffects_terrain = {B(l.AffectsTerrain)};\r\n");
            if (l.Kind == AuthoredLightKind.Directional)
            {
                sb.Append("\t\t[direction]\r\n\t\t{\r\n");
                sb.Append($"\t\t\tx = {F(l.Direction.X)};\r\n\t\t\ty = {F(l.Direction.Y)};\r\n\t\t\tz = {F(l.Direction.Z)};\r\n");
                sb.Append("\t\t}\r\n");
            }
            else
            {
                sb.Append($"\t\tinner_radius = {F(l.InnerRadius)};\r\n\t\touter_radius = {F(l.OuterRadius)};\r\n");
                sb.Append("\t\t[position]\r\n\t\t{\r\n");
                sb.Append($"\t\t\tx = {F(l.Position.X)};\r\n\t\t\ty = {F(l.Position.Y)};\r\n\t\t\tz = {F(l.Position.Z)};\r\n");
                sb.Append($"\t\t\tnode = 0x{l.NodeGuid:X8};\r\n");
                sb.Append("\t\t}\r\n");
            }
            sb.Append("\t}\r\n");
        }
        sb.Append("}\r\n");
        return sb.ToString();
    }

    private static string F(float v) => v.ToString("0.0######", CultureInfo.InvariantCulture);
    private static string B(bool v) => v ? "true" : "false";
}

/// <summary>A map-global mood: name (by convention <c>map_&lt;map&gt;_&lt;region&gt;_N</c>), interior flag,
/// and the three music tracks the engine reads. Written to
/// <c>/world/global/moods/&lt;map&gt;/moods.gas</c> — mood audio is map-global, not per-region.</summary>
public sealed class AuthoredMood
{
    public string Name = "";
    public bool Interior;
    public string Ambient = "";
    public string Standard = "";
    public string Battle = "";

    public bool HasAudio => !string.IsNullOrWhiteSpace(Ambient);
}

/// <summary>Writes a map-global <c>moods.gas</c>. The engine's MoodStore reads only mood_name,
/// interior, and the three <c>[music]</c> tracks.</summary>
public static class MoodsGasWriter
{
    public static string Write(AuthoredMood m)
    {
        var sb = new StringBuilder();
        sb.Append($"[t:mood_setting,n:{m.Name}]\r\n{{\r\n");
        sb.Append($"\tmood_name = {m.Name};\r\n");
        sb.Append($"\tinterior = {(m.Interior ? "true" : "false")};\r\n");
        sb.Append("\t[music]\r\n\t{\r\n");
        if (!string.IsNullOrWhiteSpace(m.Ambient)) sb.Append($"\t\tambient_track = {m.Ambient.Trim()};\r\n");
        if (!string.IsNullOrWhiteSpace(m.Standard)) sb.Append($"\t\tstandard_track = {m.Standard.Trim()};\r\n");
        if (!string.IsNullOrWhiteSpace(m.Battle)) sb.Append($"\t\tbattle_track = {m.Battle.Trim()};\r\n");
        sb.Append("\t}\r\n}\r\n");
        return sb.ToString();
    }
}
