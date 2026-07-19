using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using SiegeFX.Core.Assets;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// DS1's Adventurer's Handbook — <c>/ui/interfaces/backend/world_tip/
/// world_tip.gas</c>. A modal cpbox panel that pauses the game and shows one
/// tip page: a title bar, up to four icon+text bullet rows, a "Tip: N of M"
/// counter, a "Disable tips" checkbox, and the bottom button row (Resume, or
/// Previous/Next/Resume in F12 recall mode) plus a corner close X.
///
/// <para>Auto-pop mode (as the player progresses) shows a single tip with a
/// centered Resume; browse mode (F12) adds Previous/Next so the player can page
/// the whole set. Tip content comes from <see cref="WorldTip"/> loaded off the
/// map tank — nothing is embedded here.</para>
///
/// <para>Bullet text may carry DS1's <c>&lt;c:0xAARRGGBB&gt;…&lt;/c&gt;</c>
/// inline color markup (the green key-name highlights); <see cref="ParseRuns"/>
/// turns it into colored word runs and <see cref="DrawWrapped"/> word-wraps
/// them across the row.</para>
/// </summary>
public sealed class HandbookPanel
{
    public bool IsOpen { get; private set; }
    public bool BrowseMode { get; private set; }
    public int CurrentIndex { get; private set; }
    public bool Disabled { get; set; }
    public int Count => _tips.Count;

    public enum Result { None, Close, ToggledDisable }

    private const int RefW = 640, RefH = 480;
    private IReadOnlyList<WorldTip> _tips = System.Array.Empty<WorldTip>();

    // Authored 640×480 rects from world_tip.gas (x0,y0,x1,y1).
    private static readonly (int, int, int, int) RPanel    = (105,  55, 535, 425);
    private static readonly (int, int, int, int) RTitleBar = (114,  62, 508,  90);
    private static readonly (int, int, int, int) RTextBg   = (114,  92, 526, 377);
    // Per-bullet icon + text rects, rows 1..4.
    private static readonly (int, int, int, int)[] RIcon =
    {
        (124, 112, 156, 144), (124, 183, 156, 215), (124, 252, 156, 284), (124, 320, 156, 352),
    };
    private static readonly (int, int, int, int)[] RText =
    {
        (160, 100, 520, 160), (160, 169, 520, 229), (160, 238, 520, 298), (160, 307, 520, 367),
    };
    private static readonly (int, int, int, int) RPrev    = (114, 382, 214, 398);
    private static readonly (int, int, int, int) RNext    = (223, 382, 323, 398);
    private static readonly (int, int, int, int) RResumeC = (270, 382, 370, 398); // centered (auto mode)
    private static readonly (int, int, int, int) RResumeR = (425, 382, 525, 398); // right (browse mode)
    private static readonly (int, int, int, int) RClose   = (516,  57, 532,  73); // corner X
    private static readonly (int, int, int, int) RCheck   = (115, 406, 131, 418);
    private static readonly (int, int, int, int) RDisable = (134, 405, 234, 419); // "Disable tips" text
    private static readonly (int, int, int, int) RTipNum  = (257, 405, 357, 419);

    private enum Hit { None, Prev, Next, Resume, Close, DisableToggle }
    private Hit _pressed = Hit.None, _hover = Hit.None;

    // Cached screen-space button rects (recomputed in Layout; hit-tests read).
    private (int x, int y, int w, int h) _sPrev, _sNext, _sResumeC, _sResumeR, _sClose, _sCheck, _sDisable;

    public void SetTips(IReadOnlyList<WorldTip> tips) => _tips = tips ?? System.Array.Empty<WorldTip>();

    /// <summary>Auto-pop a single tip as the player advances (no Prev/Next).</summary>
    public void OpenAuto(int index)
    {
        if (_tips.Count == 0) return;
        CurrentIndex = Math.Clamp(index, 0, _tips.Count - 1);
        BrowseMode = false;
        _pressed = _hover = Hit.None;
        IsOpen = true;
    }

    /// <summary>F12 recall — open in browse mode with Prev/Next.</summary>
    public void OpenBrowse(int index)
    {
        if (_tips.Count == 0) return;
        CurrentIndex = Math.Clamp(index, 0, _tips.Count - 1);
        BrowseMode = true;
        _pressed = _hover = Hit.None;
        IsOpen = true;
    }

    // SC-DEFEAT-TIP — event-driven filtered tips (the authored [world_tips]
    // defeat_tip) show once, outside the ordered set: no Prev/Next, no
    // "Tip N of M" footer, and closing never advances the auto cadence.
    private WorldTip? _oneOff;
    public bool IsOneOff => _oneOff is not null;
    public void OpenOneOff(WorldTip tip)
    {
        _oneOff = tip;
        BrowseMode = false;
        _pressed = _hover = Hit.None;
        IsOpen = true;
    }

    public void Close() { IsOpen = false; _oneOff = null; _pressed = Hit.None; }

    public void Next() { if (CurrentIndex < _tips.Count - 1) CurrentIndex++; }
    public void Prev() { if (CurrentIndex > 0) CurrentIndex--; }

    // ---- layout / input ----------------------------------------------------

    private void Layout(int vw, int vh)
    {
        float s = HudScale.Modal(vw, vh);
        int ox = (int)MathF.Round((vw - RefW * s) / 2f);
        int oy = (int)MathF.Round((vh - RefH * s) / 2f);
        (int, int, int, int) Scr((int x0, int y0, int x1, int y1) r) => (
            ox + (int)MathF.Round(r.x0 * s), oy + (int)MathF.Round(r.y0 * s),
            (int)MathF.Round((r.x1 - r.x0) * s), (int)MathF.Round((r.y1 - r.y0) * s));
        _sPrev    = Scr(RPrev);
        _sNext    = Scr(RNext);
        _sResumeC = Scr(RResumeC);
        _sResumeR = Scr(RResumeR);
        _sClose   = Scr(RClose);
        _sCheck   = Scr(RCheck);
        _sDisable = Scr(RDisable);
    }

    public void OnMouseMove(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return;
        Layout(vw, vh);
        _hover = HitTest(px, py);
    }

    public bool OnMouseDown(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return false;
        Layout(vw, vh);
        _pressed = HitTest(px, py);
        return true;
    }

    public Result OnMouseUp(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return Result.None;
        Layout(vw, vh);
        var up = HitTest(px, py);
        var was = _pressed;
        _pressed = Hit.None;
        if (up == Hit.None || up != was) return Result.None;
        switch (up)
        {
            case Hit.Prev:  Prev(); return Result.None;
            case Hit.Next:  Next(); return Result.None;
            case Hit.Close:
            case Hit.Resume: return Result.Close;
            case Hit.DisableToggle:
                Disabled = !Disabled;
                return Result.ToggledDisable;
            default: return Result.None;
        }
    }

    private Hit HitTest(int px, int py)
    {
        if (In(px, py, _sClose)) return Hit.Close;
        if (In(px, py, _sCheck) || In(px, py, _sDisable)) return Hit.DisableToggle;
        if (BrowseMode)
        {
            if (In(px, py, _sPrev) && CurrentIndex > 0) return Hit.Prev;
            if (In(px, py, _sNext) && CurrentIndex < _tips.Count - 1) return Hit.Next;
            if (In(px, py, _sResumeR)) return Hit.Resume;
        }
        else if (In(px, py, _sResumeC)) return Hit.Resume;
        return Hit.None;
    }

    private static bool In(int px, int py, (int x, int y, int w, int h) r)
        => px >= r.x && px < r.x + r.w && py >= r.y && py < r.y + r.h;

    // ---- draw --------------------------------------------------------------

    public void Draw(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                     Func<string, GlTexture?>? guiTex, Func<string, GlTexture?>? commonChrome,
                     int vw, int vh)
    {
        if (!IsOpen || (_tips.Count == 0 && _oneOff is null)) return;
        Layout(vw, vh);
        float s = HudScale.Modal(vw, vh);
        int fs = Math.Max(1, (int)MathF.Round(s));
        int lineH = text.LineHeight * fs + Math.Max(1, (int)MathF.Round(2 * s));
        // NinePatch (cpbox chrome) needs the COMMON-CHROME resolver — bare
        // family keys ("cpbox_ul") that GetCommonTexture b_gui_cmn_-prefixes.
        // Passing the plain gui resolver was the original "no border" bug.
        bool chrome = icons is not null && commonChrome is not null;

        var parch = new Vector4(0.86f, 0.80f, 0.66f, 1f);
        var gold  = new Vector4(1f, 0.90f, 0.55f, 1f);
        var dim   = new Vector4(0.60f, 0.57f, 0.50f, 1f);

        // DS1's handbook floats over the live world (no full-screen scrim). A
        // dark backing fill under the frame brings the panel to ~25%
        // transparency; inner cpbox boxes stack over it and read as recessed.
        var panelScr = Scr(RPanel, vw, vh);
        bars.DrawRect(vw, vh, panelScr.x, panelScr.y, panelScr.w, panelScr.h,
                      new Vector4(0.03f, 0.03f, 0.04f, 0.55f));
        void Frame((int, int, int, int) refRect)
        {
            var r = Scr(refRect, vw, vh);
            if (chrome) NinePatch.DrawCpbox(icons!, commonChrome!, vw, vh, r.x, r.y, r.w, r.h, Vector4.One);
            else
            {
                bars.DrawRect(vw, vh, r.x, r.y, r.w, r.h, new Vector4(0.07f, 0.07f, 0.08f, 0.9f));
                bars.DrawBorder(vw, vh, r.x, r.y, r.w, r.h, new Vector4(0.45f, 0.42f, 0.34f, 1f));
            }
        }
        Frame(RPanel);
        Frame(RTitleBar);
        Frame(RTextBg);

        // Title.
        var titleBar = Scr(RTitleBar, vw, vh);
        int titleScale = Math.Max(1, (int)MathF.Round(s * 1.16f));
        const string title = "Adventurer's Handbook";
        int tw = text.MeasureWidth(title, titleScale);
        text.DrawString(vw, vh, title, titleBar.x + (titleBar.w - tw) / 2,
                        titleBar.y + (titleBar.h - text.LineHeight * titleScale) / 2, gold, titleScale);

        // Bullets: icon + wrapped text per row.
        var tip = _oneOff ?? _tips[CurrentIndex];
        for (int i = 0; i < RText.Length && i < tip.Bullets.Count; i++)
        {
            var b = tip.Bullets[i];
            var ir = Scr(RIcon[i], vw, vh);
            var tr = Scr(RText[i], vw, vh);
            GlTexture? icon = guiTex?.Invoke(b.IconTexture);
            if (icon is not null && icons is not null)
                icons.DrawIcon(vw, vh, icon, ir.x, ir.y, ir.w, ir.h, Vector4.One);
            else
                bars.DrawRect(vw, vh, ir.x + ir.w / 3, ir.y + ir.h / 3, ir.w / 3, ir.h / 3, parch);
            DrawWrapped(text, vw, vh, b.Text, tr.x, tr.y, tr.w, fs, parch, lineH);
        }

        // Counter, checkbox + label. One-off filtered tips sit outside the
        // ordered set — no "Tip N of M".
        if (_oneOff is null)
        {
            var num = Scr(RTipNum, vw, vh);
            text.DrawString(vw, vh, $"Tip: {CurrentIndex + 1} of {_tips.Count}", num.x, num.y, dim, fs);
        }

        var chk = _sCheck;
        var chkTex = guiTex?.Invoke(Disabled ? "b_gui_cmn_checkbox_x" : "b_gui_cmn_checkbox");
        if (chkTex is not null && icons is not null)
            icons.DrawIcon(vw, vh, chkTex, chk.x, chk.y, chk.w, chk.h, Vector4.One);
        else
        {
            bars.DrawBorder(vw, vh, chk.x, chk.y, chk.w, chk.h, parch);
            if (Disabled) bars.DrawRect(vw, vh, chk.x + chk.w / 4, chk.y + chk.h / 4, chk.w / 2, chk.h / 2, parch);
        }
        var dis = _sDisable;
        text.DrawString(vw, vh, "Disable tips", dis.x, dis.y + (dis.h - text.LineHeight * fs) / 2,
                        _hover == Hit.DisableToggle ? gold : parch, fs);

        // Corner close X.
        var cx = _sClose;
        var xTex = guiTex?.Invoke("b_gui_cmn_button_x_up");
        if (xTex is not null && icons is not null)
            icons.DrawIcon(vw, vh, xTex, cx.x, cx.y, cx.w, cx.h,
                           _hover == Hit.Close ? new Vector4(1f, 1f, 1f, 1f) : new Vector4(0.85f, 0.85f, 0.85f, 1f));
        else
        {
            bars.DrawBorder(vw, vh, cx.x, cx.y, cx.w, cx.h, parch);
            text.DrawString(vw, vh, "x", cx.x + cx.w / 4, cx.y, _hover == Hit.Close ? gold : parch, fs);
        }

        // Bottom button row.
        if (BrowseMode)
        {
            DrawButton(bars, text, icons, guiTex, vw, vh, _sPrev, "Previous Tip", Hit.Prev, CurrentIndex > 0, fs);
            DrawButton(bars, text, icons, guiTex, vw, vh, _sNext, "Next Tip", Hit.Next, CurrentIndex < _tips.Count - 1, fs);
            DrawButton(bars, text, icons, guiTex, vw, vh, _sResumeR, "Resume", Hit.Resume, true, fs);
        }
        else
        {
            DrawButton(bars, text, icons, guiTex, vw, vh, _sResumeC, "Resume", Hit.Resume, true, fs);
        }
    }

    private void DrawButton(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                            Func<string, GlTexture?>? guiTex, int vw, int vh,
                            (int x, int y, int w, int h) r, string label, Hit id, bool enabled, int fs)
    {
        var state = !enabled ? ButtonChrome.State.Up
                  : _pressed == id ? ButtonChrome.State.Down
                  : _hover == id ? ButtonChrome.State.Hover : ButtonChrome.State.Up;
        var tint = enabled ? Vector4.One : new Vector4(0.55f, 0.55f, 0.55f, 1f);
        if (!ButtonChrome.Draw(icons, guiTex, vw, vh, r.x, r.y, r.w, r.h, "button4", state, tint))
        {
            bars.DrawRect(vw, vh, r.x, r.y, r.w, r.h, new Vector4(0.14f, 0.12f, 0.08f, 0.9f));
            bars.DrawBorder(vw, vh, r.x, r.y, r.w, r.h, new Vector4(0.4f, 0.36f, 0.28f, 1f));
        }
        var ink = !enabled ? new Vector4(0.5f, 0.47f, 0.42f, 1f)
                : _hover == id ? new Vector4(1f, 0.96f, 0.85f, 1f) : new Vector4(0.86f, 0.80f, 0.66f, 1f);
        int lw = text.MeasureWidth(label, fs);
        int fh = text.LineHeight * fs;
        text.DrawString(vw, vh, label, r.x + (r.w - lw) / 2,
                        r.y + (r.h - fh) / 2 + (_pressed == id ? fs : 0), ink, fs);
    }

    // ---- text markup + wrapping -------------------------------------------

    // Split raw tip text into colored word tokens, honoring <c:0xAARRGGBB>…</c>
    // markup. `space` = whether real whitespace preceded the word in the source
    // (false = glued to the previous token, e.g. "F12" then "." from
    // "<c:..>F12</c>.").
    private static List<(string word, Vector4 color, bool space)> ParseRuns(string raw, Vector4 def)
    {
        var outp = new List<(string, Vector4, bool)>();
        var cur = new StringBuilder();
        var color = def;
        bool spaceSeen = false;
        void Flush()
        {
            if (cur.Length == 0) return;
            outp.Add((cur.ToString(), color, spaceSeen));
            cur.Clear();
            spaceSeen = false;
        }
        int i = 0;
        while (i < raw.Length)
        {
            char c = raw[i];
            if (c == '<')
            {
                int close = raw.IndexOf('>', i);
                if (close < 0) { cur.Append(c); i++; continue; }
                string tag = raw.Substring(i + 1, close - i - 1).Trim();
                Flush();
                if (tag.StartsWith("c:", StringComparison.OrdinalIgnoreCase))
                    color = ParseColor(tag[2..], def);
                else if (tag.Equals("/c", StringComparison.OrdinalIgnoreCase))
                    color = def;
                i = close + 1;
                continue;
            }
            if (char.IsWhiteSpace(c)) { Flush(); spaceSeen = true; i++; continue; }
            cur.Append(c);
            i++;
        }
        Flush();
        return outp;
    }

    // "0xAARRGGBB" (or "0xRRGGBB") -> RGBA vector. Falls back to `def` on parse
    // failure. DS1's tips use 0xff00ff00 (opaque green) for key-name highlights.
    private static Vector4 ParseColor(string hex, Vector4 def)
    {
        hex = hex.Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
        if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var v)) return def;
        float a, r, g, b;
        if (hex.Length > 6)
        {
            a = ((v >> 24) & 0xFF) / 255f;
            r = ((v >> 16) & 0xFF) / 255f;
            g = ((v >> 8) & 0xFF) / 255f;
            b = (v & 0xFF) / 255f;
        }
        else { a = 1f; r = ((v >> 16) & 0xFF) / 255f; g = ((v >> 8) & 0xFF) / 255f; b = (v & 0xFF) / 255f; }
        return new Vector4(r, g, b, a);
    }

    private static void DrawWrapped(TextRenderer text, int vw, int vh, string raw,
                                    int x, int y, int maxW, int fs, Vector4 def, int lineH)
    {
        var runs = ParseRuns(raw, def);
        int spaceW = text.MeasureWidth(" ", fs);
        if (spaceW <= 0) spaceW = Math.Max(1, text.LineHeight * fs / 3);
        int cx = x, cy = y;
        bool firstOnLine = true;
        foreach (var (word, color, space) in runs)
        {
            int ww = text.MeasureWidth(word, fs);
            int pre = (!firstOnLine && space) ? spaceW : 0;
            if (!firstOnLine && cx + pre + ww > x + maxW)
            {
                cx = x; cy += lineH; firstOnLine = true; pre = 0;
            }
            cx += pre;
            text.DrawString(vw, vh, word, cx, cy, color, fs);
            cx += ww;
            firstOnLine = false;
        }
    }

    private static (int x, int y, int w, int h) Scr((int x0, int y0, int x1, int y1) r, int vw, int vh)
    {
        float s = HudScale.Modal(vw, vh);
        int ox = (int)MathF.Round((vw - RefW * s) / 2f);
        int oy = (int)MathF.Round((vh - RefH * s) / 2f);
        return (ox + (int)MathF.Round(r.x0 * s), oy + (int)MathF.Round(r.y0 * s),
                (int)MathF.Round((r.x1 - r.x0) * s), (int)MathF.Round((r.y1 - r.y0) * s));
    }
}
