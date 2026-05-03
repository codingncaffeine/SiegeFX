using System.Numerics;

namespace SiegeFX.Runtime.Render;

/// <summary>
/// First-person camera driven by yaw/pitch in radians. +Y is up.
/// Default orientation looks down -Z (yaw=0, pitch=0).
/// </summary>
public sealed class Camera
{
    public Vector3 Position { get; set; } = new(0f, 1.7f, 5f);
    public float Yaw   { get; set; } = 0f;
    public float Pitch { get; set; } = 0f;
    public float FovRadians  { get; set; } = MathF.PI / 3f;   // 60deg
    public float NearPlane   { get; set; } = 0.1f;
    public float FarPlane    { get; set; } = 1000f;
    public float MoveSpeed   { get; set; } = 6f;              // units/sec
    public float MouseSens   { get; set; } = 0.0025f;         // radians/pixel
    // Phase 23-SC-OPTIONS-FOLD2 — runtime mouse-look knobs from the
    // Options Menu's Input tab. SensitivityScale rides on top of MouseSens
    // (OptionsMenu maps slider 0..100 → 0.25..2.0x, default 50 → 1.0x).
    // InvertX flips horizontal yaw; InvertY flips vertical pitch.
    public float SensitivityScale { get; set; } = 1f;
    public bool  InvertX { get; set; }
    public bool  InvertY { get; set; }

    private const float PitchLimit = MathF.PI / 2f - 0.01f;

    public Vector3 Forward
    {
        get
        {
            var cp = MathF.Cos(Pitch);
            return Vector3.Normalize(new Vector3(
                MathF.Sin(Yaw) * cp,
                MathF.Sin(Pitch),
                -MathF.Cos(Yaw) * cp));
        }
    }

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY));
    public Vector3 Up    => Vector3.UnitY;

    public void LookDelta(float dxPixels, float dyPixels)
    {
        Yaw   += YawIncrement(dxPixels);
        Pitch -= PitchIncrement(dyPixels);
        Pitch = Math.Clamp(Pitch, -PitchLimit, PitchLimit);
    }

    /// <summary>Phase 23-SC-OPTIONS-FOLD2 — yaw delta in radians for a
    /// horizontal mouse drag of <paramref name="dxPixels"/> pixels, with
    /// the active sensitivity + invert-X knobs applied. Exposed so the
    /// chase-camera path can compute the same value without re-deriving
    /// the formula and drifting from <see cref="LookDelta"/>.</summary>
    public float YawIncrement(float dxPixels)
    {
        float sx = InvertX ? -1f : 1f;
        return dxPixels * MouseSens * SensitivityScale * sx;
    }

    /// <summary>Phase 23-SC-OPTIONS-FOLD2 — pitch delta companion for
    /// <see cref="YawIncrement"/>. Subtract this from Pitch (positive
    /// dy = mouse moved down = look down by default), then clamp to
    /// PitchLimit. Currently only first-person mode uses this — chase
    /// derives pitch from the look-at target.</summary>
    public float PitchIncrement(float dyPixels)
    {
        float sy = InvertY ? -1f : 1f;
        return dyPixels * MouseSens * SensitivityScale * sy;
    }

    /// <summary>
    /// Apply per-frame movement. Directions:
    ///   forward +1/-1, strafe +1/-1 (right positive), vertical +1/-1 (world up).
    /// </summary>
    public void Move(float forward, float strafe, float vertical, float dt, bool sprint)
    {
        var speed = MoveSpeed * (sprint ? 3f : 1f) * dt;
        // walk vector lives in the XZ plane so pitch doesn't throw off horizontal speed
        var walkForward = Vector3.Normalize(new Vector3(Forward.X, 0f, Forward.Z));
        if (walkForward.LengthSquared() < 1e-6f) walkForward = -Vector3.UnitZ;
        Position += walkForward * forward * speed;
        Position += Right        * strafe  * speed;
        Position += Vector3.UnitY * vertical * speed;
    }

    public Matrix4x4 GetView() => Matrix4x4.CreateLookAt(Position, Position + Forward, Up);

    public Matrix4x4 GetProjection(float aspect)
        => Matrix4x4.CreatePerspectiveFieldOfView(FovRadians, aspect, NearPlane, FarPlane);

    // System.Numerics uses row-vector convention (v * M), so the C# composition is
    // View * Projection. We upload with transpose=false, which reinterprets the
    // row-major storage as column-major in GLSL. That effectively transposes the
    // matrix, converting it to column-vector form so `uViewProj * vec4(pos,1)`
    // applies View then Projection. If anyone ever flips the uniform's transpose
    // flag or swaps to a glm-style math lib, this order must flip too.
    public Matrix4x4 GetViewProjection(float aspect) => GetView() * GetProjection(aspect);
}
