using System.Numerics;
using SiegeFX.Core.Assets;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Conversation overlay rendered to DS1's authored chrome
/// (<c>/ui/interfaces/backend/dialogue_box/dialogue_box.gas</c>): a top-centre
/// cpbox nine-slice frame with a recessed cpbox text panel, left-justified
/// copperplate speech, a corner X, and context buttons. Geometry is the
/// authored 640×480 reference scaled by viewport height.
///
/// We page a <see cref="ConversationDef"/> one node at a time (DS1 scrolls the
/// whole speech; paging keeps each voice line paired with its own screen).
/// Buttons follow the authored groups:
/// <list type="bullet">
///   <item><c>quest_dialog</c> OR <c>choice = potential_member</c> →
///     "Accept" / "Decline" (group <c>potential_member</c>). Accept emits the
///     node's <c>activate_quest</c> and, for a recruit offer, flags
///     <see cref="PendingRecruit"/> so the host adds the speaker to the party.
///     Decline advances to the polite-decline tail.</item>
///   <item>Anything else → a single bottom-right button, "Continue" mid-thread
///     and "Close" on the last line.</item>
/// </list>
/// The corner X dismisses at any time (a fork closed this way is "no answer").
/// If the speech overflows the panel we clip; shipped lines fit.
/// </summary>
public sealed class DialoguePanel
{
    public bool IsOpen { get; private set; }
    public string? PendingQuestActivation { get; private set; }

    /// <summary>Phase 26 — set true when the player hits Accept on a recruit
    /// offer node (<c>choice = potential_member</c>). The host consumes it via
    /// <see cref="ConsumePendingRecruit"/> to add the speaker to the party.</summary>
    public bool PendingRecruit { get; private set; }

    // Authentic DS1 conversation chrome — /ui/interfaces/backend/
    // dialogue_box/dialogue_box.gas, authored in a 640×480 reference and
    // top-centre anchored. A cpbox nine-slice frame wraps a recessed cpbox
    // text panel; the copperplate text is left-justified; a recruit/quest
    // fork shows Accept/Decline at fixed rects, other lines show a single
    // Close button bottom-right, and the corner X always dismisses. Rects
    // are (x0,y0,x1,y1) in the 640×480 reference.
    static readonly (int x0, int y0, int x1, int y1) RFrame    = (187,   5, 602, 190);
    static readonly (int x0, int y0, int x1, int y1) RTextBg   = (201,  12, 564, 137);
    static readonly (int x0, int y0, int x1, int y1) RText     = (204,  15, 545, 134);
    static readonly (int x0, int y0, int x1, int y1) RAccept   = (205, 145, 297, 161);
    static readonly (int x0, int y0, int x1, int y1) RDecline  = (308, 145, 400, 161);
    static readonly (int x0, int y0, int x1, int y1) RCloseBtn = (471, 165, 563, 181);
    static readonly (int x0, int y0, int x1, int y1) RCloseX   = (581,  10, 597,  26);

    private readonly MenuButton _accept   = new("Accept",   0, 0, 10, 10);
    private readonly MenuButton _decline  = new("Decline",  0, 0, 10, 10);
    private readonly MenuButton _more     = new("Continue", 0, 0, 10, 10);
    private readonly MenuButton _continue = new("Continue", 0, 0, 10, 10);
    private readonly MenuButton _closeX   = new("X",        0, 0, 10, 10);

    static float Scale(int viewportH) => viewportH / 480f;
    static (int x, int y, int w, int h) Px((int x0, int y0, int x1, int y1) r, float s, int originX)
        => (originX + (int)MathF.Round(r.x0 * s), (int)MathF.Round(r.y0 * s),
            (int)MathF.Round((r.x1 - r.x0) * s), (int)MathF.Round((r.y1 - r.y0) * s));
    static void Place(MenuButton b, (int x, int y, int w, int h) p)
    { b.X = p.x; b.Y = p.y; b.Width = p.w; b.Height = p.h; }

    private ConversationDef? _conv;
    private int _index;

    /// <summary>SC-QUEST-UI-D — the conversation currently on screen, so the
    /// journal can log its spoken text onto a quest at the acceptance edge.</summary>
    public ConversationDef? CurrentConversation => _conv;

    /// <summary>SC-QUEST-UI-D — the conversation that was on screen when the
    /// player last hit Accept. Accepting closes the panel (nulling
    /// <see cref="_conv"/>) in the same call the caller then reads, so the
    /// live reference is already gone; this snapshot survives the Close() so
    /// the journal can still log what the player heard.</summary>
    public ConversationDef? LastQuestConversation { get; private set; }

    /// <summary>Present a fresh conversation. Resets the cursor and clears any
    /// stale press state on the buttons. <paramref name="speakerName"/> is
    /// accepted for call-site compatibility but not drawn — DS1's
    /// dialogue_box.gas has no speaker-name control (the portrait/voice
    /// identifies the speaker).</summary>
    public void Open(string speakerName, ConversationDef conv)
    {
        _ = speakerName;
        _conv = conv;
        _index = 0;
        PendingQuestActivation = null;
        PendingRecruit = false;
        LastQuestConversation = null;
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
        _closeX.CancelPress();
    }

    private DialogueNode? CurrentNode =>
        _conv is null || _index < 0 || _index >= _conv.Nodes.Count
            ? null : _conv.Nodes[_index];

    private void Layout(int viewportW, int viewportH)
    {
        // Map the authored 640×480 button rects to screen pixels, top-centre
        // anchored, so hit-testing matches what the player sees.
        float s = Scale(viewportH);
        int originX = (viewportW - (int)MathF.Round(640f * s)) / 2;

        Place(_accept,  Px(RAccept,   s, originX));
        Place(_decline, Px(RDecline,  s, originX));
        var closeBtn = Px(RCloseBtn, s, originX);
        Place(_more,     closeBtn);
        Place(_continue, closeBtn);
        Place(_closeX,   Px(RCloseX, s, originX));

        // DS1 has no paging button — the whole speech scrolls. We page by
        // node, so the bottom-right button reads "Continue" mid-thread and
        // "Close" on the last line.
        bool last = _conv is null || _index >= _conv.Nodes.Count - 1;
        _continue.Label = last ? "Close" : "Continue";
        _more.Label = "Continue";
    }

    public void OnMouseMove(int px, int py)
    {
        if (!IsOpen) return;
        _closeX.UpdateHover(px, py);
        var node = CurrentNode;
        if (node is { IsChoiceFork: true })
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
        _closeX.TryPress(px, py);
        var node = CurrentNode;
        if (node is { IsChoiceFork: true })
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
        // Corner X — always dismisses the conversation (a fork closed this
        // way counts as no answer: no recruit, no quest activation).
        if (_closeX.Release(px, py)) { Close(); return true; }
        var node = CurrentNode;

        if (node is { IsChoiceFork: true })
        {
            if (_accept.Release(px, py))
            {
                PendingQuestActivation = node.ActivateQuest;
                // Phase 26 — recruit offer accepted: flag it so the host adds
                // the speaker to the party. A join node can also carry
                // activate_quest (Gyorn's join fires quest_gyorn_seek_overseer),
                // so both hooks fire off the one Accept.
                if (node.IsRecruitOffer) PendingRecruit = true;
                LastQuestConversation = _conv; // snapshot before Close() nulls it
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
            if (!_conv.Nodes[i].IsChoiceFork) { _index = i; return; }
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

    /// <summary>Phase 26 — one-shot read of the recruit flag. The host calls
    /// this once after a dialogue closes; a true return means the player
    /// accepted the join offer and the speaker should enter the party.</summary>
    public bool ConsumePendingRecruit()
    {
        var r = PendingRecruit;
        PendingRecruit = false;
        return r;
    }

    public void Draw(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                     Func<string, GlTexture?>? guiTex, int viewportW, int viewportH)
    {
        if (!IsOpen) return;
        var node = CurrentNode;
        if (node is null) { Close(); return; }
        Layout(viewportW, viewportH);

        float s = Scale(viewportH);
        int originX = (viewportW - (int)MathF.Round(640f * s)) / 2;
        var ink = new Vector4(0.90f, 0.86f, 0.74f, 1f);

        // cpbox nine-slice frame + recessed text panel — DS1's dialogue_main_bg
        // / dialogue_text_bg (both common_template = cpbox). Falls back to a
        // flat panel when the chrome textures aren't resolvable so the box is
        // still legible in the texture-load diagnostic case.
        void Cpbox((int x0, int y0, int x1, int y1) r)
        {
            var p = Px(r, s, originX);
            if (icons is not null && guiTex is not null)
                NinePatch.DrawCpbox(icons, guiTex, viewportW, viewportH, p.x, p.y, p.w, p.h, Vector4.One);
            else
            {
                bars.DrawRect(viewportW, viewportH, p.x, p.y, p.w, p.h, new Vector4(0.08f, 0.08f, 0.10f, 0.96f));
                bars.DrawBorder(viewportW, viewportH, p.x, p.y, p.w, p.h, new Vector4(0.667f, 0.655f, 0.557f, 1f));
            }
        }

        Cpbox(RFrame);
        Cpbox(RTextBg);

        // Left-justified, word-wrapped speech inside the recessed panel
        // (DS1's text_box: justify = left, copperplate-light).
        var tr = Px(RText, s, originX);
        int lineH = (text.HasFont ? text.Font!.Height : 14) + 2;
        int maxLines = Math.Max(1, tr.h / lineH);
        int line = 0;
        foreach (var rawLine in node.Text.Replace("\\n", "\n").Split('\n'))
        {
            foreach (var visual in WrapLine(rawLine, text, tr.w))
            {
                if (line >= maxLines) break;
                text.DrawString(viewportW, viewportH, visual, tr.x, tr.y + line * lineH, ink);
                line++;
            }
            if (line >= maxLines) break;
        }

        // Context buttons — Accept/Decline for a recruit or quest fork
        // (group = potential_member), a single Close/Continue otherwise.
        if (node.IsChoiceFork)
        {
            _accept.Draw(bars, text, viewportW, viewportH);
            _decline.Draw(bars, text, viewportW, viewportH);
        }
        else if (node.Choice == "more")
            _more.Draw(bars, text, viewportW, viewportH);
        else
            _continue.Draw(bars, text, viewportW, viewportH);

        // Corner X (button_x) — always present.
        _closeX.Draw(bars, text, viewportW, viewportH);
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
