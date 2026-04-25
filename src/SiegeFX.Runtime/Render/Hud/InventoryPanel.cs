using System.Numerics;
using SiegeFX.Core.Actors;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Centered grid-style inventory panel. Reads a live <see cref="LootEntry"/>
/// list and lays one slot per item left-to-right, top-to-bottom across a fixed
/// grid (DS1 ships 8×5 in the player.gas — close enough for a placeholder).
/// Item icons aren't loaded yet; slots show the trimmed template reference so
/// we can confirm pickup/equip plumbing is wiring through correctly.
///
/// Pure presentation — no drag-drop, no right-click-to-use. Toggling visibility
/// and feeding the live inventory list is the caller's job.
/// </summary>
public static class InventoryPanel
{
    public const int GridCols = 8;
    public const int GridRows = 5;
    public const int CellPx   = 36;
    public const int Padding  = 12;
    public const int TitleH   = 22;

    public static int PanelWidth  => GridCols * CellPx + Padding * 2;
    public static int PanelHeight => GridRows * CellPx + Padding * 2 + TitleH;

    public static void Draw(BarRenderer bars, TextRenderer text, int viewportW, int viewportH,
                            IReadOnlyList<LootEntry> items)
    {
        int px = (viewportW - PanelWidth) / 2;
        int py = (viewportH - PanelHeight) / 2;

        // Backdrop, title bar, body, outer border.
        var dim    = new Vector4(0f, 0f, 0f, 0.55f);
        var panel  = new Vector4(0.08f, 0.08f, 0.10f, 0.92f);
        var title  = new Vector4(0.16f, 0.13f, 0.10f, 1f);
        var border = new Vector4(0.78f, 0.66f, 0.42f, 1f);
        var slotBg = new Vector4(0.04f, 0.04f, 0.05f, 1f);
        var slotEm = new Vector4(0.13f, 0.11f, 0.09f, 1f);
        var ink    = new Vector4(0.92f, 0.88f, 0.78f, 1f);
        var dimInk = new Vector4(0.50f, 0.46f, 0.40f, 1f);

        bars.DrawRect(viewportW, viewportH, 0, 0, viewportW, viewportH, dim);
        bars.DrawRect(viewportW, viewportH, px, py, PanelWidth, PanelHeight, panel);
        bars.DrawRect(viewportW, viewportH, px, py, PanelWidth, TitleH, title);
        bars.DrawBorder(viewportW, viewportH, px, py, PanelWidth, PanelHeight, border);
        bars.DrawBorder(viewportW, viewportH, px, py + TitleH, PanelWidth, 1, border);

        text.DrawString(viewportW, viewportH, $"Inventory  ({items.Count})", px + Padding, py + 4, ink);

        // Grid of slots. Item references go in left-to-right, top-to-bottom; once
        // we have item gui_grid_w/h support, this loop will skip occupied cells
        // and route 2x1 weapons into two horizontally-adjacent slots.
        int gridX = px + Padding;
        int gridY = py + TitleH + Padding;
        int idx = 0;
        for (int row = 0; row < GridRows; row++)
        {
            for (int col = 0; col < GridCols; col++)
            {
                int sx = gridX + col * CellPx;
                int sy = gridY + row * CellPx;
                bool filled = idx < items.Count;

                bars.DrawRect(viewportW, viewportH, sx, sy, CellPx - 2, CellPx - 2,
                              filled ? slotEm : slotBg);
                bars.DrawBorder(viewportW, viewportH, sx, sy, CellPx - 2, CellPx - 2,
                                new Vector4(0.30f, 0.26f, 0.20f, 1f));

                if (filled)
                {
                    // Strip the leading underscore convention DS1 uses on item refs
                    // ("_2hsword_iron" -> "2hsword_iron") so the cell label reads
                    // cleaner. Truncate to fit the cell width.
                    var name = items[idx].Reference;
                    if (name.StartsWith('_')) name = name[1..];
                    if (name.Length > 6) name = name[..6];
                    text.DrawString(viewportW, viewportH, name, sx + 2, sy + 2, ink);
                    idx++;
                }
            }
        }

        // Footer: equipped items (if any) and a hint line. Keeps 'I' usable as a
        // sanity check that pickups + equipment are wiring up correctly even
        // before drag-drop arrives.
        int footY = py + PanelHeight - 18;
        text.DrawString(viewportW, viewportH, "Press I to close", px + Padding, footY, dimInk);
    }
}
