using System;
using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 22-AUTH-CHAR-AWP — DS1's authentic always-on player AWP widget
/// at the top-left of the screen. Source:
/// <c>/ui/interfaces/backend/character_awp/character_awp.gas</c>
/// (extracted as hud_character_awp.gas). Atlas: <c>b_gui_ig_mnu_awp.raw</c>
/// (single 131KB texture; per-element uvcoords pick the right strip).
///
/// What it shows for the player (character_1):
/// <list type="bullet">
///   <item>HP bar (vertical, dynamic_edge=top, rect 2,6,11,52)</item>
///   <item>MP bar (vertical, dynamic_edge=top, rect 54,6,63,52)</item>
///   <item>Portrait itemslot (rect 13,6,52,52) — clicking opens character panel,
///     double-click tracks camera, hover shows rollover highlight</item>
///   <item>4 weapon/skill slots side-by-side (rects 68,6 / 88,6 / 108,6 /
///     128,6, each 16w×32h) — slot 1 melee, slot 2 ranged, slot 3 primary
///     spell, slot 4 secondary spell. Click selects active for RMB use.</item>
///   <item>Inventory button (rect 64,40,148,55) — wide bar across the
///     bottom that toggles inventory panel</item>
/// </list>
///
/// Reference resolution 640×480; render at viewportH/480 scale matching
/// data_bar + overhead_bars convention.
/// </summary>
public sealed class CharacterAwp
{
    public const int RefRes = 480;
    public static float Scale(int viewportH) => viewportH / (float)RefRes;

    /// <summary>Action IDs returned from a click on the AWP. RenderHost
    /// dispatches each to the corresponding host-side toggle (per gas's
    /// [messages] notify keys).</summary>
    public enum HitTarget
    {
        None,
        Portrait,        // notify(character) — toggle char panel
        InventoryButton, // notify(inventory) — toggle inventory
        Slot1,           // notify(character_slot_1) — melee
        Slot2,           // notify(character_slot_2) — ranged
        Slot3,           // notify(character_slot_3) — primary spell
        Slot4,           // notify(character_slot_4) — secondary spell
    }

    // 640×480 reference rects from character_awp.gas:
    private static readonly (HitTarget Target, int X, int Y, int W, int H)[] _hits =
    {
        (HitTarget.Portrait,        13,  6, 39, 46),  // 13,6,52,52
        (HitTarget.InventoryButton, 64, 40, 84, 15),  // 64,40,148,55
        (HitTarget.Slot1,           68,  6, 16, 32),  // 68,6,84,38
        (HitTarget.Slot2,           88,  6, 16, 32),  // 88,6,104,38
        (HitTarget.Slot3,          108,  6, 16, 32),  // 108,6,124,38
        (HitTarget.Slot4,          128,  6, 16, 32),  // 128,6,144,38
    };

    public HitTarget HitTest(int x, int y, int viewportH)
    {
        float s = Scale(viewportH);
        foreach (var h in _hits)
        {
            int sx = (int)Math.Round(h.X * s);
            int sy = (int)Math.Round(h.Y * s);
            int sw = (int)Math.Round(h.W * s);
            int sh = (int)Math.Round(h.H * s);
            if (x >= sx && y >= sy && x < sx + sw && y < sy + sh) return h.Target;
        }
        return HitTarget.None;
    }

    /// <summary>Render the AWP widget at the top-left of the viewport.
    /// All rects pulled verbatim from character_awp.gas; uv-V flipped to
    /// screen convention per the bottom-up RAW rule
    /// (project_siegefx_raw_bottomup.md). Portrait icon comes from the
    /// player template's [actor]portrait_icon attribute via the host.</summary>
    public void Draw(IconRenderer iconRenderer, BarRenderer barRenderer,
                     int viewportW, int viewportH,
                     GlTexture awpAtlas, GlTexture? portrait,
                     float hpFrac, float mpFrac, int activeSlot)
    {
        if (awpAtlas is null) return;
        float s = Scale(viewportH);

        // HP bar — gas rect 2,6,11,52 (W=9, H=46), uv 0.007813,0.226563,
        // 0.042969,0.585938. dynamic_edge=top means fill from BOTTOM up;
        // visible height = h * frac, rendered at (y + h - fillH).
        DrawVerticalBar(iconRenderer, barRenderer, viewportW, viewportH, s,
            2, 6, 9, 46, hpFrac,
            0.007813f, 0.226563f, 0.042969f, 0.585938f, awpAtlas);
        // MP bar — gas rect 54,6,63,52 (W=9, H=46), uv 0.210938,0.226563,
        // 0.246095,0.585938.
        DrawVerticalBar(iconRenderer, barRenderer, viewportW, viewportH, s,
            54, 6, 9, 46, mpFrac,
            0.210938f, 0.226563f, 0.246095f, 0.585938f, awpAtlas);

        // Portrait — gas rect 13,6,52,52 (W=39, H=46). The portrait icon
        // RAW (e.g. b_gui_ig_i_ic_c_fb_01 for farmboy) loads via the host
        // and passes in; the gas authors no fallback texture, so when the
        // portrait is null we just leave the rect empty (cpbox chrome
        // would normally fill behind — until that lands the cpbox shows
        // through from the character_awp panel boundary).
        if (portrait is not null)
        {
            int px = (int)Math.Round(13 * s);
            int py = (int)Math.Round(6  * s);
            int pw = (int)Math.Round(39 * s);
            int ph = (int)Math.Round(46 * s);
            iconRenderer.DrawIcon(viewportW, viewportH, portrait, px, py, pw, ph, Vector4.One);
        }

        // 4 weapon/skill slots. Each shows the slot's "active" texture
        // (uv 0.839844,0.734375,0.902344,0.984375 — the slot frame). The
        // currently-selected slot gets a selection overlay (uv 0.675781,
        // 0.710938,0.753907,0.992188) drawn on top.
        for (int i = 0; i < 4; i++)
        {
            int gasX = 68 + i * 20; // slots at 68, 88, 108, 128
            int sx = (int)Math.Round(gasX * s);
            int sy = (int)Math.Round(6 * s);
            int sw = (int)Math.Round(16 * s);
            int sh = (int)Math.Round(32 * s);
            // Slot bg
            iconRenderer.DrawIcon(viewportW, viewportH, awpAtlas, sx, sy, sw, sh, Vector4.One,
                0.839844f, 1f - 0.984375f, 0.902344f, 1f - 0.734375f);
            // Selection texture if this is the active slot
            if (i == activeSlot)
            {
                iconRenderer.DrawIcon(viewportW, viewportH, awpAtlas, sx, sy, sw, sh, Vector4.One,
                    0.675781f, 1f - 0.992188f, 0.753907f, 1f - 0.710938f);
            }
        }

        // Inventory button — gas rect 64,40,148,55 (W=84, H=15), texture
        // b_gui_ig_mnu_awp_buttons (separate atlas; host loads + passes via
        // Draw extension — TODO when we wire it). For now skip and let the
        // user open via I key.
    }

    private static void DrawVerticalBar(
        IconRenderer iconRenderer, BarRenderer barRenderer,
        int viewportW, int viewportH, float scale,
        int gasX, int gasY, int gasW, int gasH, float frac,
        float gasU0, float gasV0, float gasU1, float gasV1,
        GlTexture atlas)
    {
        int x = (int)Math.Round(gasX * scale);
        int y = (int)Math.Round(gasY * scale);
        int w = (int)Math.Round(gasW * scale);
        int h = (int)Math.Round(gasH * scale);
        // Background — full bar at dim tint
        var dim = new Vector4(0.05f, 0.05f, 0.05f, 0.7f);
        barRenderer.DrawRect(viewportW, viewportH, x, y, w, h, dim);
        // Fill — vertical bar fills from bottom up by life fraction.
        // dynamic_edge=top in gas: as life depletes, the TOP edge of the
        // bar moves DOWN (so visible bar shrinks from the top).
        float f = Math.Clamp(frac, 0f, 1f);
        if (f > 0f)
        {
            int fillH = (int)Math.Round(h * f);
            int fillY = y + h - fillH;
            // V crop: bottom of the fill samples bottom of the texture
            // strip (gasV0 in gas's bottom-up space → screenV1 = 1-gasV0);
            // top of the fill samples up to (gasV0 + (gasV1-gasV0)*f).
            float gasVCropped = gasV0 + (gasV1 - gasV0) * f;
            iconRenderer.DrawIcon(viewportW, viewportH, atlas,
                x, fillY, w, fillH, Vector4.One,
                gasU0, 1f - gasVCropped, gasU1, 1f - gasV0);
        }
        // Black 1px outline
        barRenderer.DrawBorder(viewportW, viewportH, x, y, w, h, new Vector4(0f, 0f, 0f, 1f));
    }
}
