using System.Numerics;
using SiegeFX.Core.Assets;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 21-SC-SPELL-A — DS1-faithful spell-book pane. Sits at the right
/// of the top dock alongside the character + inventory panes; toggles
/// independently with 'B'.
///
/// Layout — DS1 ships 14 rows total:
///   * 2 skinny header rows ("ACTIVE SPELL 1", "ACTIVE SPELL 2") that
///     label the two hot-bar slots ('Q' / 'W' bindings).
///   * 12 spell rows (2 active + 10 user-organized below) where each row is
///     a wide name column (name centered) + a narrow icon column on the
///     right. The first column matches the inventory pane's width so the
///     two panels visually rhyme.
///
/// The pane only displays spells the player owns — no catalog dump. The
/// 10 below-active rows are empty until the player drags a learned spell
/// out of inventory into one of them (drag-into ships with -SPELL-B).
/// </summary>
public sealed class SpellBookPanel
{
    // Gas-cited 640×480 reference dimensions per hud_spell.gas
    // (rect 387,0,542,449 → 155w × 449h). Internal layout constants
    // below are at this reference scale; multiply by
    // InfoRailLayout.Scale(viewportH) when drawing.
    public const int RefPanelW = 155;
    public const int RefPanelH = 449;
    public const int NameColW = 102; // ref-scale; gas name col x=389..540 = 151px
    public const int IconColW = 37;  // gas icon col x=523..539 = 16px (we widen for legibility)
    public const int Padding  = 8;
    public const int TitleH   = 32;  // gas header dialog_box rect 387,0,542,32
    public const int LabelRowH = 14;
    public const int RowH     = 32;
    public const int IconSz   = 28;
    public const int ActiveSlots   = 2;
    public const int InactiveSlots = 10;
    public const int ActiveSplitGap = 3;

    // Reference-scale panel width/height (used for hit-tests at ref
    // scale only; on-screen rendering scales by InfoRailLayout.Scale).
    public const int PanelWidth  = RefPanelW;
    public static int PanelHeight => RefPanelH;

    /// <summary>Width on screen at the current viewport. Matches the
    /// info-rail clamped scale so the spellbook panel sizes alongside
    /// paperdoll + inventory instead of staying at fixed 166px while
    /// the others grow. INFORAIL-SPELLBOOK-CHROME slice.</summary>
    public static int WidthAt(int viewportH)
        => (int)System.Math.Round(RefPanelW * InfoRailLayout.Scale(viewportH));
    public static int HeightAt(int viewportH)
        => (int)System.Math.Round(RefPanelH * InfoRailLayout.Scale(viewportH));

    public bool IsOpen { get; set; }
    public int OriginX { get; set; }
    public int OriginY { get; set; }

    public bool IsPointInPanel(int x, int y) =>
        x >= OriginX && y >= OriginY &&
        x <  OriginX + (_lastScaledW > 0 ? _lastScaledW : PanelWidth) &&
        y <  OriginY + (_lastScaledH > 0 ? _lastScaledH : PanelHeight);
    private int _lastScaledW;
    private int _lastScaledH;

    /// <summary>Phase 21-SC-SPELL-A — DS1 ships a dedicated minimize-book
    /// raw (b_gui_ig_mnu_minimize-book-up/-hov/-dwn) used as the spell
    /// pane's close button. <see cref="CloseRect"/> is published by the
    /// last <see cref="Draw"/> so the host's click router can hit-test it.</summary>
    public (int X, int Y, int W, int H) CloseRect { get; private set; }
    public bool IsPointInClose(int x, int y) =>
        x >= CloseRect.X && y >= CloseRect.Y &&
        x <  CloseRect.X + CloseRect.W && y <  CloseRect.Y + CloseRect.H;

    /// <summary>Phase 21-SC-SCROLL-B-1 — which slot of the spellbook a
    /// screen-space point falls in. <see cref="None"/> is the answer for
    /// any point inside the panel that isn't a spell row (title bar,
    /// label bands, padding) and for any point outside the panel.</summary>
    public enum SlotKind { None, Active1, Active2, Placed }

    /// <summary>Hit-test a screen-space point against the spellbook's
    /// spell-row rectangles. Mirrors the layout that <see cref="Draw"/>
    /// composes from <see cref="OriginX"/> / <see cref="OriginY"/> +
    /// constants, so a click router can ask "which spell did I click?"
    /// without re-implementing the math.
    ///
    /// <para>Returns <see cref="SlotKind.None"/> + index 0 when no slot
    /// matches. For <see cref="SlotKind.Placed"/>, the index is 0..9
    /// matching the row order. Active1/Active2 ignore the index field.</para></summary>
    public (SlotKind Kind, int Index) HitTestSlot(int x, int y)
    {
        if (!IsPointInPanel(x, y)) return (SlotKind.None, 0);

        // SC-SPELLBOOK-AUTHENTIC — authored row table (spell.gas, panel-rel
        // to 387,0): bands at x 2..153; active rows at y 46..79 and 94..127
        // (header text strips between), placed rows contiguous at
        // 126 + 32·i, each 33 tall (adjacent bands share a 1px frame edge).
        float s = _lastScaledH > 0 ? _lastScaledH / (float)RefPanelH : 1f;
        int rowX = OriginX + (int)System.Math.Round(2 * s);
        int rowR = OriginX + (int)System.Math.Round(153 * s);
        if (x < rowX || x >= rowR) return (SlotKind.None, 0);
        int Y(int refY) => OriginY + (int)System.Math.Round(refY * s);
        int bandH = (int)System.Math.Round(33 * s);
        if (y >= Y(46) && y < Y(46) + bandH) return (SlotKind.Active1, 0);
        if (y >= Y(94) && y < Y(94) + bandH) return (SlotKind.Active2, 0);
        for (int i = 0; i < InactiveSlots; i++)
        {
            int by = Y(126 + 32 * i);
            if (y >= by && y < by + bandH) return (SlotKind.Placed, i);
        }
        return (SlotKind.None, 0);
    }

    /// <param name="placed">Spells the player has dragged into the 10
    /// user-organized rows below the two active hot-bar slots. The order
    /// matches the rows top-down; null entries leave a row empty. Pass
    /// an empty list (or null) to draw all 10 rows blank — the default
    /// state until drag-from-inventory lands.</param>
    /// <param name="resolveSpellIcon">Optional callback returning the
    /// <see cref="GlTexture"/> for a spell template's <c>inventory_icon</c>
    /// (e.g. <c>b_gui_ig_i_ic_sp_142_inv</c>). Returns null when the icon
    /// can't be resolved; the panel falls back to the element-tinted glyph
    /// placeholder for that row.</param>
    public void Draw(BarRenderer bars, TextRenderer text,
                     int viewportW, int viewportH,
                     SpellTemplate? active1, SpellTemplate? active2,
                     IReadOnlyList<SpellTemplate?>? placed,
                     IconRenderer? icons = null,
                     GlTexture? closeIcon = null,
                     System.Func<SpellTemplate, GlTexture?>? resolveSpellIcon = null,
                     System.Func<string, GlTexture?>? resolveCommonChrome = null,
                     GlTexture? rowBox = null)
    {
        int px = OriginX, py = OriginY;
        var white  = new Vector4(1f, 1f, 1f, 1f);
        var ink    = new Vector4(0.667f, 0.655f, 0.557f, 1f);
        var headInk = new Vector4(0.86f, 0.83f, 0.69f, 1f);
        float s = InfoRailLayout.Scale(viewportH);
        int panelW = (int)System.Math.Round(RefPanelW * s);
        int panelH = (int)System.Math.Round(RefPanelH * s);
        _lastScaledW = panelW;
        _lastScaledH = panelH;

        int X(int refX) => px + (int)System.Math.Round(refX * s);
        int Y(int refY) => py + (int)System.Math.Round(refY * s);
        int S(int refN) => (int)System.Math.Round(refN * s);

        // SC-SPELLBOOK-AUTHENTIC — spell.gas authors THREE cpbox dialog
        // boxes (the same smoked translucent nine-patch the inventory's
        // backdrop uses): header 387,0,542,32 · active-slots 387,31,542,128
        // · main shelf 387,126,542,449 (panel-rel below). The old flat
        // rect-and-border scaffolding painted opaque row boxes that read
        // as rectangles hanging over the panel — replaced wholesale.
        if (icons is not null && resolveCommonChrome is not null)
        {
            NinePatch.DrawCpbox(icons, resolveCommonChrome, viewportW, viewportH,
                X(0), Y(0), S(155), S(32), white);
            NinePatch.DrawCpbox(icons, resolveCommonChrome, viewportW, viewportH,
                X(0), Y(31), S(155), S(97), white);
            NinePatch.DrawCpbox(icons, resolveCommonChrome, viewportW, viewportH,
                X(0), Y(126), S(155), S(323), white);
        }
        else
        {
            bars.DrawRect(viewportW, viewportH, px, py, panelW, panelH,
                new Vector4(0.06f, 0.06f, 0.07f, 0.85f));
        }

        const string heading = "SPELL BOOK";
        int headW = text.MeasureWidth(heading);
        text.DrawString(viewportW, viewportH, heading,
                        px + (panelW - headW) / 2,
                        Y(0) + (S(32) - 8) / 2, headInk);

        // Close X at gas rect 524,2,540,18 — panel-rel 137,2, 16×16.
        int closeSz = S(16);
        int closeX = X(137);
        int closeY = Y(2);
        CloseRect = (closeX, closeY, closeSz, closeSz);
        if (icons is not null && closeIcon is not null)
            icons.DrawIcon(viewportW, viewportH, closeIcon, closeX, closeY, closeSz, closeSz, white);
        else
            text.DrawString(viewportW, viewportH, "X", closeX + closeSz / 3, closeY + closeSz / 3, ink);

        // "Active Spell 1/2" — authored text strips (391,33,537,46 and
        // 391,79,537,93), white centered, straight over the smoke: no
        // invented band rects.
        void Header(string label, int refY, int refY1)
        {
            int hw = text.MeasureWidth(label);
            text.DrawString(viewportW, viewportH, label,
                px + (panelW - hw) / 2,
                Y(refY) + (S(refY1 - refY) - 8) / 2, white);
        }
        Header("ACTIVE SPELL 1", 33, 46);
        Header("ACTIVE SPELL 2", 79, 93);

        // 12 sb_box row bands (151×33 at x=2; actives at y 46/94, shelf
        // contiguous from 126 at 32 pitch — adjacent bands share their 1px
        // frame edge by authoring).
        DrawSpellRow(bars, text, icons, viewportW, viewportH, X(2), Y(46), s,
                     active1, rowBox, resolveSpellIcon, ink);
        DrawSpellRow(bars, text, icons, viewportW, viewportH, X(2), Y(94), s,
                     active2, rowBox, resolveSpellIcon, ink);
        for (int i = 0; i < InactiveSlots; i++)
        {
            SpellTemplate? sp = (placed is not null && i < placed.Count) ? placed[i] : null;
            DrawSpellRow(bars, text, icons, viewportW, viewportH, X(2), Y(126 + 32 * i), s,
                         sp, rowBox, resolveSpellIcon, ink);
        }
    }

    static void DrawSpellRow(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                             int vw, int vh, int bandX, int bandY, float s,
                             SpellTemplate? spell, GlTexture? rowBox,
                             System.Func<SpellTemplate, GlTexture?>? resolveSpellIcon,
                             Vector4 ink)
    {
        int bandW = (int)System.Math.Round(151 * s);
        int bandH = (int)System.Math.Round(33 * s);
        // Authored band: b_gui_ig_mnu_sb_box, gas uvcoords
        // 0,0.484375,0.589844,1 → visual top-down V 0..0.515625 (the
        // bottom-up flip rule), U 0..0.589844.
        if (rowBox is not null && icons is not null)
            icons.DrawIcon(vw, vh, rowBox, bandX, bandY, bandW, bandH,
                new Vector4(1f, 1f, 1f, 1f), 0f, 0f, 0.589844f, 0.515625f);

        // Empty rows are just the band — DS1 shows no placeholder text.
        if (spell is null) return;

        var label = string.IsNullOrEmpty(spell.ScreenName) ? spell.Name : spell.ScreenName;
        if (label.Length > 22) label = label[..22];
        // Name centered in the authored text_box (389..520 → band-rel
        // 0..131 of the 151-wide band).
        int nameW = (int)System.Math.Round(131 * s);
        int labelW = text.MeasureWidth(label);
        text.DrawString(vw, vh, label.ToUpperInvariant(),
                        bandX + (nameW - labelW) / 2,
                        bandY + (bandH - 8) / 2,
                        ClassColor(spell));

        // Icon in the authored itemslot (523,47,539,79 → band-rel x=134,
        // 16 wide over the band height); square icon centered vertically.
        int slotX = bandX + (int)System.Math.Round(134 * s);
        int iconSz = (int)System.Math.Round(16 * s);
        int iconY = bandY + (bandH - iconSz) / 2;
        GlTexture? iconTex = resolveSpellIcon?.Invoke(spell);
        if (iconTex is not null && icons is not null)
        {
            icons.DrawIcon(vw, vh, iconTex, slotX, iconY, iconSz, iconSz,
                           new Vector4(1f, 1f, 1f, 1f));
        }
        else
        {
            bars.DrawRect(vw, vh, slotX, iconY, iconSz, iconSz, ClassColor(spell));
            var glyph = string.IsNullOrEmpty(label) ? "?" : label.Substring(0, 1).ToUpperInvariant();
            int glyphW = text.MeasureWidth(glyph);
            text.DrawString(vw, vh, glyph,
                            slotX + (iconSz - glyphW) / 2, iconY + (iconSz - 8) / 2, ink);
        }
    }

    // SC-SPELLBOOK-AUTHENTIC — retail tints spell names by their authored
    // [magic] magic_class, two classes only. Colors measured off the retail
    // 1024×768 reference screenshot's text pixels: ZAP (nature magic) =
    // RGB 67,202,131; FIRESHOT (combat magic) = RGB 234,169,53. (The old
    // per-element table — including a "dark green" zap — was a misread.)
    static Vector4 ClassColor(SpellTemplate spell) => spell.IsNatureMagic
        ? new Vector4(67f / 255f, 202f / 255f, 131f / 255f, 1f)
        : new Vector4(234f / 255f, 169f / 255f, 53f / 255f, 1f);
}
