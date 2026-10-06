using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Sfx;

/// <summary>The parameter keys the shipped effect scripts author, walked
/// through the scripts they call. Shared by <c>siegefx sfx param-audit</c>
/// and the parity ledger, so both count the same keys.</summary>
public static class SfxParamInventory
{
    /// <summary>Walks a compiled sfx script (recursing into <c>call &lt;sub&gt;</c>)
    /// collecting every param key from every statement that carries a quoted
    /// param string, tagged with the create-kind (or verb) it was authored on.</summary>
    public static void WalkParamStrings(string scriptName, string body, SfxScriptStore store,
        HashSet<string> visited, List<(string Key, string Kind)> keys, ref int paramStrings)
    {
        if (!visited.Add(scriptName)) return; // cycle / mutual-call guard
        SfxProgram prog;
        try { prog = SfxScriptCompiler.Compile(scriptName, body); }
        catch { return; }

        foreach (var stmt in prog.Statements)
        {
            if (!string.IsNullOrEmpty(stmt.ParamString))
            {
                paramStrings++;
                var kind = stmt.Kind == StatementKind.SfxCreate && stmt.Tokens.Count > 0
                    ? stmt.Tokens[0].ToLowerInvariant()
                    : "(" + stmt.Verb + ")";
                foreach (var key in ExtractParamKeys(stmt.ParamString!))
                    keys.Add((key, kind));
            }
            if (stmt.Kind == StatementKind.Call && stmt.Tokens.Count > 0)
            {
                var callName = stmt.Tokens[0].Trim('"').Trim();
                int sp = callName.IndexOf(' ');
                if (sp >= 0) callName = callName.Substring(0, sp);
                if (!string.IsNullOrEmpty(callName) && store.TryGet(callName, out var sub))
                    WalkParamStrings(sub.Name, sub.Body, store, visited, keys, ref paramStrings);
            }
        }
    }

    /// <summary>Tokenizes a DS1 param string (<c>key(args)key2(args)...[0][1]</c>)
    /// into its key names. A key is an identifier followed by <c>(</c>; paren
    /// contents are skipped flat (no shipped DS1 value nests parens). Bare
    /// identifiers outside parens are also yielded — shipped strings author
    /// flag-style keys both ways. <c>[N]</c> caller-arg slots and <c>$var</c>
    /// leftovers are not keys.</summary>
    public static IEnumerable<string> ExtractParamKeys(string raw)
    {
        int i = 0;
        while (i < raw.Length)
        {
            char c = raw[i];
            if (c == '$')
            {
                i++;
                while (i < raw.Length && (char.IsLetterOrDigit(raw[i]) || raw[i] == '_')) i++;
            }
            else if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < raw.Length && (char.IsLetterOrDigit(raw[i]) || raw[i] == '_')) i++;
                var name = raw.Substring(start, i - start);
                int j = i;
                while (j < raw.Length && char.IsWhiteSpace(raw[j])) j++;
                if (j < raw.Length && raw[j] == '(')
                {
                    yield return name;
                    int close = raw.IndexOf(')', j + 1);
                    i = close < 0 ? raw.Length : close + 1;
                }
                else
                {
                    yield return name; // bare flag-style key
                }
            }
            else i++;
        }
    }
}
