using System.Globalization;
using System.Numerics;

namespace SiegeFX.Core.Assets;

/// <summary>SC-DECALS — parses a region's <c>decals/decals.gas</c>. DS1 projects
/// flat textures onto terrain and props: blood splats, ground scorch, burnt-wood
/// char, drop shadows, dirt, straw, rugs. Each <c>[t:decal,n:*]</c> block carries
/// a 3×3 orientation basis, a node-local origin plus the anchor node's GUID, a
/// texture, and horizontal/vertical extents in metres.
///
/// <para>Orientation is stored as three rows. Empirically (validated against the
/// fh_r1 floor rug + ground scorch, which must lie flat) row0 is the projection
/// NORMAL and row1/row2 are the in-plane axes scaled by horizontal/vertical
/// metres. The burnt farmhouse's charred-door look is <c>8× b_d_burnt-wood-a</c> +
/// <c>3× b_d_scorch</c> projected over otherwise-clean door meshes — this is the
/// only place that char exists (the door texture itself is clean plank wood).</para></summary>
public sealed class DecalStore
{
    public sealed record Decal(
        Vector3 Normal,
        Vector3 AxisH,
        Vector3 AxisV,
        Vector3 LocalOrigin,
        uint NodeGuid,
        string TextureName,
        float HorizontalMeters,
        float VerticalMeters,
        float NearPlane,
        float FarPlane,
        float Lod);

    public IReadOnlyList<Decal> Decals { get; }
    private DecalStore(List<Decal> decals) => Decals = decals;

    public static DecalStore Load(byte[] gasBytes)
    {
        var list = new List<Decal>();
        GasDocument doc;
        try { doc = GasDocument.Load(gasBytes); }
        catch { return new DecalStore(list); }
        foreach (var root in doc.Roots) Collect(root, list);
        return new DecalStore(list);
    }

    private static void Collect(GasNode node, List<Decal> list)
    {
        if (IsDecalHeader(node.Header))
        {
            var d = Parse(node);
            if (d is not null) list.Add(d);
        }
        foreach (var child in node.Children) Collect(child, list);
    }

    private static bool IsDecalHeader(string header)
    {
        var h = header.Trim().TrimStart('[').TrimEnd(']');
        foreach (var part in h.Split(','))
        {
            var p = part.Trim();
            if (p.StartsWith("t:", StringComparison.OrdinalIgnoreCase) &&
                p[2..].Trim().Equals("decal", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static Decal? Parse(GasNode node)
    {
        string? ori = Read(node, "decal_orientation");
        string? org = Read(node, "decal_origin");
        string? tex = Read(node, "texture");
        if (ori is null || org is null || tex is null) return null;

        var o = SplitFloats(ori);
        if (o.Length < 9) return null;
        var normal = new Vector3(o[0], o[1], o[2]);
        var axisH  = new Vector3(o[3], o[4], o[5]);
        var axisV  = new Vector3(o[6], o[7], o[8]);
        // A degenerate (all-zero) basis can't project anything — skip it.
        if (axisH.LengthSquared() < 1e-8f || axisV.LengthSquared() < 1e-8f) return null;

        // decal_origin = x, y, z, 0xNODEGUID  (coords node-local, GUID = anchor node)
        var op = org.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (op.Length < 3) return null;
        var localOrigin = new Vector3(ParseF(op[0]), ParseF(op[1]), ParseF(op[2]));
        uint nodeGuid = op.Length >= 4 ? ParseHex(op[3]) : 0u;

        string texName = TextureBasename(tex);
        if (texName.Length == 0) return null;

        float hm  = ReadF(node, "horizontal_meters", 1f);
        float vm  = ReadF(node, "vertical_meters",   1f);
        float np  = ReadF(node, "near_plane", 0.1f);
        float fp  = ReadF(node, "far_plane",  1.1f);
        float lod = ReadF(node, "lod", 1f);
        if (hm <= 0f || vm <= 0f) return null;

        return new Decal(normal, axisH, axisV, localOrigin, nodeGuid, texName, hm, vm, np, fp, lod);
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private static string? Read(GasNode node, string name)
    {
        foreach (var a in node.Attributes)
            if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                return a.Value;
        return null;
    }

    private static float ReadF(GasNode node, string name, float fallback)
    {
        var s = Read(node, name);
        return s is not null ? ParseF(s, fallback) : fallback;
    }

    private static float[] SplitFloats(string s)
    {
        var parts = s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var vals = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++) vals[i] = ParseF(parts[i]);
        return vals;
    }

    private static float ParseF(string s, float fallback = 0f) =>
        float.TryParse(s.Trim().TrimEnd(';'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static uint ParseHex(string s)
    {
        s = s.Trim().TrimEnd(';');
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0u;
    }

    private static string TextureBasename(string raw)
    {
        // "Art\Bitmaps\Decals\b_d_burnt-wood-a.%img%" -> "b_d_burnt-wood-a"
        var s = raw.Trim().TrimEnd(';').Trim();
        int slash = s.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0) s = s[(slash + 1)..];
        int dot = s.IndexOf('.');
        if (dot >= 0) s = s[..dot];
        return s.Trim();
    }
}
