using System.Numerics;
using SiegeFX.Core.Actors;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 21-SC-INV-A — DS1-faithful character pane. Sits at the top-left
/// of the screen alongside <see cref="InventoryPanel"/> and
/// <see cref="SpellBookPanel"/>; toggles independently with 'C'.
///
/// Reads its content off the live actor + progression rather than caching:
/// every draw pulls current life/mana/skills/damage so the user sees the
/// numbers move as XP rolls in. The panel is a read-only view in this
/// slice — equipment slot clicks and a dedicated "VIEW" button (per
/// 21-SC-INV-B) are layered on later.
/// </summary>
public sealed class CharacterPanel
{
    // Phase 21-SC-INV-A2 (round 7) — narrowed by 25% (220 → 165) so the
    // top-dock layout fits to the right of the always-on ability bar
    // without crowding the spell book pane on common widths.
    public const int PanelWidth  = 165;
    public const int PanelHeight = 460;
    public const int Padding     = 8;
    public const int TitleH      = 22;

    public bool IsOpen { get; set; }
    public int OriginX { get; set; }
    public int OriginY { get; set; }

    /// <summary>SC-EQUIP-ROUTING — the info-rail scale the last Draw used.
    /// The hit rect must match the DRAWN panel: the old unscaled 165×460
    /// both swallowed clicks it didn't render under (killing the paperdoll
    /// slots that overlap the sheet) and let clicks on the scaled chrome
    /// below y=460 fall through to the world (a dragged item thrown to the
    /// ground instead of equipped).</summary>
    public float LastScale { get; private set; } = 1f;

    public bool IsPointInPanel(int x, int y) =>
        x >= OriginX && y >= OriginY &&
        x <  OriginX + (int)(PanelWidth * LastScale) &&
        y <  OriginY + (int)(PanelHeight * LastScale);

    public void Draw(BarRenderer bars, TextRenderer text,
                     int viewportW, int viewportH,
                     string playerName,
                     Actor? player,
                     PlayerProgression? progression,
                     ActorStats attackerStats,
                     int armorRating,
                     float skillXpFraction = 0f,
                     IconRenderer? icons = null,
                     GlTexture? portraitIcon = null,
                     System.Func<string, GlTexture?>? chromeLookup = null,
                     string startingClassTitle = "Farmer")
    {
        int px = OriginX, py = OriginY;
        var ink    = new Vector4(0.667f, 0.655f, 0.557f, 1f); // DS1 panel ink #aaa78e
        var headInk = new Vector4(0.86f, 0.83f, 0.69f, 1f);   // brighter for headings
        var dimInk = new Vector4(0.55f, 0.55f, 0.5f, 1f);
        var hpFill = new Vector4(0.78f, 0.10f, 0.10f, 1f);
        var mpFill = new Vector4(0.18f, 0.40f, 0.90f, 1f);
        var slotBg = new Vector4(0.04f, 0.04f, 0.05f, 1f);
        var slotEm = new Vector4(0.55f, 0.57f, 0.60f, 1f);

        // INFORAIL-CHROME — gas-textured upper panes. hud_character.gas:175
        // character_pane_1 rect 87,0,254,116 tex cp_top_01 uv 0,0.09375,
        // 0.652344,1; line 187 character_pane_2 rect 87,116,254,228 tex
        // cp_mid_01 uv 0,0.125,0.652344,1. Uses the clamped info-rail
        // scale (InfoRailLayout.Scale) so the upper panes match the
        // paperdoll + inventory + spellbook sizing on modern resolutions.
        float s = InfoRailLayout.Scale(viewportH);
        LastScale = s; // SC-EQUIP-ROUTING — keep the hit rect in step with the drawn size
        int paneW = (int)System.Math.Round((254 - 87) * s);
        int pane1H = (int)System.Math.Round(116 * s);
        int pane2H = (int)System.Math.Round(112 * s); // 228-116
        var topPane = chromeLookup?.Invoke("b_gui_ig_mnu_cp_top_01");
        var midPane = chromeLookup?.Invoke("b_gui_ig_mnu_cp_mid_01");
        if (icons is not null && topPane is not null)
            icons.DrawIcon(viewportW, viewportH, topPane, px, py, paneW, pane1H, Vector4.One,
                0f, 1f - 1f, 0.652344f, 1f - 0.09375f);
        else
            bars.DrawRect(viewportW, viewportH, px, py, paneW, pane1H,
                new Vector4(0.08f, 0.08f, 0.10f, 0.92f));
        if (icons is not null && midPane is not null)
            icons.DrawIcon(viewportW, viewportH, midPane, px, py + pane1H, paneW, pane2H, Vector4.One,
                0f, 1f - 1f, 0.652344f, 1f - 0.125f);
        else
            bars.DrawRect(viewportW, viewportH, px, py + pane1H, paneW, pane2H,
                new Vector4(0.08f, 0.08f, 0.10f, 0.92f));

        // Helper: convert a gas (X0,Y0,X1,Y1) to scaled screen rect anchored
        // at this panel's origin (panel gas-x=87 maps to px).
        (int x, int y, int w, int h) R(int gx0, int gy0, int gx1, int gy1) =>
            (px + (int)System.Math.Round((gx0 - 87) * s),
             py + (int)System.Math.Round(gy0 * s),
                  (int)System.Math.Round((gx1 - gx0) * s),
                  (int)System.Math.Round((gy1 - gy0) * s));

        // INFORAIL-CHAR-NAME-CLASS — character name + dynamic class.
        // hud_character.gas:162 character_name rect 91,5,248,17 centered.
        // hud_character.gas:146 character_class rect 91,23,249,36 centered.
        string nameStr = string.IsNullOrEmpty(playerName) ? "Adventurer" : playerName;
        var nameR = R(91, 5, 248, 17);
        int nameW = text.MeasureWidth(nameStr);
        text.DrawString(viewportW, viewportH, nameStr,
            nameR.x + (nameR.w - nameW) / 2,
            nameR.y + (nameR.h - 8) / 2, headInk);

        int meleeLv  = progression?.SkillLevel(SiegeFX.Core.Assets.SkillKind.Melee)       ?? 0;
        int rangedLv = progression?.SkillLevel(SiegeFX.Core.Assets.SkillKind.Ranged)      ?? 0;
        int natureLv = progression?.SkillLevel(SiegeFX.Core.Assets.SkillKind.NatureMagic) ?? 0;
        int combatLv = progression?.SkillLevel(SiegeFX.Core.Assets.SkillKind.CombatMagic) ?? 0;
        string classStr = SiegeFX.Core.Assets.ClassTitleResolver.Resolve(
            startingClassTitle, meleeLv, rangedLv, natureLv, combatLv);
        var classR = R(91, 23, 249, 36);
        int classW = text.MeasureWidth(classStr);
        text.DrawString(viewportW, viewportH, classStr,
            classR.x + (classR.w - classW) / 2,
            classR.y + (classR.h - 8) / 2, ink);

        // INFORAIL-VITAL-LABELS — Health/Mana labels + numeric values
        // sandwiched between the two vertical bars.
        if (player is not null)
        {
            // Vertical Health bar — gas:879 rect 88,44,101,114. Fills
            // bottom-up. INFORAIL-VIAL-GRADIENT — horizontal gradient
            // (dark edges, bright center) like the small overhead bars
            // and the AWP vials, instead of a flat fill. DrawHGradientFill
            // composes the dark→bright→dark across the bar width.
            var hb = R(88, 44, 101, 114);
            float hpFrac = player.Stats.MaxLife > 0
                ? System.Math.Clamp(player.Combat.CurrentLife / player.Stats.MaxLife, 0f, 1f) : 0f;
            bars.DrawRect(viewportW, viewportH, hb.x, hb.y, hb.w, hb.h, slotBg);
            int fillH = (int)System.Math.Round(hb.h * hpFrac);
            if (fillH > 0)
                bars.DrawHGradientFill(viewportW, viewportH, hb.x, hb.y + hb.h - fillH, hb.w, fillH, hpFill);
            bars.DrawBorder(viewportW, viewportH, hb.x, hb.y, hb.w, hb.h, slotEm);

            // Vertical Mana bar — gas:892 rect 238,44,251,114.
            var mb = R(238, 44, 251, 114);
            float mpFrac = player.Stats.MaxMana > 0
                ? System.Math.Clamp(player.Combat.CurrentMana / player.Stats.MaxMana, 0f, 1f) : 0f;
            bars.DrawRect(viewportW, viewportH, mb.x, mb.y, mb.w, mb.h, slotBg);
            int mFillH = (int)System.Math.Round(mb.h * mpFrac);
            if (mFillH > 0)
                bars.DrawHGradientFill(viewportW, viewportH, mb.x, mb.y + mb.h - mFillH, mb.w, mFillH, mpFill);
            bars.DrawBorder(viewportW, viewportH, mb.x, mb.y, mb.w, mb.h, slotEm);

            // Labels + numeric values, all gas-cited.
            DrawCentered(text, viewportW, viewportH, R(102, 45, 168, 58), "Health", ink);
            DrawCentered(text, viewportW, viewportH, R(171, 44, 237, 58), "Mana",   ink);
            DrawCentered(text, viewportW, viewportH, R(102, 58, 168, 72),
                $"{(int)player.Combat.CurrentLife}/{(int)player.Stats.MaxLife}", headInk);
            DrawCentered(text, viewportW, viewportH, R(171, 58, 237, 72),
                $"{(int)player.Combat.CurrentMana}/{(int)player.Stats.MaxMana}", headInk);

            // STR/DEX/INT — labels left, bars middle, values right, all gas rects.
            // SC-ATTR-XP — the bars track each ATTRIBUTE'S OWN pool progress
            // (AttrProgressFraction), not the feeding skills'. The skill
            // fractions fill ~37% faster (the skill takes 100% of an award,
            // the attribute only its influence share) and reset on SKILL
            // crossings — the user-reported "looks like INT is about to level,
            // then the bar starts over" without the number moving.
            DrawLabeledStatRow(bars, text, viewportW, viewportH,
                R(104, 73, 211, 86), R(214, 73, 236, 86),
                "Strength",    ((int)player.Stats.Strength).ToString(),
                progression?.AttrProgressFraction(0) ?? skillXpFraction,
                ink, slotBg, slotEm);
            DrawLabeledStatRow(bars, text, viewportW, viewportH,
                R(104, 87, 211, 100), R(214, 87, 236, 100),
                "Dexterity",   ((int)player.Stats.Dexterity).ToString(),
                progression?.AttrProgressFraction(1) ?? skillXpFraction,
                ink, slotBg, slotEm);
            DrawLabeledStatRow(bars, text, viewportW, viewportH,
                R(104,101, 211, 114), R(214,101, 236, 114),
                "Intelligence",((int)player.Stats.Intelligence).ToString(),
                progression?.AttrProgressFraction(2) ?? skillXpFraction,
                ink, slotBg, slotEm);
        }

        // INFORAIL-LEVEL — gas:978 text_level rect 201,117,250,130.
        var levelR = R(201, 117, 250, 130);
        int overallLv = progression?.Level ?? 0;
        string levelStr = $"Level {overallLv}";
        int lvW = text.MeasureWidth(levelStr);
        text.DrawString(viewportW, viewportH, levelStr,
            levelR.x + (levelR.w - lvW) / 2, levelR.y + (levelR.h - 8) / 2, ink);

        // INFORAIL-SKILLS-WITH-ICONS — gas rects:
        //   bar  + value rows: 89,130..200,143 etc.
        //   skill labels: text_skill_melee/ranged/nature/combat (gas:1080..)
        //   skill icons:  icon_melee/ranged/nmagic/cmagic (gas:708/730/719/697)
        DrawSkillRow(bars, text, icons, viewportW, viewportH, s, px, py,
            "Melee",       meleeLv, progression?.SkillProgressFraction(SiegeFX.Core.Assets.SkillKind.Melee)       ?? 0f,
            89, 130, 200, 143,  183, 128, 199, 144, "b_gui_ig_mnu_combat",       chromeLookup, ink, slotBg, slotEm);
        DrawSkillRow(bars, text, icons, viewportW, viewportH, s, px, py,
            "Ranged",      rangedLv, progression?.SkillProgressFraction(SiegeFX.Core.Assets.SkillKind.Ranged)      ?? 0f,
            89, 144, 200, 157,  184, 142, 200, 158, "b_gui_ig_mnu_ranged",       chromeLookup, ink, slotBg, slotEm);
        DrawSkillRow(bars, text, icons, viewportW, viewportH, s, px, py,
            "Nature Magic",natureLv, progression?.SkillProgressFraction(SiegeFX.Core.Assets.SkillKind.NatureMagic) ?? 0f,
            89, 158, 200, 171,  183, 156, 199, 172, "b_gui_ig_mnu_nature-magic", chromeLookup, ink, slotBg, slotEm);
        DrawSkillRow(bars, text, icons, viewportW, viewportH, s, px, py,
            "Combat Magic",combatLv, progression?.SkillProgressFraction(SiegeFX.Core.Assets.SkillKind.CombatMagic) ?? 0f,
            89, 172, 200, 185,  183, 170, 199, 186, "b_gui_ig_mnu_combat-magic", chromeLookup, ink, slotBg, slotEm);

        // INFORAIL-DAMAGE-ARMOR-LABELS — gas labels + values:
        //   Melee Damage  label 90,186..199 + value 200,186..199
        //   Ranged Damage label 90,200..213 + value 200,200..213
        //   Armor Rating  label 90,214..227 + value 200,214..227
        DrawLabelValueRow(text, viewportW, viewportH,
            R(90, 186, 199, 199), R(200, 186, 250, 199),
            "Melee Damage",  $"{(int)attackerStats.DamageMin}-{(int)attackerStats.DamageMax}", ink);
        DrawLabelValueRow(text, viewportW, viewportH,
            R(90, 200, 199, 213), R(200, 200, 250, 213),
            "Ranged Damage", $"{(int)attackerStats.DamageMin}-{(int)attackerStats.DamageMax}", dimInk);
        DrawLabelValueRow(text, viewportW, viewportH,
            R(90, 214, 199, 227), R(200, 214, 250, 227),
            "Armor Rating",  armorRating.ToString(), ink);
        return;
    }

    static void DrawCentered(TextRenderer text, int vw, int vh,
                             (int x, int y, int w, int h) r, string s, Vector4 ink)
    {
        int tw = text.MeasureWidth(s);
        text.DrawString(vw, vh, s, r.x + (r.w - tw) / 2, r.y + (r.h - 8) / 2, ink);
    }

    static void DrawLabeledStatRow(BarRenderer bars, TextRenderer text, int vw, int vh,
                                   (int x, int y, int w, int h) labelR,
                                   (int x, int y, int w, int h) valR,
                                   string label, string value, float fraction,
                                   Vector4 ink, Vector4 bg, Vector4 em)
    {
        bars.DrawRect(vw, vh, labelR.x, labelR.y, labelR.w, labelR.h, bg);
        // Left-to-right horizontal fill matching the gas's
        // dynamic_edge=right convention for the info-panel stat rows.
        if (fraction > 0f)
        {
            int fw = (int)(labelR.w * System.MathF.Max(0f, System.MathF.Min(1f, fraction)));
            if (fw > 0) bars.DrawRect(vw, vh, labelR.x, labelR.y, fw, labelR.h, ProgressFill);
        }
        bars.DrawBorder(vw, vh, labelR.x, labelR.y, labelR.w, labelR.h, em);
        text.DrawString(vw, vh, label, labelR.x + 4, labelR.y + (labelR.h - 8) / 2, ink);
        DrawCentered(text, vw, vh, valR, value, ink);
    }

    static void DrawSkillRow(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                             int vw, int vh, float s, int px, int py,
                             string label, int level, float fraction,
                             int barX0, int barY0, int barX1, int barY1,
                             int iconX0, int iconY0, int iconX1, int iconY1,
                             string iconTex,
                             System.Func<string, GlTexture?>? chromeLookup,
                             Vector4 ink, Vector4 bg, Vector4 em)
    {
        int bx = px + (int)System.Math.Round((barX0 - 87) * s);
        int by = py + (int)System.Math.Round(barY0 * s);
        int bw = (int)System.Math.Round((barX1 - barX0) * s);
        int bh = (int)System.Math.Round((barY1 - barY0) * s);
        bars.DrawRect(vw, vh, bx, by, bw, bh, bg);
        // Info-panel skill row: left-to-right horizontal fill per the
        // gas's `dynamic_edge = right` (status_bar.dynamic_edge: the
        // RIGHT edge moves as the bar fills, so fill grows from left).
        // The AWP slot frames carry the bottom-up vertical fill for the
        // same skill (drawn elsewhere).
        if (fraction > 0f)
        {
            int fw = (int)(bw * System.MathF.Max(0f, System.MathF.Min(1f, fraction)));
            if (fw > 0) bars.DrawRect(vw, vh, bx, by, fw, bh, ProgressFill);
        }
        bars.DrawBorder(vw, vh, bx, by, bw, bh, em);
        text.DrawString(vw, vh, label, bx + 4, by + (bh - 8) / 2, ink);
        // Value at the right column (200..250 in gas, scaled).
        int vx = px + (int)System.Math.Round((200 - 87) * s);
        int vy = py + (int)System.Math.Round(barY0 * s);
        int vw2 = (int)System.Math.Round((250 - 200) * s);
        int vh2 = (int)System.Math.Round((barY1 - barY0) * s);
        var lvlStr = level.ToString();
        int lvlW = text.MeasureWidth(lvlStr);
        text.DrawString(vw, vh, lvlStr, vx + (vw2 - lvlW) / 2, vy + (vh2 - 8) / 2, ink);
        // Skill icon (window_icon_*), inset into the right side of the bar
        // per gas rects 183/184,128/142/156/170..199/200,144/158/172/186.
        var iconTexLoaded = chromeLookup?.Invoke(iconTex);
        if (icons is not null && iconTexLoaded is not null)
        {
            int ix = px + (int)System.Math.Round((iconX0 - 87) * s);
            int iy = py + (int)System.Math.Round(iconY0 * s);
            int iw = (int)System.Math.Round((iconX1 - iconX0) * s);
            int ih = (int)System.Math.Round((iconY1 - iconY0) * s);
            icons.DrawIcon(vw, vh, iconTexLoaded, ix, iy, iw, ih, Vector4.One);
        }
    }

    static void DrawLabelValueRow(TextRenderer text, int vw, int vh,
                                  (int x, int y, int w, int h) labelR,
                                  (int x, int y, int w, int h) valR,
                                  string label, string value, Vector4 ink)
    {
        text.DrawString(vw, vh, label, labelR.x, labelR.y + (labelR.h - 8) / 2, ink);
        int vw3 = text.MeasureWidth(value);
        text.DrawString(vw, vh, value, valR.x + (valR.w - vw3) / 2, valR.y + (valR.h - 8) / 2, ink);
    }
    // — legacy SiegeFX-invented stat block was here; deleted in the
    //   INFORAIL CharacterPanel rewrite (chrome textures + gas rects).
    //   See git history if you need the previous render. —
#if FALSE
    static void _LegacyDrawUnused_Body_Removed()
    {
        int contentTop = 0;

        // Phase 21-SC-INV-B (round 4) — vertical HP/MP vials flank STR/DEX/INT.
        // Layout left→right: HEALTH word (rotated rendering not feasible w/
        // the 8x8 bitmap font, so we draw the word above its vial), HP vial,
        // STR/DEX/INT centered column, MP vial, MANA word above its vial.
        // 2px thinner horizontally and 5px longer vertically per the DS1
        // proportions; gradient fill (bright center, dim edges) is applied
        // by the vial helper so the pools read as glassy tubes.
        const int vialW = 16;
        const int vialPad = 4;
        int statsTop = contentTop + 12; // leave headroom for HEALTH/MANA labels above
        int statsBlockH = 12 * 4;       // 4 stat rows × 12px (XP bar + stat row)
        // Phase 21-SC-INV-A2 (round 7) — vials grown 25% taller so the glassy
        // gradient reads from across the room, not just at this distance.
        int vialH = (int)((statsBlockH + 13) * 1.25f);
        int leftVialX = px + Padding;
        int rightVialX = px + PanelWidth - Padding - vialW;
        int statsX = leftVialX + vialW + vialPad;
        int statsW = rightVialX - vialPad - statsX;

        // HEALTH / MANA labels above their vials.
        text.DrawString(viewportW, viewportH, "HP", leftVialX - 1, contentTop, hpFill);
        text.DrawString(viewportW, viewportH, "MP", rightVialX + 1, contentTop, mpFill);

        if (player is not null)
        {
            DrawVerticalVial(bars, viewportW, viewportH, leftVialX, statsTop, vialW, vialH,
                             player.Combat.CurrentLife, player.Stats.MaxLife, hpFill, slotBg, slotEm);
            DrawVerticalVial(bars, viewportW, viewportH, rightVialX, statsTop, vialW, vialH,
                             player.Combat.CurrentMana, player.Stats.MaxMana, mpFill, slotBg, slotEm);

            // Phase 21-SC-INV-A2 (round 9) — numeric HP/MP readout sits
            // ABOVE the STR/DEX/INT block, joined by a pipe separator
            // ("49/49 | 30/30"). Mirrors the DS1 layout the user keyed
            // off — the numbers belong with the vials they describe.
            string hpText = $"{(int)player.Combat.CurrentLife}/{(int)player.Stats.MaxLife}";
            string mpText = $"{(int)player.Combat.CurrentMana}/{(int)player.Stats.MaxMana}";
            string combinedReadout = $"{hpText} | {mpText}";
            int combW = text.MeasureWidth(combinedReadout);
            text.DrawString(viewportW, viewportH, combinedReadout,
                            statsX + (statsW - combW) / 2, statsTop, ink);

            // STR / DEX / INT — centered between the two vials. Each row's
            // progress fill tracks the skill that drives that attribute's
            // SC-ATTR-XP — the attribute bars show progress toward the NEXT
            // ATTRIBUTE level (the redistributed pool between its table
            // thresholds). They used to mirror the SKILL fractions, so a bar
            // could reset (skill leveled) while the attribute number stayed
            // put — the user-reported "bar started over but the number
            // didn't change".
            float strFrac = progression?.AttrProgressFraction(0) ?? skillXpFraction;
            float dexFrac = progression?.AttrProgressFraction(1) ?? skillXpFraction;
            float intFrac = progression?.AttrProgressFraction(2) ?? skillXpFraction;
            int statRowH = 11;
            int sy = statsTop + 12; // headroom for the HP|MP readout above
            DrawStatBarRow(bars, text, viewportW, viewportH, statsX, sy, statsW, statRowH,
                           "STR", ((int)player.Stats.Strength).ToString(),
                           strFrac, ink, slotBg, slotEm);
            sy += statRowH + 1;
            DrawStatBarRow(bars, text, viewportW, viewportH, statsX, sy, statsW, statRowH,
                           "DEX", ((int)player.Stats.Dexterity).ToString(),
                           dexFrac, ink, slotBg, slotEm);
            sy += statRowH + 1;
            DrawStatBarRow(bars, text, viewportW, viewportH, statsX, sy, statsW, statRowH,
                           "INT", ((int)player.Stats.Intelligence).ToString(),
                           intFrac, ink, slotBg, slotEm);
        }

        int y = statsTop + vialH + 8;
        // Phase 22-AUTH-MINIHUD-REMOVE — the SiegeFX-invented mini-HUD
        // (which used to host a portrait stub) is gone. DS1 doesn't ship
        // a player-portrait widget in the always-on HUD; party-member
        // portraits surface via team_portraits.gas AWP (MP-only) when
        // 22-D SC-HUD-PORTRAITS lands. Player HP/MP is on the floating
        // overhead bars (22-H, shipped).

        // SKILLS block — header row "SKILLS / LEVEL" then four skill rows
        // pulled off the progression. 21b ships one combined progression
        // level; until per-skill pools land each row reads the same value
        // so the column layout already matches DS1. Each row gets its own
        // bordered box so the section reads as a list of named slots.
        text.DrawString(viewportW, viewportH, "SKILLS", px + Padding, y, headInk);
        text.DrawString(viewportW, viewportH, "LEVEL",
                        px + PanelWidth - Padding - text.MeasureWidth("LEVEL"), y, headInk);
        y += 12;

        // Phase 21-SC-INV-A2 (round 8) — per-skill XP pools landed in
        // PlayerProgression; each skill row reads its own pool's level
        // and progress fraction. Falls back to the global level + 0
        // fraction when progression isn't bound (creator preview path).
        int meleeLv  = progression?.SkillLevel(SiegeFX.Core.Assets.SkillKind.Melee)       ?? 1;
        int rangedLv = progression?.SkillLevel(SiegeFX.Core.Assets.SkillKind.Ranged)      ?? 1;
        int natureLv = progression?.SkillLevel(SiegeFX.Core.Assets.SkillKind.NatureMagic) ?? 1;
        int combatLv = progression?.SkillLevel(SiegeFX.Core.Assets.SkillKind.CombatMagic) ?? 1;
        float meleeFrac  = progression?.SkillProgressFraction(SiegeFX.Core.Assets.SkillKind.Melee)       ?? 0f;
        float rangedFrac = progression?.SkillProgressFraction(SiegeFX.Core.Assets.SkillKind.Ranged)      ?? 0f;
        float natureFrac = progression?.SkillProgressFraction(SiegeFX.Core.Assets.SkillKind.NatureMagic) ?? 0f;
        float combatFrac = progression?.SkillProgressFraction(SiegeFX.Core.Assets.SkillKind.CombatMagic) ?? 0f;
        int skillRowH = 13;
        int skillRowW = PanelWidth - Padding * 2;
        DrawSkillBox(bars, text, viewportW, viewportH, px + Padding, y, skillRowW, skillRowH,
                     "MELEE",        meleeLv,  meleeFrac,  ink, slotBg, slotEm); y += skillRowH + 1;
        DrawSkillBox(bars, text, viewportW, viewportH, px + Padding, y, skillRowW, skillRowH,
                     "RANGED",       rangedLv, rangedFrac, ink, slotBg, slotEm); y += skillRowH + 1;
        DrawSkillBox(bars, text, viewportW, viewportH, px + Padding, y, skillRowW, skillRowH,
                     "NATURE MAGIC", natureLv, natureFrac, ink, slotBg, slotEm); y += skillRowH + 1;
        DrawSkillBox(bars, text, viewportW, viewportH, px + Padding, y, skillRowW, skillRowH,
                     "COMBAT MAGIC", combatLv, combatFrac, ink, slotBg, slotEm); y += skillRowH + 5;

        // Damage + armor summary. Melee damage range is the active attacker
        // stats (weapon if equipped, baseline otherwise); ranged echoes the
        // same fields until per-skill weapon pools land. Armor rating is
        // pre-summed by the host off equipped armor templates.
        DrawKv(bars, text, viewportW, viewportH, px, y, "MELEE DAMAGE",
               $"{(int)attackerStats.DamageMin}-{(int)attackerStats.DamageMax}", ink); y += 11;
        DrawKv(bars, text, viewportW, viewportH, px, y, "RANGED DAMAGE",
               $"{(int)attackerStats.DamageMin}-{(int)attackerStats.DamageMax}", dimInk); y += 11;
        DrawKv(bars, text, viewportW, viewportH, px, y, "ARMOR RATING",
               armorRating.ToString(), ink); y += 14;

        // Footer hint so the user knows how to dismiss.
        int footY = py + PanelHeight - 14;
        text.DrawString(viewportW, viewportH, "C - close", px + Padding, footY, dimInk);
    }
#endif

    /// <summary>Phase 21-SC-INV-B — single STR/DEX/INT row with a thin XP
    /// progress bar drawn behind the label so the player can see how close
    /// the skill is to leveling. Bar fills left→right; text + numeral are
    /// drawn on top so they read against the fill.</summary>
    // Phase 21-SC-INV-A2 (round 8) — DS1 ships #635757 as the progress
    // fill behind STR/DEX/INT and the four skill rows. Quiet mauve-grey
    // that reads as "background bar" rather than competing with the ink.
    static readonly Vector4 ProgressFill = new(0.388f, 0.341f, 0.341f, 1f);

    static void DrawStatBarRow(BarRenderer bars, TextRenderer text, int vw, int vh,
                               int x, int y, int w, int h,
                               string label, string value, float fraction,
                               Vector4 ink, Vector4 trackBg, Vector4 trackEm)
    {
        bars.DrawRect(vw, vh, x, y, w, h, trackBg);
        if (fraction > 0f)
        {
            int fw = (int)(w * MathF.Max(0f, MathF.Min(1f, fraction)));
            if (fw > 0) bars.DrawRect(vw, vh, x, y, fw, h, ProgressFill);
        }
        bars.DrawBorder(vw, vh, x, y, w, h, trackEm);
        text.DrawString(vw, vh, label, x + 4, y + 1, ink);
        int valW = text.MeasureWidth(value);
        text.DrawString(vw, vh, value, x + w - 4 - valW, y + 1, ink);
    }

    /// <summary>Phase 21-SC-INV-B (round 4) — vertical HP/MP vial. Fills from
    /// the bottom up, matching how DS1's vials drain. The fill uses a
    /// horizontal gradient (bright center, dim edges) so the pool reads as a
    /// glassy tube rather than a flat block.</summary>
    static void DrawVerticalVial(BarRenderer bars, int vw, int vh, int x, int y, int w, int h,
                                 float current, float max,
                                 Vector4 fill, Vector4 bg, Vector4 outline)
    {
        bars.DrawRect(vw, vh, x, y, w, h, bg);
        if (max > 0f)
        {
            float frac = MathF.Max(0f, MathF.Min(1f, current / max));
            int fillH = (int)(h * frac);
            if (fillH > 0)
                bars.DrawHGradientFill(vw, vh, x, y + (h - fillH), w, fillH, fill);
        }
        bars.DrawBorder(vw, vh, x, y, w, h, outline);
    }

    /// <summary>Phase 21-SC-INV-B (round 2) — boxed skill row (label left,
    /// level right) with its own border so each named skill reads as a slot.</summary>
    static void DrawSkillBox(BarRenderer bars, TextRenderer text, int vw, int vh,
                             int x, int y, int w, int h,
                             string name, int level, float fraction, Vector4 ink,
                             Vector4 bg, Vector4 em)
    {
        bars.DrawRect(vw, vh, x, y, w, h, bg);
        if (fraction > 0f)
        {
            int fw = (int)(w * MathF.Max(0f, MathF.Min(1f, fraction)));
            if (fw > 0) bars.DrawRect(vw, vh, x, y, fw, h, ProgressFill);
        }
        bars.DrawBorder(vw, vh, x, y, w, h, em);
        text.DrawString(vw, vh, name, x + 4, y + (h - 8) / 2, ink);
        var lvl = level.ToString();
        text.DrawString(vw, vh, lvl, x + w - 4 - text.MeasureWidth(lvl), y + (h - 8) / 2, ink);
    }

    static void DrawKv(BarRenderer bars, TextRenderer text, int vw, int vh,
                       int px, int y, string key, string value, Vector4 ink)
    {
        text.DrawString(vw, vh, key, px + Padding, y, ink);
        text.DrawString(vw, vh, value,
                        px + PanelWidth - Padding - text.MeasureWidth(value), y, ink);
    }
}
