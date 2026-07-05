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
    public static float Scale(int viewportH) => viewportH / (float)RefRes;

    // First follower cell top + per-cell vertical stride (640×480 ref).
    const int CellTop0 = 56, CellStep = 53;

    public readonly record struct Member(
        GlTexture? Portrait, float HpFrac, float MpFrac, bool Dead, bool Selected,
        GlTexture? Slot1 = null, GlTexture? Slot2 = null, GlTexture? Slot3 = null,
        GlTexture? Slot4 = null, int ActiveSlot = -1);

    /// <summary>Follower cell (0-based, i.e. PartyIndex-1) under the cursor,
    /// or -1. Only the portrait rect is clickable (matches the gas
    /// itemslot).</summary>
    public int HitTest(int x, int y, int viewportH, int followerCount)
    {
        float s = Scale(viewportH);
        for (int i = 0; i < followerCount; i++)
        {
            int top = CellTop0 + i * CellStep;
            int cx = (int)MathF.Round(13 * s), cy = (int)MathF.Round((top + 3) * s);
            int cw = (int)MathF.Round(39 * s), ch = (int)MathF.Round(46 * s);
            if (x >= cx && x < cx + cw && y >= cy && y < cy + ch) return i;
        }
        return -1;
    }

    public void Draw(IconRenderer icons, BarRenderer bars, int viewportW, int viewportH,
                     GlTexture awpAtlas, IReadOnlyList<Member> members, GlTexture? deathTex,
                     GlTexture? chevronTex = null)
    {
        if (awpAtlas is null) return;
        float s = Scale(viewportH);
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
            DrawWeaponStrip(icons, bars, viewportW, viewportH, s, top, m, awpAtlas, chevronTex);

            // Chrome frame behind the bars + portrait (gas window_portait_panel
            // uv 0,0.59375,0.253907,1; V-flipped for the bottom-up RAW).
            int wx = (int)MathF.Round(0 * s), wy = (int)MathF.Round(top * s);
            int ww = (int)MathF.Round(65 * s), wh = (int)MathF.Round(52 * s);
            icons.DrawIcon(viewportW, viewportH, awpAtlas, wx, wy, ww, wh, Vector4.One,
                0f, 1f - 1f, 0.253907f, 1f - 0.59375f);

            // HP (left) + MP (right) vertical bars.
            DrawVerticalBar(icons, bars, viewportW, viewportH, s, 2, top + 3, 9, 46, m.HpFrac,
                0.007813f, 0.226563f, 0.042969f, 0.585938f, awpAtlas);
            DrawVerticalBar(icons, bars, viewportW, viewportH, s, 54, top + 3, 9, 46, m.MpFrac,
                0.210938f, 0.226563f, 0.246095f, 0.585938f, awpAtlas);

            // Portrait: frame first (uv 0.050781..0.203125), then the face,
            // then the death mask / selection ring.
            int px = (int)MathF.Round(13 * s), py = (int)MathF.Round((top + 3) * s);
            int pw = (int)MathF.Round(39 * s), ph = (int)MathF.Round(46 * s);
            icons.DrawIcon(viewportW, viewportH, awpAtlas, px, py, pw, ph, Vector4.One,
                0.050781f, 1f - 0.585938f, 0.203125f, 1f - 0.226563f);
            if (m.Portrait is not null)
                icons.DrawIcon(viewportW, viewportH, m.Portrait, px, py, pw, ph, Vector4.One);
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
        IconRenderer icons, BarRenderer bars, int viewportW, int viewportH, float s,
        int top, in Member m, GlTexture awpAtlas, GlTexture? chevronTex)
    {
        // Slot chrome (the wide 4-slot box): leader window_slots_panel
        // 64,3,148,40 → here (64, top, 84, 37). uv V-flipped for the RAW.
        int sx = (int)MathF.Round(64 * s), sy = (int)MathF.Round(top * s);
        int sw = (int)MathF.Round(84 * s), sh = (int)MathF.Round(37 * s);
        icons.DrawIcon(viewportW, viewportH, awpAtlas, sx, sy, sw, sh, Vector4.One,
            0.25f, 1f - 1f, 0.578125f, 1f - 0.710938f);

        // Four slots at x 68/88/108/128, y top+3, 16×32. Active gets the green
        // ring DS1 draws around the selected weapon set.
        var slots = new[] { m.Slot1, m.Slot2, m.Slot3, m.Slot4 };
        for (int k = 0; k < 4; k++)
        {
            int slx = (int)MathF.Round((68 + k * 20) * s), sly = (int)MathF.Round((top + 3) * s);
            int slw = (int)MathF.Round(16 * s), slh = (int)MathF.Round(32 * s);
            var slotTex = slots[k];
            if (slotTex is not null)
                icons.DrawIcon(viewportW, viewportH, slotTex, slx, sly, slw, slh, Vector4.One);
            if (k == m.ActiveSlot)
                bars.DrawBorder(viewportW, viewportH, slx, sly, slw, slh,
                    new Vector4(0.36f, 0.85f, 0.31f, 1f));
        }

        // >>> chevron button below the slots: leader awp_buttons 64,40,148,55.
        if (chevronTex is not null)
        {
            int chx = (int)MathF.Round(64 * s), chy = (int)MathF.Round((top + 37) * s);
            int chw = (int)MathF.Round(84 * s), chh = (int)MathF.Round(15 * s);
            icons.DrawIcon(viewportW, viewportH, chevronTex, chx, chy, chw, chh, Vector4.One);
        }
    }

    // Verbatim from CharacterAwp — vertical bar with dynamic_edge=top fill.
    private static void DrawVerticalBar(
        IconRenderer icons, BarRenderer bars, int viewportW, int viewportH, float scale,
        int gasX, int gasY, int gasW, int gasH, float frac,
        float gasU0, float gasV0, float gasU1, float gasV1, GlTexture atlas)
    {
        int x = (int)MathF.Round(gasX * scale), y = (int)MathF.Round(gasY * scale);
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
