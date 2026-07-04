using System.Numerics;
using SiegeFX.Core.Actors;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 20d — vendor trade UI. Two-column list layout: left column is the
/// vendor's stock (buy from), right column is the player's inventory (sell to,
/// at half list price). Top of the panel shows the vendor's name + the
/// player's gold counter; bottom is a "Press Esc to close" hint.
///
/// State-light: the panel itself owns hover/press for the per-row buttons but
/// reads vendor + player data live each frame from the host. The host applies
/// the actual gold + inventory mutations through <see cref="Buy"/> /
/// <see cref="Sell"/> callbacks so the trade authority stays in one place
/// (RenderHost) instead of being spread across the UI.
/// </summary>
public sealed class VendorPanel
{
    public bool IsOpen { get; private set; }

    /// <summary>Currently-open vendor, or null when closed. The host reads
    /// this back when applying a Buy action so it indexes into the right
    /// stock list (rather than guessing from the talked-template name).</summary>
    public VendorDefinition? OpenVendor => _vendor;

    private VendorDefinition? _vendor;

    private const int PanelW   = 720;
    private const int PanelH   = 380;
    private const int Padding  = 14;
    private const int TitleH   = 24;
    private const int RowH     = 28;
    private const int ColGap   = 18;
    private const int BtnW     = 56;
    private const int BtnH     = 22;
    private const int MaxRows  = 9;

    // Per-row buttons. We over-allocate to MaxRows on each side and only draw
    // the ones backed by a real row this frame; cheap enough at 9+9 buttons
    // and avoids re-allocating MenuButton instances per open.
    private readonly MenuButton[] _buyBtns  = new MenuButton[MaxRows];
    private readonly MenuButton[] _sellBtns = new MenuButton[MaxRows];

    public VendorPanel()
    {
        for (int i = 0; i < MaxRows; i++)
        {
            _buyBtns[i]  = new MenuButton("Buy",  0, 0, BtnW, BtnH);
            _sellBtns[i] = new MenuButton("Sell", 0, 0, BtnW, BtnH);
        }
    }

    public void Open(VendorDefinition vendor)
    {
        _vendor = vendor;
        IsOpen  = true;
        for (int i = 0; i < MaxRows; i++)
        {
            _buyBtns[i].CancelPress();
            _sellBtns[i].CancelPress();
        }
    }

    public void Close()
    {
        IsOpen  = false;
        _vendor = null;
    }

    private static (int px, int py) Origin(int viewportW, int viewportH)
        => ((viewportW - PanelW) / 2, (viewportH - PanelH) / 2);

    private void Layout(int viewportW, int viewportH, int playerRowCount, int vendorRowCount)
    {
        var (px, py) = Origin(viewportW, viewportH);
        int colW = (PanelW - Padding * 2 - ColGap) / 2;
        int leftRowsX  = px + Padding;
        int rightRowsX = px + Padding + colW + ColGap;
        int rowsTopY   = py + TitleH + Padding + 18; // +18 for the column header line

        // Buy buttons sit at the right edge of the left column; sell buttons
        // sit at the right edge of the right column. Each row is RowH tall.
        int buyBtnX  = leftRowsX  + colW - BtnW - 4;
        int sellBtnX = rightRowsX + colW - BtnW - 4;
        int btnYOff  = (RowH - BtnH) / 2;

        for (int i = 0; i < MaxRows; i++)
        {
            int y = rowsTopY + i * RowH + btnYOff;
            _buyBtns[i].X  = buyBtnX;  _buyBtns[i].Y  = y;
            _sellBtns[i].X = sellBtnX; _sellBtns[i].Y = y;
        }
    }

    public void OnMouseMove(int px, int py, int playerRowCount, int viewportW, int viewportH)
    {
        if (!IsOpen || _vendor is null) return;
        Layout(viewportW, viewportH, playerRowCount, _vendor.Stock.Count);
        for (int i = 0; i < MaxRows; i++)
        {
            _buyBtns[i].UpdateHover(px, py);
            _sellBtns[i].UpdateHover(px, py);
        }
    }

    /// <summary>LMB-down. Latches a press on whatever button the cursor is over.
    /// Returns true so the host knows to swallow the click.</summary>
    public bool OnMouseDown(int px, int py, int playerRowCount, int viewportW, int viewportH)
    {
        if (!IsOpen || _vendor is null) return false;
        Layout(viewportW, viewportH, playerRowCount, _vendor.Stock.Count);
        for (int i = 0; i < _vendor.Stock.Count && i < MaxRows; i++) _buyBtns[i].TryPress(px, py);
        for (int i = 0; i < playerRowCount     && i < MaxRows; i++) _sellBtns[i].TryPress(px, py);
        return true;
    }

    /// <summary>LMB-up. Returns the action that fired this release, if any.
    /// Index is the row position in the relevant column (caller indexes into
    /// the vendor stock or player inventory accordingly). One action per
    /// release — the per-row buttons don't overlap so multiple firings are
    /// impossible without a UI bug.</summary>
    public VendorAction OnMouseUp(int px, int py, int playerRowCount, int viewportW, int viewportH)
    {
        if (!IsOpen || _vendor is null) return VendorAction.None();
        Layout(viewportW, viewportH, playerRowCount, _vendor.Stock.Count);
        for (int i = 0; i < _vendor.Stock.Count && i < MaxRows; i++)
            if (_buyBtns[i].Release(px, py))  return new VendorAction(VendorActionKind.Buy,  i);
        for (int i = 0; i < playerRowCount     && i < MaxRows; i++)
            if (_sellBtns[i].Release(px, py)) return new VendorAction(VendorActionKind.Sell, i);
        return VendorAction.None();
    }

    public void Draw(BarRenderer bars, TextRenderer text, int viewportW, int viewportH,
                     IReadOnlyList<LootEntry> playerInventory, long playerGold)
    {
        if (!IsOpen || _vendor is null) return;
        Layout(viewportW, viewportH, playerInventory.Count, _vendor.Stock.Count);

        var (px, py) = Origin(viewportW, viewportH);

        var dim    = new Vector4(0f, 0f, 0f, 0.55f);
        var panel  = new Vector4(0.08f, 0.08f, 0.10f, 0.94f);
        var title  = new Vector4(0.16f, 0.13f, 0.10f, 1f);
        var border = new Vector4(0.78f, 0.66f, 0.42f, 1f);
        var ink    = new Vector4(0.92f, 0.88f, 0.78f, 1f);
        var dimInk = new Vector4(0.55f, 0.50f, 0.42f, 1f);
        var hdr    = new Vector4(1.00f, 0.85f, 0.40f, 1f);
        var rowAlt = new Vector4(0.13f, 0.11f, 0.09f, 1f);

        bars.DrawRect  (viewportW, viewportH, 0, 0, viewportW, viewportH, dim);
        bars.DrawRect  (viewportW, viewportH, px, py, PanelW, PanelH, panel);
        bars.DrawRect  (viewportW, viewportH, px, py, PanelW, TitleH, title);
        bars.DrawBorder(viewportW, viewportH, px, py, PanelW, PanelH, border);
        bars.DrawBorder(viewportW, viewportH, px, py + TitleH, PanelW, 1, border);

        var vendorTitle = $"{_vendor.ScreenName}  —  Trade";
        text.DrawString(viewportW, viewportH, vendorTitle, px + Padding, py + 6, ink);
        var goldStr = $"Gold: {playerGold}";
        int goldW = text.MeasureWidth(goldStr);
        text.DrawString(viewportW, viewportH, goldStr, px + PanelW - Padding - goldW, py + 6, hdr);

        int colW = (PanelW - Padding * 2 - ColGap) / 2;
        int leftRowsX  = px + Padding;
        int rightRowsX = px + Padding + colW + ColGap;
        int hdrY       = py + TitleH + Padding;

        text.DrawString(viewportW, viewportH, "FOR SALE",   leftRowsX,  hdrY, hdr);
        text.DrawString(viewportW, viewportH, "YOUR ITEMS", rightRowsX, hdrY, hdr);

        int rowsTopY = hdrY + 18;
        int rowTextY = rowsTopY + (RowH - (text.HasFont ? text.Font!.Height : 14)) / 2;

        // Vendor stock column.
        for (int i = 0; i < _vendor.Stock.Count && i < MaxRows; i++)
        {
            int y = rowsTopY + i * RowH;
            if ((i & 1) == 0)
                bars.DrawRect(viewportW, viewportH, leftRowsX, y, colW, RowH, rowAlt);
            var item = _vendor.Stock[i];
            var name = item.ScreenName.Length > 0 ? item.ScreenName : item.ItemReference;
            text.DrawString(viewportW, viewportH, name, leftRowsX + 6, rowTextY + i * RowH, ink);
            var price = $"{item.Price}g";
            int pw = text.MeasureWidth(price);
            text.DrawString(viewportW, viewportH, price,
                            leftRowsX + colW - BtnW - 12 - pw, rowTextY + i * RowH, hdr);
            _buyBtns[i].Draw(bars, text, viewportW, viewportH);
        }

        // Player inventory column. Sell price is half the list price; without a
        // shipped pricing table for non-vendor items we fall back to a flat
        // 5g for anything not in the catalog so the loop still demos.
        for (int i = 0; i < playerInventory.Count && i < MaxRows; i++)
        {
            int y = rowsTopY + i * RowH;
            if ((i & 1) == 0)
                bars.DrawRect(viewportW, viewportH, rightRowsX, y, colW, RowH, rowAlt);
            var entry = playerInventory[i];
            var label = entry.Reference.StartsWith('_')
                ? entry.Reference[1..] : entry.Reference;
            text.DrawString(viewportW, viewportH, label, rightRowsX + 6, rowTextY + i * RowH, ink);
            long sellPrice = ResolveSellPrice(entry.Reference);
            var price = $"{sellPrice}g";
            int pw = text.MeasureWidth(price);
            text.DrawString(viewportW, viewportH, price,
                            rightRowsX + colW - BtnW - 12 - pw, rowTextY + i * RowH, dimInk);
            _sellBtns[i].Draw(bars, text, viewportW, viewportH);
        }

        const string foot = "Press Esc to close";
        int fw = text.MeasureWidth(foot);
        text.DrawString(viewportW, viewportH, foot,
                        px + (PanelW - fw) / 2, py + PanelH - 18, dimInk);
    }

    /// <summary>Sell-price lookup. If the item is in any vendor's stock, sell
    /// at half the list price; otherwise default to a flat 5g placeholder.
    /// 20d-incomplete: items without a catalog entry get the flat price; full
    /// per-template economy lands when treasure_set.gas is parsed.</summary>
    /// <summary>Phase 25b — host-provided item valuation (authored
    /// gold_value chain, or the provisional power curve). Sell price =
    /// half the base value until the 25d pricing fit says otherwise.</summary>
    public static Func<string, long>? PriceResolver;

    public static long ResolveSellPrice(string itemRef)
    {
        if (PriceResolver is not null)
            return Math.Max(1, PriceResolver(itemRef) / 2);
        // Legacy fallback: any open catalog row, else a flat 5g.
        foreach (var v in EnumerateAllStock())
        {
            if (string.Equals(v.ItemReference, itemRef, StringComparison.OrdinalIgnoreCase))
                return Math.Max(1, v.Price / 2);
        }
        return 5;
    }

    private static IEnumerable<VendorStockItem> EnumerateAllStock()
    {
        foreach (var def in VendorCatalog.AllDefinitions)
            foreach (var s in def.Stock) yield return s;
    }
}

public enum VendorActionKind { None, Buy, Sell }

public readonly record struct VendorAction(VendorActionKind Kind, int Index)
{
    public static VendorAction None() => new(VendorActionKind.None, -1);
    public bool IsNone => Kind == VendorActionKind.None;
}
