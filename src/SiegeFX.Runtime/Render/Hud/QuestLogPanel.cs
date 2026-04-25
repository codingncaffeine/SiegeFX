using System.Numerics;
using SiegeFX.Core.Actors;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Phase 20b — read-only journal overlay (toggle with 'L'). Lists active quests
/// first, then completed/failed in a separate section. DS1's journal screen
/// shows objective text + cleared flags per quest; we don't have authored
/// objectives plumbed yet, so this slice just displays the GAS keys grouped by
/// state. Objective resolution lands in Phase 20c when the QuestStore arrives.
/// Keys come through prettified (underscores → spaces, lower → title-ish) so a
/// raw <c>quest_edgaar_basement</c> reads as <i>Edgaar Basement</i> in the
/// list — close enough for the placeholder UI to be glanceable.
/// </summary>
public static class QuestLogPanel
{
    private const int PanelW = 480;
    private const int PanelH = 360;
    private const int Padding = 14;
    private const int TitleH  = 22;
    private const int LineGap = 4;

    public static void Draw(BarRenderer bars, TextRenderer text, int viewportW, int viewportH,
                            QuestJournal journal)
    {
        int px = (viewportW - PanelW) / 2;
        int py = (viewportH - PanelH) / 2;

        var dim    = new Vector4(0f, 0f, 0f, 0.55f);
        var panel  = new Vector4(0.08f, 0.08f, 0.10f, 0.92f);
        var title  = new Vector4(0.16f, 0.13f, 0.10f, 1f);
        var border = new Vector4(0.78f, 0.66f, 0.42f, 1f);
        var ink    = new Vector4(0.92f, 0.88f, 0.78f, 1f);
        var dimInk = new Vector4(0.55f, 0.50f, 0.42f, 1f);
        var hdr    = new Vector4(1.00f, 0.85f, 0.40f, 1f);

        bars.DrawRect  (viewportW, viewportH, 0, 0, viewportW, viewportH, dim);
        bars.DrawRect  (viewportW, viewportH, px, py, PanelW, PanelH, panel);
        bars.DrawRect  (viewportW, viewportH, px, py, PanelW, TitleH, title);
        bars.DrawBorder(viewportW, viewportH, px, py, PanelW, PanelH, border);
        bars.DrawBorder(viewportW, viewportH, px, py + TitleH, PanelW, 1, border);

        int active = 0, closed = 0;
        foreach (var e in journal.Entries)
        {
            if (e.State == QuestState.Active) active++;
            else if (e.State == QuestState.Completed || e.State == QuestState.Failed) closed++;
        }
        var titleStr = $"Quest Log  (active {active}, closed {closed})";
        int sw = text.MeasureWidth(titleStr);
        text.DrawString(viewportW, viewportH, titleStr, px + (PanelW - sw) / 2, py + 5, ink);

        int lineH = (text.HasFont ? text.Font!.Height : 14) + LineGap;
        int x = px + Padding;
        int y = py + TitleH + Padding;
        int maxY = py + PanelH - Padding;

        if (active > 0)
        {
            text.DrawString(viewportW, viewportH, "Active", x, y, hdr);
            y += lineH;
            foreach (var e in journal.Active)
            {
                if (y + lineH > maxY) break;
                text.DrawString(viewportW, viewportH, "  • " + Pretty(e.Key), x, y, ink);
                y += lineH;
            }
            y += LineGap;
        }

        if (closed > 0 && y + lineH <= maxY)
        {
            text.DrawString(viewportW, viewportH, "Completed", x, y, hdr);
            y += lineH;
            foreach (var e in journal.Entries)
            {
                if (e.State != QuestState.Completed && e.State != QuestState.Failed) continue;
                if (y + lineH > maxY) break;
                var prefix = e.State == QuestState.Failed ? "  ✗ " : "  ✓ ";
                text.DrawString(viewportW, viewportH, prefix + Pretty(e.Key), x, y, dimInk);
                y += lineH;
            }
        }

        if (active == 0 && closed == 0)
        {
            const string empty = "No quests yet — find an NPC and accept one.";
            int ew = text.MeasureWidth(empty);
            text.DrawString(viewportW, viewportH, empty,
                            px + (PanelW - ew) / 2, py + PanelH / 2 - 6, dimInk);
        }

        const string foot = "Press L to close";
        int fw = text.MeasureWidth(foot);
        text.DrawString(viewportW, viewportH, foot,
                        px + (PanelW - fw) / 2, py + PanelH - lineH, dimInk);
    }

    /// <summary>Strip the conventional <c>quest_</c> prefix and convert remaining
    /// underscores to spaces with a title-case hint on the first letter of each
    /// word. DS1 keys are lowercase-snake (<c>quest_edgaar_basement</c>); cheap
    /// transformation gets us a readable label without an authored screen-name
    /// pipeline (which 20c will actually need for objectives anyway).</summary>
    private static string Pretty(string key)
    {
        var s = key;
        if (s.StartsWith("quest_", StringComparison.OrdinalIgnoreCase)) s = s.Substring(6);
        else if (s.StartsWith("Quest_", StringComparison.Ordinal))      s = s.Substring(6);
        s = s.Replace('_', ' ').Trim();
        if (s.Length == 0) return key;
        var chars = s.ToCharArray();
        bool atStart = true;
        for (int i = 0; i < chars.Length; i++)
        {
            if (atStart && char.IsLetter(chars[i])) { chars[i] = char.ToUpperInvariant(chars[i]); atStart = false; }
            else if (chars[i] == ' ') atStart = true;
        }
        return new string(chars);
    }
}
