using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Stateful pixel-rect button for HUD menus. Owns its bounds + label, tracks
/// hover/press from caller-supplied mouse events, and renders itself via the
/// shared <see cref="BarRenderer"/> + <see cref="TextRenderer"/> pair.
///
/// Pure-data widget — the host (menu, panel, dialog) is responsible for
/// laying buttons out, forwarding mouse moves/clicks, and reacting to
/// <see cref="WasClicked"/> on the frame the click resolves. A click is
/// "press inside the rect" + "release inside the rect" so a drag-off-then-up
/// cancels, matching DS1's UI feel.
/// </summary>
public sealed class MenuButton
{
    public string Label { get; set; }
    public int X      { get; set; }
    public int Y      { get; set; }
    public int Width  { get; set; }
    public int Height { get; set; }

    private bool _hover;
    private bool _pressed;

    /// <summary>Current hover / press state, for callers that render their own
    /// chrome (e.g. the button_5 push-button faces) instead of the flat fill.</summary>
    public bool Hovered => _hover;
    public bool Pressed => _pressed;

    public MenuButton(string label, int x, int y, int width, int height)
    {
        Label  = label;
        X      = x;
        Y      = y;
        Width  = width;
        Height = height;
    }

    public bool HitTest(int px, int py) =>
        px >= X && px < X + Width && py >= Y && py < Y + Height;

    /// <summary>Update the hover flag from the latest cursor position. Call from MouseMove.</summary>
    public void UpdateHover(int px, int py) => _hover = HitTest(px, py);

    /// <summary>Begin a press if the cursor is inside. Call from MouseDown(LMB).
    /// Returns true if the press was captured (consume the click upstream).</summary>
    public bool TryPress(int px, int py)
    {
        if (!HitTest(px, py)) return false;
        _pressed = true;
        return true;
    }

    /// <summary>End a press. Returns true iff this is a "click" (was pressed AND
    /// released inside the rect). Caller invokes the action on true.</summary>
    public bool Release(int px, int py)
    {
        bool clicked = _pressed && HitTest(px, py);
        _pressed = false;
        return clicked;
    }

    /// <summary>Drop press state without firing (e.g. menu closed mid-press).</summary>
    public void CancelPress() => _pressed = false;

    public void Draw(BarRenderer bars, TextRenderer text, int viewportW, int viewportH)
        => Draw(bars, text, null, null, viewportW, viewportH);

    /// <summary>ALPHA-2H — DS1 draws every in-panel button with the same
    /// authored button_4 3-slice chrome (user: "every button in Dungeon
    /// Siege essentially looks the same"). Callers that can resolve GUI
    /// textures pass them; the flat bevel remains the diagnostics fallback.</summary>
    public void Draw(BarRenderer bars, TextRenderer text,
                     IconRenderer? icons, System.Func<string, GlTexture?>? guiTex,
                     int viewportW, int viewportH)
    {
        Vector4 ink = _hover ? new Vector4(1f, 0.96f, 0.85f, 1f)
                             : new Vector4(0.88f, 0.82f, 0.70f, 1f);

        var state = _pressed ? ButtonChrome.State.Down
                  : _hover   ? ButtonChrome.State.Hover
                             : ButtonChrome.State.Up;
        if (!ButtonChrome.Draw(icons, guiTex, viewportW, viewportH, X, Y, Width, Height, "button4", state))
        {
            // Three states with subtly different fill + border. Pressed gets a
            // 1px nudge so the label visibly "sinks" — DS1 buttons do the same.
            Vector4 fill = _pressed ? new Vector4(0.20f, 0.16f, 0.10f, 1f)
                         : _hover   ? new Vector4(0.26f, 0.21f, 0.14f, 1f)
                                    : new Vector4(0.16f, 0.13f, 0.10f, 1f);
            Vector4 border = _hover  ? new Vector4(0.92f, 0.78f, 0.50f, 1f)
                                     : new Vector4(0.60f, 0.50f, 0.32f, 1f);
            bars.DrawRect  (viewportW, viewportH, X, Y, Width, Height, fill);
            bars.DrawBorder(viewportW, viewportH, X, Y, Width, Height, border);
        }

        int labelW = text.MeasureWidth(Label);
        int labelH = text.HasFont ? text.Font!.Height : 14;
        int lx = X + (Width  - labelW) / 2;
        int ly = Y + (Height - labelH) / 2 + (_pressed ? 1 : 0);
        text.DrawString(viewportW, viewportH, Label, lx, ly, ink);
    }
}
