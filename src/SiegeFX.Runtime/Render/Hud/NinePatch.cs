using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>SC-OPTIONS-CHROME — 9-patch (nine-slice) chrome rendering.
/// DS1's backend dialogs (options, inventory, dialogue, vendor, etc)
/// use templated `cpbox` / `cpbox_wide` / `cpbox_thin` / `jbox` /
/// `woodbox` chrome instead of bone-driven ASPs. Each template is
/// a 9-piece set: 4 corners (fixed-size, native pixel dims) + 4
/// sides (stretched along their axis) + center fill (stretched).
/// This helper wraps <see cref="IconRenderer.DrawIcon"/> with the
/// standard 9-cell layout. Sides currently STRETCH; tiling is a
/// follow-up if visible artifacts show on long edges.</summary>
public static class NinePatch
{
    /// <summary>Render the 9 textures across the (x,y,w,h) rect.
    /// Corner textures' native pixel dimensions drive the corner
    /// cell size; sides + fill stretch to the remaining area.
    /// Any null texture is skipped (so callers can opt out of fill).</summary>
    public static void Draw(
        IconRenderer iconRenderer, int viewportW, int viewportH,
        int x, int y, int w, int h,
        GlTexture? tlCorner, GlTexture? trCorner,
        GlTexture? blCorner, GlTexture? brCorner,
        GlTexture? topSide, GlTexture? bottomSide,
        GlTexture? leftSide, GlTexture? rightSide,
        GlTexture? fill,
        Vector4 tint)
    {
        if (tlCorner is null || trCorner is null || blCorner is null || brCorner is null) return;
        int cw = tlCorner.Width;
        int ch = tlCorner.Height;
        int sideW = w - cw * 2;
        int sideH = h - ch * 2;
        if (sideW < 0) sideW = 0;
        if (sideH < 0) sideH = 0;

        if (fill is not null && sideW > 0 && sideH > 0)
            iconRenderer.DrawIcon(viewportW, viewportH, fill,
                x + cw, y + ch, sideW, sideH, tint);

        if (topSide is not null && sideW > 0)
            iconRenderer.DrawIcon(viewportW, viewportH, topSide,
                x + cw, y, sideW, ch, tint);
        if (bottomSide is not null && sideW > 0)
            iconRenderer.DrawIcon(viewportW, viewportH, bottomSide,
                x + cw, y + h - ch, sideW, ch, tint);
        if (leftSide is not null && sideH > 0)
            iconRenderer.DrawIcon(viewportW, viewportH, leftSide,
                x, y + ch, cw, sideH, tint);
        if (rightSide is not null && sideH > 0)
            iconRenderer.DrawIcon(viewportW, viewportH, rightSide,
                x + w - cw, y + ch, cw, sideH, tint);

        iconRenderer.DrawIcon(viewportW, viewportH, tlCorner,
            x, y, cw, ch, tint);
        iconRenderer.DrawIcon(viewportW, viewportH, trCorner,
            x + w - cw, y, cw, ch, tint);
        iconRenderer.DrawIcon(viewportW, viewportH, blCorner,
            x, y + h - ch, cw, ch, tint);
        iconRenderer.DrawIcon(viewportW, viewportH, brCorner,
            x + w - cw, y + h - ch, cw, ch, tint);
    }

    /// <summary>cpbox_wide template (uses b_gui_cmn_cpbox2_* textures
    /// per common_control_art.gas). DS1's wider-frame chrome used for
    /// the options-menu outer panel.</summary>
    public static void DrawCpboxWide(IconRenderer iconRenderer, FrontendScene scene,
        int viewportW, int viewportH, int x, int y, int w, int h, Vector4 tint)
    {
        Draw(iconRenderer, viewportW, viewportH, x, y, w, h,
            tlCorner:    scene.GetCommonTexture("cpbox2_ul"),
            trCorner:    scene.GetCommonTexture("cpbox2_ur"),
            blCorner:    scene.GetCommonTexture("cpbox2_ll"),
            brCorner:    scene.GetCommonTexture("cpbox2_lr"),
            topSide:     scene.GetCommonTexture("cpbox2_top"),
            bottomSide:  scene.GetCommonTexture("cpbox2_bot"),
            leftSide:    scene.GetCommonTexture("cpbox2_l"),
            rightSide:   scene.GetCommonTexture("cpbox2_r"),
            fill:        scene.GetCommonTexture("box_alpha_154"),
            tint);
    }

    /// <summary>cpbox template (uses b_gui_cmn_cpbox_* textures).
    /// DS1's standard chrome — used for the options-menu inner
    /// content panel.</summary>
    public static void DrawCpbox(IconRenderer iconRenderer, FrontendScene scene,
        int viewportW, int viewportH, int x, int y, int w, int h, Vector4 tint)
    {
        Draw(iconRenderer, viewportW, viewportH, x, y, w, h,
            tlCorner:    scene.GetCommonTexture("cpbox_ul"),
            trCorner:    scene.GetCommonTexture("cpbox_ur"),
            blCorner:    scene.GetCommonTexture("cpbox_ll"),
            brCorner:    scene.GetCommonTexture("cpbox_lr"),
            topSide:     scene.GetCommonTexture("cpbox_top"),
            bottomSide:  scene.GetCommonTexture("cpbox_bot"),
            leftSide:    scene.GetCommonTexture("cpbox_l"),
            rightSide:   scene.GetCommonTexture("cpbox_r"),
            fill:        scene.GetCommonTexture("box_alpha_154"),
            tint);
    }
}
