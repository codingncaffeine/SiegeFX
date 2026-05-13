using System;
using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>SC-OPTIONS-CHROME + Phase 22-AUTH-CHROME — 9-patch (nine-slice)
/// chrome rendering. DS1's backend dialogs (options, inventory, dialogue,
/// vendor, journal, etc.) use templated `cpbox` / `cpbox_wide` /
/// `cpbox_thin` / `cpbox_thin_dark` / `jbox` / `woodbox` chrome instead
/// of bone-driven ASPs. Each template is a 9-piece set: 4 corners
/// (fixed-size, native pixel dims) + 4 sides (stretched along their
/// axis) + center fill (stretched). This helper wraps
/// <see cref="IconRenderer.DrawIcon"/> with the standard 9-cell layout.
///
/// Texture resolution is delegated via a <c>Func&lt;string, GlTexture?&gt;</c>
/// resolver so the same helpers run from both the frontend scene (boot
/// menus) and the in-game RenderHost (after the world has loaded). Pass
/// a resolver that maps a bare common-control name like
/// <c>"cpbox_ul"</c> to the <c>b_gui_cmn_cpbox_ul.raw</c> texture; for
/// FrontendScene that's <c>scene.GetCommonTexture</c>; for RenderHost
/// it's the equivalent in-game accessor.
///
/// Sides currently STRETCH; tiling is a follow-up if visible artifacts
/// show on long edges (the gas authors `wrap_mode = tiled` on some sides).</summary>
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
    /// the options-menu outer panel and similar wide dialogs.
    /// NOTE: the gas authors no <c>cpbox_wide_fill</c> key — only
    /// <c>cpbox_fill = b_gui_cmn_box_alpha_154</c>. We re-use that
    /// 154-alpha translucent black for cpbox_wide by inference. Matches
    /// what was already shipping for the options-menu outer panel.</summary>
    public static void DrawCpboxWide(IconRenderer iconRenderer, Func<string, GlTexture?> resolver,
        int viewportW, int viewportH, int x, int y, int w, int h, Vector4 tint)
        => DrawFamily(iconRenderer, resolver, viewportW, viewportH, x, y, w, h, "cpbox2", "box_alpha_154", tint);

    /// <summary>cpbox template (uses b_gui_cmn_cpbox_* textures).
    /// DS1's standard chrome — used for the options-menu inner content
    /// panel and most in-game backend panels (inventory, character,
    /// spellbook, vendor, journal).</summary>
    public static void DrawCpbox(IconRenderer iconRenderer, Func<string, GlTexture?> resolver,
        int viewportW, int viewportH, int x, int y, int w, int h, Vector4 tint)
        => DrawFamily(iconRenderer, resolver, viewportW, viewportH, x, y, w, h, "cpbox", "box_alpha_154", tint);

    /// <summary>cpbox_thin (cpbox3) — DS1's slim variant used where a
    /// lighter chrome reads better against a busy in-world background
    /// (e.g. floating tooltips, world tips).
    /// NOTE: like cpbox_wide, the gas authors no <c>cpbox_thin_fill</c>
    /// key — we reuse <c>box_alpha_154</c> by family-sibling inference.</summary>
    public static void DrawCpboxThin(IconRenderer iconRenderer, Func<string, GlTexture?> resolver,
        int viewportW, int viewportH, int x, int y, int w, int h, Vector4 tint)
        => DrawFamily(iconRenderer, resolver, viewportW, viewportH, x, y, w, h, "cpbox3", "box_alpha_154", tint);

    /// <summary>cpbox_thin_dark (cpbox4) — DS1's darker slim variant.
    /// Uses the 255-alpha fill (fully opaque) per common_control_art.</summary>
    public static void DrawCpboxThinDark(IconRenderer iconRenderer, Func<string, GlTexture?> resolver,
        int viewportW, int viewportH, int x, int y, int w, int h, Vector4 tint)
        => DrawFamily(iconRenderer, resolver, viewportW, viewportH, x, y, w, h, "cpbox4", "box_alpha_255", tint);

    /// <summary>jbox template — DS1's journal/log chrome (used by
    /// journal.gas's quest list panel and chatbox sub-frames).</summary>
    public static void DrawJbox(IconRenderer iconRenderer, Func<string, GlTexture?> resolver,
        int viewportW, int viewportH, int x, int y, int w, int h, Vector4 tint)
        => DrawFamily(iconRenderer, resolver, viewportW, viewportH, x, y, w, h, "jbox", "jbox_fill", tint);

    /// <summary>Shared family-template dispatch. Family prefix maps to
    /// <c>b_gui_cmn_&lt;prefix&gt;_ul/_ur/_ll/_lr/_top/_bot/_l/_r</c>
    /// per common_control_art.gas's per-family key naming convention.
    /// `fillKey` is the family-specific fill texture key (cpbox uses
    /// box_alpha_154, cpbox4 uses box_alpha_255, jbox uses jbox_fill).</summary>
    // NOT WIRED: the woodbox family in common_control_art.gas points to
    // b_gui_cmn_brd_01_* basenames, which break the simple `prefix + "_ul"`
    // naming convention DrawFamily relies on. No backend panel in the
    // Phase 22 rework inventory references woodbox; if a future slice
    // needs it, write a dedicated DrawWoodbox helper that maps each gas
    // key (woodbox_top_left_corner = b_gui_cmn_brd_01_ul, etc.) verbatim
    // rather than extending DrawFamily.
    private static void DrawFamily(IconRenderer iconRenderer, Func<string, GlTexture?> resolver,
        int viewportW, int viewportH, int x, int y, int w, int h,
        string prefix, string fillKey, Vector4 tint)
    {
        Draw(iconRenderer, viewportW, viewportH, x, y, w, h,
            tlCorner:    resolver(prefix + "_ul"),
            trCorner:    resolver(prefix + "_ur"),
            blCorner:    resolver(prefix + "_ll"),
            brCorner:    resolver(prefix + "_lr"),
            topSide:     resolver(prefix + "_top"),
            bottomSide:  resolver(prefix + "_bot"),
            leftSide:    resolver(prefix + "_l"),
            rightSide:   resolver(prefix + "_r"),
            fill:        resolver(fillKey),
            tint);
    }
}
