using System.Numerics;

namespace SiegeFX.Core.Geometry;

/// <summary>The six planes of a camera's view volume, taken from its
/// view-projection matrix (System.Numerics row-vector convention: clip =
/// v × M), for skipping draws that can't reach the screen. Tests are
/// conservative: a shape is culled only when it lies wholly outside one
/// plane, so nothing visible is ever dropped.</summary>
public readonly struct Frustum
{
    readonly Vector4 _left, _right, _bottom, _top, _near, _far;

    /// <summary>A frustum every shape intersects (all planes pass
    /// everything): culling turned off.</summary>
    public static Frustum Everything { get; } = new(float.PositiveInfinity);

    Frustum(float always)
    {
        var p = new Vector4(0f, 0f, 0f, always);
        _left = _right = _bottom = _top = _near = _far = p;
    }

    public Frustum(Matrix4x4 m)
    {
        // Column j of M produces clip component j.
        var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        _left = Normalize(c4 + c1);
        _right = Normalize(c4 - c1);
        _bottom = Normalize(c4 + c2);
        _top = Normalize(c4 - c2);
        // -w <= z: the OpenGL near plane, which is at or in front of the
        // D3D-style one (0 <= z) CreatePerspectiveFieldOfView uses, so the
        // test holds for either depth range.
        _near = Normalize(c4 + c3);
        _far = Normalize(c4 - c3);
    }

    static Vector4 Normalize(Vector4 p)
    {
        float len = new Vector3(p.X, p.Y, p.Z).Length();
        return len > 1e-12f ? p / len : p;
    }

    static float Distance(Vector4 p, Vector3 v) => p.X * v.X + p.Y * v.Y + p.Z * v.Z + p.W;

    /// <summary>False only when the sphere is wholly outside the view.</summary>
    public bool IntersectsSphere(Vector3 center, float radius) =>
        Distance(_left, center) >= -radius && Distance(_right, center) >= -radius &&
        Distance(_bottom, center) >= -radius && Distance(_top, center) >= -radius &&
        Distance(_near, center) >= -radius && Distance(_far, center) >= -radius;

    /// <summary>False only when the box is wholly outside the view.</summary>
    public bool IntersectsAabb(Vector3 min, Vector3 max) =>
        Inside(_left, min, max) && Inside(_right, min, max) && Inside(_bottom, min, max) &&
        Inside(_top, min, max) && Inside(_near, min, max) && Inside(_far, min, max);

    // The box corner furthest along the plane's normal is inside or on it.
    static bool Inside(Vector4 p, Vector3 min, Vector3 max) =>
        Distance(p, new Vector3(p.X >= 0f ? max.X : min.X,
                                p.Y >= 0f ? max.Y : min.Y,
                                p.Z >= 0f ? max.Z : min.Z)) >= 0f;
}
