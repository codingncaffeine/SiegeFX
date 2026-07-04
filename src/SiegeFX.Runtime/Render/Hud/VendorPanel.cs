using System.Numerics;
using SiegeFX.Core.Actors;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 25c — DS1-authentic store screen, rebuilt from
/// /ui/interfaces/backend/store/store.gas (640×480 reference, right-
/// docked). Chrome: cpbox frame 366,0,639,449 with portrait box
/// 371,3,413,52, name plate 415,12,636,46, grid frame 369,82,636,412
/// holding the 8×10 32px shelf grid at 374,88,630,408; six radio tabs
/// (front row ARMOR/WEAPONS/SHIELDS, back row SPELLS/POTIONS/MISC —
/// checked tab shifts down 5px per the authored shift_y(5));
/// Previous/Next paging and Close along the bottom (button_4 chrome —
/// colour-matched frames until SC-AUTH-INV-BUTTON-4-CHROME lands,
/// same bridge the paperdoll View button uses).
///
/// Interaction: LMB a shelf item = Buy (host applies the mutation via
/// <see cref="VendorAction"/>). Selling is host-side: DS1 opens your
/// own inventory beside the store; clicking one of your items while
/// the store is open sells it (RenderHost intercept).
/// </summary>
public sealed class VendorPanel
{
    public bool IsOpen { get; private set; }
    public VendorDefinition? OpenVendor => _vendor;

    private VendorDefinition? _vendor;
    private int _tab;   // 0 armor, 1 weapons, 2 shields, 3 magic, 4 potions, 5 misc
    private int _page;

    // Authored rects (640×480 ref; the whole screen right-docks).
    static readonly (int x0, int y0, int x1, int y1) RFrame     = (366, 0, 639, 449);
    static readonly (int x0, int y0, int x1, int y1) RPortrait  = (371, 3, 413, 52);
    static readonly (int x0, int y0, int x1, int y1) RNamePlate = (415, 12, 636, 46);
    static readonly (int x0, int y0, int x1, int y1) RGridFrame = (369, 82, 636, 412);
    static readonly (int x0, int y0, int x1, int y1) RGrid      = (374, 88, 630, 408);
    const int GridCols = 8, GridRows = 10, CellPx = 32;
    static readonly ((int x0, int y0, int x1, int y1) Rect, string Label)[] Tabs =
    {
        ((373, 68, 453, 84), "ARMOR"),
        ((453, 67, 533, 83), "WEAPONS"),
        ((534, 67, 614, 83), "SHIELDS"),
        ((393, 53, 473, 68), "SPELLS"),
        ((472, 53, 552, 68), "POTIONS"),
        ((551, 53, 631, 68), "MISC"),
    };
    static readonly (int x0, int y0, int x1, int y1) RPrev  = (371, 418, 451, 434);
    static readonly (int x0, int y0, int x1, int y1) RNext  = (457, 418, 537, 434);
    static readonly (int x0, int y0, int x1, int y1) RClose = (551, 418, 631, 434);

    // Per-frame layout products (screen space).
    readonly List<(int StockIndex, int Cx, int Cy, int W, int H)> _placed = new(48);
    int _pageCount = 1;
    (int x, int y, int w, int h) _gridPx, _prevPx, _nextPx, _closePx, _framePx;
    readonly (int x, int y, int w, int h)[] _tabPx = new (int, int, int, int)[6];
    int _hoverStock = -1;
    int _pressStock = -1;
    int _pressButton = -1; // 0 prev, 1 next, 2 close

    /// <summary>Phase 25b — host-provided item valuation. Sell price =
    /// half the base value until the 25d pricing fit says otherwise.</summary>
    public static Func<string, long>? PriceResolver;

    public static long ResolveSellPrice(string itemRef)
        => PriceResolver is not null ? Math.Max(1, PriceResolver(itemRef) / 2) : 5;

    public void Open(VendorDefinition vendor)
    {
        _vendor = vendor;
        IsOpen = true;
        _tab = FirstStockedTab();
        _page = 0;
        _pressStock = -1;
        _pressButton = -1;
    }

    public void Close()
    {
        IsOpen = false;
        _vendor = null;
    }

    int FirstStockedTab()
    {
        if (_vendor is null) return 0;
        for (int t = 0; t < 6; t++)
            if (StockForTab(t).Any()) return t;
        return 0;
    }

    /// <summary>Host-supplied classifiers, set once at wiring time.</summary>
    public static Func<string, bool>? IsPotion;          // template → potion chain?
    public static Func<string, (int W, int H)>? Footprint; // inventory_width/height

    IEnumerable<(int Index, VendorStockItem Item)> StockForTab(int tab)
    {
        if (_vendor is null) yield break;
        for (int i = 0; i < _vendor.Stock.Count; i++)
        {
            var it = _vendor.Stock[i];
            var t = it.Slot; // Slot carries the store tab name (set by the host)
            bool match = tab switch
            {
                0 => t.Equals("armor", StringComparison.OrdinalIgnoreCase),
                1 => t.Equals("weapons", StringComparison.OrdinalIgnoreCase),
                2 => t.Equals("shields", StringComparison.OrdinalIgnoreCase),
                3 => t.Equals("magic", StringComparison.OrdinalIgnoreCase),
                4 => t.Equals("misc", StringComparison.OrdinalIgnoreCase)
                     && (IsPotion?.Invoke(it.ItemReference) ?? false),
                5 => t.Equals("misc", StringComparison.OrdinalIgnoreCase)
                     && !(IsPotion?.Invoke(it.ItemReference) ?? false),
                _ => false,
            };
            if (match) yield return (i, it);
        }
    }

    static float Scale(int viewportH) => viewportH / 480f;

    static (int x, int y, int w, int h) Px((int x0, int y0, int x1, int y1) r, float s, int originX)
        => (originX + (int)MathF.Round(r.x0 * s), (int)MathF.Round(r.y0 * s),
            (int)MathF.Round((r.x1 - r.x0) * s), (int)MathF.Round((r.y1 - r.y0) * s));

    void Layout(int viewportW, int viewportH)
    {
        float s = Scale(viewportH);
        // Right-dock the authored 640-wide frame.
        int originX = viewportW - (int)MathF.Round(640f * s);

        _framePx = Px(RFrame, s, originX);
        _gridPx  = Px(RGrid, s, originX);
        _prevPx  = Px(RPrev, s, originX);
        _nextPx  = Px(RNext, s, originX);
        _closePx = Px(RClose, s, originX);
        for (int t = 0; t < 6; t++) _tabPx[t] = Px(Tabs[t].Rect, s, originX);

        // Shelf packing: first-fit rows by footprint across the 8×10 grid,
        // split into pages. Records screen-space cells for hit-testing.
        _placed.Clear();
        int cellW = _gridPx.w / GridCols, cellH = _gridPx.h / GridRows;
        var occupied = new bool[GridCols, GridRows];
        int page = 0;
        void ResetGrid() => Array.Clear(occupied);
        bool TryPlace(int w, int h, out int cx, out int cy)
        {
            for (int y = 0; y + h <= GridRows; y++)
            for (int x = 0; x + w <= GridCols; x++)
            {
                bool free = true;
                for (int dy = 0; dy < h && free; dy++)
                for (int dx = 0; dx < w && free; dx++)
                    if (occupied[x + dx, y + dy]) free = false;
                if (!free) continue;
                for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                    occupied[x + dx, y + dy] = true;
                cx = x; cy = y;
                return true;
            }
            cx = cy = 0;
            return false;
        }

        foreach (var (idx, it) in StockForTab(_tab))
        {
            var (fw, fh) = Footprint?.Invoke(it.ItemReference) ?? (1, 1);
            fw = Math.Clamp(fw, 1, GridCols);
            fh = Math.Clamp(fh, 1, GridRows);
            if (!TryPlace(fw, fh, out var cx, out var cy))
            {
                page++;
                ResetGrid();
                TryPlace(fw, fh, out cx, out cy);
            }
            if (page == _page)
                _placed.Add((idx, _gridPx.x + cx * cellW, _gridPx.y + cy * cellH, fw * cellW, fh * cellH));
        }
        _pageCount = page + 1;
        if (_page >= _pageCount) _page = Math.Max(0, _pageCount - 1);
    }

    public void OnMouseMove(int px, int py, int playerRowCount, int viewportW, int viewportH)
    {
        if (!IsOpen || _vendor is null) return;
        Layout(viewportW, viewportH);
        _hoverStock = -1;
        foreach (var p in _placed)
            if (px >= p.Cx && px < p.Cx + p.W && py >= p.Cy && py < p.Cy + p.H)
            { _hoverStock = p.StockIndex; break; }
    }

    public bool OnMouseDown(int px, int py, int playerRowCount, int viewportW, int viewportH)
    {
        if (!IsOpen || _vendor is null) return false;
        Layout(viewportW, viewportH);
        _pressStock = -1;
        _pressButton = -1;
        // Clicks left of the store frame belong to the co-open inventory
        // (sell path) - do not swallow them.
        if (px < _framePx.x) return false;
        for (int t = 0; t < 6; t++)
        {
            var r = _tabPx[t];
            if (px >= r.x && px < r.x + r.w && py >= r.y && py < r.y + r.h)
            {
                if (_tab != t) { _tab = t; _page = 0; }
                return true;
            }
        }
        bool In((int x, int y, int w, int h) r) => px >= r.x && px < r.x + r.w && py >= r.y && py < r.y + r.h;
        if (In(_prevPx))  { _pressButton = 0; return true; }
        if (In(_nextPx))  { _pressButton = 1; return true; }
        if (In(_closePx)) { _pressButton = 2; return true; }
        foreach (var p in _placed)
            if (px >= p.Cx && px < p.Cx + p.W && py >= p.Cy && py < p.Cy + p.H)
            { _pressStock = p.StockIndex; return true; }
        return true; // inside the frame - swallow
    }

    public VendorAction OnMouseUp(int px, int py, int playerRowCount, int viewportW, int viewportH)
    {
        if (!IsOpen || _vendor is null) return VendorAction.None();
        Layout(viewportW, viewportH);
        bool In((int x, int y, int w, int h) r) => px >= r.x && px < r.x + r.w && py >= r.y && py < r.y + r.h;
        int pressedButton = _pressButton;
        int pressedStock  = _pressStock;
        _pressButton = -1;
        _pressStock  = -1;
        switch (pressedButton)
        {
            case 0 when In(_prevPx):
                if (_page > 0) _page--;
                return VendorAction.None();
            case 1 when In(_nextPx):
                if (_page + 1 < _pageCount) _page++;
                return VendorAction.None();
            case 2 when In(_closePx):
                Close();
                return VendorAction.None();
        }
        if (pressedStock >= 0)
            foreach (var p in _placed)
                if (p.StockIndex == pressedStock
                    && px >= p.Cx && px < p.Cx + p.W && py >= p.Cy && py < p.Cy + p.H)
                    return new VendorAction(VendorActionKind.Buy, pressedStock);
        return VendorAction.None();
    }

    /// <summary>Draw the authored store screen. Icon plumbing comes from
    /// the host: <paramref name="guiTex"/> resolves chrome raws (cpbox
    /// pieces + b_gui_ig_mnu_ip_grid), <paramref name="itemIcon"/> the
    /// per-item inventory icons.</summary>
    public void Draw(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                     Func<string, GlTexture?>? guiTex,
                     Func<string, GlTexture?>? itemIcon,
                     int viewportW, int viewportH, long playerGold)
    {
        if (!IsOpen || _vendor is null) return;
        Layout(viewportW, viewportH);
        float s = Scale(viewportH);
        int originX = viewportW - (int)MathF.Round(640f * s);

        var ink    = new Vector4(0.86f, 0.83f, 0.69f, 1f);
        var dimInk = new Vector4(0.60f, 0.58f, 0.49f, 1f);

        void Cpbox((int x0, int y0, int x1, int y1) r)
        {
            var p = Px(r, s, originX);
            if (icons is not null && guiTex is not null)
                NinePatch.DrawCpbox(icons, guiTex, viewportW, viewportH, p.x, p.y, p.w, p.h, Vector4.One);
            else
            {
                bars.DrawRect(viewportW, viewportH, p.x, p.y, p.w, p.h, new Vector4(0.08f, 0.08f, 0.10f, 0.95f));
                bars.DrawBorder(viewportW, viewportH, p.x, p.y, p.w, p.h, new Vector4(0.667f, 0.655f, 0.557f, 1f));
            }
        }

        Cpbox(RFrame);
        Cpbox(RPortrait);
        Cpbox(RNamePlate);
        Cpbox(RGridFrame);

        // Store name (authored: 14p copperplate centered in the plate).
        {
            var p = Px(RNamePlate, s, originX);
            var name = _vendor.ScreenName;
            int lw = text.MeasureWidth(name);
            text.DrawString(viewportW, viewportH, name, p.x + (p.w - lw) / 2, p.y + p.h / 3, ink);
        }

        // Grid backdrop — the authored b_gui_ig_mnu_ip_grid tile (8×10).
        {
            var g = _gridPx;
            var gridTex = guiTex?.Invoke("b_gui_ig_mnu_ip_grid");
            if (icons is not null && gridTex is not null)
                icons.DrawIcon(viewportW, viewportH, gridTex, g.x, g.y, g.w, g.h, Vector4.One,
                               0f, 10f, 8f, 0f);
            else
                bars.DrawRect(viewportW, viewportH, g.x, g.y, g.w, g.h, new Vector4(0.05f, 0.05f, 0.07f, 0.9f));
        }

        // Shelf items — icon + price under hover highlight.
        foreach (var p in _placed)
        {
            var it = _vendor.Stock[p.StockIndex];
            bool hovered = p.StockIndex == _hoverStock;
            if (hovered)
                bars.DrawRect(viewportW, viewportH, p.Cx, p.Cy, p.W, p.H, new Vector4(0.30f, 0.28f, 0.18f, 0.45f));
            var tex = itemIcon?.Invoke(it.ItemReference);
            if (icons is not null && tex is not null)
                icons.DrawIcon(viewportW, viewportH, tex, p.Cx + 1, p.Cy + 1, p.W - 2, p.H - 2, Vector4.One);
            else
            {
                var label = it.ScreenName.Length > 6 ? it.ScreenName[..6] : it.ScreenName;
                text.DrawString(viewportW, viewportH, label, p.Cx + 2, p.Cy + p.H / 3, dimInk);
            }
            if (hovered)
            {
                // Hover readout: name + buy price near the cursor cell —
                // affordable stays gold-ink, unaffordable reads red.
                var afford = it.Price <= playerGold;
                var line = $"{it.ScreenName}  {it.Price}g";
                int lw = text.MeasureWidth(line);
                int tx = Math.Clamp(p.Cx, _gridPx.x, _gridPx.x + _gridPx.w - lw - 2);
                int ty = Math.Max(0, p.Cy - 12);
                bars.DrawRect(viewportW, viewportH, tx - 2, ty - 2, lw + 4, 12,
                              new Vector4(0f, 0f, 0f, 0.85f));
                text.DrawString(viewportW, viewportH, line, tx, ty,
                    afford ? ink : new Vector4(0.85f, 0.25f, 0.2f, 1f));
            }
        }

        // Tabs — checked tab shifts down 5 (authored shift_y(5)); the
        // button_4 chrome bridge is the colour-matched frame.
        for (int t = 0; t < 6; t++)
        {
            var r = _tabPx[t];
            int yOff = t == _tab ? (int)MathF.Round(5f * s) : 0;
            bool stocked = StockForTab(t).Any();
            var fill = t == _tab
                ? new Vector4(0.12f, 0.11f, 0.08f, 1f)
                : new Vector4(0.08f, 0.08f, 0.10f, 1f);
            bars.DrawRect(viewportW, viewportH, r.x, r.y + yOff, r.w, r.h, fill);
            bars.DrawBorder(viewportW, viewportH, r.x, r.y + yOff, r.w, r.h,
                stocked ? new Vector4(0.667f, 0.655f, 0.557f, 1f) : new Vector4(0.35f, 0.34f, 0.30f, 1f));
            var label = Tabs[t].Label;
            int lw = text.MeasureWidth(label);
            text.DrawString(viewportW, viewportH, label,
                r.x + (r.w - lw) / 2, r.y + yOff + r.h / 4,
                stocked ? ink : dimInk);
        }

        // Prev / Next / Close.
        void Button((int x, int y, int w, int h) r, string label, bool enabled)
        {
            bars.DrawRect(viewportW, viewportH, r.x, r.y, r.w, r.h, new Vector4(0.08f, 0.08f, 0.10f, 1f));
            bars.DrawBorder(viewportW, viewportH, r.x, r.y, r.w, r.h,
                enabled ? new Vector4(0.667f, 0.655f, 0.557f, 1f) : new Vector4(0.35f, 0.34f, 0.30f, 1f));
            int lw = text.MeasureWidth(label);
            text.DrawString(viewportW, viewportH, label, r.x + (r.w - lw) / 2, r.y + r.h / 4,
                enabled ? ink : dimInk);
        }
        Button(_prevPx, "< Previous", _page > 0);
        Button(_nextPx, "Next >", _page + 1 < _pageCount);
        Button(_closePx, "Close", true);

        // Gold + page footer inside the frame's bottom band.
        {
            var f = Px(RFrame, s, originX);
            var line = _pageCount > 1
                ? $"Gold: {playerGold}    page {_page + 1}/{_pageCount}"
                : $"Gold: {playerGold}";
            text.DrawString(viewportW, viewportH, line, f.x + 8, f.y + f.h - 12, ink);
        }
    }
}

public enum VendorActionKind { None, Buy, Sell }

public readonly record struct VendorAction(VendorActionKind Kind, int Index)
{
    public static VendorAction None() => new(VendorActionKind.None, -1);
    public bool IsNone => Kind == VendorActionKind.None;
}
