using System.Globalization;
using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>One line of an NPC's dialogue. DS1 conversations are flat lists of
/// <c>[text*]</c> blocks under a <c>[conversation_&lt;key&gt;]</c> root; we keep
/// the order field explicit because DS1 authors leave it off the last node when
/// the line is a "decline" / wrap-up branch (see fh_r1's <c>conversation_edgaar</c>).
/// <see cref="Order"/> is <c>-1</c> for those untagged tail nodes.</summary>
public sealed class DialogueNode
{
    public int Order { get; init; } = -1;
    public string Text { get; init; } = "";
    public string? VoiceSample { get; init; }
    public string Choice { get; init; } = "";   // "more" advances; "" = continue/end
    public string? ActivateQuest { get; init; } // emit on accept; Phase 20b consumes
    /// <summary>SC-QUEST-TURNIN — authored <c>complete_quest*</c>: playing this
    /// node IS the quest turn-in (4 quests author it: apprentice_books,
    /// open_gate, water_dungeon + fort_kroth's deactivate).</summary>
    public string? CompleteQuest { get; init; }
    /// <summary>SC-QUEST-TURNIN — authored <c>deactivate_quest*</c>: the quest
    /// is withdrawn from the journal without a completion fanfare.</summary>
    public string? DeactivateQuest { get; init; }
    public bool IsQuestDialog { get; init; }    // "Accept" / "Decline" buttons
    public bool IsNonInteractive { get; init; } // narrator banners; auto-close on click

    /// <summary>Phase 26 — DS1 recruitment: a text node with
    /// <c>choice = potential_member</c> ("...can I come along?") is the
    /// join offer. It renders the same Accept/Decline fork as a quest
    /// dialog; Accept adds the speaker to the party.</summary>
    public bool IsRecruitOffer =>
        string.Equals(Choice, "potential_member", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the node presents an Accept/Decline choice
    /// (quest accept OR recruit offer) rather than a plain Continue.</summary>
    public bool IsChoiceFork => IsQuestDialog || IsRecruitOffer;
}

/// <summary>One named conversation tree. Multiple actors can reference the same
/// key (the narrator + intro speech is one) so these are pooled at the region
/// level rather than copied onto each actor.</summary>
public sealed class ConversationDef
{
    public string Key { get; init; } = "";
    public IReadOnlyList<DialogueNode> Nodes { get; init; } = Array.Empty<DialogueNode>();
}

/// <summary>
/// Loads a region's <c>conversations/conversations.gas</c> into a keyed dictionary
/// of <see cref="ConversationDef"/>. Region-scoped: a save in <c>fh_r1</c> sees
/// only fh_r1's conversations, which matches DS1's per-region layout and keeps
/// the keyspace small enough that name collisions across regions don't matter.
///
/// Each root block is shaped <c>[conversation_KEY] { [text*] { ... } [text*] { ... } }</c>.
/// The leading <c>conversation_</c> prefix is stripped from the key when we
/// store it because the actor's <c>[conversation][conversations]</c> block
/// references the same name with the prefix — keeping it once in the key avoids
/// double-prefixing on lookup.
/// </summary>
public static class ConversationStore
{
    public static (IReadOnlyDictionary<string, ConversationDef> Conversations,
                   IReadOnlyList<string> Diagnostics) Load(TankReader tank, string regionPath)
    {
        var diags = new List<string>();
        var norm = regionPath.TrimEnd('/');
        var path = norm + "/conversations/conversations.gas";

        var dict = new Dictionary<string, ConversationDef>(StringComparer.OrdinalIgnoreCase);
        if (!tank.TryGetFile(path, out _))
        {
            diags.Add($"{path}: not present — region has no scripted dialogue");
            return (dict, diags);
        }

        byte[] bytes;
        try { bytes = tank.ExtractToMemory(path); }
        catch (Exception ex) { diags.Add($"{path}: extract failed: {ex.Message}"); return (dict, diags); }

        GasDocument doc;
        try { doc = GasDocument.Load(bytes); }
        catch (Exception ex) { diags.Add($"{path}: parse failed: {ex.Message}"); return (dict, diags); }

        ParseDocument(doc, dict);
        return (dict, diags);
    }

    /// <summary>Parser entry-point for already-loaded gas documents — used by
    /// the dialogue self-test so it can verify the node-shape rules without a
    /// real tank on disk. Does the same work as <see cref="Load"/> minus the
    /// tank lookup and diagnostics list.</summary>
    public static IReadOnlyDictionary<string, ConversationDef> LoadFromDocument(GasDocument doc)
    {
        var dict = new Dictionary<string, ConversationDef>(StringComparer.OrdinalIgnoreCase);
        ParseDocument(doc, dict);
        return dict;
    }

    private static void ParseDocument(GasDocument doc, Dictionary<string, ConversationDef> dict)
    {
        foreach (var root in doc.Roots)
        {
            // Header is the bare key — no [t:type,n:name] prefix on conversations,
            // unlike actor instance roots.
            var key = root.Header.Trim();
            if (string.IsNullOrEmpty(key)) continue;

            var nodes = new List<DialogueNode>();
            foreach (var child in root.Children)
            {
                if (!child.Header.StartsWith("text", StringComparison.OrdinalIgnoreCase)) continue;

                int order = -1;
                string text = "", choice = "";
                string? sample = null, activateQuest = null;
                string? completeQuest = null, deactivateQuest = null;
                bool questDialog = false, nis = false;

                foreach (var attr in child.Attributes)
                {
                    // DS1 sometimes authors entries with a trailing `*` to mark
                    // multi-valued slots (`activate_quest* = …;`); the parser
                    // keeps that on the name. Strip it for the lookup so both
                    // shapes match the same field.
                    var name = attr.Name.TrimEnd('*');
                    var raw  = attr.Value;
                    if (NameEq(name, "order"))
                    {
                        if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var o))
                            order = o;
                    }
                    else if (NameEq(name, "screen_text")) text = StripQuotes(raw);
                    else if (NameEq(name, "sample"))      sample = raw.Trim();
                    else if (NameEq(name, "choice"))      choice = raw.Trim().ToLowerInvariant();
                    else if (NameEq(name, "activate_quest")) activateQuest = raw.Trim();
                    else if (NameEq(name, "complete_quest")) completeQuest = raw.Trim();
                    else if (NameEq(name, "deactivate_quest")) deactivateQuest = raw.Trim();
                    else if (NameEq(name, "quest_dialog"))   questDialog = ParseBool(raw);
                    else if (NameEq(name, "nis"))            nis = ParseBool(raw);
                }

                if (text.Length == 0) continue; // empty placeholders skipped
                nodes.Add(new DialogueNode
                {
                    Order            = order,
                    Text             = text,
                    VoiceSample      = sample,
                    Choice           = choice,
                    ActivateQuest    = activateQuest,
                    CompleteQuest    = completeQuest,
                    DeactivateQuest  = deactivateQuest,
                    IsQuestDialog    = questDialog,
                    IsNonInteractive = nis,
                });
            }

            // DS1 nodes with `order` come first, in numeric order; tail nodes
            // without `order` follow. Stable sort preserves authoring order
            // within each bucket so the no-order-tail story still reads as
            // authored.
            nodes.Sort((a, b) =>
            {
                int ao = a.Order < 0 ? int.MaxValue : a.Order;
                int bo = b.Order < 0 ? int.MaxValue : b.Order;
                return ao.CompareTo(bo);
            });

            dict[key] = new ConversationDef { Key = key, Nodes = nodes };
        }
    }

    static bool NameEq(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static bool ParseBool(string s)
    {
        var t = s.Trim().ToLowerInvariant();
        return t is "true" or "yes" or "1";
    }

    /// <summary>DS1 GAS strings come through the parser with their surrounding
    /// double-quotes stripped already, but copy-pasted ones occasionally arrive
    /// quoted; tolerate both. Newlines inside the original string survived as
    /// literal <c>\n</c>; the parser already decodes those.</summary>
    static string StripQuotes(string s)
    {
        var t = s.Trim();
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"') t = t[1..^1];
        return t;
    }

    /// <summary>Helper for pulling conversation keys off an actor instance's
    /// <c>[conversation][conversations]</c> block. Returns an empty list when
    /// the block is absent or has no entries — most actors aren't talkable.</summary>
    public static IReadOnlyList<string> KeysFromInstance(GasNode instanceNode)
    {
        if (instanceNode is null) return Array.Empty<string>();
        var conversation = instanceNode.Children.FirstOrDefault(c =>
            string.Equals(c.Header, "conversation", StringComparison.OrdinalIgnoreCase));
        if (conversation is null) return Array.Empty<string>();
        var inner = conversation.Children.FirstOrDefault(c =>
            string.Equals(c.Header, "conversations", StringComparison.OrdinalIgnoreCase));
        if (inner is null) return Array.Empty<string>();

        var list = new List<string>();
        foreach (var attr in inner.Attributes)
        {
            // Authored as `* = conversation_edgaar;` — the parser preserves the
            // wildcard `*` as the attribute name. Value is the conversation key.
            var v = attr.Value.Trim();
            if (v.Length > 0) list.Add(v);
        }
        return list;
    }
}
