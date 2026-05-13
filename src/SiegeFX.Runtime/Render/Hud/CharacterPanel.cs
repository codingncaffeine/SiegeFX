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

    public bool IsPointInPanel(int x, int y) =>
        x >= OriginX && y >= OriginY &&
        x <  OriginX + PanelWidth &&
        y <  OriginY + PanelHeight;

    public void Draw(BarRenderer bars, TextRenderer text,
                     int viewportW, int viewportH,
                     string playerName,
                     Actor? player,
                     PlayerProgression? progression,
                     ActorStats attackerStats,
                     int armorRating,
                     float skillXpFraction = 0f,
                     IconRenderer? icons = null,
                     GlTexture? portraitIcon = null)
    {
        int px = OriginX, py = OriginY;
        var panel  = new Vector4(0.08f, 0.08f, 0.10f, 0.92f);
        var title  = new Vector4(0.16f, 0.13f, 0.10f, 1f);
        var border = new Vector4(0.72f, 0.74f, 0.78f, 1f); // light grey
        // DS1 panel font is #AAA78E — applied uniformly across labels, headings, and dim copy.
        var ink    = new Vector4(0.667f, 0.655f, 0.557f, 1f);
        var dimInk = new Vector4(0.667f, 0.655f, 0.557f, 1f);
        var headInk = new Vector4(0.667f, 0.655f, 0.557f, 1f);
        var hpFill = new Vector4(0.78f, 0.10f, 0.10f, 1f);
        var mpFill = new Vector4(0.18f, 0.40f, 0.90f, 1f);
        var slotBg = new Vector4(0.04f, 0.04f, 0.05f, 1f);
        var slotEm = new Vector4(0.55f, 0.57f, 0.60f, 1f); // light grey accent
        const int cornerR = 2;

        bars.DrawRoundedRect(viewportW, viewportH, px, py, PanelWidth, PanelHeight, panel, cornerR, cornerR);
        bars.DrawRoundedRect(viewportW, viewportH, px, py, PanelWidth, TitleH, title, cornerR, 0);
        bars.DrawRoundedBorder(viewportW, viewportH, px, py, PanelWidth, PanelHeight, border, cornerR);
        bars.DrawRect(viewportW, viewportH, px + 1, py + TitleH, PanelWidth - 2, 1, border);

        // Title — player name centered. DS1 grays the panel banner; we
        // brighten ours slightly so the creator-typed name reads at glance.
        var nameText = string.IsNullOrEmpty(playerName) ? "ADVENTURER" : playerName.ToUpperInvariant();
        int nameW = text.MeasureWidth(nameText);
        text.DrawString(viewportW, viewportH, nameText,
                        px + (PanelWidth - nameW) / 2, py + 4, headInk);

        int contentTop = py + TitleH + 6;

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
            // auto-grow: STR grows fastest off Melee gains, DEX off Ranged,
            // INT off Combat Magic (DS1 ProportionalGains rows). Until per-
            // skill pools are bound, falls back to skillXpFraction so the
            // creator preview path still shows movement.
            float strFrac = progression?.SkillProgressFraction(SiegeFX.Core.Assets.SkillKind.Melee)       ?? skillXpFraction;
            float dexFrac = progression?.SkillProgressFraction(SiegeFX.Core.Assets.SkillKind.Ranged)      ?? skillXpFraction;
            float intFrac = progression?.SkillProgressFraction(SiegeFX.Core.Assets.SkillKind.CombatMagic) ?? skillXpFraction;
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
