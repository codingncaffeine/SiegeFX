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
        Yaw   += dxPixels * MouseSens;
        Pitch -= dyPixels * MouseSens;
        Pitch = Math.Clamp(Pitch, -PitchLimit, PitchLimit);
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

    public Matrix4x4 GetViewProjection(float aspect) => GetView() * GetProjection(aspect);
}
