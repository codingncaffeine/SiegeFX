namespace SiegeFX.Runtime.Render.Hud;

/// <summary>ALPHA-2V — global HUD scale multiplier behind Options → Advanced →
/// UI Scale %. Every panel's viewport-height-derived scale factor multiplies
/// through <see cref="User"/>, so one knob resizes the whole GUI at once.
/// Default 1.0 = each panel's existing look, byte-for-byte.
///
/// Note for the panel-baseline audit (parked): the panels do NOT share one
/// baseline today — the info-rail trio clamps at 1.5× (InfoRailLayout.MaxScale)
/// while dialogue/vendor/AWP run raw viewportH/480 (2.25× at 1080p, 3× at
/// 1440p). That inconsistency is why some panels read oversized; this knob
/// scales all of them uniformly but does not re-baseline them.</summary>
public static class HudScale
{
    /// <summary>0.5 .. 1.5, from Options → Advanced → UI Scale %.</summary>
    public static float User = 1f;

    /// <summary>ALPHA-2V RE-BASELINE — the shared cap every in-game HUD
    /// panel now clamps to (the info-rail trio already did; user verdict:
    /// that group was "small and correct" while the unclamped panels read
    /// oversized at 1440p/4K). One baseline = consistent GUI; the User
    /// knob provides growth beyond it for those who want bigger.</summary>
    public const float BaseMax = 1.5f;

    /// <summary>Height-proportional HUD scale (the house convention:
    /// gas-authored 640×480 coords × viewportH/480) clamped to
    /// <see cref="BaseMax"/>, times the user knob. Works identically for
    /// 16:9, 21:9 ultrawide, and 4K — width only widens the world view,
    /// height drives UI size.</summary>
    public static float Hud(int viewportH)
    {
        float raw = viewportH / 480f;
        return (raw < BaseMax ? raw : BaseMax) * User;
    }

    /// <summary>Modal-dialog scale: shrinks with the user knob but never
    /// grows past the fit bound (a modal scaled above min(vh/480, vw/640)
    /// would overflow the window).</summary>
    public static float Modal(int viewportW, int viewportH)
    {
        float fit = MathF.Min(viewportH / 480f, viewportW / 640f);
        return MathF.Min(viewportH / 480f * User, fit);
    }
}
