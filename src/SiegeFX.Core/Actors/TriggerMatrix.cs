using System.Globalization;
using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Actors;

/// <summary>
/// Phase 10-SC-1 — DS1's <c>[instance_triggers]</c> matrix language. Templates and
/// region placements both author rows like:
///
/// <code>
/// [common] {
///   [instance_triggers] {
///     [*] {
///       condition* = party_member_within_sphere(6.0, "on_first_enter");
///       action* = send_world_message("we_req_activate", 0x01C0021E, 0f, "default", "", 0);
///       single_shot = true;
///       flip_flop = false;
///     }
///   }
/// }
/// </code>
///
/// Each <c>[*]</c> is a row: a list of conditions (OR'd inside the row), a list of
/// actions, and a few flags. Conditions and actions can be tagged with <c>group(N)</c>
/// to pair them — only actions whose group matches a satisfied condition's group fire.
/// Untagged conditions and actions implicitly belong to group 0.
/// </summary>
public sealed class TriggerMatrix
{
    public IReadOnlyList<TriggerRow> Rows { get; }

    public TriggerMatrix(IReadOnlyList<TriggerRow> rows) { Rows = rows; }

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Total count of authored conditions across all rows. Used by the
    /// region-triggers CLI to print verb coverage.</summary>
    public int ConditionCount
    {
        get { int n = 0; foreach (var r in Rows) n += r.Conditions.Count; return n; }
    }

    public int ActionCount
    {
        get { int n = 0; foreach (var r in Rows) n += r.Actions.Count; return n; }
    }

    /// <summary>Looks up the matrix attached to <paramref name="instance"/>. Walks first
    /// the per-instance GAS node (region special.gas overrides), then the template
    /// specializes chain. Returns null when no <c>[instance_triggers]</c> block exists
    /// anywhere along that chain.</summary>
    public static TriggerMatrix? FromInstanceOrTemplate(
        ActorInstance instance, Template template, TemplateStore store, List<string>? diagnostics = null)
    {
        // DS1 splits choreography between TWO sections with identical row
        // grammar: [instance_triggers] (per placement) and
        // [template_triggers] (every instance of the template —
        // gom_super's death chain, the townstones' quest credit). Only the
        // first ever parsed, so template-authored we_killed rows silently
        // never fired. Each section resolves independently with the same
        // override rule (instance node first, then the specializes chain,
        // first occurrence wins); the two sections' rows then MERGE.
        var rows = new List<TriggerRow>();
        foreach (var section in new[] { "instance_triggers", "template_triggers" })
        {
            if (TryCollectSection(instance.Node, instance.ToString(), section, rows, diagnostics))
                continue;
            for (var t = template; t is not null; t = t.Specializes)
                if (TryCollectSection(t.Node, $"template {t.Name}", section, rows, diagnostics))
                    break;
        }
        return rows.Count == 0 ? null : new TriggerMatrix(rows);
    }

    static bool TryCollectSection(GasNode root, string sourceLabel, string sectionName,
                                  List<TriggerRow> rows, List<string>? diagnostics)
    {
        // Both placement nodes and template nodes nest the matrix under [common]; but a
        // few shipped templates put it at the root. Try both.
        var common = TemplateStore.FindChild(root, "common");
        var triggersNode = common is null ? null : TemplateStore.FindChild(common, sectionName);
        triggersNode ??= TemplateStore.FindChild(root, sectionName);
        if (triggersNode is null) return false;

        foreach (var rowNode in triggersNode.Children)
        {
            // Each [*] block in DS1 maps to one row. Other headers are unexpected; record
            // them as diagnostics rather than skipping silently.
            if (!string.Equals(rowNode.Header, "*", StringComparison.Ordinal))
            {
                diagnostics?.Add($"{sourceLabel}: unexpected header '[{rowNode.Header}]' inside [{sectionName}]");
                continue;
            }
            rows.Add(TriggerRow.Parse(rowNode, sourceLabel, diagnostics));
        }
        return true;
    }
}

/// <summary>One <c>[*]</c> entry inside <c>[instance_triggers]</c>. Holds the parsed
/// conditions, actions, and per-row flags (single_shot, flip_flop, reset_duration,
/// start_active, delay). Runtime state (last fired time, fired-once flag, edge
/// cache) lives separately on <see cref="TriggerInstance"/> so a matrix can be
/// shared by-reference across many placement instances.</summary>
public sealed class TriggerRow
{
    public IReadOnlyList<TriggerCall> Conditions { get; }
    public IReadOnlyList<TriggerCall> Actions { get; }
    public bool FlipFlop { get; }
    public bool SingleShot { get; }
    public bool StartActive { get; }
    public float ResetDuration { get; }
    public float Delay { get; }
    /// <summary>Region-group filter for entered/left conditions; the empty string means
    /// "no occupants_group authored", which is treated as "any" by the runtime.</summary>
    public string OccupantsGroup { get; }

    internal TriggerRow(
        IReadOnlyList<TriggerCall> conditions,
        IReadOnlyList<TriggerCall> actions,
        bool flipFlop, bool singleShot, bool startActive,
        float resetDuration, float delay,
        string occupantsGroup)
    {
        Conditions = conditions;
        Actions = actions;
        FlipFlop = flipFlop;
        SingleShot = singleShot;
        StartActive = startActive;
        ResetDuration = resetDuration;
        Delay = delay;
        OccupantsGroup = occupantsGroup;
    }

    public static TriggerRow Parse(GasNode rowNode, string sourceLabel, List<string>? diagnostics)
    {
        var conditions = new List<TriggerCall>();
        var actions = new List<TriggerCall>();
        bool flipFlop = false, singleShot = false, startActive = true;
        float resetDuration = 0f, delay = 0f;
        string occupantsGroup = "";

        foreach (var attr in rowNode.Attributes)
        {
            // condition* / action* — the asterisk marks repeating multi-value keys. The
            // parser stores each repeat as its own attribute entry (in declaration order),
            // so iterating preserves grouping.
            if (string.Equals(attr.Name, "condition*", StringComparison.Ordinal))
            {
                if (TriggerCall.TryParse(attr.Value, sourceLabel, diagnostics, out var call))
                    conditions.Add(call);
                continue;
            }
            if (string.Equals(attr.Name, "action*", StringComparison.Ordinal))
            {
                if (TriggerCall.TryParse(attr.Value, sourceLabel, diagnostics, out var call))
                    actions.Add(call);
                continue;
            }
            // Per-row flags — DS1 uses `b flag = true;` form. Type tag handled by the
            // GAS parser; we just look at the trimmed value.
            switch (attr.Name.ToLowerInvariant())
            {
                case "flip_flop":       flipFlop = ParseBool(attr.Value, flipFlop); break;
                case "single_shot":     singleShot = ParseBool(attr.Value, singleShot); break;
                case "start_active":    startActive = ParseBool(attr.Value, startActive); break;
                case "reset_duration":  resetDuration = ParseFloat(attr.Value, resetDuration); break;
                case "delay":           delay = ParseFloat(attr.Value, delay); break;
                case "occupants_group": occupantsGroup = attr.Value; break;
                // Other authored fields (multi_player, single_player, can_self_destruct,
                // dev_instance_text) are ignored at runtime — they're authoring hints
                // for the editor, not gameplay state.
            }
        }
        return new TriggerRow(conditions, actions, flipFlop, singleShot, startActive,
            resetDuration, delay, occupantsGroup);
    }

    static bool ParseBool(string raw, bool fallback)
    {
        var s = raw.Trim();
        if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        return fallback;
    }

    static float ParseFloat(string raw, float fallback)
    {
        var s = raw.Trim().TrimEnd('f', 'F');
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}

/// <summary>One parsed verb call: <c>verb(arg, arg, ...)</c> with optional trailing
/// option tags <c>group(1)</c>, <c>delay(2)</c>, <c>doc("...")</c>. Used for both
/// conditions and actions — they share the same call shape in DS1 GAS.</summary>
public sealed class TriggerCall
{
    public string Verb { get; }
    public IReadOnlyList<string> Args { get; }
    /// <summary>The <c>group(N)</c> tag binds conditions to actions inside one row;
    /// 0 means "ungrouped" (the default for both untagged conditions and actions).</summary>
    public int Group { get; }
    /// <summary>Per-call delay from a <c>delay(N)</c> option tag. Stacks with the
    /// row-level delay.</summary>
    public float CallDelay { get; }
    /// <summary>Phase 10-SC-1c — author-side <c>when_false</c> prefix. The action fires
    /// on the row's true→false transition (e.g., "fade in" when the player leaves the
    /// trigger box) instead of on every-tick-true. Conditions don't use this prefix.</summary>
    public bool WhenFalse { get; }

    internal TriggerCall(string verb, IReadOnlyList<string> args, int group, float callDelay, bool whenFalse)
    {
        Verb = verb;
        Args = args;
        Group = group;
        CallDelay = callDelay;
        WhenFalse = whenFalse;
    }

    public override string ToString() =>
        Args.Count == 0 ? $"{Verb}()" : $"{Verb}({string.Join(",", Args)})";

    /// <summary>Splits an attribute value like
    /// <c>send_world_message("we_x", 0x01, 0f), delay(1), doc("foo")</c>
    /// into the leading verb call plus any trailing option tags.</summary>
    public static bool TryParse(string raw, string sourceLabel, List<string>? diagnostics, out TriggerCall call)
    {
        call = null!;
        var tokens = SplitTopLevelCommas(raw);
        if (tokens.Count == 0)
        {
            diagnostics?.Add($"{sourceLabel}: empty trigger call value '{raw}'");
            return false;
        }

        // The when_false prefix sits before the verb call inside the same top-level token,
        // e.g. `when_false fade_node(0,"in")`. Lift it before splitting verb from args.
        var head = tokens[0].TrimStart();
        bool whenFalse = false;
        const string prefix = "when_false";
        if (head.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && head.Length > prefix.Length && char.IsWhiteSpace(head[prefix.Length]))
        {
            whenFalse = true;
            head = head[prefix.Length..].TrimStart();
        }

        if (!TryParseCall(head, out var verb, out var args))
        {
            diagnostics?.Add($"{sourceLabel}: malformed trigger call '{tokens[0]}'");
            return false;
        }

        int group = 0;
        float callDelay = 0f;
        for (int i = 1; i < tokens.Count; i++)
        {
            if (!TryParseCall(tokens[i], out var optName, out var optArgs)) continue;
            switch (optName.ToLowerInvariant())
            {
                case "group":
                    if (optArgs.Count > 0 && int.TryParse(optArgs[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g))
                        group = g;
                    break;
                case "delay":
                    if (optArgs.Count > 0)
                    {
                        var s = optArgs[0].TrimEnd('f', 'F');
                        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                            callDelay = d;
                    }
                    break;
                // doc() is editor metadata; ignored at runtime.
            }
        }

        call = new TriggerCall(verb, args, group, callDelay, whenFalse);
        return true;
    }

    static bool TryParseCall(string token, out string verb, out IReadOnlyList<string> args)
    {
        verb = "";
        args = Array.Empty<string>();
        var s = token.Trim();
        var open = s.IndexOf('(');
        if (open <= 0)
        {
            // Bareword like `when_false` — verb with no args.
            if (s.Length == 0) return false;
            verb = s;
            return true;
        }
        int close = -1;
        int depth = 0;
        bool inQuote = false;
        for (int i = open; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '"') { inQuote = !inQuote; continue; }
            if (inQuote) continue;
            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0) { close = i; break; }
            }
        }
        if (close < 0) return false;
        verb = s[..open].Trim();
        var inside = s[(open + 1)..close];
        args = SplitTopLevelCommas(inside).Select(Dequote).ToList();
        return verb.Length > 0;
    }

    static string Dequote(string raw)
    {
        var s = raw.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') return s[1..^1];
        return s;
    }

    static List<string> SplitTopLevelCommas(string raw)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return parts;
        int start = 0, depth = 0;
        bool inQuote = false;
        for (int i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c == '"') { inQuote = !inQuote; continue; }
            if (inQuote) continue;
            if (c == '(' || c == '[') depth++;
            else if (c == ')' || c == ']') depth--;
            else if (c == ',' && depth == 0)
            {
                var seg = raw[start..i].Trim();
                if (seg.Length > 0) parts.Add(seg);
                start = i + 1;
            }
        }
        var tail = raw[start..].Trim();
        if (tail.Length > 0) parts.Add(tail);
        return parts;
    }
}
