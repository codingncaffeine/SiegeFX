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
    // Phase 21-SC-INV-A2 (round 7) — pane narrowed 25% (was 232 wide,
    // now 174) so the top dock fits beside the ability bar + character
    // pane on common widths. Name column carries the bulk of the cut;
    // icon column stays 40 since the 28×28 icons can't shrink further.
    public const int NameColW = 110;
    public const int IconColW = 40;
    public const int Padding  = 8;
    public const int TitleH   = 22;
    public const int LabelRowH = 14; // skinny "ACTIVE SPELL N" header
    public const int RowH     = 32;
    public const int IconSz   = 28;
    public const int ActiveSlots   = 2;
    public const int InactiveSlots = 10;
    // Phase 21-SC-INV-A2 (round 9) — small vertical gap between the two
    // active-spell rows and the 10 organize-yourself rows below. DS1's
    // book sits with a thin separator there so the active hot-bar reads
    // as its own block.
    public const int ActiveSplitGap = 3;

    // Phase 21-SC-INV-A2 (round 9) — name and icon columns abut directly,
    // so the panel collapses by one Padding (was * 3, now * 2).
    public const int PanelWidth  = NameColW + IconColW + Padding * 2;
    public static int PanelHeight =>
        TitleH + Padding
        + (ActiveSlots * (LabelRowH + RowH))
        + ActiveSplitGap
        + (InactiveSlots * RowH)
        + Padding;

    public bool IsOpen { get; set; }
    public int OriginX { get; set; }
    public int OriginY { get; set; }

    public bool IsPointInPanel(int x, int y) =>
        x >= OriginX && y >= OriginY &&
        x <  OriginX + PanelWidth &&
        y <  OriginY + PanelHeight;

    /// <summary>Phase 21-SC-SPELL-A — DS1 ships a dedicated minimize-book
    /// raw (b_gui_ig_mnu_minimize-book-up/-hov/-dwn) used as the spell
    /// pane's close button. <see cref="CloseRect"/> is published by the
    /// last <see cref="Draw"/> so the host's click router can hit-test it.</summary>
    public (int X, int Y, int W, int H) CloseRect { get; private set; }
    public bool IsPointInClose(int x, int y) =>
        x >= CloseRect.X && y >= CloseRect.Y &&
        x <  CloseRect.X + CloseRect.W && y <  CloseRect.Y + CloseRect.H;

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
                     System.Func<SpellTemplate, GlTexture?>? resolveSpellIcon = null)
    {
        int px = OriginX, py = OriginY;
        var panel  = new Vector4(0.08f, 0.08f, 0.10f, 0.92f);
        var title  = new Vector4(0.16f, 0.13f, 0.10f, 1f);
        var border = new Vector4(0.72f, 0.74f, 0.78f, 1f); // light grey
        // DS1 panel font is #AAA78E — applied to chrome (heading, labels). Spell
        // names take their per-element color via SpellElementColor() below.
        var ink    = new Vector4(0.667f, 0.655f, 0.557f, 1f);
        var dimInk = new Vector4(0.667f, 0.655f, 0.557f, 1f);
        var headInk = new Vector4(0.667f, 0.655f, 0.557f, 1f);
        var slotBg = new Vector4(0.04f, 0.04f, 0.05f, 1f);
        var slotEm = new Vector4(0.55f, 0.57f, 0.60f, 1f);
        int panelH = PanelHeight;
        const int cornerR = 2;

        bars.DrawRoundedRect(viewportW, viewportH, px, py, PanelWidth, panelH, panel, cornerR, cornerR);
        bars.DrawRoundedRect(viewportW, viewportH, px, py, PanelWidth, TitleH, title, cornerR, 0);
        bars.DrawRoundedBorder(viewportW, viewportH, px, py, PanelWidth, panelH, border, cornerR);
        bars.DrawRect(viewportW, viewportH, px + 1, py + TitleH, PanelWidth - 2, 1, border);

        const string heading = "SPELL BOOK";
        int headW = text.MeasureWidth(heading);
        text.DrawString(viewportW, viewportH, heading,
                        px + (PanelWidth - headW) / 2, py + 4, headInk);

        // Phase 21-SC-SPELL-A — DS1's "minimize-book" close button. Pinned
        // to the title bar's top-right; cursor target is consistent with
        // the inventory pane's close X.
        const int closeSz = 16;
        int closeX = px + PanelWidth - closeSz - 3;
        int closeY = py + (TitleH - closeSz) / 2;
        CloseRect = (closeX, closeY, closeSz, closeSz);
        if (icons is not null && closeIcon is not null)
        {
            icons.DrawIcon(viewportW, viewportH, closeIcon, closeX, closeY, closeSz, closeSz, new Vector4(1f, 1f, 1f, 1f));
        }
        else
        {
            bars.DrawRect  (viewportW, viewportH, closeX, closeY, closeSz, closeSz, slotBg);
            bars.DrawBorder(viewportW, viewportH, closeX, closeY, closeSz, closeSz, border);
            text.DrawString(viewportW, viewportH, "X", closeX + 5, closeY + 4, ink);
        }

        int rowX = px + Padding;
        int totalRowW = PanelWidth - Padding * 2;
        int nameColW  = NameColW;
        // Phase 21-SC-INV-A2 (round 9) — name and icon columns share an
        // edge so the row reads like a spreadsheet. No more 8px gap.
        int iconColW  = totalRowW - nameColW;
        int iconColX  = rowX + nameColW;

        int y = py + TitleH + Padding;

        // Active slot 1 — skinny header label, then the wide name+icon row.
        DrawActiveHeader(bars, text, viewportW, viewportH, rowX, y, totalRowW,
                         "ACTIVE SPELL 1", title, dimInk);
        y += LabelRowH;
        DrawSpellRow(bars, text, icons, viewportW, viewportH,
                     rowX, y, nameColW, iconColX, iconColW,
                     active1, true, ink, dimInk, slotBg, slotEm, resolveSpellIcon);
        y += RowH;

        // Active slot 2.
        DrawActiveHeader(bars, text, viewportW, viewportH, rowX, y, totalRowW,
                         "ACTIVE SPELL 2", title, dimInk);
        y += LabelRowH;
        DrawSpellRow(bars, text, icons, viewportW, viewportH,
                     rowX, y, nameColW, iconColX, iconColW,
                     active2, true, ink, dimInk, slotBg, slotEm, resolveSpellIcon);
        y += RowH;

        // Phase 21-SC-INV-A2 (round 9) — split gap so the two active-spell
        // rows visibly separate from the 10 below them.
        y += ActiveSplitGap;

        // 10 user-organized rows. Empty until the player drags a learned
        // spell out of inventory into one of these slots; we draw the cell
        // chrome regardless so the layout reads "you can put a spell here".
        for (int i = 0; i < InactiveSlots; i++)
        {
            SpellTemplate? s = (placed is not null && i < placed.Count) ? placed[i] : null;
            DrawSpellRow(bars, text, icons, viewportW, viewportH,
                         rowX, y, nameColW, iconColX, iconColW,
                         s, false, ink, dimInk, slotBg, slotEm, resolveSpellIcon);
            y += RowH;
        }
    }

    static void DrawActiveHeader(BarRenderer bars, TextRenderer text, int vw, int vh,
                                 int x, int y, int w, string label,
                                 Vector4 bg, Vector4 ink)
    {
        bars.DrawRect(vw, vh, x, y, w, LabelRowH, bg);
        text.DrawString(vw, vh, label, x + 4, y + 3, ink);
    }

    static void DrawSpellRow(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                             int vw, int vh,
                             int rowX, int rowY,
                             int nameColW, int iconColX, int iconColW,
                             SpellTemplate? spell, bool isActive,
                             Vector4 ink, Vector4 dimInk,
                             Vector4 slotBg, Vector4 slotEm,
                             System.Func<SpellTemplate, GlTexture?>? resolveSpellIcon)
    {
        bars.DrawRect  (vw, vh, rowX, rowY, nameColW, RowH, slotBg);
        bars.DrawBorder(vw, vh, rowX, rowY, nameColW, RowH, slotEm);
        bars.DrawRect  (vw, vh, iconColX, rowY, iconColW, RowH, slotBg);
        bars.DrawBorder(vw, vh, iconColX, rowY, iconColW, RowH, slotEm);

        // Name column — centered text. Empty rows show "(empty)" in dim
        // ink so the slot still reads as "you can put a spell here". Filled
        // rows tint the spell name with its element color (DS1: zap is dark
        // green #253928, fire shot is warm orange, etc.).
        var label = spell is null
            ? "(empty)"
            : (string.IsNullOrEmpty(spell.ScreenName) ? spell.Name : spell.ScreenName);
        if (label.Length > 22) label = label[..22];
        var nameInk = spell is null ? dimInk : SpellElementColor(spell.Element);
        int labelW = text.MeasureWidth(label);
        int textY = rowY + (RowH - 8) / 2;
        text.DrawString(vw, vh, label.ToUpperInvariant(),
                        rowX + (nameColW - labelW) / 2, textY, nameInk);

        // Icon column — Phase 21-SC-SPELL-A: prefer the template's
        // b_gui_ig_i_ic_sp_*_inv RAW. Fall back to an element-tinted square
        // with a glyph when the lookup misses.
        int iconX = iconColX + (iconColW - IconSz) / 2;
        int iconY = rowY + (RowH - IconSz) / 2;
        if (spell is null) return;
        GlTexture? iconTex = resolveSpellIcon?.Invoke(spell);
        if (iconTex is not null && icons is not null)
        {
            icons.DrawIcon(vw, vh, iconTex, iconX, iconY, IconSz, IconSz,
                           new Vector4(1f, 1f, 1f, 1f));
            bars.DrawBorder(vw, vh, iconX, iconY, IconSz, IconSz, slotEm);
            return;
        }
        var elemColor = SpellElementColor(spell.Element);
        bars.DrawRect  (vw, vh, iconX, iconY, IconSz, IconSz, elemColor);
        bars.DrawBorder(vw, vh, iconX, iconY, IconSz, IconSz, slotEm);
        var glyph = string.IsNullOrEmpty(label) ? "?" : label.Substring(0, 1).ToUpperInvariant();
        int glyphW = text.MeasureWidth(glyph);
        text.DrawString(vw, vh, glyph,
                        iconX + (IconSz - glyphW) / 2, iconY + (IconSz - 8) / 2, ink);
    }

    // Phase 21-SC-SPELL-A — DS1-observed spell label tints. User-confirmed
    // anchors: zap (Lightning/nature) reads as #253928 dark green, fire shot
    // reads as warm orange. Other elements lean on the same nature/combat
    // split: nature spells skew green, combat-magic spells skew warm.
    static Vector4 SpellElementColor(SpellElement e) => e switch
    {
        SpellElement.Fire      => new Vector4(0.769f, 0.408f, 0.220f, 1f), // #C46838 warm orange
        SpellElement.Ice       => new Vector4(0.251f, 0.471f, 0.769f, 1f), // cool blue
        SpellElement.Lightning => new Vector4(0.145f, 0.224f, 0.157f, 1f), // #253928 nature green
        SpellElement.Acid      => new Vector4(0.145f, 0.224f, 0.157f, 1f), // #253928 nature green
        SpellElement.Death     => new Vector4(0.376f, 0.235f, 0.376f, 1f), // muted purple
        SpellElement.Holy      => new Vector4(0.769f, 0.667f, 0.314f, 1f), // gold
        _                      => new Vector4(0.667f, 0.655f, 0.557f, 1f), // #AAA78E fallback
    };
}
