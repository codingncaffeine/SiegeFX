using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace SiegeSmith.Services;

/// <summary>A particle emitter authored on a node. Ships to <c>objects/emitter.gas</c> as a legacy
/// <c>[particle_emitter]</c> block — the one per-region particle path SiegeFX turns into live particles.
/// The engine reads only count/rgb/fade/particle_size/growth/dark; <c>dark=true</c> forces smoke.</summary>
public sealed class RegionEmitter
{
    public uint Scid;
    public string Template = "emt_generic"; // any emitter template — the legacy block drives the look
    public uint NodeGuid;
    public Vector3 LocalPos;
    public bool Smoke;                       // dark=true → smoke; else fire
    public int Count = 40;
    public float Fade = 1.2f;
    public float ParticleSize = 0.4f;
    public float Growth;

    public string Label => Smoke ? "Smoke emitter" : "Fire emitter";
    public string Detail => $"count {Count} · fade {Fade.ToString("0.0#", CultureInfo.InvariantCulture)} · node 0x{NodeGuid:X8}";
}

/// <summary>A ground/wall decal. Origin is node-local (transformed by RegionLayout at load); the
/// orientation basis is WORLD-absolute (do NOT node-rotate it — that double-rotates it off the
/// surface). Ships to <c>decals/decals.gas</c>; texture resolves to <c>Art/Bitmaps/Decals/&lt;name&gt;.raw</c>.</summary>
public sealed class RegionDecal
{
    public uint Scid;
    public uint NodeGuid;
    public Vector3 OriginLocal;
    public Vector3 Normal = new(0, 0, 1); // world-space; flat ground decal points up (Z-up)
    public Vector3 AxisH = new(1, 0, 0);
    public Vector3 AxisV = new(0, 1, 0);
    public float HorizExtent = 2f;
    public float VertExtent = 2f;
    public string Texture = "";

    public string Label => string.IsNullOrWhiteSpace(Texture) ? "Decal" : $"Decal · {Texture}";
    public string Detail => $"{HorizExtent.ToString("0.#", CultureInfo.InvariantCulture)}×{VertExtent.ToString("0.#", CultureInfo.InvariantCulture)}m · node 0x{NodeGuid:X8}";
}

/// <summary>Writes <c>objects/emitter.gas</c> — top-level placements, each with a legacy
/// <c>[particle_emitter]</c> block.</summary>
public static class EmitterGasWriter
{
    public static string Write(IReadOnlyList<RegionEmitter> emitters)
    {
        var sb = new StringBuilder();
        foreach (var e in emitters)
        {
            // rgb only nudges fire-vs-smoke; the visible tint is engine-forced.
            (float r, float g, float b) = e.Smoke ? (0.3f, 0.3f, 0.3f) : (1.0f, 0.6f, 0.2f);
            sb.Append($"[t:{e.Template},n:0x{e.Scid:X8}]\r\n{{\r\n");
            sb.Append("\t[placement]\r\n\t{\r\n");
            sb.Append("\t\torientation = 0,0,0,1;\r\n");
            sb.Append($"\t\tposition = {F(e.LocalPos.X)},{F(e.LocalPos.Y)},{F(e.LocalPos.Z)},0x{e.NodeGuid:X8};\r\n");
            sb.Append("\t}\r\n");
            sb.Append("\t[particle_emitter]\r\n\t{\r\n");
            sb.Append($"\t\tcount = {e.Count};\r\n");
            sb.Append($"\t\tred = {F(r)};\r\n\t\tgreen = {F(g)};\r\n\t\tblue = {F(b)};\r\n");
            sb.Append($"\t\tfade = {F(e.Fade)};\r\n");
            sb.Append($"\t\tparticle_size = {F(e.ParticleSize)};\r\n");
            sb.Append($"\t\tgrowth = {F(e.Growth)};\r\n");
            sb.Append($"\t\tdark = {(e.Smoke ? "true" : "false")};\r\n");
            sb.Append("\t}\r\n}\r\n");
        }
        return sb.ToString();
    }

    private static string F(float v) => v.ToString("0.0######", CultureInfo.InvariantCulture);
}

/// <summary>Writes <c>decals/decals.gas</c>: a <c>[decals]</c> root with one <c>[t:decal]</c> child per
/// decal — node-local origin, world-space orientation basis, extents, texture basename.</summary>
public static class DecalGasWriter
{
    public static string Write(IReadOnlyList<RegionDecal> decals)
    {
        var sb = new StringBuilder();
        sb.Append("[decals]\r\n{\r\n");
        foreach (var d in decals)
        {
            sb.Append($"\t[t:decal,n:0x{d.Scid:X8}]\r\n\t{{\r\n");
            sb.Append("\t\t[decal_origin]\r\n\t\t{\r\n");
            sb.Append($"\t\t\tx = {F(d.OriginLocal.X)};\r\n\t\t\ty = {F(d.OriginLocal.Y)};\r\n\t\t\tz = {F(d.OriginLocal.Z)};\r\n");
            sb.Append($"\t\t\tnode = 0x{d.NodeGuid:X8};\r\n\t\t}}\r\n");
            sb.Append("\t\t[decal_orientation]\r\n\t\t{\r\n");
            sb.Append($"\t\t\t[normal] {{ x = {F(d.Normal.X)}; y = {F(d.Normal.Y)}; z = {F(d.Normal.Z)}; }}\r\n");
            sb.Append($"\t\t\t[axis_h] {{ x = {F(d.AxisH.X)}; y = {F(d.AxisH.Y)}; z = {F(d.AxisH.Z)}; }}\r\n");
            sb.Append($"\t\t\t[axis_v] {{ x = {F(d.AxisV.X)}; y = {F(d.AxisV.Y)}; z = {F(d.AxisV.Z)}; }}\r\n");
            sb.Append("\t\t}\r\n");
            sb.Append($"\t\thorizontal_extent = {F(d.HorizExtent)};\r\n");
            sb.Append($"\t\tvertical_extent = {F(d.VertExtent)};\r\n");
            sb.Append($"\t\ttexture = {d.Texture.Trim()};\r\n");
            sb.Append("\t}\r\n");
        }
        sb.Append("}\r\n");
        return sb.ToString();
    }

    private static string F(float v) => v.ToString("0.0######", CultureInfo.InvariantCulture);
}
