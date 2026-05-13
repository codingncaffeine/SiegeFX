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

        // Match the scaled layout from the last Draw call. Falls back
        // to the unscaled constants when Draw hasn't run yet.
        float s = _lastScaledH > 0 ? _lastScaledH / (float)RefPanelH : 1f;
        int padding   = (int)System.Math.Round(Padding * s);
        int titleH    = (int)System.Math.Round(TitleH * s);
        int labelRowH = (int)System.Math.Round(LabelRowH * s);
        int rowH      = (int)System.Math.Round(RowH * s);
        int gap       = (int)System.Math.Round(ActiveSplitGap * s);
        int panelW    = _lastScaledW > 0 ? _lastScaledW : PanelWidth;

        int rowX = OriginX + padding;
        int rowR = OriginX + panelW - padding;
        if (x < rowX || x >= rowR) return (SlotKind.None, 0);

        int active1Y = OriginY + titleH + padding + labelRowH;
        if (y >= active1Y && y < active1Y + rowH) return (SlotKind.Active1, 0);
        int active2Y = active1Y + rowH + labelRowH;
        if (y >= active2Y && y < active2Y + rowH) return (SlotKind.Active2, 0);
        int placedY0 = active2Y + rowH + gap;
        int relY = y - placedY0;
        if (relY < 0) return (SlotKind.None, 0);
        int row = rowH > 0 ? relY / rowH : 0;
        if (row < 0 || row >= InactiveSlots) return (SlotKind.None, 0);
        return (SlotKind.Placed, row);
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
                     System.Func<SpellTemplate, GlTexture?>? resolveSpellIcon = null)
    {
        int px = OriginX, py = OriginY;
        var panel  = new Vector4(0.08f, 0.08f, 0.10f, 0.92f);
        var title  = new Vector4(0.16f, 0.13f, 0.10f, 1f);
        var border = new Vector4(0.72f, 0.74f, 0.78f, 1f);
        var ink    = new Vector4(0.667f, 0.655f, 0.557f, 1f);
        var dimInk = new Vector4(0.667f, 0.655f, 0.557f, 1f);
        var headInk = new Vector4(0.86f, 0.83f, 0.69f, 1f);
        var slotBg = new Vector4(0.04f, 0.04f, 0.05f, 1f);
        var slotEm = new Vector4(0.55f, 0.57f, 0.60f, 1f);
        // INFORAIL-SPELLBOOK-CHROME — scale all layout by the clamped
        // info-rail scale so the spellbook sizes alongside paperdoll +
        // inventory. Previously this panel used fixed 166px regardless
        // of viewport.
        float s = InfoRailLayout.Scale(viewportH);
        int panelW = (int)System.Math.Round(RefPanelW * s);
        int panelH = (int)System.Math.Round(RefPanelH * s);
        int titleH = (int)System.Math.Round(TitleH * s);
        int padding = (int)System.Math.Round(Padding * s);
        int labelRowH = (int)System.Math.Round(LabelRowH * s);
        int rowH = (int)System.Math.Round(RowH * s);
        int activeSplitGap = (int)System.Math.Round(ActiveSplitGap * s);
        int cornerR = (int)System.Math.Max(2, System.Math.Round(2 * s));

        _lastScaledW = panelW;
        _lastScaledH = panelH;
        bars.DrawRoundedRect(viewportW, viewportH, px, py, panelW, panelH, panel, cornerR, cornerR);
        bars.DrawRoundedRect(viewportW, viewportH, px, py, panelW, titleH, title, cornerR, 0);
        bars.DrawRoundedBorder(viewportW, viewportH, px, py, panelW, panelH, border, cornerR);
        bars.DrawRect(viewportW, viewportH, px + 1, py + titleH, panelW - 2, 1, border);

        const string heading = "SPELL BOOK";
        int headW = text.MeasureWidth(heading);
        text.DrawString(viewportW, viewportH, heading,
                        px + (panelW - headW) / 2,
                        py + (titleH - 8) / 2, headInk);

        // Close X at gas rect 524,2,540,18 (hud_spell.gas:10) — panel-
        // relative offset = 524-387,2 = 137,2 with size 16×16. Scaled.
        int closeSz = (int)System.Math.Round(16 * s);
        int closeX = px + (int)System.Math.Round((524 - 387) * s);
        int closeY = py + (int)System.Math.Round(2 * s);
        CloseRect = (closeX, closeY, closeSz, closeSz);
        if (icons is not null && closeIcon is not null)
        {
            icons.DrawIcon(viewportW, viewportH, closeIcon, closeX, closeY, closeSz, closeSz, new Vector4(1f, 1f, 1f, 1f));
        }
        else
        {
            bars.DrawRect  (viewportW, viewportH, closeX, closeY, closeSz, closeSz, slotBg);
            bars.DrawBorder(viewportW, viewportH, closeX, closeY, closeSz, closeSz, border);
            text.DrawString(viewportW, viewportH, "X", closeX + closeSz / 3, closeY + closeSz / 3, ink);
        }

        int rowX = px + padding;
        int totalRowW = panelW - padding * 2;
        int nameColW  = (int)System.Math.Round(NameColW * s);
        int iconColW  = totalRowW - nameColW;
        int iconColX  = rowX + nameColW;

        int y = py + titleH + padding;

        DrawActiveHeader(bars, text, viewportW, viewportH, rowX, y, totalRowW, labelRowH,
                         "ACTIVE SPELL 1", title, dimInk);
        y += labelRowH;
        DrawSpellRow(bars, text, icons, viewportW, viewportH,
                     rowX, y, nameColW, iconColX, iconColW, rowH, s,
                     active1, true, ink, dimInk, slotBg, slotEm, resolveSpellIcon);
        y += rowH;

        DrawActiveHeader(bars, text, viewportW, viewportH, rowX, y, totalRowW, labelRowH,
                         "ACTIVE SPELL 2", title, dimInk);
        y += labelRowH;
        DrawSpellRow(bars, text, icons, viewportW, viewportH,
                     rowX, y, nameColW, iconColX, iconColW, rowH, s,
                     active2, true, ink, dimInk, slotBg, slotEm, resolveSpellIcon);
        y += rowH + activeSplitGap;

        for (int i = 0; i < InactiveSlots; i++)
        {
            SpellTemplate? sp = (placed is not null && i < placed.Count) ? placed[i] : null;
            DrawSpellRow(bars, text, icons, viewportW, viewportH,
                         rowX, y, nameColW, iconColX, iconColW, rowH, s,
                         sp, false, ink, dimInk, slotBg, slotEm, resolveSpellIcon);
            y += rowH;
        }
    }

    static void DrawActiveHeader(BarRenderer bars, TextRenderer text, int vw, int vh,
                                 int x, int y, int w, int labelRowH, string label,
                                 Vector4 bg, Vector4 ink)
    {
        bars.DrawRect(vw, vh, x, y, w, labelRowH, bg);
        text.DrawString(vw, vh, label, x + 4, y + (labelRowH - 8) / 2, ink);
    }

    static void DrawSpellRow(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                             int vw, int vh,
                             int rowX, int rowY,
                             int nameColW, int iconColX, int iconColW, int rowH, float s,
                             SpellTemplate? spell, bool isActive,
                             Vector4 ink, Vector4 dimInk,
                             Vector4 slotBg, Vector4 slotEm,
                             System.Func<SpellTemplate, GlTexture?>? resolveSpellIcon)
    {
        int iconSz = (int)System.Math.Round(IconSz * s);
        bars.DrawRect  (vw, vh, rowX, rowY, nameColW, rowH, slotBg);
        bars.DrawBorder(vw, vh, rowX, rowY, nameColW, rowH, slotEm);
        bars.DrawRect  (vw, vh, iconColX, rowY, iconColW, rowH, slotBg);
        bars.DrawBorder(vw, vh, iconColX, rowY, iconColW, rowH, slotEm);

        var label = spell is null
            ? "(empty)"
            : (string.IsNullOrEmpty(spell.ScreenName) ? spell.Name : spell.ScreenName);
        if (label.Length > 22) label = label[..22];
        var nameInk = spell is null ? dimInk : SpellElementColor(spell.Element);
        int labelW = text.MeasureWidth(label);
        int textY = rowY + (rowH - 8) / 2;
        text.DrawString(vw, vh, label.ToUpperInvariant(),
                        rowX + (nameColW - labelW) / 2, textY, nameInk);

        int iconX = iconColX + (iconColW - iconSz) / 2;
        int iconY = rowY + (rowH - iconSz) / 2;
        if (spell is null) return;
        GlTexture? iconTex = resolveSpellIcon?.Invoke(spell);
        if (iconTex is not null && icons is not null)
        {
            icons.DrawIcon(vw, vh, iconTex, iconX, iconY, iconSz, iconSz,
                           new Vector4(1f, 1f, 1f, 1f));
            bars.DrawBorder(vw, vh, iconX, iconY, iconSz, iconSz, slotEm);
            return;
        }
        var elemColor = SpellElementColor(spell.Element);
        bars.DrawRect  (vw, vh, iconX, iconY, iconSz, iconSz, elemColor);
        bars.DrawBorder(vw, vh, iconX, iconY, iconSz, iconSz, slotEm);
        var glyph = string.IsNullOrEmpty(label) ? "?" : label.Substring(0, 1).ToUpperInvariant();
        int glyphW = text.MeasureWidth(glyph);
        text.DrawString(vw, vh, glyph,
                        iconX + (iconSz - glyphW) / 2, iconY + (iconSz - 8) / 2, ink);
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
