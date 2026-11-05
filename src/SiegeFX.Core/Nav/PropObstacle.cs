using System.Numerics;

namespace SiegeFX.Core.Nav;

/// <summary>A static prop's ground footprint in world space: a rectangle in
/// XZ (<see cref="Corner"/> plus <see cref="AxisU"/>·[0, <see cref="LenU"/>]
/// and <see cref="AxisV"/>·[0, <see cref="LenV"/>]) and the vertical band
/// the prop occupies.</summary>
public readonly record struct PropFootprint(
    Vector3 Corner, Vector3 AxisU, float LenU, Vector3 AxisV, float LenV, float BaseY, float TopY)
{
    /// <summary>Center of the rectangle in XZ.</summary>
    public Vector2 Center => new(Corner.X + (AxisU.X * LenU + AxisV.X * LenV) * 0.5f,
                                 Corner.Z + (AxisU.Z * LenU + AxisV.Z * LenV) * 0.5f);

    /// <summary>Half the rectangle's diagonal: the disc that covers it.</summary>
    public float Radius => 0.5f * MathF.Sqrt(LenU * LenU + LenV * LenV);

    /// <summary>The footprint of a mesh-space bounding box placed by
    /// <paramref name="world"/>. Of the box's three axes, the one the
    /// transform points most nearly up is the prop's height; the other two
    /// span the rectangle. A rigid prop authored Z-up and stood up by its
    /// root bind pose therefore gets mesh X × mesh Y as its footprint and
    /// mesh Z as its height, as it stands in the world. The band is the
    /// lowest to highest corner of the placed box.</summary>
    public static PropFootprint FromBounds(Vector3 min, Vector3 max, Matrix4x4 world)
    {
        var size = max - min;
        Span<Vector3> edge = stackalloc Vector3[3];
        edge[0] = Vector3.TransformNormal(new Vector3(size.X, 0f, 0f), world);
        edge[1] = Vector3.TransformNormal(new Vector3(0f, size.Y, 0f), world);
        edge[2] = Vector3.TransformNormal(new Vector3(0f, 0f, size.Z), world);
        int up = 0;
        float bestUp = -1f;
        for (int i = 0; i < 3; i++)
        {
            float len = edge[i].Length();
            float upness = len > 1e-6f ? MathF.Abs(edge[i].Y) / len : 0f;
            if (upness > bestUp) { bestUp = upness; up = i; }
        }
        int iu = up == 0 ? 1 : 0, iv = up == 2 ? 1 : 2;
        var corner = Vector3.Transform(min, world);
        var flatU = new Vector3(edge[iu].X, 0f, edge[iu].Z);
        var flatV = new Vector3(edge[iv].X, 0f, edge[iv].Z);
        float lenU = flatU.Length(), lenV = flatV.Length();
        // A box edge running backwards along its world axis still spans the
        // same rectangle from the same corner; normalizing keeps the sign.
        var axisU = lenU > 1e-6f ? flatU / lenU : Vector3.UnitX;
        var axisV = lenV > 1e-6f ? flatV / lenV : Vector3.UnitZ;
        float baseY = float.PositiveInfinity, topY = float.NegativeInfinity;
        for (int k = 0; k < 8; k++)
        {
            var c = new Vector3((k & 1) == 0 ? min.X : max.X,
                                (k & 2) == 0 ? min.Y : max.Y,
                                (k & 4) == 0 ? min.Z : max.Z);
            float y = Vector3.Transform(c, world).Y;
            baseY = MathF.Min(baseY, y);
            topY = MathF.Max(topY, y);
        }
        return new PropFootprint(corner, axisU, lenU, axisV, lenV, baseY, topY);
    }
}

/// <summary>Marks a blocking prop on the nav mesh. The game
/// (RenderHost.MarkAllObstacles) and the nav tools (hero-sim, roam-sim)
/// both mark props through here, so a tool replay blocks exactly what the
/// game blocks.</summary>
public static class PropObstacle
{
    /// <summary>Props this tall or taller also block sight lines (fences
    /// and crates under it don't: archers shoot over them).</summary>
    public const float SightOccluderMinHeight = 2.2f;

    /// <summary>Footprints smaller than this disc radius block nothing.</summary>
    public const float MinRadius = 0.5f;

    /// <summary>Marks <paramref name="f"/> on <paramref name="mesh"/> and
    /// returns the number of triangles it blocked. Blame tags carry the
    /// prop's tag and its footprint center. Tall props add their discs to
    /// <paramref name="sightOccluders"/> when given.</summary>
    public static int Mark(NavMesh mesh, in PropFootprint f, string tag,
                           List<(Vector2 C, float R, float TopY)>? sightOccluders = null)
    {
        float radius = f.Radius;
        if (radius < MinRadius) return 0;
        var center = f.Center;
        string blame = FormattableString.Invariant($"{tag}@{center.X:F1},{center.Y:F1}");
        bool occludesSight = f.TopY - f.BaseY >= SightOccluderMinHeight;
        float longLen = MathF.Max(f.LenU, f.LenV);
        float shortLen = MathF.Max(0.4f, MathF.Min(f.LenU, f.LenV));
        int marked = 0;
        if (longLen > shortLen * 2.2f && longLen > 2f)
        {
            // SC-PATHING — elongated props (fence sections, walls) block as a
            // CAPSULE of small discs along their long axis: one bounding disc
            // would over-block the walkable ground beside a 6u fence.
            var axis = f.LenU >= f.LenV ? f.AxisU : f.AxisV;
            float discR = MathF.Max(0.45f, shortLen * 0.5f);
            int discs = Math.Max(2, (int)MathF.Ceiling(longLen / (discR * 1.4f)));
            for (int di = 0; di < discs; di++)
            {
                float along = -longLen * 0.5f + longLen * (di / (float)(discs - 1));
                float cx = center.X + axis.X * along, cz = center.Y + axis.Z * along;
                marked += mesh.MarkObstacle(cx, cz, discR, f.BaseY, f.TopY, blame);
                if (occludesSight) sightOccluders?.Add((new Vector2(cx, cz), discR, f.TopY));
            }
        }
        else if (shortLen > 2.4f)
        {
            // SC-PATHING-FOOTPRINT — big squat props (wagons, stalls,
            // boulders) block their rectangle as a grid of small discs; one
            // bounding disc overshoots the corners and sealed rooms and pens.
            const float gridR = 0.8f;
            int nu = Math.Max(2, (int)MathF.Ceiling(f.LenU / (gridR * 1.4f)) + 1);
            int nv = Math.Max(2, (int)MathF.Ceiling(f.LenV / (gridR * 1.4f)) + 1);
            for (int iu = 0; iu < nu; iu++)
            for (int iv = 0; iv < nv; iv++)
            {
                var c = f.Corner + f.AxisU * (f.LenU * iu / (nu - 1)) + f.AxisV * (f.LenV * iv / (nv - 1));
                marked += mesh.MarkObstacle(c.X, c.Z, gridR, f.BaseY, f.TopY, blame);
                if (occludesSight) sightOccluders?.Add((new Vector2(c.X, c.Z), gridR, f.TopY));
            }
        }
        else
        {
            // SC-NAV-OBSTACLE-RECT — mid-size props block their own rectangle,
            // not the disc around it (the disc overshoots a rectangle's sides).
            marked = f.LenU > 0.05f && f.LenV > 0.05f
                ? mesh.MarkObstacleRect(f.Corner, f.AxisU, f.LenU, f.AxisV, f.LenV, f.BaseY, f.TopY, blame)
                : mesh.MarkObstacle(center.X, center.Y, radius, f.BaseY, f.TopY, blame);
            if (occludesSight) sightOccluders?.Add((center, radius, f.TopY));
        }
        return marked;
    }
}
