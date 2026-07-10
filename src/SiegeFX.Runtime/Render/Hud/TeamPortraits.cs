using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 27 — DS1's team-portraits strip
/// (<c>/ui/interfaces/backend/team_portraits/team_portraits.gas</c>): a
/// left-docked vertical column of party-member cells stacked below the
/// leader's character_awp portrait. Each cell reuses the shared
/// <c>b_gui_ig_mnu_awp</c> atlas — a chrome frame, a vertical HP bar (left),
/// a vertical MP bar (right), and the member portrait — exactly like
/// <see cref="CharacterAwp"/>'s slot 1, just offset down the strip.
///
/// Authored cells: slot 2 at y=56, then ~53px per slot (slot 3 y=109,
/// slot 4 y=162, …), each 65×52. Portrait/bars sit 3px inside the frame.
/// </summary>
public sealed class TeamPortraits
{
    public const int RefRes = 480;
    public static float Scale(int viewportH) => HudScale.Hud(viewportH);

    /// <summary>SC-HUD-DRAG — pixel offset of the whole strip from its
    /// authored left-dock. Set by the host from the user's shift-dragged
    /// position; (0,0) = the authored layout. Draw and HitTest both honor
    /// it so visuals and clicks can never desync.</summary>
    public int OffsetX, OffsetY;

    /// <summary>SC-HUD-DRAG — the strip's actual on-screen bounds from the
    /// last Draw. The host's drag pickup tests THIS (not re-derived math)
    /// so grabbing can never disagree with what's on screen.</summary>
    public (int X, int Y, int W, int H) LastDrawnRect { get; private set; }

    // First follower cell top + per-cell vertical stride (640×480 ref).
    // Public: the host derives the strip's default top for drag pickup.
    public const int CellTop0 = 56, CellStep = 53;

    public readonly record struct Member(
        GlTexture? Portrait, float HpFrac, float MpFrac, bool Dead, bool Selected,
        GlTexture? Slot1 = null, GlTexture? Slot2 = null, GlTexture? Slot3 = null,
        GlTexture? Slot4 = null, int ActiveSlot = -1);

    /// <summary>Which widget in a follower cell was clicked.</summary>
    public enum HitKind { None, Portrait, Chevron, Slot }

    /// <summary>Result of <see cref="HitTest"/>: the follower index (0-based,
    /// i.e. PartyIndex-1), the widget kind, and — for <see cref="HitKind.Slot"/>
    /// — the 0-based slot (0 melee, 1 ranged, 2 primary spell, 3 secondary).</summary>
    public readonly record struct HitResult(int Member, HitKind Kind, int Slot);

    /// <summary>Hit-tests a follower cell's interactive widgets: the portrait
    /// (select), the >>> chevron (open inventory), and the four weapon/skill
    /// slots (switch active combat mode) — at the same rects Draw uses.</summary>
    public HitResult HitTest(int x, int y, int viewportH, int followerCount)
    {
        float s = Scale(viewportH);
        // SC-HUD-DRAG — transform the point into the un-offset frame so
        // every authored rect below stays valid at any dragged position.
        x -= OffsetX; y -= OffsetY;
        for (int i = 0; i < followerCount; i++)
        {
            int top = CellTop0 + i * CellStep;
            if (Hit(x, y, s, 13, top + 3, 39, 46)) return new(i, HitKind.Portrait, -1);
            if (Hit(x, y, s, 64, top + 37, 84, 15)) return new(i, HitKind.Chevron, -1);
            for (int k = 0; k < 4; k++)
                if (Hit(x, y, s, 68 + k * 20, top + 3, 16, 32)) return new(i, HitKind.Slot, k);
        }
        return new(-1, HitKind.None, -1);
    }

    private static bool Hit(int x, int y, float s, int rx, int ry, int rw, int rh)
    {
        int ax = (int)MathF.Round(rx * s), ay = (int)MathF.Round(ry * s);
        int aw = (int)MathF.Round(rw * s), ah = (int)MathF.Round(rh * s);
        return x >= ax && x < ax + aw && y >= ay && y < ay + ah;
    }

    public void Draw(IconRenderer icons, BarRenderer bars, int viewportW, int viewportH,
                     GlTexture awpAtlas, IReadOnlyList<Member> members, GlTexture? deathTex,
                     GlTexture? chevronTex = null)
    {
        if (awpAtlas is null) return;
        float s = Scale(viewportH);
        LastDrawnRect = members.Count == 0
            ? default
            : (OffsetX,
               (int)MathF.Round(CellTop0 * s) + OffsetY,
               (int)MathF.Round(150 * s),
               (int)MathF.Round(members.Count * CellStep * s));
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            int top = CellTop0 + i * CellStep;

            // Weapon/skill strip — character_awp's 4-slot frame + >>> chevron,
            // replicated per member. The leader authors these at frame-top y3;
            // this cell's frame top is `top`, so everything shifts down by top-3.
            //   slot chrome: window_slots_panel 64,3,148,40 uv 0.25,0.710938,0.578125,1
            //   4 slots: x 68/88/108/128, y6, 16×32 (slot1 melee … slot4 spell)
            //   chevron: awp_buttons 64,40,148,55
            DrawWeaponStrip(icons, viewportW, viewportH, s, OffsetX, OffsetY, top, m, awpAtlas, chevronTex);

            // Chrome frame behind the bars + portrait (gas window_portait_panel
            // uv 0,0.59375,0.253907,1; V-flipped for the bottom-up RAW).
            int wx = (int)MathF.Round(0 * s) + OffsetX, wy = (int)MathF.Round(top * s) + OffsetY;
            int ww = (int)MathF.Round(65 * s), wh = (int)MathF.Round(52 * s);
            icons.DrawIcon(viewportW, viewportH, awpAtlas, wx, wy, ww, wh, Vector4.One,
                0f, 1f - 1f, 0.253907f, 1f - 0.59375f);

            // HP (left) + MP (right) vertical bars.
            DrawVerticalBar(icons, bars, viewportW, viewportH, s, OffsetX, OffsetY, 2, top + 3, 9, 46, m.HpFrac,
                0.007813f, 0.226563f, 0.042969f, 0.585938f, awpAtlas);
            DrawVerticalBar(icons, bars, viewportW, viewportH, s, OffsetX, OffsetY, 54, top + 3, 9, 46, m.MpFrac,
                0.210938f, 0.226563f, 0.246095f, 0.585938f, awpAtlas);

            // Portrait: frame first (uv 0.050781..0.203125), then the face,
            // then the death mask / selection ring.
            int px = (int)MathF.Round(13 * s) + OffsetX, py = (int)MathF.Round((top + 3) * s) + OffsetY;
            int pw = (int)MathF.Round(39 * s), ph = (int)MathF.Round(46 * s);
            icons.DrawIcon(viewportW, viewportH, awpAtlas, px, py, pw, ph, Vector4.One,
                0.050781f, 1f - 0.585938f, 0.203125f, 1f - 0.226563f);
            if (m.Portrait is not null)
            {
                // Crop to opaque bounds so the face fills the frame (the raw
                // pads the face with transparency; framing varies per member),
                // then inset ~5% so it doesn't touch the frame/screen border.
                var uv = m.Portrait.ContentUv;
                int inx = (int)MathF.Round(pw * 0.05f), iny = (int)MathF.Round(ph * 0.05f);
                icons.DrawIcon(viewportW, viewportH, m.Portrait,
                    px + inx, py + iny, pw - 2 * inx, ph - 2 * iny, Vector4.One,
                    uv.X, uv.Y, uv.Z, uv.W);
            }
            if (m.Dead && deathTex is not null)
                icons.DrawIcon(viewportW, viewportH, deathTex, px, py, pw, ph, Vector4.One);
            if (m.Selected)
                bars.DrawBorder(viewportW, viewportH, px - 1, py - 1, pw + 2, ph + 2,
                    new Vector4(0.95f, 0.85f, 0.40f, 1f));
        }
    }

    // The per-member weapon/skill strip: the 4-slot chrome frame, each slot's
    // weapon/spell icon, a green ring on the active slot, and the >>> chevron —
    // the same widgets character_awp draws for the leader, offset to this cell.
    private static void DrawWeaponStrip(
        IconRenderer icons, int viewportW, int viewportH, float s,
        int offX, int offY,
        int top, in Member m, GlTexture awpAtlas, GlTexture? chevronTex)
    {
        // Slot chrome (the wide 4-slot box): leader window_slots_panel
        // 64,3,148,40 → here (64, top, 84, 37). uv V-flipped for the RAW.
        int sx = (int)MathF.Round(64 * s) + offX, sy = (int)MathF.Round(top * s) + offY;
        int sw = (int)MathF.Round(84 * s), sh = (int)MathF.Round(37 * s);
        icons.DrawIcon(viewportW, viewportH, awpAtlas, sx, sy, sw, sh, Vector4.One,
            0.25f, 1f - 1f, 0.578125f, 1f - 0.710938f);

        // Four slots at x 68/88/108/128, y top+3, 16×32 — exactly the leader
        // AWP: the weapon/spell icon (1px inset) plus, on the active slot, the
        // selection overlay (awp atlas uv 0.675781,0.710938,0.753907,0.992188).
        var slots = new[] { m.Slot1, m.Slot2, m.Slot3, m.Slot4 };
        for (int k = 0; k < 4; k++)
        {
            int slx = (int)MathF.Round((68 + k * 20) * s) + offX, sly = (int)MathF.Round((top + 3) * s) + offY;
            int slw = (int)MathF.Round(16 * s), slh = (int)MathF.Round(32 * s);
            var slotTex = slots[k];
            if (slotTex is not null)
            {
                int inset = (int)MathF.Round(1 * s);
                icons.DrawIcon(viewportW, viewportH, slotTex,
                    slx + inset, sly + inset, slw - 2 * inset, slh - 2 * inset, Vector4.One);
            }
            if (k == m.ActiveSlot)
                icons.DrawIcon(viewportW, viewportH, awpAtlas, slx, sly, slw, slh, Vector4.One,
                    0.675781f, 1f - 0.992188f, 0.753907f, 1f - 0.710938f);
        }

        // >>> chevron: the leader's wide-button crop of awp_buttons
        // (uv 0,0.0625,0.65625,1), same texture and region the player uses.
        if (chevronTex is not null)
        {
            int chx = (int)MathF.Round(64 * s) + offX, chy = (int)MathF.Round((top + 37) * s) + offY;
            int chw = (int)MathF.Round(84 * s), chh = (int)MathF.Round(15 * s);
            icons.DrawIcon(viewportW, viewportH, chevronTex, chx, chy, chw, chh, Vector4.One,
                0f, 0.0625f, 0.65625f, 1f);
        }
    }

    // Verbatim from CharacterAwp — vertical bar with dynamic_edge=top fill.
    private static void DrawVerticalBar(
        IconRenderer icons, BarRenderer bars, int viewportW, int viewportH, float scale,
        int offX, int offY,
        int gasX, int gasY, int gasW, int gasH, float frac,
        float gasU0, float gasV0, float gasU1, float gasV1, GlTexture atlas)
    {
        int x = (int)MathF.Round(gasX * scale) + offX, y = (int)MathF.Round(gasY * scale) + offY;
        int w = (int)MathF.Round(gasW * scale), h = (int)MathF.Round(gasH * scale);
        bars.DrawRect(viewportW, viewportH, x, y, w, h, new Vector4(0.05f, 0.05f, 0.05f, 0.7f));
        float f = Math.Clamp(frac, 0f, 1f);
        if (f > 0f)
        {
            int fillH = (int)MathF.Round(h * f);
            int fillY = y + h - fillH;
            float gasVCropped = gasV0 + (gasV1 - gasV0) * f;
            icons.DrawIcon(viewportW, viewportH, atlas, x, fillY, w, fillH, Vector4.One,
                gasU0, 1f - gasVCropped, gasU1, 1f - gasV0);
        }
        bars.DrawBorder(viewportW, viewportH, x, y, w, h, new Vector4(0f, 0f, 0f, 1f));
    }
}
