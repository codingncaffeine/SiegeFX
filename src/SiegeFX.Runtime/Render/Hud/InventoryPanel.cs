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
    // Phase 22-AUTH-INV — gas-authored layout from
    // /ui/interfaces/backend/inventory/inventory.gas. Reference resolution
    // is 640×480 (inferred from sibling panels). Every dimension below is
    // in that 640×480 reference space; runtime renders at scale =
    // viewportH / 480, matching the same scaling pattern the data_bar
    // (Phase 22-A) and overhead-bars (Phase 22-H) slices use.
    //
    // gas dialog_box_inv_bg rect = 253,0,387,449     (134 wide × 449 tall)
    // gas gridbox_13x4     rect = 255,30,384,447     (4 cols × 13 rows × 32px cells = 129×417)
    // gas button_arrange   rect = 255,2,279,30       (24 wide × 28 tall)
    // gas window_gold_bg   rect = 279,2,362,30       (gold bg: 83 wide × 28 tall)
    // gas window_gold_icon rect = 285,8,301,24       (gold coin: 16×16)
    // gas button_gold      rect = 302,8,360,24       (gold count text: 58×16)
    // gas button_inventory_exit rect = 369,2,385,18  (X close: 16×16)
    public const int GridCols = 4;
    public const int GridRows = 13;
    public const int RefCellPx       = 32;   // gas box_width/box_height
    public const int RefPanelW       = 134;  // 387 - 253
    public const int RefPanelH       = 449;
    public const int RefGridXOffset  = 2;    // grid x=255 - panel x=253
    public const int RefGridYOffset  = 30;   // grid y=30 - panel y=0
    public const int RefArrangeW     = 24;   // 279 - 255
    public const int RefArrangeH     = 28;   // 30 - 2
    public const int RefGoldBgW      = 83;   // 362 - 279
    public const int RefGoldBgH      = 28;   // 30 - 2
    public const int RefGoldIconSz   = 16;
    public const int RefGoldTextW    = 58;   // 360 - 302
    public const int RefCloseSz      = 16;
    public const int RefRes          = 480;  // 640×480 reference

    /// <summary>Scale factor for the current viewport — gas rects multiply
    /// by this to land at the right pixel size. Mirrors the data_bar /
    /// overhead-bars convention so HUD panels all scale together.</summary>
    /// <summary>INFORAIL fold — share the clamped info-rail scale so
    /// inventory stays the same size as paperdoll + spellbook on
    /// modern resolutions (cap 1.5× per InfoRailLayout.MaxScale).
    /// Previously this returned the raw viewportH/480, which made the
    /// inventory grow past the other two rail panels at 1080p+.</summary>
    public static float Scale(int viewportH) => InfoRailLayout.Scale(viewportH);

    public static int PanelWidth(int viewportH)  => (int)System.Math.Round(RefPanelW * Scale(viewportH));
    public static int PanelHeight(int viewportH) => (int)System.Math.Round(RefPanelH * Scale(viewportH));

    /// <summary>Title-bar height in screen pixels for the current viewport.
    /// gas authors a 30px title strip (top 30 of the 449-tall panel) which
    /// holds the arrange button + gold readout + X-close button.</summary>
    public static int TitleH(int viewportH) => (int)System.Math.Round(30 * Scale(viewportH));

    public bool IsOpen { get; set; }

    /// <summary>Phase 21-SC-INV-A — explicit top-left in screen pixels. The
    /// pause/centered draw stays the default (both negative); when both are
    /// &gt;=0 the panel docks at that position so it can sit alongside the
    /// CharacterPanel + SpellBookPanel at the top of the screen.</summary>
    public int OriginX { get; set; } = -1;
    public int OriginY { get; set; } = -1;

    /// <summary>Phase 21-SC-INV-A — gold counter shown in the title bar.
    /// Pulled from <see cref="Actors.PlayerProgression.Gold"/> by the host
    /// each frame; defaults to 0 if the panel is opened before progression
    /// lands (creator preview, viewer modes).</summary>
    public long Gold { get; set; }

    /// <summary>Phase 21-SC-INV-A — top-docked draw skips the screen-dim
    /// backdrop so the world stays interactive while the panel is open.
    /// The centered (modal) draw still dims for read-only browsing.</summary>
    public bool DimBackdrop { get; set; } = true;

    /// <summary>Phase 21-SC-INV-A — DS1 ships a "minimize" close button
    /// (b_gui_ig_mnu_minimize-up/-hov/-dwn) pinned to the top-right of the
    /// inventory pane. Host pre-loads the GlTexture and hands it in; the
    /// rect of the last-drawn button is published back via
    /// <see cref="CloseRect"/> so the click handler can hit-test it without
    /// re-deriving panel dimensions.</summary>
    public (int X, int Y, int W, int H) CloseRect { get; private set; }
    public bool IsPointInClose(int x, int y) =>
        x >= CloseRect.X && y >= CloseRect.Y &&
        x <  CloseRect.X + CloseRect.W && y <  CloseRect.Y + CloseRect.H;

    private (int x, int y) Origin(int viewportW, int viewportH)
    {
        if (OriginX >= 0 && OriginY >= 0) return (OriginX, OriginY);
        int pw = PanelWidth(viewportH);
        int ph = PanelHeight(viewportH);
        return ((viewportW - pw) / 2, (viewportH - ph) / 2);
    }

    /// <summary>Scaled cell size + grid-origin offsets for the current
    /// viewport. Centralized so the chrome render and the hit-test paths
    /// agree on where each cell lives.</summary>
    private static (int CellPx, int GridXOff, int GridYOff) ScaledGrid(int viewportH)
    {
        float s = Scale(viewportH);
        return (
            (int)System.Math.Round(RefCellPx * s),
            (int)System.Math.Round(RefGridXOffset * s),
            (int)System.Math.Round(RefGridYOffset * s)
        );
    }

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
        var (px, py) = Origin(viewportW, viewportH);
        int pw = PanelWidth(viewportH);
        int ph = PanelHeight(viewportH);
        return x >= px && y >= py && x < px + pw && y < py + ph;
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

    /// <summary>Phase 21-SC-SCROLL-E-1 — non-latching hit test against the
    /// inventory grid. Returns the index of the item under the cursor (in
    /// the supplied <paramref name="items"/> list), or -1 when the click
    /// lands on an empty cell or panel chrome. Use this when the host
    /// wants to intercept a click before the intra-grid drag latch (e.g.
    /// "is this a scroll? pick it up onto the cursor instead").</summary>
    public int TryHitTestItem(int x, int y, int viewportW, int viewportH,
                              IReadOnlyList<LootEntry> items,
                              Func<string, (int W, int H)>? resolveGridSize)
    {
        EnsurePlacements(items.Count);
        Pack(items, resolveGridSize);
        var (px, py) = Origin(viewportW, viewportH);
        var (cellPx, gridXOff, gridYOff) = ScaledGrid(viewportH);
        int gridX = px + gridXOff;
        int gridY = py + gridYOff;
        for (int i = 0; i < items.Count; i++)
        {
            var (row, col) = _placements[i];
            if (row < 0 || col < 0) continue;
            var (w, h) = ResolveGrid(items[i].Reference, resolveGridSize);
            int sx = gridX + col * cellPx;
            int sy = gridY + row * cellPx;
            if (x >= sx && y >= sy && x < sx + w * cellPx && y < sy + h * cellPx)
                return i;
        }
        return -1;
    }

    /// <summary>LMB-down inside the panel. Latches the dragged item if the
    /// cursor lands on an item rect. No-op on empty cells.</summary>
    public void OnMouseDown(int x, int y, int viewportW, int viewportH,
                            IReadOnlyList<LootEntry> items,
                            Func<string, (int W, int H)>? resolveGridSize)
    {
        EnsurePlacements(items.Count);
        Pack(items, resolveGridSize);
        _mouseX = x; _mouseY = y;

        var (px, py) = Origin(viewportW, viewportH);
        var (cellPx, gridXOff, gridYOff) = ScaledGrid(viewportH);
        int gridX = px + gridXOff;
        int gridY = py + gridYOff;
        for (int i = 0; i < items.Count; i++)
        {
            var (row, col) = _placements[i];
            if (row < 0 || col < 0) continue;
            var (w, h) = ResolveGrid(items[i].Reference, resolveGridSize);
            int sx = gridX + col * cellPx;
            int sy = gridY + row * cellPx;
            if (x >= sx && y >= sy && x < sx + w * cellPx && y < sy + h * cellPx)
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
        var (poX, poY) = Origin(viewportW, viewportH);
        var (cellPx, gridXOff, gridYOff) = ScaledGrid(viewportH);
        int gridX = poX + gridXOff;
        int gridY = poY + gridYOff;
        int targetCol = (x - gridX) / cellPx;
        int targetRow = (y - gridY) / cellPx;
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
                     Func<string, (int W, int H)>? resolveGridSize = null,
                     GlTexture? closeIcon = null,
                     GlTexture? goldCoinIcon = null,
                     Func<string, GlTexture?>? resolveCommonChrome = null,
                     GlTexture? arrangeUp = null,
                     GlTexture? goldBg = null,
                     GlTexture? gridTile = null)
    {
        EnsurePlacements(items.Count);
        Pack(items, resolveGridSize);

        var (px, py) = Origin(viewportW, viewportH);
        int pw = PanelWidth(viewportH);
        int ph = PanelHeight(viewportH);
        int titleH = TitleH(viewportH);
        var (cellPx, gridXOff, gridYOff) = ScaledGrid(viewportH);
        int gridX = px + gridXOff;
        int gridY = py + gridYOff;
        float s = Scale(viewportH);

        // Phase 22-AUTH-INV — every color/ink in this method is DS1-derived
        // (white tints on textured chrome, copperplate-ink for text). Per
        // feedback_ship_ds1_end_to_end.md, no SiegeFX-invented placeholder
        // hex literals remain in the panel chrome path.
        var dim    = new Vector4(0f, 0f, 0f, 0.55f);
        var ink    = new Vector4(0xff / 255f, 0xff / 255f, 0xff / 255f, 1f); // gas font_color = 0xffffffff
        var white  = new Vector4(1f, 1f, 1f, 1f);
        var ghost  = new Vector4(1f, 1f, 1f, 0.65f);

        // === Attribute coverage (per feedback_audit_asset_paths.md) ====
        // Authored attributes consumed by this method, per inventory.gas:
        //   dialog_box_inv_bg .common_template=cpbox → NinePatch.DrawCpbox
        //                     .uvcoords          → cpbox is per-tile, not
        //                                          per-element; uv on a
        //                                          dialog_box wrapper is
        //                                          ignored by the nine-patch
        //                                          helper (each corner/edge
        //                                          has its own native UV).
        //   button_arrange    .texture          → arrangeUp param
        //                     .uvcoords         → consumed (V-flipped)
        //                     .rect             → consumed (V-flipped)
        //                     .rollover_help    → SC-AUTH-INV-INTERACT
        //                                          splinter (tooltip)
        //                     .[messages] notify(arrange_inventory) →
        //                                         SC-AUTH-INV-INTERACT
        //                                         splinter (sort impl)
        //                     button down/hov state-swaps → splinter too
        //   button_gold       .common_template=button_4 → SC-AUTH-INV-
        //                                          BUTTON-4-CHROME splinter
        //                                          (NinePatch helper +
        //                                          button bg texture set)
        //                     .[messages] notify(gold_transfer) →
        //                                         SC-AUTH-INV-INTERACT
        //                                         splinter (needs pack-
        //                                         mule transfer system)
        //   inventory_gold    .font_type=b_gui_fnt_12p_copperplate-light →
        //                                         SC-HUD-FONT-AUTH splinter
        //                                         (engine-wide font load)
        //                     .text="999999"    → placeholder, replaced at
        //                                         render with Gold.ToString()
        //                     .justify=center   → consumed (center math)
        //   window_gold_bg    .texture+rect+uv  → consumed (V-flipped)
        //   window_gold_icon  .texture+rect     → consumed (goldCoin param)
        //   button_inventory_exit .common_template=x → consumed via cpbox
        //                                              button_x_up resolver
        //                     .rollover_help    → SC-AUTH-INV-INTERACT
        //                     .[messages] notify(character_exit) →
        //                                         consumed indirectly: host
        //                                         hit-tests CloseRect and
        //                                         flips _inventoryOpen
        //   gridbox_13x4      .texture=b_gui_ig_mnu_ip_grid → consumed
        //                                          (one tile per cell)
        //                     .wrap_mode=tiled  → replaced with per-cell
        //                                         render (semantically
        //                                         identical for 4×13)
        //                     .uvcoords=0,-12.03125,4.03125,1 → the
        //                                          negative-V tiling is
        //                                          the gas's wrap-mode
        //                                          shorthand; per-cell
        //                                          render achieves the
        //                                          same final pixels.
        if (DimBackdrop)
            bars.DrawRect(viewportW, viewportH, 0, 0, viewportW, viewportH, dim);

        // === Panel chrome ============================================
        // gas: dialog_box_inv_bg common_template=cpbox. Render via the
        // nine-patch chrome helper if the resolver was supplied; fall back
        // to a black-rect placeholder if the host hasn't wired it yet
        // (e.g. unit-test bootstrap, headless mode).
        if (icons is not null && resolveCommonChrome is not null)
        {
            NinePatch.DrawCpbox(icons, resolveCommonChrome, viewportW, viewportH,
                px, py, pw, ph, white);
        }
        else
        {
            bars.DrawRect(viewportW, viewportH, px, py, pw, ph, new Vector4(0.05f, 0.05f, 0.08f, 0.95f));
        }

        // === Title bar elements ======================================
        // gas authors these at fixed 640-ref X coords inside the panel.
        // Compute panel-relative X for each then scale.
        // arrange button: rect 255,2,279,30 → panel-rel x=2 y=2 w=24 h=28
        int arrangeX  = px + (int)System.Math.Round(2  * s);
        int arrangeY  = py + (int)System.Math.Round(2  * s);
        int arrangeW  = (int)System.Math.Round(RefArrangeW * s);
        int arrangeH  = (int)System.Math.Round(RefArrangeH * s);
        if (arrangeUp is not null && icons is not null)
        {
            // gas uvcoords = 0,0.125,0.75,1 — bottom-up V-flip rule from the
            // data_bar fold applies (DS1 RAWs are stored bottom-up and gas
            // V values are authored in that frame): screenVMin = 1 - gasV1,
            // screenVMax = 1 - gasV0 → (0,0) and (0.75,0.875) in screen
            // top-down convention.
            icons.DrawIcon(viewportW, viewportH, arrangeUp,
                arrangeX, arrangeY, arrangeW, arrangeH, white,
                0f, 0f, 0.75f, 1f - 0.125f);
        }

        // gold readout bg: rect 279,2,362,30 → panel-rel x=26 y=2 w=83 h=28
        int goldBgX = px + (int)System.Math.Round(26 * s);
        int goldBgY = py + (int)System.Math.Round(2  * s);
        int goldBgW = (int)System.Math.Round(RefGoldBgW * s);
        int goldBgH = (int)System.Math.Round(RefGoldBgH * s);
        if (goldBg is not null && icons is not null)
        {
            // gas uvcoords = 0,0.125,0.648438,1 (same V-flip rule).
            icons.DrawIcon(viewportW, viewportH, goldBg,
                goldBgX, goldBgY, goldBgW, goldBgH, white,
                0f, 0f, 0.648438f, 1f - 0.125f);
        }

        // gold coin icon: rect 285,8,301,24 → panel-rel x=32 y=8 w=16 h=16
        int coinX = px + (int)System.Math.Round(32 * s);
        int coinY = py + (int)System.Math.Round(8  * s);
        int coinSz = (int)System.Math.Round(RefGoldIconSz * s);
        if (goldCoinIcon is not null && icons is not null)
            icons.DrawIcon(viewportW, viewportH, goldCoinIcon, coinX, coinY, coinSz, coinSz, white);

        // gold count text: rect 302,8,360,24 → panel-rel x=49 y=8 w=58 h=16
        // gas authors justify=center inside that rect, font copperplate-light.
        int goldTextX = px + (int)System.Math.Round(49 * s);
        int goldTextY = py + (int)System.Math.Round(8  * s);
        var countText = Gold.ToString();
        int countW = text.MeasureWidth(countText);
        int textCenterX = goldTextX + ((int)System.Math.Round(RefGoldTextW * s) - countW) / 2;
        text.DrawString(viewportW, viewportH, countText, textCenterX, goldTextY, ink);

        // X close button: rect 369,2,385,18 → panel-rel x=116 y=2 w=16 h=16
        int closeX = px + (int)System.Math.Round(116 * s);
        int closeY = py + (int)System.Math.Round(2   * s);
        int closeSz = (int)System.Math.Round(RefCloseSz * s);
        CloseRect = (closeX, closeY, closeSz, closeSz);
        // Resolve the X-close button texture via the common-control chrome
        // (gas: button_inventory_exit common_template=x → b_gui_cmn_button_x_up).
        var xUp = resolveCommonChrome?.Invoke("button_x_up");
        if (xUp is not null && icons is not null)
        {
            icons.DrawIcon(viewportW, viewportH, xUp, closeX, closeY, closeSz, closeSz, white);
        }
        else if (closeIcon is not null && icons is not null)
        {
            // Legacy fallback to the previously-supplied close icon.
            icons.DrawIcon(viewportW, viewportH, closeIcon, closeX, closeY, closeSz, closeSz, white);
        }

        // === Grid background ==========================================
        // gas: gridbox_13x4 texture=b_gui_ig_mnu_ip_grid, wrap_mode=tiled.
        // The texture is a 32×32 cell tile; uvcoords = 0,-12.03125,4.03125,1
        // imply tiling across 4 cols × 13 rows (the negative V starts above
        // the texture and wraps tiled-mode upward — the implementation just
        // tiles a single cell texture across the whole grid). We render
        // each cell's tile individually to keep the math obvious; same end
        // result and skips the wrap-mode dependency.
        int gridW = (int)System.Math.Round(129 * s); // 384 - 255
        int gridH = (int)System.Math.Round(417 * s); // 447 - 30
        if (gridTile is not null && icons is not null)
        {
            for (int row = 0; row < GridRows; row++)
                for (int col = 0; col < GridCols; col++)
                {
                    int sx = gridX + col * cellPx;
                    int sy = gridY + row * cellPx;
                    icons.DrawIcon(viewportW, viewportH, gridTile,
                        sx, sy, cellPx, cellPx, white);
                }
        }
        else
        {
            // Fallback when the grid tile RAW didn't resolve — solid dim
            // cells so the player still sees a visible grid.
            var cellBg     = new Vector4(0.04f, 0.04f, 0.05f, 1f);
            var cellBorder = new Vector4(0.32f, 0.27f, 0.18f, 1f);
            for (int row = 0; row < GridRows; row++)
                for (int col = 0; col < GridCols; col++)
                {
                    int sx = gridX + col * cellPx;
                    int sy = gridY + row * cellPx;
                    bars.DrawRect  (viewportW, viewportH, sx, sy, cellPx, cellPx, cellBg);
                    bars.DrawBorder(viewportW, viewportH, sx, sy, cellPx, cellPx, cellBorder);
                }
        }

        // === Item rects ===============================================
        // Skip the dragged item from the static layer; it's drawn at the
        // cursor instead. Everything else gets its icon-or-text on top of
        // the underlying grid tile.
        for (int i = 0; i < items.Count; i++)
        {
            if (i == _dragIndex) continue;
            var (row, col) = _placements[i];
            if (row < 0 || col < 0) continue;
            var (w, h) = ResolveGrid(items[i].Reference, resolveGridSize);
            int sx = gridX + col * cellPx;
            int sy = gridY + row * cellPx;
            int sw = w * cellPx;
            int sh = h * cellPx;
            DrawItemFace(bars, text, icons, viewportW, viewportH,
                         items[i].Reference, sx, sy, sw, sh, resolveIcon, ink, white);
        }

        // === Drag ghost ==============================================
        // Render at the cursor, top-left of the footprint anchored under
        // the cursor's grid cell so the user sees where the drop will land.
        if (_dragIndex >= 0 && _dragIndex < items.Count)
        {
            var (w, h) = ResolveGrid(items[_dragIndex].Reference, resolveGridSize);
            int targetCol = (_mouseX - gridX) / cellPx;
            int targetRow = (_mouseY - gridY) / cellPx;
            int gx, gy;
            bool snapped = targetCol >= 0 && targetRow >= 0
                && targetCol + w <= GridCols && targetRow + h <= GridRows;
            if (snapped)
            {
                gx = gridX + targetCol * cellPx;
                gy = gridY + targetRow * cellPx;
            }
            else
            {
                gx = _mouseX - (w * cellPx) / 2;
                gy = _mouseY - (h * cellPx) / 2;
            }
            int gw = w * cellPx;
            int gh = h * cellPx;
            DrawItemFace(bars, text, icons, viewportW, viewportH,
                         items[_dragIndex].Reference, gx, gy, gw, gh,
                         resolveIcon, ink, ghost);
        }
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
