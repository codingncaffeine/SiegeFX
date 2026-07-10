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
    /// panel now clamps to (the info-rail trio already clamped ~here; user
    /// verdict: that group was "small and correct" while the unclamped
    /// panels read oversized at 1440p/4K). 1.6 = retail's own maximum GUI
    /// density: DS1 topped out at 1024×768, and 768/480 = 1.6, so capped
    /// panels render exactly the pixel density the original game ever
    /// reached. The User knob provides growth beyond it.</summary>
    public const float BaseMax = 1.6f;

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

    /// <summary>Modal-dialog scale (Options menu, Quest Log): the SAME
    /// shared baseline × knob as every HUD panel — "everything matches" —
    /// with a width-fit cap purely as an overflow guard so a narrow or
    /// portrait window can't crop the 640-wide authored dialog. (The
    /// original form built on raw vh/480, which left dialogs rendering
    /// nearly double the rest of the interface at 1440p/4K.)</summary>
    public static float Modal(int viewportW, int viewportH)
        => MathF.Min(Hud(viewportH), viewportW / 640f);
}
