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
/// compared lowercased, with punctuation ignored. Only text with whitespace
/// counts, on both sides, and an underscore stays inside its word: an
/// identifier such as <c>robo_suit_minigun</c> is how code names data, not
/// a copy of the spell's title "Robo-Suit Minigun". Common phrasing does not
/// count: five shared words need two of five or more letters ("are you sure
/// you want" has none), and a short game string inside a longer one needs
/// 15 letters ("load a saved game" has 14). A string that IS a whole game
/// string ("Camera: Zoom Out") always counts.</summary>
public sealed class GameTextIndex
{
    public const int WindowWords = 5;
    public const int ShortMinWords = 3;
    public const int ShortMinChars = 16;
    public const int LongWordChars = 5;
    public const int MinLongWords = 2;
    public const int ShortInsideMinLetters = 15;

    readonly List<(string Source, string Text)> _texts = new();
    readonly Dictionary<ulong, int> _windows = new();   // hash of 5 words → text index
    readonly Dictionary<ulong, int> _shorts = new();    // hash of a whole short string → text index
    readonly Dictionary<ulong, int> _wholes = new();    // hash of every whole string → text index

    public int TextCount => _texts.Count;

    public static GameTextIndex Build(IEnumerable<(string Source, string Text)> texts)
    {
        var index = new GameTextIndex();
        foreach (var (source, text) in texts) index.Add(source, text);
        return index;
    }

    void Add(string source, string text)
    {
        if (!HasWhitespace(text)) return;
        var words = Words(text);
        if (words.Length < ShortMinWords) return;
        int id = _texts.Count;
        _texts.Add((source, text));
        _wholes.TryAdd(Hash(words, 0, words.Length), id);
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
        if (!HasWhitespace(s)) return null;
        var words = Words(s);
        if (words.Length < ShortMinWords) return null;
        // A word-for-word copy of a whole game string, however plain.
        if (_wholes.TryGetValue(Hash(words, 0, words.Length), out var whole)
            && Words(_texts[whole].Text).SequenceEqual(words))
            return new TextMatch(_texts[whole].Text, _texts[whole].Source);
        for (int i = 0; i + WindowWords <= words.Length; i++)
            if (_windows.TryGetValue(Hash(words, i, WindowWords), out var id) && Confirm(id, words, i, WindowWords)
                && Distinctive(words, i, WindowWords, whole: false, window: true))
                return new TextMatch(_texts[id].Text, _texts[id].Source);
        for (int len = Math.Min(WindowWords - 1, words.Length); len >= ShortMinWords; len--)
            for (int i = 0; i + len <= words.Length; i++)
                if (_shorts.TryGetValue(Hash(words, i, len), out var id) && Confirm(id, words, i, len)
                    && Words(_texts[id].Text).Length == len && Distinctive(words, i, len, whole: len == words.Length, window: false))
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

    static bool HasWhitespace(string s) => s.Any(char.IsWhiteSpace);

    /// <summary>Whether the matched words say something particular rather
    /// than share common phrasing (see the class summary).</summary>
    static bool Distinctive(string[] words, int start, int count, bool whole, bool window)
    {
        if (whole) return true;
        int longWords = 0, letters = 0;
        for (int k = start; k < start + count; k++)
        {
            if (words[k].Length >= LongWordChars) longWords++;
            letters += words[k].Length;
        }
        return window ? longWords >= MinLongWords : letters >= ShortInsideMinLetters;
    }

    /// <summary>Lowercased words: letters, digits, underscores and inner apostrophes.</summary>
    public static string[] Words(string s)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool inner = c == '\'' && sb.Length > 0 && i + 1 < s.Length && char.IsLetterOrDigit(s[i + 1]);
            if (char.IsLetterOrDigit(c) || c == '_' || inner) sb.Append(char.ToLowerInvariant(c));
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
