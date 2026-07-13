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
    public static float Scale(int viewportH) => HudScale.Hud(viewportH);

    /// <summary>SC-HUD-DRAG — pixel offset of the whole AWP cluster from its
    /// authored top-left dock. Set by the host from the user's shift-dragged
    /// position; (0,0) = the authored layout. Draw and HitTest both honor it
    /// so visuals and clicks can never desync.</summary>
    public int OffsetX, OffsetY;

    /// <summary>Action IDs returned from a click on the AWP. RenderHost
    /// dispatches each to the corresponding host-side toggle (per gas's
    /// [messages] notify keys).</summary>
    public enum HitTarget
    {
        None,
        Portrait,        // notify(character) — toggle char panel
        InventoryButton, // notify(inventory) — wide max-mode button, opens info rail
        CloseArrow,      // INFORAIL-C — only when rail is open; closes rail
        Slot1,           // notify(character_slot_1) — melee
        Slot2,           // notify(character_slot_2) — ranged
        Slot3,           // notify(character_slot_3) — primary spell
        Slot4,           // notify(character_slot_4) — secondary spell
    }

    /// <summary>HitTest with rail-state awareness. When the info-rail is
    /// open, the close-arrow at rect 64,40,87,56 is the ONLY widget below
    /// the slots (the wide inventory button disappears per DS1's max-
    /// mode → min-mode transformation, gas group=character_1_min vs
    /// character_1_max). When the rail is closed neither the close arrow
    /// nor the wide button is shown — the rail is opened solely via the
    /// I key.</summary>
    public HitTarget HitTest(int x, int y, int viewportH, bool railOpen)
    {
        float s = Scale(viewportH);
        // SC-HUD-DRAG — transform the point into the un-offset frame so
        // every authored rect below stays valid at any dragged position.
        x -= OffsetX; y -= OffsetY;
        // Portrait (always-on, character_1 group)
        if (Hit(x, y, s, 13, 6, 39, 46)) return HitTarget.Portrait;
        // DS1 min-mode behavior (rail open): slot 1 stays visible as
        // the "active skill" indicator directly above the close arrow;
        // slots 2/3/4 fold out. In max mode (rail closed) all 4 show.
        if (Hit(x, y, s, 68, 6, 16, 32)) return HitTarget.Slot1;
        if (!railOpen)
        {
            if (Hit(x, y, s,  88, 6, 16, 32)) return HitTarget.Slot2;
            if (Hit(x, y, s, 108, 6, 16, 32)) return HitTarget.Slot3;
            if (Hit(x, y, s, 128, 6, 16, 32)) return HitTarget.Slot4;
        }
        // Below the slot strip: mutually exclusive per character_awp.gas
        // group=character_1_max (wide InventoryButton, rail closed) vs
        // group=character_1_min (narrow CloseArrow, rail open). Both
        // gas-authored as clickable buttons with hover/press art.
        if (railOpen)
        {
            if (Hit(x, y, s, 64, 40, 23, 16)) return HitTarget.CloseArrow;
        }
        else
        {
            if (Hit(x, y, s, 64, 40, 84, 15)) return HitTarget.InventoryButton;
        }
        return HitTarget.None;
    }

    private static bool Hit(int x, int y, float s, int gx, int gy, int gw, int gh)
    {
        int sx = (int)Math.Round(gx * s);
        int sy = (int)Math.Round(gy * s);
        int sw = (int)Math.Round(gw * s);
        int sh = (int)Math.Round(gh * s);
        return x >= sx && y >= sy && x < sx + sw && y < sy + sh;
    }

    /// <summary>Render the AWP widget at the top-left of the viewport.
    /// All rects pulled verbatim from character_awp.gas; uv-V flipped to
    /// screen convention per the bottom-up RAW rule
    /// (project_siegefx_raw_bottomup.md). Portrait icon comes from the
    /// player template's [actor]portrait_icon attribute via the host.</summary>
    public void Draw(IconRenderer iconRenderer, BarRenderer barRenderer,
                     int viewportW, int viewportH,
                     GlTexture awpAtlas, GlTexture? portrait,
                     float hpFrac, float mpFrac, int activeSlot,
                     GlTexture? slot1Icon = null, GlTexture? slot2Icon = null,
                     GlTexture? slot3Icon = null, GlTexture? slot4Icon = null,
                     GlTexture? inventoryBtnAtlas = null,
                     bool railOpen = false,
                     GlTexture? inventoryBtnHovAtlas = null,
                     GlTexture? inventoryBtnDwnAtlas = null,
                     HitTarget hovered = HitTarget.None,
                     HitTarget pressed = HitTarget.None,
                     float slot1Progress = 0f, float slot2Progress = 0f,
                     float slot3Progress = 0f, float slot4Progress = 0f,
                     float slot1Flash = 0f, float slot2Flash = 0f,
                     float slot3Flash = 0f, float slot4Flash = 0f)
    {
        if (awpAtlas is null) return;
        float s = Scale(viewportH);

        // INFORAIL-AWP-CHROME — DS1 authors a single chrome window
        // BEHIND the portrait + HP/MP bars (gas:616 window_portait_panel_1
        // rect 0,3,65,55 uv 0,0.59375,0.253907,1) which pre-bakes the
        // "nifty boxes" around the bars. Without this layer the bars
        // and portrait appear to float on the world background.
        {
            int wx = (int)Math.Round(0  * s) + OffsetX;
            int wy = (int)Math.Round(3  * s) + OffsetY;
            int ww = (int)Math.Round(65 * s);
            int wh = (int)Math.Round(52 * s);
            iconRenderer.DrawIcon(viewportW, viewportH, awpAtlas, wx, wy, ww, wh, Vector4.One,
                0f, 1f - 1f, 0.253907f, 1f - 0.59375f);
        }

        // Slot-strip chrome. Two gas-authored variants:
        //   Max mode (rail closed, group=character_1_max):
        //     window_slots_panel_1 rect 64,3,148,40 uv 0.25,0.710938,
        //     0.578125,1 — the wide 4-slot frame.
        //   Min mode (rail open, group=character_1_min):
        //     window_pack_panel_min_1 rect 65,3,88,40 uv 0.65625,
        //     0.421875,0.835938,1 from b_gui_ig_mnu_awp_blank — the
        //     single-slot frame that sits above the close arrow.
        // Slot 1 stays visible in BOTH modes so the player can always
        // see (and click) the active skill. Slots 2/3/4 fold out
        // when the rail is open.
        if (!railOpen)
        {
            int wx = (int)Math.Round(64  * s) + OffsetX;
            int wy = (int)Math.Round(3   * s) + OffsetY;
            int ww = (int)Math.Round(84  * s); // 148-64
            int wh = (int)Math.Round(37  * s); // 40-3
            iconRenderer.DrawIcon(viewportW, viewportH, awpAtlas, wx, wy, ww, wh, Vector4.One,
                0.25f, 1f - 1f, 0.578125f, 1f - 0.710938f);
        }
        // (Min-mode single-slot chrome from b_gui_ig_mnu_awp_blank is
        // a separate atlas; lazy-load wiring is SC-AWP-MIN-SLOT-CHROME.)

        // HP bar — gas rect 2,6,11,52 (W=9, H=46), uv 0.007813,0.226563,
        // 0.042969,0.585938. dynamic_edge=top means fill from BOTTOM up;
        // visible height = h * frac, rendered at (y + h - fillH).
        DrawVerticalBar(iconRenderer, barRenderer, viewportW, viewportH, s, OffsetX, OffsetY,
            2, 6, 9, 46, hpFrac,
            0.007813f, 0.226563f, 0.042969f, 0.585938f, awpAtlas);
        // MP bar — gas rect 54,6,63,52 (W=9, H=46), uv 0.210938,0.226563,
        // 0.246095,0.585938.
        DrawVerticalBar(iconRenderer, barRenderer, viewportW, viewportH, s, OffsetX, OffsetY,
            54, 6, 9, 46, mpFrac,
            0.210938f, 0.226563f, 0.246095f, 0.585938f, awpAtlas);

        // Portrait — gas rect 13,6,52,52 (W=39, H=46). Three stacked layers:
        //   1. awp_portrait_selection_1 — frame BEHIND the portrait icon
        //      (uv 0.050781,0.226563,0.203125,0.585938). This is "the box
        //      around the head" — without it the portrait floats with no
        //      visible widget. Always-on per the gas (no visible=false).
        //   2. portrait icon (e.g. b_gui_ig_i_ic_c_fb_01 for farmboy) from
        //      the player template's [actor]portrait_icon. May be null
        //      pre-load; the frame still renders.
        //   3. (future) death / health_warning / unconscious overlays —
        //      SC-AUTH-CHAR-AWP-STATES.
        {
            int px = (int)Math.Round(13 * s) + OffsetX;
            int py = (int)Math.Round(6  * s) + OffsetY;
            int pw = (int)Math.Round(39 * s);
            int ph = (int)Math.Round(46 * s);
            // Frame (selection texture) — drawn first so the icon sits inside.
            // V-flip per the bottom-up RAW rule.
            iconRenderer.DrawIcon(viewportW, viewportH, awpAtlas, px, py, pw, ph, Vector4.One,
                0.050781f, 1f - 0.585938f, 0.203125f, 1f - 0.226563f);
            // INFORAIL-E (user fold) — portrait fills the gas frame
            // rect (39w × 46h). DS1 portrait RAWs (b_gui_ig_i_ic_c_*)
            // are authored in a matching aspect, so stretching to fill
            // looks correct and matches the original game's "face fills
            // the box" presentation. Previous aspect-preserve square
            // left visible empty top/bottom strips.
            if (portrait is not null)
            {
                // Crop to opaque bounds so the face fills the frame regardless
                // of how the character-creator padded the portrait raw, then
                // inset ~5% so it doesn't touch the frame/screen border.
                var uv = portrait.ContentUv;
                int inx = (int)Math.Round(pw * 0.05f), iny = (int)Math.Round(ph * 0.05f);
                iconRenderer.DrawIcon(viewportW, viewportH, portrait,
                    px + inx, py + iny, pw - 2 * inx, ph - 2 * iny, Vector4.One,
                    uv.X, uv.Y, uv.Z, uv.W);
            }
        }

        // 4 weapon/skill slots. Each shows the slot's "active" texture
        // (uv 0.839844,0.734375,0.902344,0.984375 — the slot frame). The
        // currently-selected slot gets a selection overlay (uv 0.675781,
        // 0.710938,0.753907,0.992188) drawn on top.
        // Slot frames + icons + selection overlay.
        //   Max mode: all 4 slots draw.
        //   Min mode: only slot 1 (the active-skill indicator) draws;
        //     slots 2/3/4 fold out so the world is visible.
        // In min mode the active slot shows whichever weapon/spell is
        // selected (driven by activeSlot), and clicking it opens the
        // spell list — same gas messages as max mode's slot 1.
        var slotIcons = new[] { slot1Icon, slot2Icon, slot3Icon, slot4Icon };
        var slotProgs = new[] { slot1Progress, slot2Progress, slot3Progress, slot4Progress };
        var slotFlash = new[] { slot1Flash, slot2Flash, slot3Flash, slot4Flash };
        int maxSlot = railOpen ? 1 : 4;
        for (int i = 0; i < maxSlot; i++)
        {
            int gasX = 68 + i * 20; // slots at 68, 88, 108, 128
            int sx = (int)Math.Round(gasX * s) + OffsetX;
            int sy = (int)Math.Round(6 * s) + OffsetY;
            int sw = (int)Math.Round(16 * s);
            int sh = (int)Math.Round(32 * s);
            // INFORAIL skill progress — per gas bar_slot_N_skill_1
            // (line 75+) the SLOT FRAME TEXTURE itself is the progress
            // indicator. It's a status_bar with dynamic_edge=top, so
            // the texture reveals bottom-to-top as the skill XP fills
            // toward the next level. At 0% the slot is "empty" (only
            // the underlying dark window_slots_panel_1 chrome at
            // #2F2C2B with rounded corners shows through); at 100%
            // the full mauve #635757-tinted frame texture is visible.
            // Hitting next level resets the bar to 0%.
            //
            // Slot 1 = Melee XP, slot 2 = Ranged, slot 3 = Combat Magic
            // (primary spell's caster), slot 4 = Nature Magic (secondary
            // spell's caster). In min mode the visible slot mirrors the
            // ACTIVE slot's progress, not slot 1's.
            float progFrac = (railOpen && i == 0 && activeSlot >= 0 && activeSlot < 4)
                ? slotProgs[activeSlot] : slotProgs[i];
            progFrac = Math.Clamp(progFrac, 0f, 1f);
            if (progFrac > 0f)
            {
                // Reveal the gas-authored frame texture from bottom up.
                // gas uv v range = 0.734375..0.984375 (height 0.25 in
                // texture space); when we sample only the BOTTOM
                // progFrac of that range, the drawn rect is also
                // progFrac of the slot's screen height, anchored at
                // the slot's bottom edge.
                const float gasV0 = 0.734375f, gasV1 = 0.984375f;
                int fillH = (int)Math.Round(sh * progFrac);
                if (fillH > 0)
                {
                    // Sample the BOTTOM portion of the gas v-range; in
                    // bottom-up gas space, bottom=v0 and top=v0 + delta.
                    float gasVTop = gasV0 + (gasV1 - gasV0) * progFrac;
                    iconRenderer.DrawIcon(viewportW, viewportH, awpAtlas,
                        sx, sy + sh - fillH, sw, fillH, Vector4.One,
                        0.839844f, 1f - gasVTop, 0.902344f, 1f - gasV0);
                }
            }
            // Slot content (weapon or spell icon) — drawn inside the frame
            // with a 1px inset so the chrome stays visible. In min mode
            // (i == 0 and railOpen) we display the ACTIVE skill's icon
            // even though we're drawing in the slot-1 rect — slot 1 is
            // doubling as the "currently active" indicator the way DS1's
            // bar_slot_active_skill_1 (gas:40) does.
            var iconToDraw = (railOpen && i == 0 && activeSlot >= 0 && activeSlot < 4)
                ? slotIcons[activeSlot] : slotIcons[i];
            if (iconToDraw is { } ico)
            {
                int inset = (int)Math.Round(1 * s);
                iconRenderer.DrawIcon(viewportW, viewportH, ico,
                    sx + inset, sy + inset, sw - 2 * inset, sh - 2 * inset, Vector4.One);
            }
            // SC-AWP-XP-PULSE — DS1's razor-thin green flash on each XP tick.
            // A solid green line (the game's selection-green) sits at the TOP
            // edge of the current fill and fades out as the pulse decays, so
            // the box reads as "just nudged up". No gradient — that's on the
            // persistent fill, not this tick. In min mode slot 0 mirrors the
            // active slot's pulse, same as its progress/icon.
            float flashVal = (railOpen && i == 0 && activeSlot >= 0 && activeSlot < 4)
                ? slotFlash[activeSlot] : slotFlash[i];
            if (flashVal > 0f)
            {
                int fillHnow = (int)Math.Round(sh * progFrac);
                int thick = Math.Max(1, (int)Math.Round(1.3f * s)); // razor thin
                int lineTop = sy + sh - fillHnow - thick;
                if (lineTop < sy) lineTop = sy;
                if (lineTop > sy + sh - thick) lineTop = sy + sh - thick;
                int inx = (int)Math.Round(1 * s);
                var green = new Vector4(0.05f, 0.78f, 0.10f, Math.Clamp(flashVal, 0f, 1f) * 0.95f);
                barRenderer.DrawRect(viewportW, viewportH,
                    sx + inx, lineTop, sw - 2 * inx, thick, green);
            }
            // Selection overlay on the active slot
            if (i == activeSlot)
            {
                iconRenderer.DrawIcon(viewportW, viewportH, awpAtlas, sx, sy, sw, sh, Vector4.One,
                    0.675781f, 1f - 0.992188f, 0.753907f, 1f - 0.710938f);
            }
        }

        // INFORAIL-C — DS1's max-mode → min-mode transformation under
        // the slot strip. There are TWO mutually exclusive widgets here:
        //   group=character_1_max → wide "Inventory" button at rect
        //     64,40,148,55 (gas line 169, texture b_gui_ig_mnu_awp_buttons
        //     uv 0,0.0625,0.65625,1). Shown ONLY when the info-rail
        //     is closed (i.e. when the AWP is in its "default playing"
        //     state and clicking the wide button OPENS the rail).
        //   group=character_1_min → narrow ⟵-close arrow at rect
        //     64,40,87,56 (gas line 197 awp_button_inventory_small_1,
        //     uv 0.820313,0,1,1). Shown ONLY when the rail is OPEN, as
        //     the close button below skill slot 1.
        // Previously this widget was rendered always-on which produced
        // two arrow boxes; the rail-open gate fixes the duplication
        // and matches DS1's group-toggle behavior.
        if (inventoryBtnAtlas is not null)
        {
            // Hover/press state swap per gas messages on both
            // awp_button_inventory_1 (line 169) and
            // awp_button_inventory_inv_small_1 (line 197):
            //   onlbuttondown → b_gui_ig_mnu_awp_buttons-dwn
            //   onrollover    → b_gui_ig_mnu_awp_buttons-hov
            //   default       → b_gui_ig_mnu_awp_buttons
            HitTarget target = railOpen ? HitTarget.CloseArrow
                                        : HitTarget.InventoryButton;
            GlTexture atlas = inventoryBtnAtlas;
            if (pressed == target && inventoryBtnDwnAtlas is not null)
                atlas = inventoryBtnDwnAtlas;
            else if (hovered == target && inventoryBtnHovAtlas is not null)
                atlas = inventoryBtnHovAtlas;

            if (railOpen)
            {
                // Close ⟵ arrow (min mode), uv 0.820313,0,1,1.
                int bx = (int)Math.Round(64 * s) + OffsetX;
                int by = (int)Math.Round(40 * s) + OffsetY;
                int bw = (int)Math.Round(23 * s); // 87-64
                int bh = (int)Math.Round(16 * s); // 56-40
                iconRenderer.DrawIcon(viewportW, viewportH, atlas,
                    bx, by, bw, bh, Vector4.One,
                    0.820313f, 0f, 1f, 1f);
            }
            else
            {
                // Wide Inventory button (max mode), uv 0,0.0625,0.65625,1.
                int bx = (int)Math.Round(64 * s) + OffsetX;
                int by = (int)Math.Round(40 * s) + OffsetY;
                int bw = (int)Math.Round(84 * s); // 148-64
                int bh = (int)Math.Round(15 * s); // 55-40
                iconRenderer.DrawIcon(viewportW, viewportH, atlas,
                    bx, by, bw, bh, Vector4.One,
                    0f, 0.0625f, 0.65625f, 1f);
            }
        }
    }

    private static void DrawVerticalBar(
        IconRenderer iconRenderer, BarRenderer barRenderer,
        int viewportW, int viewportH, float scale, int offX, int offY,
        int gasX, int gasY, int gasW, int gasH, float frac,
        float gasU0, float gasV0, float gasU1, float gasV1,
        GlTexture atlas)
    {
        int x = (int)Math.Round(gasX * scale) + offX;
        int y = (int)Math.Round(gasY * scale) + offY;
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
