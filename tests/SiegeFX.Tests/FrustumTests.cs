using System.Numerics;
using SiegeFX.Core.Geometry;

namespace SiegeFX.Tests;

public class FrustumTests
{
    // A camera at the origin looking down -Z, 60° vertical field, 4:3.
    static readonly Frustum F = new(
        Matrix4x4.CreateLookAt(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY)
        * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 4f / 3f, 0.1f, 100f));

    [Fact]
    public void KeepsWhatIsInFront() => Assert.True(F.IntersectsSphere(new Vector3(0f, 0f, -10f), 1f));

    [Fact]
    public void DropsWhatIsBehind() => Assert.False(F.IntersectsSphere(new Vector3(0f, 0f, 10f), 1f));

    [Fact]
    public void DropsWhatIsFarToTheSide() => Assert.False(F.IntersectsSphere(new Vector3(50f, 0f, -10f), 1f));

    [Fact]
    public void DropsWhatIsBeyondTheFarPlane() => Assert.False(F.IntersectsSphere(new Vector3(0f, 0f, -200f), 1f));

    [Fact]
    public void KeepsASphereStraddlingTheEdge()
    {
        // tan(30°)·10 ≈ 5.77 is the top edge at 10u; a sphere centered just
        // above it still reaches into view.
        Assert.True(F.IntersectsSphere(new Vector3(0f, 6.2f, -10f), 1f));
        Assert.False(F.IntersectsSphere(new Vector3(0f, 8f, -10f), 1f));
    }

    [Fact]
    public void BoxesFollowTheSameRule()
    {
        Assert.True(F.IntersectsAabb(new Vector3(-1f, -1f, -11f), new Vector3(1f, 1f, -9f)));
        Assert.True(F.IntersectsAabb(new Vector3(-100f, -1f, -11f), new Vector3(100f, 1f, -9f)));
        Assert.False(F.IntersectsAabb(new Vector3(-1f, -1f, 5f), new Vector3(1f, 1f, 9f)));
    }

    [Fact]
    public void EverythingKeepsEverything()
    {
        Assert.True(Frustum.Everything.IntersectsSphere(new Vector3(0f, 0f, 1e6f), 0f));
        Assert.True(Frustum.Everything.IntersectsAabb(new Vector3(1e5f), new Vector3(2e5f)));
    }
}
