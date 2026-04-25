using System.Numerics;
using SiegeFX.Core.Assets;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Modal dialogue overlay. Walks a <see cref="ConversationDef"/> one node at a
/// time and renders the speaker name, the current node's text (word-wrapped to
/// the panel width), and the action buttons that drive node-to-node advance.
///
/// Buttons follow DS1's <c>dialogue_box.gas</c> shape:
/// <list type="bullet">
///   <item><c>choice == "more"</c> → single "More" advances to the next node.</item>
///   <item><c>quest_dialog == true</c> → "Accept" / "Decline" branch. Accept emits
///     <see cref="QuestActivated"/> with the node's <c>activate_quest</c>; Decline
///     advances to the next un-quest-flagged node (typically the no-order tail
///     authored as the polite-decline reply) before closing.</item>
///   <item>Anything else → "Continue" advances if there's a next node, otherwise closes.</item>
/// </list>
///
/// The panel is bottom-anchored — DS1's authored layout puts the buttons near
/// the bottom of the screen and the wrapped text scrolls upward from there.
/// We don't actually scroll yet; if a line overflows we clip it and rely on the
/// shipped lines fitting (most do — DS1 keeps single-screen dialogue tight).
/// </summary>
public sealed class DialoguePanel
{
    public bool IsOpen { get; private set; }
    public string? PendingQuestActivation { get; private set; }

    private const int PanelW = 560;
    private const int PanelH = 200;
    private const int Padding = 14;
    private const int TitleH  = 22;
    private const int ButtonW = 110;
    private const int ButtonH = 26;
    private const int ButtonGap = 12;
    private const int BottomMargin = 32;

    private readonly MenuButton _accept   = new("Accept",   0, 0, ButtonW, ButtonH);
    private readonly MenuButton _decline  = new("Decline",  0, 0, ButtonW, ButtonH);
    private readonly MenuButton _more     = new("More",     0, 0, ButtonW, ButtonH);
    private readonly MenuButton _continue = new("Continue", 0, 0, ButtonW, ButtonH);

    private string _speaker = "";
    private ConversationDef? _conv;
    private int _index;

    /// <summary>Present a fresh conversation. Resets the cursor and clears any
    /// stale press state on the buttons.</summary>
    public void Open(string speakerName, ConversationDef conv)
    {
        _speaker = string.IsNullOrWhiteSpace(speakerName) ? "Speaker" : speakerName;
        _conv = conv;
        _index = 0;
        PendingQuestActivation = null;
        IsOpen = conv.Nodes.Count > 0;
        CancelAllPresses();
    }

    public void Close()
    {
        IsOpen = false;
        _conv = null;
        CancelAllPresses();
    }

    private void CancelAllPresses()
    {
        _accept.CancelPress();
        _decline.CancelPress();
        _more.CancelPress();
        _continue.CancelPress();
    }

    private DialogueNode? CurrentNode =>
        _conv is null || _index < 0 || _index >= _conv.Nodes.Count
            ? null : _conv.Nodes[_index];

    private void Layout(int viewportW, int viewportH)
    {
        int px = (viewportW - PanelW) / 2;
        int py = viewportH - PanelH - BottomMargin;
        int by = py + PanelH - Padding - ButtonH;

        // Two-button layout when the active node is a quest fork; one-button
        // otherwise. Center the active button group inside the panel.
        var node = CurrentNode;
        if (node is { IsQuestDialog: true })
        {
            int totalW = ButtonW * 2 + ButtonGap;
            int bx = px + (PanelW - totalW) / 2;
            _accept.X  = bx;                       _accept.Y  = by;
            _decline.X = bx + ButtonW + ButtonGap; _decline.Y = by;
        }
        else
        {
            int bx = px + (PanelW - ButtonW) / 2;
            _more.X     = bx; _more.Y     = by;
            _continue.X = bx; _continue.Y = by;
        }
    }

    public void OnMouseMove(int px, int py)
    {
        if (!IsOpen) return;
        var node = CurrentNode;
        if (node is { IsQuestDialog: true })
        {
            _accept.UpdateHover(px, py);
            _decline.UpdateHover(px, py);
        }
        else if (node is { Choice: "more" })
        {
            _more.UpdateHover(px, py);
        }
        else
        {
            _continue.UpdateHover(px, py);
        }
    }

    public bool OnMouseDown(int px, int py)
    {
        if (!IsOpen) return false;
        var node = CurrentNode;
        if (node is { IsQuestDialog: true })
        {
            _accept.TryPress(px, py);
            _decline.TryPress(px, py);
        }
        else if (node is { Choice: "more" })
        {
            _more.TryPress(px, py);
        }
        else
        {
            _continue.TryPress(px, py);
        }
        return true;
    }

    public bool OnMouseUp(int px, int py)
    {
        if (!IsOpen) return false;
        var node = CurrentNode;

        if (node is { IsQuestDialog: true })
        {
            if (_accept.Release(px, py))
            {
                PendingQuestActivation = node.ActivateQuest;
                Close();
                return true;
            }
            if (_decline.Release(px, py))
            {
                AdvanceToDeclineTail();
                return true;
            }
        }
        else if (node is { Choice: "more" })
        {
            if (_more.Release(px, py)) { Advance(); return true; }
        }
        else
        {
            if (_continue.Release(px, py)) { Advance(); return true; }
        }
        return true; // consume the up-event regardless so it doesn't fall through
    }

    private void Advance()
    {
        if (_conv is null) { Close(); return; }
        _index++;
        if (_index >= _conv.Nodes.Count) Close();
    }

    /// <summary>Decline path: jump to the first remaining node that *isn't* a
    /// quest-dialog node (the no-order tail in DS1's authoring convention).
    /// If none exists, close immediately.</summary>
    private void AdvanceToDeclineTail()
    {
        if (_conv is null) { Close(); return; }
        for (int i = _index + 1; i < _conv.Nodes.Count; i++)
        {
            if (!_conv.Nodes[i].IsQuestDialog) { _index = i; return; }
        }
        Close();
    }

    /// <summary>Caller pulls the activated quest key once after Accept fires.
    /// One-shot to keep the quest hook non-idempotent — the same conversation
    /// re-opened wouldn't auto-fire the quest a second time.</summary>
    public string? ConsumePendingQuestActivation()
    {
        var q = PendingQuestActivation;
        PendingQuestActivation = null;
        return q;
    }

    public void Draw(BarRenderer bars, TextRenderer text, int viewportW, int viewportH)
    {
        if (!IsOpen) return;
        var node = CurrentNode;
        if (node is null) { Close(); return; }
        Layout(viewportW, viewportH);

        int px = (viewportW - PanelW) / 2;
        int py = viewportH - PanelH - BottomMargin;

        var dim    = new Vector4(0f, 0f, 0f, 0.45f);
        var panel  = new Vector4(0.08f, 0.08f, 0.10f, 0.94f);
        var title  = new Vector4(0.16f, 0.13f, 0.10f, 1f);
        var border = new Vector4(0.78f, 0.66f, 0.42f, 1f);
        var ink    = new Vector4(0.92f, 0.88f, 0.78f, 1f);

        bars.DrawRect  (viewportW, viewportH, 0, 0, viewportW, viewportH, dim);
        bars.DrawRect  (viewportW, viewportH, px, py, PanelW, PanelH, panel);
        bars.DrawRect  (viewportW, viewportH, px, py, PanelW, TitleH, title);
        bars.DrawBorder(viewportW, viewportH, px, py, PanelW, PanelH, border);
        bars.DrawBorder(viewportW, viewportH, px, py + TitleH, PanelW, 1, border);

        int sw = text.MeasureWidth(_speaker);
        text.DrawString(viewportW, viewportH, _speaker, px + (PanelW - sw) / 2, py + 5, ink);

        // Wrap node text into the panel interior. Line height comes from the
        // font; we add 2px leading so successive lines breathe.
        int textX = px + Padding;
        int textY = py + TitleH + Padding;
        int wrapW = PanelW - Padding * 2;
        int lineH = (text.HasFont ? text.Font!.Height : 14) + 2;
        int maxLines = (PanelH - TitleH - Padding * 2 - ButtonH - ButtonGap) / lineH;

        int line = 0;
        foreach (var rawLine in node.Text.Replace("\\n", "\n").Split('\n'))
        {
            foreach (var visual in WrapLine(rawLine, text, wrapW))
            {
                if (line >= maxLines) break;
                text.DrawString(viewportW, viewportH, visual,
                                textX, textY + line * lineH, ink);
                line++;
            }
            if (line >= maxLines) break;
        }

        if (node.IsQuestDialog)
        {
            _accept.Draw(bars, text, viewportW, viewportH);
            _decline.Draw(bars, text, viewportW, viewportH);
        }
        else if (node.Choice == "more")
        {
            _more.Draw(bars, text, viewportW, viewportH);
        }
        else
        {
            _continue.Draw(bars, text, viewportW, viewportH);
        }
    }

    /// <summary>Word-wrap to a pixel width using the active font. Falls back to
    /// the raw line when measurement isn't possible (no font yet) so the panel
    /// is still readable in the "font failed to load" diagnostic case.</summary>
    private static IEnumerable<string> WrapLine(string raw, TextRenderer text, int maxWidthPx)
    {
        if (string.IsNullOrEmpty(raw)) { yield return ""; yield break; }
        if (!text.HasFont) { yield return raw; yield break; }

        var words = raw.Split(' ');
        var line  = "";
        foreach (var w in words)
        {
            var probe = line.Length == 0 ? w : line + " " + w;
            if (text.MeasureWidth(probe) <= maxWidthPx) { line = probe; continue; }
            if (line.Length > 0) yield return line;
            line = w;
        }
        if (line.Length > 0) yield return line;
    }
}
