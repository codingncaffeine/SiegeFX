using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 27 — DS1's field-commands panel
/// (<c>/ui/interfaces/backend/field_commands/field_commands.gas</c>): the
/// bottom-right party-command cluster. Six formation buttons (radio_group
/// radio_formations), three crystal-radio order groups (movement / attack /
/// targeting), plus select-all, disband, and a follow toggle. Rects are the
/// authored 640×480 reference, right-anchored and scaled by viewportH/480.
///
/// Chrome uses the authored b_gui_* textures when they resolve, falling back
/// to labelled cells/crystals (the VendorPanel approach) so the panel is
/// legible even if a raw is missing.
/// </summary>
public sealed class FieldCommandsPanel
{
    public const int RefRes = 480;
    public static float Scale(int viewportH) => viewportH / (float)RefRes;

    public enum Action
    {
        None,
        FormRow, FormDoubleRow, FormColumn, FormDoubleColumn, FormPyramid, FormCircle,
        MoveFree, MoveEngage, MoveHoldGround,
        AtkFree, AtkFightback, AtkHoldFire,
        TgtClosest, TgtStrongest, TgtWeakest,
        SelectAll, Disband, ToggleFollow,
    }

    // Authored rects (x0,y0,x1,y1) in the 640×480 reference.
    static readonly (Action A, (int, int, int, int) R, string Label)[] Formations =
    {
        (Action.FormRow,          (428, 401, 453, 425), "Row"),
        (Action.FormDoubleRow,    (452, 401, 477, 425), "2Row"),
        (Action.FormColumn,       (475, 401, 500, 425), "Col"),
        (Action.FormDoubleColumn, (499, 401, 524, 425), "2Col"),
        (Action.FormPyramid,      (523, 401, 548, 425), "Pyr"),
        (Action.FormCircle,       (547, 401, 572, 425), "Cir"),
    };
    // Three crystal-radio order rows. Each option is a 12×13 crystal.
    static readonly (Action A, (int, int, int, int) R)[] Movement =
    {
        (Action.MoveFree, (574, 354, 586, 367)), (Action.MoveEngage, (587, 354, 599, 367)),
        (Action.MoveHoldGround, (600, 354, 612, 367)),
    };
    static readonly (Action A, (int, int, int, int) R)[] Attack =
    {
        (Action.AtkFree, (574, 370, 586, 383)), (Action.AtkFightback, (587, 370, 599, 383)),
        (Action.AtkHoldFire, (600, 370, 612, 383)),
    };
    static readonly (Action A, (int, int, int, int) R)[] Targeting =
    {
        (Action.TgtClosest, (574, 386, 586, 399)), (Action.TgtStrongest, (587, 386, 599, 399)),
        (Action.TgtWeakest, (600, 386, 612, 399)),
    };
    static readonly (Action A, (int, int, int, int) R, string Label)[] Buttons =
    {
        (Action.SelectAll,    (571, 332, 603, 352), "All"),
        (Action.Disband,      (602, 332, 635, 353), "Dis"),
        (Action.ToggleFollow, (519, 284, 563, 308), "Follow"),
    };

    public readonly record struct State(
        Action Formation, Action Movement, Action Attack, Action Targeting, bool Follow);

    static (int x, int y, int w, int h) Px((int x0, int y0, int x1, int y1) r, float s, int originX)
        => (originX + (int)MathF.Round(r.x0 * s), (int)MathF.Round(r.y0 * s),
            (int)MathF.Round((r.x1 - r.x0) * s), (int)MathF.Round((r.y1 - r.y0) * s));

    static bool In((int x, int y, int w, int h) p, int px, int py)
        => px >= p.x && px < p.x + p.w && py >= p.y && py < p.y + p.h;

    public Action HitTest(int px, int py, int viewportW, int viewportH)
    {
        float s = Scale(viewportH);
        int originX = viewportW - (int)MathF.Round(640f * s);
        foreach (var f in Formations) if (In(Px(f.R, s, originX), px, py)) return f.A;
        foreach (var g in new[] { Movement, Attack, Targeting })
            foreach (var o in g) if (In(Px(o.R, s, originX), px, py)) return o.A;
        foreach (var b in Buttons) if (In(Px(b.R, s, originX), px, py)) return b.A;
        return Action.None;
    }

    // Formation radio icon per action (DS1's six formations use form1-4/6/7;
    // form5 is unused). Selected radios swap the _up face for the pressed _dwn.
    static string FormationTexture(Action a) => a switch
    {
        Action.FormRow          => "b_gui_ig_mnu_form1",
        Action.FormDoubleRow    => "b_gui_ig_mnu_form2",
        Action.FormColumn       => "b_gui_ig_mnu_form3",
        Action.FormDoubleColumn => "b_gui_ig_mnu_form4",
        Action.FormPyramid      => "b_gui_ig_mnu_form6",
        Action.FormCircle       => "b_gui_ig_mnu_form7",
        _                       => "",
    };

    // The order-row buttons display the CURRENT selection's label (DS1 sets the
    // button text to the active radio in that group), all-caps like the original.
    static string OrderLabel(Action a) => a switch
    {
        Action.MoveFree       => "MOVE FREELY",
        Action.MoveEngage     => "ENGAGE",
        Action.MoveHoldGround => "HOLD GROUND",
        Action.AtkFree        => "ATTACK FREELY",
        Action.AtkFightback   => "DEFEND",
        Action.AtkHoldFire    => "HOLD FIRE",
        Action.TgtClosest     => "TARGET CLOSEST",
        Action.TgtWeakest     => "TARGET WEAKEST",
        Action.TgtStrongest   => "TARGET STRONGEST",
        _                     => "",
    };

    public void Draw(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                     Func<string, GlTexture?>? guiTex, int viewportW, int viewportH, State st)
    {
        // DS1's field_commands authors no background frame — every control is a
        // b_gui_* raw blitted at its authored rect with the authored UV crop
        // (the metal button face is baked into the texture), and the icons dock
        // on the command bar. Selected formation radios swap to the _dwn face;
        // order crystals swap b_gui_cmn_crystal_off_up -> _on_up. Without the
        // icon renderer / atlas (headless) nothing draws.
        if (icons is null || guiTex is null) return;

        float s = Scale(viewportH);
        int originX = viewportW - (int)MathF.Round(640f * s);
        var ink = new Vector4(0.88f, 0.84f, 0.70f, 1f);

        // DS1 RAWs are stored bottom-up and the gas authors uvcoords in that
        // frame, so convert to the renderer's top-down space before drawing:
        // vMin = 1 - gasV1, vMax = 1 - gasV0 (the inventory/data_bar flip rule).
        // Passing the gas V values straight through cropped the top of each icon.
        void Blit(string tex, (int, int, int, int) r, float gu0, float gv0, float gu1, float gv1)
        {
            var t = guiTex(tex);
            if (t is null) return;
            var p = Px(r, s, originX);
            icons.DrawIcon(viewportW, viewportH, t, p.x, p.y, p.w, p.h, Vector4.One,
                           gu0, 1f - gv1, gu1, 1f - gv0);
        }

        // Follow checkbox (docked above the main cluster).
        Blit(st.Follow ? "b_gui_ig_mnu_follow_on_up" : "b_gui_ig_mnu_follow_off_up",
             (519, 284, 563, 308), 0f, 0.25f, 0.6875f, 1f);

        // Select-all / disband / collect-loot icon buttons.
        Blit("b_gui_ig_mnu_select_up",   (571, 332, 603, 352), 0f, 0.375f,   1f,       1f);
        Blit("b_gui_ig_mnu_disband_up",  (602, 332, 635, 353), 0f, 0.34375f, 1.03125f, 1f);
        Blit("b_gui_ig_mnu_get_loot_up", (571, 425, 603, 445), 0f, 0.375f,   1f,       1f);

        // Order rows: copperplate label on the left, three selection crystals on
        // the right (lit = the current order in that group).
        void Row((Action A, (int, int, int, int) R)[] grp, Action sel, int labelY)
        {
            var lp = Px((432, labelY, 574, labelY + 14), s, originX);
            text.DrawString(viewportW, viewportH, OrderLabel(sel), lp.x, lp.y, ink);
            foreach (var o in grp)
                Blit(o.A == sel ? "b_gui_cmn_crystal_on_up" : "b_gui_cmn_crystal_off_up",
                     o.R, 0f, 0.1875f, 0.8125f, 1f);
        }
        Row(Movement,  st.Movement,  355);
        Row(Attack,    st.Attack,    371);
        Row(Targeting, st.Targeting, 387);

        // Formation radios (selected → pressed face).
        foreach (var f in Formations)
            Blit(FormationTexture(f.A) + (f.A == st.Formation ? "_dwn" : "_up"),
                 f.R, 0f, 0.25f, 0.78125f, 1f);
    }
}
