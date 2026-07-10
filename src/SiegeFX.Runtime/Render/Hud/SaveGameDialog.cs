using System;
using System.Collections.Generic;
using System.Numerics;
using SiegeFX.Core.Save;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// DS1's Save Game window — <c>/ui/interfaces/backend/loadsave_game/
/// loadsave_game.gas</c>. A modal, screen-centered cpbox panel: a title, a
/// preview + description pair up top, a scrollable list of existing saves, a
/// name edit box (pre-filled with today's date), and Save / Delete / Cancel
/// buttons. Authored to the gas 640×480 reference rects, uniformly scaled by
/// the shared <see cref="HudScale.Modal"/> factor and centered, so it tracks
/// the UI-scale knob exactly like every other panel.
///
/// <para>The host drives it: <see cref="Open"/> with the current save list +
/// default name, feed <see cref="OnChar"/> for the edit box, route mouse
/// events, and act on the <see cref="Result"/> from <see cref="OnMouseUp"/>.
/// The dialog owns no save logic — Save/Delete return the intent and the host
/// performs the <see cref="SaveStore"/> call.</para>
/// </summary>
public sealed class SaveGameDialog
{
    public bool IsOpen { get; private set; }

    public enum Result { None, Save, Delete, Cancel }

    private const int RefW = 640, RefH = 480;

    // Authored 640×480 rects (x0,y0,x1,y1) from loadsave_game.gas.
    private static readonly (int x0, int y0, int x1, int y1) RPanel   = (150,  56, 492, 430);
    private static readonly (int x0, int y0, int x1, int y1) RTitle   = (246,  72, 388, 101);
    private static readonly (int x0, int y0, int x1, int y1) RPreview = (171, 109, 258, 181);
    private static readonly (int x0, int y0, int x1, int y1) RDesc    = (258, 109, 467, 181);
    private static readonly (int x0, int y0, int x1, int y1) RList    = (171, 187, 467, 349);
    private static readonly (int x0, int y0, int x1, int y1) REdit    = (171, 353, 467, 377);
    private static readonly (int x0, int y0, int x1, int y1) RSave    = (171, 388, 263, 404);
    private static readonly (int x0, int y0, int x1, int y1) RDelete  = (273, 388, 365, 404);
    private static readonly (int x0, int y0, int x1, int y1) RCancel  = (375, 388, 467, 404);

    private readonly List<SaveStore.SaveSlot> _saves = new();
    private string _name = "";
    private int _selected = -1;   // index into _saves; -1 = none (Delete disabled)
    private int _scrollRow;
    private float _caret;         // blink timer

    // Per-frame screen-space rects (recomputed in Layout). Mouse hit-tests read
    // these; Draw writes them. All handlers call Layout first so a resize between
    // a draw and a click can't stale the geometry.
    private (int x, int y, int w, int h) _sPanel, _sList, _sEdit, _sSave, _sDelete, _sCancel;
    private int _rowH, _visibleRows;

    private enum Btn { None, Save, Delete, Cancel }
    private Btn _pressed = Btn.None;
    private Btn _hover = Btn.None;

    /// <summary>The player-typed save label (never null).</summary>
    public string NameText => _name;

    /// <summary>The highlighted existing save, or null when nothing is
    /// selected — Delete acts on this.</summary>
    public SaveStore.SaveSlot? Selected =>
        _selected >= 0 && _selected < _saves.Count ? _saves[_selected] : null;

    /// <summary>Open the dialog against the current on-disk save list, with the
    /// name box pre-filled (DS1 defaults it to the day's date).</summary>
    public void Open(IReadOnlyList<SaveStore.SaveSlot> saves, string defaultName)
    {
        _saves.Clear();
        _saves.AddRange(saves);
        _name = defaultName ?? "";
        _selected = -1;
        _scrollRow = 0;
        _caret = 0f;
        _pressed = Btn.None;
        _hover = Btn.None;
        IsOpen = true;
    }

    public void Close() { IsOpen = false; _pressed = Btn.None; }

    public void Tick(float dt) { if (IsOpen) _caret += dt; }

    /// <summary>Append/erase in the name box. Mirrors the character creator's
    /// edit-box rule (printable ASCII, DS1's excluded set) but allows a longer
    /// label than a 14-char hero name.</summary>
    public void OnChar(char c)
    {
        if (!IsOpen) return;
        if (c == '\b') { if (_name.Length > 0) _name = _name[..^1]; return; }
        if (c < ' ' || c > '~') return;
        if ("<>:/\\|?*%\"".IndexOf(c) >= 0) return; // filename-hostile + gas-hostile
        if (_name.Length >= 30) return;
        _name += c;
    }

    // ---- layout ------------------------------------------------------------

    private void Layout(int vw, int vh)
    {
        float s = HudScale.Modal(vw, vh);
        int ox = (int)MathF.Round((vw - RefW * s) / 2f);
        int oy = (int)MathF.Round((vh - RefH * s) / 2f);
        (int, int, int, int) Scr((int x0, int y0, int x1, int y1) r) => (
            ox + (int)MathF.Round(r.x0 * s),
            oy + (int)MathF.Round(r.y0 * s),
            (int)MathF.Round((r.x1 - r.x0) * s),
            (int)MathF.Round((r.y1 - r.y0) * s));

        _sPanel  = Scr(RPanel);
        _sList   = Scr(RList);
        _sEdit   = Scr(REdit);
        _sSave   = Scr(RSave);
        _sDelete = Scr(RDelete);
        _sCancel = Scr(RCancel);

        _rowH = Math.Max(1, (int)MathF.Round(15 * s));
        // Inset the list interior a hair so rows don't kiss the frame.
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
    /// press, selects a list row, or does nothing.</summary>
    public bool OnMouseDown(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return false;
        Layout(vw, vh);
        _pressed = HitButton(px, py);
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
            Btn.Save   => Result.Save,
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

    private Btn HitButton(int px, int py)
    {
        if (In(px, py, _sSave))   return Btn.Save;
        if (In(px, py, _sDelete)) return Btn.Delete;
        if (In(px, py, _sCancel)) return Btn.Cancel;
        return Btn.None;
    }

    private void TrySelectRow(int px, int py)
    {
        if (!In(px, py, _sList)) return;
        int pad = Math.Max(0, (_sList.h - _visibleRows * _rowH) / 2);
        int rel = py - (_sList.y + pad);
        if (rel < 0) return;
        int row = rel / _rowH + _scrollRow;
        if (row >= 0 && row < _saves.Count)
        {
            _selected = row;
            // Clicking a save copies its label into the name box, matching DS1
            // (overwrite-in-place; the timestamp is re-appended on save).
            _name = _saves[row].DisplayName;
        }
    }

    private static bool In(int px, int py, (int x, int y, int w, int h) r)
        => px >= r.x && px < r.x + r.w && py >= r.y && py < r.y + r.h;

    // ---- draw --------------------------------------------------------------

    public void Draw(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                     Func<string, GlTexture?>? guiTex, int vw, int vh)
    {
        if (!IsOpen) return;
        Layout(vw, vh);
        float s = HudScale.Modal(vw, vh);
        int fs = Math.Max(1, (int)MathF.Round(s));

        // Modal scrim — same 55% black the pause/options menus use.
        bars.DrawRect(vw, vh, 0, 0, vw, vh, new Vector4(0f, 0f, 0f, 0.55f));

        var parch = new Vector4(0.88f, 0.82f, 0.70f, 1f);
        var gold  = new Vector4(1f, 0.90f, 0.55f, 1f);
        var dim   = new Vector4(0.55f, 0.52f, 0.46f, 1f);

        // Panel + inner frames. cpbox chrome if the raws resolve; flat fallback
        // keeps the dialog usable headless / with missing art.
        bool chrome = icons is not null && guiTex is not null;
        void Frame((int x, int y, int w, int h) r, float fill)
        {
            if (chrome)
                NinePatch.DrawCpbox(icons!, guiTex!, vw, vh, r.x, r.y, r.w, r.h, Vector4.One);
            else
            {
                bars.DrawRect(vw, vh, r.x, r.y, r.w, r.h, new Vector4(0.06f, 0.06f, 0.07f, fill));
                bars.DrawBorder(vw, vh, r.x, r.y, r.w, r.h, new Vector4(0.35f, 0.33f, 0.28f, 1f));
            }
        }

        Frame(_sPanel, 0.92f);
        Frame(Scr(RPreview, vw, vh), 0.92f);
        Frame(Scr(RDesc, vw, vh), 0.92f);
        Frame(_sList, 0.92f);
        Frame(_sEdit, 0.92f);

        // Title — one size up (14p), gold, centered over the panel.
        int titleScale = Math.Max(1, (int)MathF.Round(s * 1.16f));
        const string title = "SAVE GAME";
        int tw = text.MeasureWidth(title, titleScale);
        text.DrawString(vw, vh, title, _sPanel.x + (_sPanel.w - tw) / 2,
                        Scr(RTitle, vw, vh).y, gold, titleScale);

        // Description box — details for the highlighted save (region + time),
        // or a hint. DS1 shows the save's screenshot here; we show its metadata.
        var desc = Scr(RDesc, vw, vh);
        int descPad = (int)MathF.Round(6 * s);
        if (Selected is { } sel)
        {
            text.DrawString(vw, vh, Truncate(text, sel.DisplayName, desc.w - descPad * 2, fs),
                            desc.x + descPad, desc.y + descPad, parch, fs);
            text.DrawString(vw, vh, sel.SavedAt.ToLocalTime().ToString("MMM d, yyyy  h:mm tt"),
                            desc.x + descPad, desc.y + descPad + _rowH, dim, fs);
            string region = ShortRegion(sel.RegionPath);
            if (region.Length > 0)
                text.DrawString(vw, vh, Truncate(text, region, desc.w - descPad * 2, fs),
                                desc.x + descPad, desc.y + descPad + _rowH * 2, dim, fs);
        }
        else
        {
            text.DrawString(vw, vh, "Type a name and", desc.x + descPad, desc.y + descPad, dim, fs);
            text.DrawString(vw, vh, "click Save.", desc.x + descPad, desc.y + descPad + _rowH, dim, fs);
        }

        // Save list. Rows centered vertically inside the frame; selection
        // highlight; clip by _visibleRows with wheel scroll.
        int listPad = Math.Max(0, (_sList.h - _visibleRows * _rowH) / 2);
        int lx = _sList.x + (int)MathF.Round(8 * s);
        int lw = _sList.w - (int)MathF.Round(16 * s);
        for (int i = 0; i < _visibleRows; i++)
        {
            int idx = _scrollRow + i;
            if (idx >= _saves.Count) break;
            var slot = _saves[idx];
            int ry = _sList.y + listPad + i * _rowH;
            if (idx == _selected)
                bars.DrawRect(vw, vh, _sList.x + (int)MathF.Round(3 * s), ry,
                              _sList.w - (int)MathF.Round(6 * s), _rowH,
                              new Vector4(0.30f, 0.20f, 0.08f, 0.85f));
            string row = $"{slot.DisplayName} ({slot.SavedAt.ToLocalTime():M/d h:mmtt})";
            int rowTextH = text.LineHeight * fs;
            text.DrawString(vw, vh, Truncate(text, row, lw, fs), lx,
                            ry + Math.Max(0, (_rowH - rowTextH) / 2),
                            idx == _selected ? gold : parch, fs);
        }
        // Scroll affordance — up/down carets when there's overflow.
        if (_saves.Count > _visibleRows)
        {
            var arrowCol = dim;
            int ax = _sList.x + _sList.w - (int)MathF.Round(14 * s);
            if (_scrollRow > 0)
                text.DrawString(vw, vh, "^", ax, _sList.y + (int)MathF.Round(2 * s), arrowCol, fs);
            if (_scrollRow < _saves.Count - _visibleRows)
                text.DrawString(vw, vh, "v", ax, _sList.y + _sList.h - _rowH, arrowCol, fs);
        }

        // Edit box — the typed name + a blinking caret.
        int ePad = (int)MathF.Round(6 * s);
        string shown = _name;
        int caretX = _sEdit.x + ePad + text.MeasureWidth(shown, fs);
        int ey = _sEdit.y + (_sEdit.h - text.LineHeight * fs) / 2;
        text.DrawString(vw, vh, shown, _sEdit.x + ePad, ey, parch, fs);
        if (((int)(_caret * 2)) % 2 == 0)
            bars.DrawRect(vw, vh, caretX + (int)MathF.Round(1 * s), ey,
                          Math.Max(1, (int)MathF.Round(1.5f * s)), text.LineHeight * fs, parch);

        // Buttons.
        DrawButton(bars, text, icons, guiTex, vw, vh, _sSave,   "Save",   Btn.Save,   true,  fs);
        DrawButton(bars, text, icons, guiTex, vw, vh, _sDelete, "Delete", Btn.Delete, Selected is not null, fs);
        DrawButton(bars, text, icons, guiTex, vw, vh, _sCancel, "Cancel", Btn.Cancel, true,  fs);
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
        var ink = !enabled ? new Vector4(0.55f, 0.52f, 0.46f, 1f)
                : _hover == id ? new Vector4(1f, 0.96f, 0.85f, 1f)
                               : new Vector4(0.88f, 0.82f, 0.70f, 1f);
        int lw = text.MeasureWidth(label, fs);
        int fh = text.LineHeight * fs;
        text.DrawString(vw, vh, label, r.x + (r.w - lw) / 2,
                        r.y + (r.h - fh) / 2 + (_pressed == id ? fs : 0), ink, fs);
    }

    // Ref-rect → screen-rect, standalone (Layout caches the hot ones; this
    // serves the cold decorative frames).
    private static (int x, int y, int w, int h) Scr((int x0, int y0, int x1, int y1) r, int vw, int vh)
    {
        float s = HudScale.Modal(vw, vh);
        int ox = (int)MathF.Round((vw - RefW * s) / 2f);
        int oy = (int)MathF.Round((vh - RefH * s) / 2f);
        return (ox + (int)MathF.Round(r.x0 * s), oy + (int)MathF.Round(r.y0 * s),
                (int)MathF.Round((r.x1 - r.x0) * s), (int)MathF.Round((r.y1 - r.y0) * s));
    }

    private static string ShortRegion(string regionPath)
    {
        if (string.IsNullOrEmpty(regionPath)) return "";
        int i = regionPath.LastIndexOf('/');
        return i >= 0 && i + 1 < regionPath.Length ? regionPath[(i + 1)..] : regionPath;
    }

    private static string Truncate(TextRenderer text, string s, int maxW, int fs)
    {
        if (text.MeasureWidth(s, fs) <= maxW) return s;
        while (s.Length > 1 && text.MeasureWidth(s + "...", fs) > maxW) s = s[..^1];
        return s + "...";
    }
}
