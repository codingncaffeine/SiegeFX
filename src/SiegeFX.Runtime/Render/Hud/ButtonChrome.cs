using System;
using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// DS1's common 3-slice push-button chrome (common_control_art
/// <c>button_4</c> / <c>button_5</c>): a left cap, a horizontally-stretched
/// centre, and a right cap — the brownish beveled gradient with corner
/// brackets seen on every menu and in-panel button. button_4 caps are 16×16
/// (in-panel buttons: dialogue, store, Field Commands order rows); button_5
/// caps are 16×32 (menu buttons: in_game_menu, frontend). The state suffix
/// swaps <c>_up</c> / <c>_dwn</c> / <c>_hov</c> raws.
/// </summary>
public static class ButtonChrome
{
    public enum State { Up, Down, Hover }

    /// <summary>Draws the 3-slice button into [x,y,w,h]. Returns false if the
    /// chrome raws don't resolve, so the caller can fall back to a flat fill.</summary>
    public static bool Draw(IconRenderer? icons, Func<string, GlTexture?>? guiTex,
                            int vw, int vh, int x, int y, int w, int h,
                            string template, State state)
        => Draw(icons, guiTex, vw, vh, x, y, w, h, template, state, Vector4.One);

    /// <summary>ALPHA-2H — tinted variant: the store's inactive tab row draws
    /// the same authored chrome dimmed so the selected row reads as a unit.</summary>
    public static bool Draw(IconRenderer? icons, Func<string, GlTexture?>? guiTex,
                            int vw, int vh, int x, int y, int w, int h,
                            string template, State state, Vector4 tint)
    {
        if (icons is null || guiTex is null || w <= 0 || h <= 0) return false;
        string suf = state switch { State.Down => "_dwn", State.Hover => "_hov", _ => "_up" };
        var lt = guiTex($"b_gui_cmn_{template}_lt{suf}");
        var ce = guiTex($"b_gui_cmn_{template}_center{suf}");
        var rt = guiTex($"b_gui_cmn_{template}_rt{suf}");
        if (lt is null || ce is null || rt is null) return false;

        // Cap width preserves each segment's native aspect: button_4 caps are
        // square (16×16 → capW = h), button_5 caps are 16×32 (→ capW = h/2).
        float aspect = template == "button5" ? 0.5f : 1f;
        int cap = Math.Clamp((int)MathF.Round(h * aspect), 1, w / 2);

        icons.DrawIcon(vw, vh, ce, x + cap, y, w - 2 * cap, h, tint); // stretched centre
        icons.DrawIcon(vw, vh, lt, x, y, cap, h, tint);
        icons.DrawIcon(vw, vh, rt, x + w - cap, y, cap, h, tint);
        return true;
    }
}
