using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>Draws the original's interface elements by their authored values:
/// rects in the 800x600 frame the multiplayer screens are authored in (scaled
/// and centered like every frontend screen), dialog boxes by common template
/// (cpbox, cpbox_wide, cpbox_thin, cpbox_thin_dark), button_4 buttons, the
/// left/right/up/down arrow buttons, textured windows, the selection bar and
/// text in the authored copperplate sizes. Shared by the staging area and the
/// co-op character screens.</summary>
internal sealed class AuthoredFrame
{
    public enum Justify { Left, Center, Right }
    public enum Press { Up, Hover, Down }

    public static readonly Vector4 White = Vector4.One;
    public static readonly Vector4 Green = new(0f, 1f, 0f, 1f);                 // font_color 0xff00ff00
    public static readonly Vector4 Disabled = new(0.33f, 0.33f, 0.33f, 0.6f);   // disable_color 0x99555555
    public static readonly Vector4 Faint = new(0.72f, 0.70f, 0.64f, 1f);

    public float Scale = 1f;
    public int Dx, Dy, Fs = 1, Vw, Vh;
    IconRenderer? _icons;
    BarRenderer? _bars;
    TextRenderer? _text;
    Func<string, GlTexture?>? _tex, _common;

    public void Layout(int vw, int vh)
    {
        Vw = vw; Vh = vh;
        Scale = MathF.Min(vh / 600f, vw / 800f);
        Dx = (vw - (int)MathF.Round(800 * Scale)) / 2;
        Dy = (vh - (int)MathF.Round(600 * Scale)) / 2;
        Fs = Math.Max(1, (int)MathF.Round(Scale));
    }

    public void Begin(int vw, int vh, IconRenderer icons, BarRenderer bars, TextRenderer text,
                      Func<string, GlTexture?> tex, Func<string, GlTexture?> common)
    {
        Layout(vw, vh);
        _icons = icons; _bars = bars; _text = text; _tex = tex; _common = common;
    }

    public (int X, int Y, int W, int H) S(int x0, int y0, int x1, int y1) => (
        Dx + (int)MathF.Round(x0 * Scale), Dy + (int)MathF.Round(y0 * Scale),
        (int)MathF.Round((x1 - x0) * Scale), (int)MathF.Round((y1 - y0) * Scale));

    public bool Hit(int x0, int y0, int x1, int y1, int px, int py)
    {
        var r = S(x0, y0, x1, y1);
        return px >= r.X && px < r.X + r.W && py >= r.Y && py < r.Y + r.H;
    }

    /// <summary>A dialog_box by its common_template.</summary>
    public void Box(int x0, int y0, int x1, int y1, string template = "cpbox", float alpha = 1f)
    {
        if (_icons is null || _common is null) return;
        var r = S(x0, y0, x1, y1);
        var tint = new Vector4(1f, 1f, 1f, alpha);
        switch (template)
        {
            case "cpbox_wide": NinePatch.DrawCpboxWide(_icons, _common, Vw, Vh, r.X, r.Y, r.W, r.H, tint); break;
            case "cpbox_thin": NinePatch.DrawCpboxThin(_icons, _common, Vw, Vh, r.X, r.Y, r.W, r.H, tint); break;
            case "cpbox_thin_dark": NinePatch.DrawCpboxThinDark(_icons, _common, Vw, Vh, r.X, r.Y, r.W, r.H, tint); break;
            default: NinePatch.DrawCpbox(_icons, _common, Vw, Vh, r.X, r.Y, r.W, r.H, tint); break;
        }
    }

    /// <summary>A textured window (uvcoords as the gas authors them: u0,v0,u1,v1 bottom-up).</summary>
    public void Window(int x0, int y0, int x1, int y1, string texture, float alpha = 1f,
                       float u0 = 0f, float v0 = 0f, float u1 = 1f, float v1 = 1f)
    {
        if (_icons is null || _tex?.Invoke(texture) is not { } t) return;
        var r = S(x0, y0, x1, y1);
        _icons.DrawIcon(Vw, Vh, t, r.X, r.Y, r.W, r.H, new Vector4(1f, 1f, 1f, alpha), u0, 1f - v1, u1, 1f - v0);
    }

    public void Window(int x0, int y0, int x1, int y1, GlTexture? t, float alpha = 1f)
    {
        if (_icons is null || t is null) return;
        var r = S(x0, y0, x1, y1);
        _icons.DrawIcon(Vw, Vh, t, r.X, r.Y, r.W, r.H, new Vector4(1f, 1f, 1f, alpha));
    }

    /// <summary>The common selection bar (selection_box texture b_gui_cmn_selection).</summary>
    public void Selection(int x0, int y0, int x1, int y1, float alpha)
    {
        if (_common?.Invoke("selection") is { } sel) Window(x0, y0, x1, y1, sel, alpha);
        else if (_bars is not null) { var r = S(x0, y0, x1, y1); _bars.DrawRect(Vw, Vh, r.X, r.Y, r.W, r.H, new Vector4(0.72f, 0.78f, 0.82f, alpha)); }
    }

    public int LineHeight(string font = "") =>
        (_text is null ? 12 : font.Length > 0 ? _text.LineHeightOf(font) : _text.LineHeight) * Fs;

    public int Measure(string s, string font = "") =>
        _text is null ? 0 : (font.Length > 0 ? _text.MeasureWidth(font, s) : _text.MeasureWidth(s)) * Fs;

    /// <summary>Text in a rect, vertically centered, cut to fit with "...".</summary>
    public void Label(int x0, int y0, int x1, int y1, string s, string font = "", Vector4? ink = null,
                      Justify j = Justify.Left)
    {
        if (_text is null || string.IsNullOrEmpty(s)) return;
        var r = S(x0, y0, x1, y1);
        s = Fit(s, r.W, font);
        int w = Measure(s, font), h = LineHeight(font);
        int x = j switch { Justify.Center => r.X + (r.W - w) / 2, Justify.Right => r.X + r.W - w, _ => r.X };
        int y = r.Y + (r.H - h) / 2;
        if (font.Length > 0) _text.DrawString(font, Vw, Vh, s, x, y, ink ?? White, Fs);
        else _text.DrawString(Vw, Vh, s, x, y, ink ?? White, Fs);
    }

    public string Fit(string s, int width, string font = "")
    {
        if (Measure(s, font) <= width) return s;
        while (s.Length > 1 && Measure(s + "...", font) > width) s = s[..^1];
        return s + "...";
    }

    /// <summary>A button_4 push button with its label.</summary>
    public void Button(int x0, int y0, int x1, int y1, string label, Press state, bool enabled = true,
                       Vector4? ink = null, Justify j = Justify.Center)
    {
        var r = S(x0, y0, x1, y1);
        var chrome = enabled ? state switch { Press.Down => ButtonChrome.State.Down, Press.Hover => ButtonChrome.State.Hover, _ => ButtonChrome.State.Up }
                             : ButtonChrome.State.Up;
        bool drawn = ButtonChrome.Draw(_icons, _tex, Vw, Vh, r.X, r.Y, r.W, r.H, "button4", chrome,
                                       enabled ? Vector4.One : new Vector4(0.55f, 0.55f, 0.55f, 1f));
        if (!drawn && _bars is not null) _bars.DrawRect(Vw, Vh, r.X, r.Y, r.W, r.H, new Vector4(0.15f, 0.12f, 0.08f, 0.9f));
        int pad = j == Justify.Center ? 0 : 4;          // reference px
        int down = enabled && state == Press.Down ? 1 : 0;
        Label(x0 + pad, y0 + down, x1 - pad, y1 + down, label, "", enabled ? ink ?? White : Disabled, j);
    }

    /// <summary>The common arrow buttons (common_template left/right/up/down).</summary>
    public void Arrow(int x0, int y0, int x1, int y1, string dir, Press state, bool enabled = true)
    {
        string suf = !enabled ? "up" : state switch { Press.Down => "down", Press.Hover => "hov", _ => "up" };
        Window(x0, y0, x1, y1, $"b_gui_cmn_button_{dir}_{suf}", enabled ? 1f : 0.5f);
    }
}
