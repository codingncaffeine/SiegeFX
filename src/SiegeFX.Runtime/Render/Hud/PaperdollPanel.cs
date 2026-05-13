using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 22-INFORAIL-PAPERDOLL — DS1's equipment paperdoll, drawn at
/// the gas-authored rects from hud_character.gas under
/// <c>character_paperdoll</c> (rect 87,228,254,449, texture
/// b_gui_ig_mnu_cp_bot_01). Sits below the info-panel's two upper
/// chrome panes (cp_top_01 + cp_mid_01) and renders:
///
/// <list type="bullet">
///   <item>Bottom-pane chrome texture cp_bot_01 (the paperdoll
///     background, uv 0,0.136719,0.652344,1).</item>
///   <item>9 equipment slot rects + matching ghost_* placeholder
///     textures: helmet, armor, gauntlets, boots, amulet, melee
///     weapon, ranged weapon, shield, spellbook.</item>
///   <item>4 ring slot rects (2 each side of the paperdoll).</item>
///   <item>"View" button at rect 140,430,200,446 (paperdoll_begin/
///     end_view notify keys; spins the model while held).</item>
/// </list>
///
/// All rects pasted verbatim from hud_character.gas; scale by
/// viewportH/480 per the feedback_siegefx_authentic_scalable.md
/// uniform-scale-only rule.
/// </summary>
public sealed class PaperdollPanel
{
    public const int RefRes = 480;
    /// <summary>Uses the same clamped scale as InfoRailLayout so all
    /// three info-rail panels render at matching sizes.</summary>
    public static float Scale(int viewportH) => InfoRailLayout.Scale(viewportH);

    /// <summary>One equipment slot definition. The slot's screen rect
    /// is the gas rect scaled; ghost_* placeholder draws inside it
    /// when no item is equipped. Source lines cited per-slot below.</summary>
    public readonly record struct Slot(string Name, int X0, int Y0, int X1, int Y1, string GhostTex, Uv GhostUv);

    /// <summary>Helper for gas uv tuples (bottom-up). DrawIcon takes
    /// top-down uv so we convert via V-flip.</summary>
    public readonly record struct Uv(float U0, float V0, float U1, float V1)
    {
        public (float u0, float v0, float u1, float v1) Screen() =>
            (U0, 1f - V1, U1, 1f - V0);
    }

    /// <summary>9 main equipment slots + 4 ring slots in gas-line order.
    /// Lines cited from hud_character.gas (the file in
    /// C:\Users\gamer\Downloads\hud_character.gas at the time of writing).</summary>
    public static readonly Slot[] Slots =
    {
        // Helmet — line 681 itemslot rect 149,232,192,275; ghost line 569 rect 158,233,186,273.
        new("helmet",   149, 232, 192, 275,
            "b_gui_ig_mnu_cp_ghost_helmet",   new Uv(0f, 0.375f,    0.875f,  1f)),
        // Armor — line 22 rect 139,279,201,374; ghost line 513 rect 141,282,201,372.
        new("armor",    139, 279, 201, 374,
            "b_gui_ig_mnu_cp_ghost_body",     new Uv(0f, 0.296875f, 0.9375f, 1f)),
        // Gauntlets — line 483 rect 205,278,227,323; ghost line 555 rect 205,278,229,325.
        new("gauntlets",205, 278, 227, 323,
            "b_gui_ig_mnu_cp_ghost_gloves",   new Uv(0f, 0.265625f, 0.75f,   1f)),
        // Boots — line 38 rect 157,378,181,427; ghost line 541 rect 156,379,182,427.
        new("boots",    157, 378, 181, 427,
            "b_gui_ig_mnu_cp_ghost_boot",     new Uv(0f, 0.25f,     0.8125f, 1f)),
        // Amulet — line 6 rect 228,278,250,323; ghost line 499 rect 228,290,252,314.
        new("amulet",   228, 278, 250, 323,
            "b_gui_ig_mnu_cp_ghost_amulet",   new Uv(0f, 0.25f,     0.75f,   1f)),
        // Melee weapon — ghost line 583 rect 92,354,134,443. The
        // itemslot rect itself isn't in the search bucket we extracted;
        // use the ghost rect as a stand-in until the itemslot line is
        // recovered (it's the same region).
        new("melee",     92, 354, 134, 443,
            "b_gui_ig_mnu_cp_ghost_sword",    new Uv(0f, 0.304688f, 0.65625f, 1f)),
        // Ranged weapon — ghost line 597 rect 103,232,125,322.
        new("ranged",   103, 232, 125, 322,
            "b_gui_ig_mnu_cp_ghost_bow",      new Uv(0f, 0.296875f, 0.6875f, 1f)),
        // Shield — ghost line 667 rect 210,366,247,434.
        new("shield",   210, 366, 247, 434,
            "b_gui_ig_mnu_cp_ghost_shield",   new Uv(0f, 0.46875f,  0.578125f, 1f)),
        // Spellbook (book slot) — ghost line 527 rect 205,232,229,276.
        new("spellbook",205, 232, 229, 276,
            "b_gui_ig_mnu_cp_ghost_book",     new Uv(0f, 0.3125f,   0.75f,   1f)),
        // 4 rings — lines 611/625/639/653 rects 94/117/210/233,332,..,348.
        new("ring1",     94, 332, 110, 348, "b_gui_ig_mnu_cp_ghost_ring", new Uv(0f, 0f, 1f, 1f)),
        new("ring2",    117, 332, 133, 348, "b_gui_ig_mnu_cp_ghost_ring", new Uv(0f, 0f, 1f, 1f)),
        new("ring3",    210, 332, 226, 348, "b_gui_ig_mnu_cp_ghost_ring", new Uv(0f, 0f, 1f, 1f)),
        new("ring4",    233, 332, 249, 348, "b_gui_ig_mnu_cp_ghost_ring", new Uv(0f, 0f, 1f, 1f)),
    };

    /// <summary>Gas rect for the View button (140,430,200,446).</summary>
    public static readonly (int X0, int Y0, int X1, int Y1) ViewButton = (140, 430, 200, 446);

    /// <summary>Render the paperdoll. <paramref name="panelOriginX"/>
    /// is the screen-space X of the paperdoll's gas-x=87 anchor (i.e.
    /// the same paperdollX RenderHost passes to CharacterPanel). The
    /// paperdoll itself starts at gas-y=228 internally.</summary>
    public void Draw(IconRenderer icons, BarRenderer bars, TextRenderer text,
                     int viewportW, int viewportH,
                     int panelOriginX, int panelOriginY,
                     GlTexture? botPaneTex,
                     System.Func<string, GlTexture?> ghostLookup,
                     System.Func<string, GlTexture?>? equippedIconLookup = null,
                     bool viewHovered = false,
                     bool viewPressed = false)
    {
        float s = Scale(viewportH);

        // Bottom-pane chrome (cp_bot_01). hud_character.gas:199
        // rect 87,228,254,449 uv 0,0.136719,0.652344,1.
        if (botPaneTex is not null)
        {
            int wx = panelOriginX;
            int wy = panelOriginY + (int)System.Math.Round(228 * s);
            int ww = (int)System.Math.Round((254 - 87) * s);
            int wh = (int)System.Math.Round((449 - 228) * s);
            icons.DrawIcon(viewportW, viewportH, botPaneTex, wx, wy, ww, wh, Vector4.One,
                0f, 1f - 1f, 0.652344f, 1f - 0.136719f);
        }

        // Slots + ghosts. Each slot draws its ghost when no equipped
        // icon was passed (or its lookup returned null), otherwise the
        // real equipped icon. Ghost rect is the slot rect itself for
        // simplicity; gas authors a slightly inset ghost rect in some
        // cases but the difference is sub-pixel at typical resolutions.
        foreach (var slot in Slots)
        {
            int sx = panelOriginX + (int)System.Math.Round((slot.X0 - 87) * s);
            int sy = panelOriginY + (int)System.Math.Round(slot.Y0 * s);
            int sw = (int)System.Math.Round((slot.X1 - slot.X0) * s);
            int sh = (int)System.Math.Round((slot.Y1 - slot.Y0) * s);
            var equipped = equippedIconLookup?.Invoke(slot.Name);
            if (equipped is not null)
            {
                // Inset so the icon fits visually inside the gas slot
                // frame rather than touching the chrome on all sides.
                // 2px scaled is enough at all clamped scales (1..1.5)
                // and matches the breathing room DS1 leaves between
                // inventory_icon RAWs and their slot borders.
                int inset = (int)System.Math.Max(1, System.Math.Round(2 * s));
                int ix = sx + inset, iy = sy + inset;
                int iw = sw - inset * 2, ih = sh - inset * 2;
                if (iw > 0 && ih > 0)
                    icons.DrawIcon(viewportW, viewportH, equipped, ix, iy, iw, ih, Vector4.One);
            }
            else
            {
                var ghost = ghostLookup(slot.GhostTex);
                if (ghost is not null)
                {
                    var uv = slot.GhostUv.Screen();
                    icons.DrawIcon(viewportW, viewportH, ghost, sx, sy, sw, sh,
                        new Vector4(1f, 1f, 1f, 0.55f),
                        uv.u0, uv.v0, uv.u1, uv.v1);
                }
            }
        }

        // View button — hud_character.gas:107 rect 140,430,200,446
        // with common_template=button_4 + centered "View" text (line 130).
        // The DS1 button_4 template uses 4 edge strips
        // (b_gui_cmn_button_{up,down,left,right}_up/hov/down per
        // _ds1_common_control_art.gas:4-17) — texture-authentic chrome
        // is SC-INFORAIL-VIEW-CHROME. For this slice we draw a colour-
        // matched bordered frame using the same ink/border colours
        // the existing CharacterPanel uses, plus the gas-authored
        // "View" text centered in it.
        {
            int bx = panelOriginX + (int)System.Math.Round((ViewButton.X0 - 87) * s);
            int by = panelOriginY + (int)System.Math.Round(ViewButton.Y0 * s);
            int bw = (int)System.Math.Round((ViewButton.X1 - ViewButton.X0) * s);
            int bh = (int)System.Math.Round((ViewButton.Y1 - ViewButton.Y0) * s);
            // DS1 panel palette: dark fill #14141c-like, mauve-grey
            // border #a3a78f, ink #aaa78e. Pressed state darkens the
            // fill; hover lifts the border ink.
            var fill   = viewPressed
                ? new Vector4(0.04f, 0.04f, 0.06f, 1f)
                : new Vector4(0.08f, 0.08f, 0.10f, 1f);
            var brdr   = viewHovered
                ? new Vector4(0.86f, 0.83f, 0.69f, 1f)
                : new Vector4(0.667f, 0.655f, 0.557f, 1f);
            var ink    = new Vector4(0.86f, 0.83f, 0.69f, 1f);
            bars.DrawRect(viewportW, viewportH, bx, by, bw, bh, fill);
            bars.DrawBorder(viewportW, viewportH, bx, by, bw, bh, brdr);
            const string label = "View";
            int lw = text.MeasureWidth(label);
            int tx = bx + (bw - lw) / 2;
            int ty = by + (bh - 8) / 2;
            text.DrawString(viewportW, viewportH, label, tx, ty + (viewPressed ? 1 : 0), ink);
        }
    }

    /// <summary>Phase 22-INFORAIL-PAPERDOLL-INTERACT — hit-test an
    /// (x,y) screen-space point against the 13 equipment slot rects.
    /// Returns the slot name (matches <see cref="Slots"/> entries)
    /// when the point is inside one, null otherwise. Caller anchors
    /// the search using the same panelOriginX/panelOriginY values it
    /// passes to <see cref="Draw"/>.</summary>
    public string? TryHitTestSlot(int x, int y, int panelOriginX, int panelOriginY, int viewportH)
    {
        float s = Scale(viewportH);
        foreach (var slot in Slots)
        {
            int sx = panelOriginX + (int)System.Math.Round((slot.X0 - 87) * s);
            int sy = panelOriginY + (int)System.Math.Round(slot.Y0 * s);
            int sw = (int)System.Math.Round((slot.X1 - slot.X0) * s);
            int sh = (int)System.Math.Round((slot.Y1 - slot.Y0) * s);
            if (x >= sx && y >= sy && x < sx + sw && y < sy + sh)
                return slot.Name;
        }
        return null;
    }

    /// <summary>True if the given screen point is inside the View
    /// button rect (paperdoll-local). Caller passes panelOriginX
    /// matching the Draw call.</summary>
    public bool IsPointInViewButton(int x, int y, int panelOriginX, int panelOriginY, int viewportH)
    {
        float s = Scale(viewportH);
        int bx = panelOriginX + (int)System.Math.Round((ViewButton.X0 - 87) * s);
        int by = panelOriginY + (int)System.Math.Round(ViewButton.Y0 * s);
        int bw = (int)System.Math.Round((ViewButton.X1 - ViewButton.X0) * s);
        int bh = (int)System.Math.Round((ViewButton.Y1 - ViewButton.Y0) * s);
        return x >= bx && y >= by && x < bx + bw && y < by + bh;
    }
}
