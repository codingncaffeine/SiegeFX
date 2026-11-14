using SiegeFX.Runtime.Render.Hud;

namespace SiegeFX.Tests;

/// <summary>The HUD's size cap is a density the eye sees, so it is taken in
/// points: a Retina Mac, with two framebuffer pixels per point, must lay the
/// HUD out at the same size in points as any other screen (#55).</summary>
public class HudScaleTests : IDisposable
{
    public HudScaleTests() { HudScale.User = 1f; HudScale.ContentScale = 1f; }
    public void Dispose() { HudScale.User = 1f; HudScale.ContentScale = 1f; }

    [Fact]
    public void GrowsWithTheViewportBelowTheCap() => Assert.Equal(1.5f, HudScale.Hud(720), 0.001f);

    [Fact]
    public void CapsAtRetailsDensity()
    {
        // 768/480: DS1's own 1024x768 maximum. 1080p and 4K alike stop here.
        Assert.Equal(1.6f, HudScale.Hud(1080), 0.001f);
        Assert.Equal(1.6f, HudScale.Hud(2160), 0.001f);
    }

    [Fact]
    public void TheUserKnobGrowsPastTheCap()
    {
        HudScale.User = 1.23f;
        Assert.Equal(1.6f * 1.23f, HudScale.Hud(1080), 0.001f);
    }

    [Fact]
    public void ARetinaScreenReadsTheSameSizeInPoints()
    {
        // A 1280x720 window is 720 framebuffer rows on an ordinary screen and
        // 1440 on a Retina Mac; the HUD must stand the same height in points.
        float plain = HudScale.Hud(720);
        HudScale.ContentScale = 2f;
        Assert.Equal(plain, HudScale.Hud(1440) / 2f, 0.001f);
    }

    [Fact]
    public void TheReportersBorderlessMacIsNoLongerHalfSize()
    {
        // #55: a 15" MacBook Air, borderless over 1710x1112 points, which is a
        // 3420x2224 framebuffer. With the cap in pixels the HUD stood 1.6 on
        // 2224 rows: half the size it has on a 1112-row screen anywhere else.
        float elsewhere = HudScale.Hud(1112);
        Assert.Equal(1.6f, HudScale.Hud(2224), 0.001f);  // what the reporter saw
        HudScale.ContentScale = 2f;
        Assert.Equal(elsewhere, HudScale.Hud(2224) / 2f, 0.001f);
        Assert.Equal(3.2f, HudScale.Hud(2224), 0.001f);
        // The user's own 123% still multiplies on top.
        HudScale.User = 1.23f;
        Assert.Equal(3.2f * 1.23f, HudScale.Hud(2224), 0.001f);
    }

    [Fact]
    public void ModalDialogsFollowTheHudOnRetina()
    {
        HudScale.ContentScale = 2f;
        Assert.Equal(HudScale.Hud(2224), HudScale.Modal(3420, 2224), 0.001f);
        // The width guard still keeps a 640-wide dialog inside a narrow window.
        Assert.Equal(1280 / 640f, HudScale.Modal(1280, 2224), 0.001f);
    }

    [Fact]
    public void OrdinaryScreensAreUnchanged()
    {
        // Content scale 1 is every Windows and Linux screen: the cap is the
        // same 1.6 it has always been.
        Assert.Equal(1.6f, HudScale.Hud(1440), 0.001f);
        Assert.Equal(1.6f, HudScale.Modal(2560, 1440), 0.001f);
    }
}
