using System.Numerics;
using SiegeFX.Core.Actors;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Centered grid-style inventory panel with drag/drop. SC-13 added icons,
/// SC-14 added multi-cell footprints, SC-9 (this file) adds the cursor work:
/// LMB-down on a cell latches the dragged item; LMB-up on an empty rect
/// relocates it; LMB-up outside the panel asks the host to drop the item
/// back into the world (and trigger the per-template put_down SFX).
///
/// Placements persist for the lifetime of the inventory list — items that
/// have been dragged keep their slot until they're picked back up; new
/// pickups first-fit into whatever cells are still free. Order in
/// <c>_playerInventory</c> is the host's identity for an item; the panel
/// holds a parallel placement array and exposes <see cref="NotifyItemAdded"/>
/// /<see cref="NotifyItemRemoved"/> so the two stay in sync as the host
/// mutates inventory across pickups, drops, and equip swaps.
/// </summary>
public sealed class InventoryPanel
{
    public const int GridCols = 8;
    public const int GridRows = 5;
    public const int CellPx   = 36;
    public const int Padding  = 12;
    public const int TitleH   = 22;

    public static int PanelWidth  => GridCols * CellPx + Padding * 2;
    public static int PanelHeight => GridRows * CellPx + Padding * 2 + TitleH;

    public bool IsOpen { get; set; }

    // Saved per-item top-left grid position. Parallel to _playerInventory; a
    // sentinel (-1,-1) means "first-fit on next draw". Items the user has
    // never dragged stay at their first-fit slot — repacking on every open
    // would shuffle anything not pinned to a slot, which is jarring, so the
    // first draw promotes the first-fit result back into _placeRow/_placeCol.
    private readonly List<(int Row, int Col)> _placements = new();

    // Active drag state. -1 when not dragging. Mouse coords are tracked so
    // the icon ghost can render at the cursor without a separate move event
    // landing path.
    private int _dragIndex = -1;
    private int _mouseX, _mouseY;

    /// <summary>Mouse cursor is inside the panel rect, given the current
    /// viewport size. Used by the host to swallow click-to-move clicks
    /// that land on the open panel.</summary>
    public bool IsPointInPanel(int x, int y, int viewportW, int viewportH)
    {
        int px = (viewportW - PanelWidth) / 2;
        int py = (viewportH - PanelHeight) / 2;
        return x >= px && y >= py && x < px + PanelWidth && y < py + PanelHeight;
    }

    /// <summary>Add a sentinel placement for a freshly-added inventory item
    /// (called by the host right after <c>_playerInventory.Add</c>).</summary>
    public void NotifyItemAdded() => _placements.Add((-1, -1));

    /// <summary>Drop the placement entry for an item the host just removed
    /// from the inventory list. Cancels any in-progress drag that pointed
    /// at this slot.</summary>
    public void NotifyItemRemoved(int index)
    {
        if (index < 0 || index >= _placements.Count) return;
        _placements.RemoveAt(index);
        if (_dragIndex == index) _dragIndex = -1;
        else if (_dragIndex > index) _dragIndex--;
    }

    /// <summary>Reset placements + cancel any drag — called on world load
    /// so the next session starts cleanly.</summary>
    public void Reset()
    {
        _placements.Clear();
        _dragIndex = -1;
    }

    /// <summary>Number of placement records the panel is tracking. The host
    /// uses this to detect a desync (e.g., after a save load that bypassed
    /// NotifyItemAdded) and rebuild from scratch.</summary>
    public int PlacementCount => _placements.Count;

    /// <summary>LMB-down inside the panel. Latches the dragged item if the
    /// cursor lands on an item rect. No-op on empty cells.</summary>
    public void OnMouseDown(int x, int y, int viewportW, int viewportH,
                            IReadOnlyList<LootEntry> items,
                            Func<string, (int W, int H)>? resolveGridSize)
    {
        EnsurePlacements(items.Count);
        Pack(items, resolveGridSize);
        _mouseX = x; _mouseY = y;

        int gridX = (viewportW - PanelWidth) / 2 + Padding;
        int gridY = (viewportH - PanelHeight) / 2 + TitleH + Padding;
        for (int i = 0; i < items.Count; i++)
        {
            var (row, col) = _placements[i];
            if (row < 0 || col < 0) continue;
            var (w, h) = ResolveGrid(items[i].Reference, resolveGridSize);
            int sx = gridX + col * CellPx;
            int sy = gridY + row * CellPx;
            if (x >= sx && y >= sy && x < sx + w * CellPx && y < sy + h * CellPx)
            {
                _dragIndex = i;
                return;
            }
        }
    }

    /// <summary>Outcome of an LMB-up: caller does the bookkeeping (move
    /// silently inside the panel; drop = pop the item out of the inventory
    /// list and spawn it back into the world).</summary>
    public enum ActionKind { None, Moved, DropToWorld }
    public readonly record struct InventoryAction(ActionKind Kind, int ItemIndex);

    /// <summary>LMB-up. If a drag is active and the cursor is over an empty
    /// rect of the dragged item's footprint, relocate it. If outside the
    /// panel entirely, return DropToWorld so the host can fire the put_down
    /// SFX and append a loot pile. Anything else cancels the drag.</summary>
    public InventoryAction OnMouseUp(int x, int y, int viewportW, int viewportH,
                                     IReadOnlyList<LootEntry> items,
                                     Func<string, (int W, int H)>? resolveGridSize)
    {
        if (_dragIndex < 0 || _dragIndex >= items.Count)
        {
            _dragIndex = -1;
            return new InventoryAction(ActionKind.None, -1);
        }
        int i = _dragIndex;
        _dragIndex = -1;
        if (!IsPointInPanel(x, y, viewportW, viewportH))
        {
            return new InventoryAction(ActionKind.DropToWorld, i);
        }

        // Inside the panel — try to land at the cell under the cursor. The
        // dragged item's top-left snaps to the cursor's cell, then we test
        // whether the (w,h) footprint fits without colliding with anything
        // else. Failed placement leaves the item at its original slot.
        int gridX = (viewportW - PanelWidth) / 2 + Padding;
        int gridY = (viewportH - PanelHeight) / 2 + TitleH + Padding;
        int targetCol = (x - gridX) / CellPx;
        int targetRow = (y - gridY) / CellPx;
        var (w, h) = ResolveGrid(items[i].Reference, resolveGridSize);
        if (targetCol < 0 || targetRow < 0
            || targetCol + w > GridCols || targetRow + h > GridRows)
        {
            return new InventoryAction(ActionKind.None, -1);
        }
        if (!FootprintFreeIgnoring(i, targetRow, targetCol, w, h, items, resolveGridSize))
        {
            return new InventoryAction(ActionKind.None, -1);
        }
        _placements[i] = (targetRow, targetCol);
        return new InventoryAction(ActionKind.Moved, i);
    }

    public void OnMouseMove(int x, int y) { _mouseX = x; _mouseY = y; }

    public void Draw(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                     int viewportW, int viewportH,
                     IReadOnlyList<LootEntry> items,
                     Func<string, GlTexture?>? resolveIcon = null,
                     Func<string, (int W, int H)>? resolveGridSize = null)
    {
        EnsurePlacements(items.Count);
        Pack(items, resolveGridSize);

        int px = (viewportW - PanelWidth) / 2;
        int py = (viewportH - PanelHeight) / 2;

        var dim    = new Vector4(0f, 0f, 0f, 0.55f);
        var panel  = new Vector4(0.08f, 0.08f, 0.10f, 0.92f);
        var title  = new Vector4(0.16f, 0.13f, 0.10f, 1f);
        var border = new Vector4(0.78f, 0.66f, 0.42f, 1f);
        var slotBg = new Vector4(0.04f, 0.04f, 0.05f, 1f);
        var slotEm = new Vector4(0.13f, 0.11f, 0.09f, 1f);
        var ink    = new Vector4(0.92f, 0.88f, 0.78f, 1f);
        var dimInk = new Vector4(0.50f, 0.46f, 0.40f, 1f);
        var cellOutline = new Vector4(0.30f, 0.26f, 0.20f, 1f);
        var white = new Vector4(1f, 1f, 1f, 1f);
        var ghost = new Vector4(1f, 1f, 1f, 0.65f);

        bars.DrawRect(viewportW, viewportH, 0, 0, viewportW, viewportH, dim);
        bars.DrawRect(viewportW, viewportH, px, py, PanelWidth, PanelHeight, panel);
        bars.DrawRect(viewportW, viewportH, px, py, PanelWidth, TitleH, title);
        bars.DrawBorder(viewportW, viewportH, px, py, PanelWidth, PanelHeight, border);
        bars.DrawBorder(viewportW, viewportH, px, py + TitleH, PanelWidth, 1, border);

        text.DrawString(viewportW, viewportH, $"Inventory  ({items.Count})", px + Padding, py + 4, ink);

        int gridX = px + Padding;
        int gridY = py + TitleH + Padding;

        // Empty cells first — anything not covered by a placed footprint.
        Span<bool> covered = stackalloc bool[GridCols * GridRows];
        for (int i = 0; i < items.Count; i++)
        {
            var (row, col) = _placements[i];
            if (row < 0 || col < 0) continue;
            var (w, h) = ResolveGrid(items[i].Reference, resolveGridSize);
            for (int dr = 0; dr < h; dr++)
                for (int dc = 0; dc < w; dc++)
                    covered[(row + dr) * GridCols + (col + dc)] = true;
        }
        for (int row = 0; row < GridRows; row++)
        {
            for (int col = 0; col < GridCols; col++)
            {
                if (covered[row * GridCols + col]) continue;
                int sx = gridX + col * CellPx;
                int sy = gridY + row * CellPx;
                bars.DrawRect(viewportW, viewportH, sx, sy, CellPx - 2, CellPx - 2, slotBg);
                bars.DrawBorder(viewportW, viewportH, sx, sy, CellPx - 2, CellPx - 2, cellOutline);
            }
        }

        // Item rects — skip the dragged item from the static layer; it's
        // drawn at the cursor instead. Everything else gets one bg/outline
        // spanning its full footprint, then icon-or-text on top.
        for (int i = 0; i < items.Count; i++)
        {
            if (i == _dragIndex) continue;
            var (row, col) = _placements[i];
            if (row < 0 || col < 0) continue;
            var (w, h) = ResolveGrid(items[i].Reference, resolveGridSize);
            int sx = gridX + col * CellPx;
            int sy = gridY + row * CellPx;
            int sw = w * CellPx - 2;
            int sh = h * CellPx - 2;

            bars.DrawRect(viewportW, viewportH, sx, sy, sw, sh, slotEm);
            bars.DrawBorder(viewportW, viewportH, sx, sy, sw, sh, cellOutline);
            DrawItemFace(bars, text, icons, viewportW, viewportH,
                         items[i].Reference, sx, sy, sw, sh, resolveIcon, ink, white);
        }

        // Drag ghost — render at the cursor, top-left of the footprint
        // anchored under the cursor's grid cell so the user sees where the
        // drop will land. Tinted slightly transparent so it reads as in-flight.
        if (_dragIndex >= 0 && _dragIndex < items.Count)
        {
            var (w, h) = ResolveGrid(items[_dragIndex].Reference, resolveGridSize);
            int targetCol = (_mouseX - gridX) / CellPx;
            int targetRow = (_mouseY - gridY) / CellPx;
            int gx, gy;
            bool snapped = targetCol >= 0 && targetRow >= 0
                && targetCol + w <= GridCols && targetRow + h <= GridRows;
            if (snapped)
            {
                gx = gridX + targetCol * CellPx;
                gy = gridY + targetRow * CellPx;
            }
            else
            {
                gx = _mouseX - (w * CellPx) / 2;
                gy = _mouseY - (h * CellPx) / 2;
            }
            int gw = w * CellPx - 2;
            int gh = h * CellPx - 2;
            DrawItemFace(bars, text, icons, viewportW, viewportH,
                         items[_dragIndex].Reference, gx, gy, gw, gh,
                         resolveIcon, ink, ghost);
        }

        int footY = py + PanelHeight - 18;
        text.DrawString(viewportW, viewportH, "Press I to close — drag items to rearrange or out to drop",
                        px + Padding, footY, dimInk);
    }

    private void EnsurePlacements(int itemCount)
    {
        // Defensive: if the host added items without notifying us (e.g. a
        // save load path), pad to length so per-index lookups stay valid.
        // Excess entries (host removed without notifying) get trimmed.
        while (_placements.Count < itemCount) _placements.Add((-1, -1));
        if (_placements.Count > itemCount)
        {
            _placements.RemoveRange(itemCount, _placements.Count - itemCount);
            if (_dragIndex >= itemCount) _dragIndex = -1;
        }
    }

    private void Pack(IReadOnlyList<LootEntry> items, Func<string, (int W, int H)>? resolveGridSize)
    {
        Span<bool> occupied = stackalloc bool[GridCols * GridRows];

        // Honor saved placements first so anything the user has dragged
        // stays where they put it. Out-of-bounds saved positions (footprint
        // changed because the spec re-rolled to a different template, etc.)
        // get reset and re-fit below.
        for (int i = 0; i < items.Count; i++)
        {
            var (row, col) = _placements[i];
            if (row < 0 || col < 0) continue;
            var (w, h) = ResolveGrid(items[i].Reference, resolveGridSize);
            if (col + w > GridCols || row + h > GridRows
                || !FootprintClear(occupied, row, col, w, h))
            {
                _placements[i] = (-1, -1);
                continue;
            }
            MarkOccupied(occupied, row, col, w, h);
        }

        // First-fit anything still unplaced. Items that don't fit silently
        // overflow (DS1 also refuses pickup when full; we keep them in the
        // list but unrendered until the user frees space).
        for (int i = 0; i < items.Count; i++)
        {
            if (_placements[i].Row >= 0) continue;
            var (w, h) = ResolveGrid(items[i].Reference, resolveGridSize);
            for (int r = 0; r <= GridRows - h; r++)
            {
                bool placed = false;
                for (int c = 0; c <= GridCols - w; c++)
                {
                    if (!FootprintClear(occupied, r, c, w, h)) continue;
                    MarkOccupied(occupied, r, c, w, h);
                    _placements[i] = (r, c);
                    placed = true;
                    break;
                }
                if (placed) break;
            }
        }
    }

    private bool FootprintFreeIgnoring(int ignoreIndex, int row, int col, int w, int h,
                                       IReadOnlyList<LootEntry> items,
                                       Func<string, (int W, int H)>? resolveGridSize)
    {
        for (int j = 0; j < items.Count; j++)
        {
            if (j == ignoreIndex) continue;
            var (jr, jc) = _placements[j];
            if (jr < 0 || jc < 0) continue;
            var (jw, jh) = ResolveGrid(items[j].Reference, resolveGridSize);
            if (col + w <= jc || jc + jw <= col) continue;
            if (row + h <= jr || jr + jh <= row) continue;
            return false;
        }
        return true;
    }

    private static bool FootprintClear(Span<bool> occupied, int row, int col, int w, int h)
    {
        for (int dr = 0; dr < h; dr++)
            for (int dc = 0; dc < w; dc++)
                if (occupied[(row + dr) * GridCols + (col + dc)]) return false;
        return true;
    }

    private static void MarkOccupied(Span<bool> occupied, int row, int col, int w, int h)
    {
        for (int dr = 0; dr < h; dr++)
            for (int dc = 0; dc < w; dc++)
                occupied[(row + dr) * GridCols + (col + dc)] = true;
    }

    private static (int W, int H) ResolveGrid(string reference,
                                              Func<string, (int W, int H)>? resolveGridSize)
    {
        var (w, h) = resolveGridSize?.Invoke(reference) ?? (1, 1);
        if (w < 1) w = 1;
        if (h < 1) h = 1;
        if (w > GridCols) w = GridCols;
        if (h > GridRows) h = GridRows;
        return (w, h);
    }

    private static void DrawItemFace(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                                     int viewportW, int viewportH,
                                     string itemRef, int sx, int sy, int sw, int sh,
                                     Func<string, GlTexture?>? resolveIcon,
                                     Vector4 ink, Vector4 tint)
    {
        GlTexture? icon = resolveIcon?.Invoke(itemRef);
        if (icon is not null && icons is not null)
        {
            icons.DrawIcon(viewportW, viewportH, icon, sx + 1, sy + 1, sw - 2, sh - 2, tint);
            return;
        }
        var name = itemRef;
        if (!string.IsNullOrEmpty(name) && name[0] == '_') name = name[1..];
        if (name.Length > 6) name = name[..6];
        text.DrawString(viewportW, viewportH, name, sx + 2, sy + 2, ink);
    }
}
