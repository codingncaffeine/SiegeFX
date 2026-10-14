using System.Text;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeFX.Guards;

/// <summary>A run of the game's own text found somewhere it should not be.</summary>
public sealed record TextMatch(string GameText, string Source);

/// <summary>The original game's text (every quoted value in its GAS files:
/// quest text, dialogue, interface labels), indexed so a string can be
/// checked for copied passages. A string matches when it holds five
/// consecutive words of one game string, or a whole short game string
/// (three or four words, 16+ characters, such as a quest title). Words are
/// compared lowercased, with punctuation ignored.</summary>
public sealed class GameTextIndex
{
    public const int WindowWords = 5;
    public const int ShortMinWords = 3;
    public const int ShortMinChars = 16;

    readonly List<(string Source, string Text)> _texts = new();
    readonly Dictionary<ulong, int> _windows = new();   // hash of 5 words → text index
    readonly Dictionary<ulong, int> _shorts = new();    // hash of a whole short string → text index

    public int TextCount => _texts.Count;

    public static GameTextIndex Build(IEnumerable<(string Source, string Text)> texts)
    {
        var index = new GameTextIndex();
        foreach (var (source, text) in texts) index.Add(source, text);
        return index;
    }

    void Add(string source, string text)
    {
        var words = Words(text);
        if (words.Length < ShortMinWords) return;
        int id = _texts.Count;
        _texts.Add((source, text));
        if (words.Length < WindowWords)
        {
            if (text.Trim().Length >= ShortMinChars) _shorts.TryAdd(Hash(words, 0, words.Length), id);
            return;
        }
        for (int i = 0; i + WindowWords <= words.Length; i++)
            _windows.TryAdd(Hash(words, i, WindowWords), id);
    }

    /// <summary>The game text <paramref name="s"/> copies, if any.</summary>
    public TextMatch? Match(string s)
    {
        var words = Words(s);
        if (words.Length < ShortMinWords) return null;
        for (int i = 0; i + WindowWords <= words.Length; i++)
            if (_windows.TryGetValue(Hash(words, i, WindowWords), out var id) && Confirm(id, words, i, WindowWords))
                return new TextMatch(_texts[id].Text, _texts[id].Source);
        for (int len = Math.Min(WindowWords - 1, words.Length); len >= ShortMinWords; len--)
            for (int i = 0; i + len <= words.Length; i++)
                if (_shorts.TryGetValue(Hash(words, i, len), out var id) && Confirm(id, words, i, len)
                    && Words(_texts[id].Text).Length == len)
                    return new TextMatch(_texts[id].Text, _texts[id].Source);
        return null;
    }

    /// <summary>Rules out a hash collision: the words really occur in that text.</summary>
    bool Confirm(int id, string[] words, int start, int count)
    {
        var have = Words(_texts[id].Text);
        for (int i = 0; i + count <= have.Length; i++)
        {
            bool all = true;
            for (int k = 0; k < count && all; k++) all = have[i + k] == words[start + k];
            if (all) return true;
        }
        return false;
    }

    /// <summary>Lowercased words: letters, digits and inner apostrophes.</summary>
    public static string[] Words(string s)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool inner = c == '\'' && sb.Length > 0 && i + 1 < s.Length && char.IsLetterOrDigit(s[i + 1]);
            if (char.IsLetterOrDigit(c) || inner) sb.Append(char.ToLowerInvariant(c));
            else if (sb.Length > 0) { words.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) words.Add(sb.ToString());
        return words.ToArray();
    }

    static ulong Hash(string[] words, int start, int count)
    {
        ulong h = 14695981039346656037UL;
        for (int k = 0; k < count; k++)
        {
            foreach (char c in words[start + k]) { h ^= c; h *= 1099511628211UL; }
            h ^= ' '; h *= 1099511628211UL;
        }
        return h;
    }

    /// <summary>Every quoted value in the GAS files of a tank, with
    /// <c>tankName:path</c> as its source. Files that do not parse are
    /// counted in <paramref name="unreadable"/> and skipped.</summary>
    public static IEnumerable<(string Source, string Text)> FromTank(TankReader tank, string tankName, Action<string>? unreadable = null)
    {
        foreach (var path in tank.ListFiles())
        {
            if (!path.EndsWith(".gas", StringComparison.OrdinalIgnoreCase)) continue;
            GasDocument doc;
            try { doc = GasDocument.Load(tank.ExtractToMemory(path)); }
            catch (Exception ex) when (ex is InvalidDataException or TankException) { unreadable?.Invoke(path); continue; }
            var pending = new Stack<GasNode>(doc.Roots);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                foreach (var a in node.Attributes)
                    if (a.Value.Length >= 2 && a.Value[0] == '"' && a.Value[^1] == '"')
                        yield return ($"{tankName}:{path}", a.Value[1..^1]);
                foreach (var c in node.Children) pending.Push(c);
            }
        }
    }
}
