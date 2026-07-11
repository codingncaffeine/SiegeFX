using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGL;
using SiegeFX.Core.Save;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// DS1's Load Game window — the sibling of <see cref="SaveGameDialog"/>. Two
/// visual poses drive from the same data:
/// <list type="bullet">
///   <item><b>In-game</b> (Esc → LOAD GAME): a screen-centered cpbox floating
///   over the live world. Preview thumbnail on the LEFT, a HERO / MAP / ELAPSED
///   info box on the RIGHT, the save list below, and LOAD / DELETE / CANCEL.</item>
///   <item><b>Main-menu</b> (Single Player → LOAD GAME): the same panel over the
///   frontend backdrop, mirrored to match <c>load_game.gas</c> — info box on the
///   LEFT, thumbnail on the RIGHT, ◄►-selectors flanking the list, and LOAD /
///   DELETE (Cancel is the shell's Back/Previous).</item>
/// </list>
/// Unlike Save there is no name edit box; the highlighted row IS the intent.
/// Authored to the gas 640×480 reference rects, uniformly scaled by the shared
/// <see cref="HudScale.Modal"/> factor and centered, so it tracks the UI-scale
/// knob exactly like every other panel.
///
/// <para>The host drives it: <see cref="Open"/> with the current save list, route
/// mouse/scroll events, and act on the <see cref="Result"/> from
/// <see cref="OnMouseUp"/>. The dialog owns no load logic — Load/Delete return the
/// intent and the host performs the <see cref="SaveStore"/> call. The preview
/// thumbnail is uploaded to a GL texture lazily on the frame the selection
/// changes and freed on <see cref="Close"/>.</para>
/// </summary>
public sealed class LoadGameDialog : IDisposable
{
    public bool IsOpen { get; private set; }

    /// <summary>true = the ornate frontend pose (info left / thumb right, ◄►,
    /// no Cancel button); false = the in-game cpbox pose.</summary>
    public bool MainMenuStyle { get; private set; }

    public enum Result { None, Load, Delete, Cancel }

    private const int RefW = 640, RefH = 480;

    // Authored 640×480 rects (x0,y0,x1,y1). Two sets: the in-game window mirrors
    // loadsave_game.gas (preview left), the frontend window mirrors load_game.gas
    // (preview right + list arrows). Chosen at Open time and cached for the frame.
    private static readonly (int x0, int y0, int x1, int y1) RPanel = (150, 56, 492, 430);
    private static readonly (int x0, int y0, int x1, int y1) RTitle = (246, 72, 388, 101);

    // In-game pose.
    private static readonly (int x0, int y0, int x1, int y1) IgPreview = (171, 109, 258, 181);
    private static readonly (int x0, int y0, int x1, int y1) IgInfo    = (267, 109, 467, 181);
    private static readonly (int x0, int y0, int x1, int y1) IgList    = (171, 187, 467, 377);
    private static readonly (int x0, int y0, int x1, int y1) IgLoad    = (171, 388, 263, 404);
    private static readonly (int x0, int y0, int x1, int y1) IgDelete  = (273, 388, 365, 404);
    private static readonly (int x0, int y0, int x1, int y1) IgCancel  = (375, 388, 467, 404);

    // Main-menu (frontend) pose — mirrored, with list arrows.
    private static readonly (int x0, int y0, int x1, int y1) MmInfo    = (171, 109, 371, 181);
    private static readonly (int x0, int y0, int x1, int y1) MmPreview = (380, 109, 467, 181);
    private static readonly (int x0, int y0, int x1, int y1) MmList    = (196, 187, 442, 377);
    private static readonly (int x0, int y0, int x1, int y1) MmArrowL  = (152, 258, 190, 306);
    private static readonly (int x0, int y0, int x1, int y1) MmArrowR  = (452, 258, 490, 306);
    private static readonly (int x0, int y0, int x1, int y1) MmLoad    = (196, 388, 315, 404);
    private static readonly (int x0, int y0, int x1, int y1) MmDelete  = (325, 388, 442, 404);

    private readonly List<SaveStore.SaveSlot> _saves = new();
    private int _selected = -1;   // index into _saves; -1 = none
    private int _scrollRow;

    // Per-frame screen-space rects. Mouse hit-tests read these; Layout writes them.
    private (int x, int y, int w, int h) _sPanel, _sList, _sLoad, _sDelete, _sCancel, _sArrowL, _sArrowR;
    private int _rowH, _visibleRows;

    private enum Btn { None, Load, Delete, Cancel, ArrowUp, ArrowDown }
    private Btn _pressed = Btn.None;
    private Btn _hover = Btn.None;

    // Lazily-built preview texture for the highlighted slot, rebuilt when the
    // selection moves. _thumbForPath keys the cache so a redraw of the same
    // selection reuses the upload.
    private GlTexture? _thumbTex;
    private string? _thumbForPath;

    /// <summary>The highlighted existing save, or null when nothing is
    /// selected — Load / Delete act on this.</summary>
    public SaveStore.SaveSlot? Selected =>
        _selected >= 0 && _selected < _saves.Count ? _saves[_selected] : null;

    /// <summary>Open the dialog against the current on-disk save list. Auto-
    /// selects the first row (DS1 highlights a row on open so Load is armed).</summary>
    public void Open(IReadOnlyList<SaveStore.SaveSlot> saves, bool mainMenuStyle)
    {
        _saves.Clear();
        _saves.AddRange(saves);
        MainMenuStyle = mainMenuStyle;
        _selected = _saves.Count > 0 ? 0 : -1;
        _scrollRow = 0;
        _pressed = Btn.None;
        _hover = Btn.None;
        InvalidateThumb();
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        _pressed = Btn.None;
        InvalidateThumb();
    }

    public void Tick(float dt) { /* no blink/caret; kept for call-site symmetry */ }

    private void InvalidateThumb()
    {
        _thumbTex?.Dispose();
        _thumbTex = null;
        _thumbForPath = null;
    }

    // ---- layout ------------------------------------------------------------

    private void Layout(int vw, int vh)
    {
        _sPanel = Scr(RPanel, vw, vh);
        _sList  = Scr(MainMenuStyle ? MmList : IgList, vw, vh);
        _sLoad  = Scr(MainMenuStyle ? MmLoad : IgLoad, vw, vh);
        _sDelete = Scr(MainMenuStyle ? MmDelete : IgDelete, vw, vh);
        _sCancel = MainMenuStyle ? default : Scr(IgCancel, vw, vh);
        _sArrowL = MainMenuStyle ? Scr(MmArrowL, vw, vh) : default;
        _sArrowR = MainMenuStyle ? Scr(MmArrowR, vw, vh) : default;

        float s = HudScale.Modal(vw, vh);
        _rowH = Math.Max(1, (int)MathF.Round(15 * s));
        int pad = (int)MathF.Round(4 * s);
        _visibleRows = Math.Max(1, (_sList.h - pad * 2) / _rowH);
    }

    // ---- input -------------------------------------------------------------

    public void OnMouseMove(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return;
        Layout(vw, vh);
        _hover = HitButton(px, py);
    }

    /// <summary>LMB-down. Always consumes while open (modal). Latches a button
    /// press, moves the list arrows, or selects a list row.</summary>
    public bool OnMouseDown(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return false;
        Layout(vw, vh);
        _pressed = HitButton(px, py);
        if (_pressed == Btn.ArrowUp) { MoveSelection(-1); _pressed = Btn.None; return true; }
        if (_pressed == Btn.ArrowDown) { MoveSelection(+1); _pressed = Btn.None; return true; }
        if (_pressed == Btn.None) TrySelectRow(px, py);
        return true;
    }

    /// <summary>LMB-up. Returns the action if released over the same button it
    /// was pressed on; the release is consumed regardless while open.</summary>
    public Result OnMouseUp(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return Result.None;
        Layout(vw, vh);
        var up = HitButton(px, py);
        var was = _pressed;
        _pressed = Btn.None;
        if (up == Btn.None || up != was) return Result.None;
        return up switch
        {
            Btn.Load   => Selected is null ? Result.None : Result.Load,
            Btn.Delete => Selected is null ? Result.None : Result.Delete,
            Btn.Cancel => Result.Cancel,
            _          => Result.None,
        };
    }

    /// <summary>Mouse wheel over the list scrolls it. dir>0 = wheel up.</summary>
    public void OnScroll(float dir)
    {
        if (!IsOpen) return;
        int maxScroll = Math.Max(0, _saves.Count - _visibleRows);
        _scrollRow = Math.Clamp(_scrollRow - Math.Sign(dir), 0, maxScroll);
    }

    /// <summary>Keyboard up/down move the highlight (DS1 lets the arrow keys
    /// walk the list). Returns true if it consumed the key.</summary>
    public bool OnArrowKey(int dir)
    {
        if (!IsOpen) return false;
        MoveSelection(dir);
        return true;
    }

    private void MoveSelection(int dir)
    {
        if (_saves.Count == 0) return;
        _selected = Math.Clamp((_selected < 0 ? 0 : _selected) + dir, 0, _saves.Count - 1);
        // Keep the highlight in view.
        if (_selected < _scrollRow) _scrollRow = _selected;
        else if (_selected >= _scrollRow + _visibleRows) _scrollRow = _selected - _visibleRows + 1;
        _scrollRow = Math.Clamp(_scrollRow, 0, Math.Max(0, _saves.Count - _visibleRows));
        InvalidateThumb();
    }

    /// <summary>True if the point falls on the dialog's panel — the host uses
    /// this in the frontend pose to treat a click anywhere off the panel (e.g.
    /// the shell's PREVIOUS button) as a back-out.</summary>
    public bool IsInsidePanel(int px, int py, int vw, int vh)
    {
        Layout(vw, vh);
        return In(px, py, _sPanel);
    }

    private Btn HitButton(int px, int py)
    {
        if (In(px, py, _sLoad))   return Btn.Load;
        if (In(px, py, _sDelete)) return Btn.Delete;
        if (!MainMenuStyle && In(px, py, _sCancel)) return Btn.Cancel;
        if (MainMenuStyle && In(px, py, _sArrowL)) return Btn.ArrowUp;
        if (MainMenuStyle && In(px, py, _sArrowR)) return Btn.ArrowDown;
        return Btn.None;
    }

    private void TrySelectRow(int px, int py)
    {
        if (!In(px, py, _sList)) return;
        int pad = Math.Max(0, (_sList.h - _visibleRows * _rowH) / 2);
        int rel = py - (_sList.y + pad);
        if (rel < 0) return;
        int row = rel / _rowH + _scrollRow;
        if (row >= 0 && row < _saves.Count && row != _selected)
        {
            _selected = row;
            InvalidateThumb();
        }
    }

    private static bool In(int px, int py, (int x, int y, int w, int h) r)
        => r.w > 0 && r.h > 0 && px >= r.x && px < r.x + r.w && py >= r.y && py < r.y + r.h;

    // ---- draw --------------------------------------------------------------

    public void Draw(GL gl, BarRenderer bars, TextRenderer text, IconRenderer? icons,
                     Func<string, GlTexture?>? guiTex, Func<string, GlTexture?>? commonChrome,
                     int vw, int vh)
    {
        if (!IsOpen) return;
        Layout(vw, vh);
        float s = HudScale.Modal(vw, vh);
        int fs = Math.Max(1, (int)MathF.Round(s));

        var parch = new Vector4(0.88f, 0.82f, 0.70f, 1f);
        var gold  = new Vector4(1f, 0.90f, 0.55f, 1f);
        var dim   = new Vector4(0.55f, 0.52f, 0.46f, 1f);

        var preview = Scr(MainMenuStyle ? MmPreview : IgPreview, vw, vh);
        var info    = Scr(MainMenuStyle ? MmInfo : IgInfo, vw, vh);

        // Dark backing fill under the frame (matches SaveGameDialog: the cpbox's
        // own fill alone reads too see-through). NinePatch needs the COMMON-CHROME
        // resolver (bare "cpbox_ul" keys); the plain gui resolver has no border.
        bool chrome = icons is not null && commonChrome is not null;
        bars.DrawRect(vw, vh, _sPanel.x, _sPanel.y, _sPanel.w, _sPanel.h,
                      new Vector4(0.03f, 0.03f, 0.04f, MainMenuStyle ? 0.72f : 0.55f));
        void Frame((int x, int y, int w, int h) r)
        {
            if (chrome)
                NinePatch.DrawCpbox(icons!, commonChrome!, vw, vh, r.x, r.y, r.w, r.h, Vector4.One);
            else
            {
                bars.DrawRect(vw, vh, r.x, r.y, r.w, r.h, new Vector4(0.07f, 0.07f, 0.08f, 0.85f));
                bars.DrawBorder(vw, vh, r.x, r.y, r.w, r.h, new Vector4(0.45f, 0.42f, 0.34f, 1f));
            }
        }

        Frame(_sPanel);
        Frame(preview);
        Frame(info);
        Frame(_sList);

        // Title — gold, centered over the panel.
        int titleScale = Math.Max(1, (int)MathF.Round(s * 1.16f));
        const string title = "LOAD GAME";
        int tw = text.MeasureWidth(title, titleScale);
        text.DrawString(vw, vh, title, _sPanel.x + (_sPanel.w - tw) / 2,
                        Scr(RTitle, vw, vh).y, gold, titleScale);

        // Preview thumbnail — upload the selected slot's bytes lazily. A slot
        // with no captured screenshot (pre-v12 / viewer save) leaves the box a
        // recessed dark frame, which is exactly how DS1 shows a thumbnail-less
        // save.
        EnsureThumb(gl);
        if (_thumbTex is not null && icons is not null)
        {
            int ins = Math.Max(1, (int)MathF.Round(3 * s));
            icons.DrawIcon(vw, vh, _thumbTex, preview.x + ins, preview.y + ins,
                           preview.w - ins * 2, preview.h - ins * 2, Vector4.One);
        }
        else if (Selected is not null)
        {
            string nm = "NO PREVIEW";
            int nmw = text.MeasureWidth(nm, fs);
            text.DrawString(vw, vh, nm, preview.x + (preview.w - nmw) / 2,
                            preview.y + (preview.h - text.LineHeight * fs) / 2, dim, fs);
        }

        // Info box — HERO / MAP / ELAPSED TIME for the highlighted save, right-
        // justified inside the box the way DS1 lays it out.
        int infoPad = (int)MathF.Round(8 * s);
        if (Selected is { } sel)
        {
            string hero = string.IsNullOrWhiteSpace(sel.HeroName) ? "-" : sel.HeroName.ToUpperInvariant();
            string map  = string.IsNullOrWhiteSpace(sel.MapName) ? "-" : sel.MapName.ToUpperInvariant();
            string time = FormatElapsedColon(sel.ElapsedSeconds);
            var lines = new[] { $"HERO: {hero}", $"MAP: {map}", $"ELAPSED TIME: {time}" };
            int lineH = Math.Max(text.LineHeight * fs, _rowH);
            int total = lines.Length * lineH;
            int iy = info.y + Math.Max(infoPad, (info.h - total) / 2);
            foreach (var line in lines)
            {
                int lw = text.MeasureWidth(line, fs);
                text.DrawString(vw, vh, line, info.x + info.w - infoPad - lw, iy, parch, fs);
                iy += lineH;
            }
        }

        // Save list. Rows centered vertically inside the frame; tan selection
        // bar under the highlight; clip by _visibleRows with wheel scroll.
        int listPad = Math.Max(0, (_sList.h - _visibleRows * _rowH) / 2);
        int lx = _sList.x + (int)MathF.Round(8 * s);
        int lw2 = _sList.w - (int)MathF.Round(16 * s);
        for (int i = 0; i < _visibleRows; i++)
        {
            int idx = _scrollRow + i;
            if (idx >= _saves.Count) break;
            var slot = _saves[idx];
            int ry = _sList.y + listPad + i * _rowH;
            if (idx == _selected)
                bars.DrawRect(vw, vh, _sList.x + (int)MathF.Round(3 * s), ry,
                              _sList.w - (int)MathF.Round(6 * s), _rowH,
                              new Vector4(0.52f, 0.42f, 0.22f, 0.92f));
            string label = RowLabel(slot);
            int rowTextH = text.LineHeight * fs;
            text.DrawString(vw, vh, Truncate(text, label, lw2, fs), lx,
                            ry + Math.Max(0, (_rowH - rowTextH) / 2),
                            idx == _selected ? new Vector4(0.12f, 0.09f, 0.04f, 1f) : parch, fs);
        }
        // Scroll affordance — carets when there's overflow (in-game pose; the
        // frontend pose uses the ◄► selectors instead).
        if (!MainMenuStyle && _saves.Count > _visibleRows)
        {
            int ax = _sList.x + _sList.w - (int)MathF.Round(14 * s);
            if (_scrollRow > 0)
                text.DrawString(vw, vh, "^", ax, _sList.y + (int)MathF.Round(2 * s), dim, fs);
            if (_scrollRow < _saves.Count - _visibleRows)
                text.DrawString(vw, vh, "v", ax, _sList.y + _sList.h - _rowH, dim, fs);
        }

        // Frontend pose: ◄► list selectors flanking the list.
        if (MainMenuStyle)
        {
            int arScale = Math.Max(1, (int)MathF.Round(s * 1.6f));
            DrawArrow(text, vw, vh, _sArrowL, "<", _hover == Btn.ArrowUp || _pressed == Btn.ArrowUp, arScale, parch, gold);
            DrawArrow(text, vw, vh, _sArrowR, ">", _hover == Btn.ArrowDown || _pressed == Btn.ArrowDown, arScale, parch, gold);
        }

        // Buttons.
        bool canAct = Selected is not null;
        DrawButton(bars, text, icons, guiTex, vw, vh, _sLoad,   "Load",   Btn.Load,   canAct, fs);
        DrawButton(bars, text, icons, guiTex, vw, vh, _sDelete, "Delete", Btn.Delete, canAct, fs);
        if (!MainMenuStyle)
            DrawButton(bars, text, icons, guiTex, vw, vh, _sCancel, "Cancel", Btn.Cancel, true, fs);
    }

    private void EnsureThumb(GL gl)
    {
        if (Selected is not { } sel) { InvalidateThumb(); return; }
        if (_thumbForPath == sel.Path) return; // already uploaded (or known-empty)
        _thumbTex?.Dispose();
        _thumbTex = null;
        _thumbForPath = sel.Path;
        if (ThumbnailCodec.TryDecode(sel.Thumbnail, out int w, out int h, out var rgba))
        {
            try { _thumbTex = new GlTexture(gl, rgba, w, h, nearestFilter: false); }
            catch { _thumbTex = null; }
        }
    }

    private static void DrawArrow(TextRenderer text, int vw, int vh, (int x, int y, int w, int h) r,
                                  string glyph, bool hot, int scale, Vector4 ink, Vector4 hotCol)
    {
        var col = hot ? hotCol : ink;
        int gw = text.MeasureWidth(glyph, scale);
        int gh = text.LineHeight * scale;
        text.DrawString(vw, vh, glyph, r.x + (r.w - gw) / 2, r.y + (r.h - gh) / 2, col, scale);
    }

    private void DrawButton(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                            Func<string, GlTexture?>? guiTex, int vw, int vh,
                            (int x, int y, int w, int h) r, string label, Btn id, bool enabled, int fs)
    {
        var state = !enabled ? ButtonChrome.State.Up
                  : _pressed == id ? ButtonChrome.State.Down
                  : _hover == id ? ButtonChrome.State.Hover
                                 : ButtonChrome.State.Up;
        var tint = enabled ? Vector4.One : new Vector4(0.55f, 0.55f, 0.55f, 1f);
        bool chrome = ButtonChrome.Draw(icons, guiTex, vw, vh, r.x, r.y, r.w, r.h, "button4", state, tint);
        if (!chrome)
        {
            bars.DrawRect(vw, vh, r.x, r.y, r.w, r.h, new Vector4(0.14f, 0.12f, 0.08f, 0.9f));
            bars.DrawBorder(vw, vh, r.x, r.y, r.w, r.h, new Vector4(0.4f, 0.36f, 0.28f, 1f));
        }
        var inkc = !enabled ? new Vector4(0.55f, 0.52f, 0.46f, 1f)
                 : _hover == id ? new Vector4(1f, 0.96f, 0.85f, 1f)
                                : new Vector4(0.88f, 0.82f, 0.70f, 1f);
        int lw = text.MeasureWidth(label, fs);
        int fh = text.LineHeight * fs;
        text.DrawString(vw, vh, label, r.x + (r.w - lw) / 2,
                        r.y + (r.h - fh) / 2 + (_pressed == id ? fs : 0), inkc, fs);
    }

    private static string RowLabel(SaveStore.SaveSlot slot)
        => (slot.IsQuicksave ? "[QUICKSAVE]" : slot.DisplayName).ToUpperInvariant();

    // Ref-rect → screen-rect (Layout caches the hot ones; this serves the cold
    // decorative frames).
    private static (int x, int y, int w, int h) Scr((int x0, int y0, int x1, int y1) r, int vw, int vh)
    {
        float s = HudScale.Modal(vw, vh);
        int ox = (int)MathF.Round((vw - RefW * s) / 2f);
        int oy = (int)MathF.Round((vh - RefH * s) / 2f);
        return (ox + (int)MathF.Round(r.x0 * s), oy + (int)MathF.Round(r.y0 * s),
                (int)MathF.Round((r.x1 - r.x0) * s), (int)MathF.Round((r.y1 - r.y0) * s));
    }

    /// <summary>h:mm:ss like DS1's info box ("0:13:16").</summary>
    private static string FormatElapsedColon(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
    }

    private static string Truncate(TextRenderer text, string s, int maxW, int fs)
    {
        if (text.MeasureWidth(s, fs) <= maxW) return s;
        while (s.Length > 1 && text.MeasureWidth(s + "...", fs) > maxW) s = s[..^1];
        return s + "...";
    }

    public void Dispose() => InvalidateThumb();
}
