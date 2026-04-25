using System.Globalization;

namespace SiegeFX.Core.Assets;

/// <summary>
/// Tiny arithmetic evaluator for the formula strings DS1 uses inside
/// <c>[magic]</c> blocks — things like
/// <c>(((#magic+5.51)-1/((#magic+1)/3))*1.23)*(1+((1/(#magic+3.3))+0.03))</c>
/// for spell damage scaling. Only what shipped data actually needs:
/// numeric literals, <c>#magic</c> substitution, <c>+ - * /</c>, parentheses.
///
/// Recursive-descent, no allocations beyond the Tokenizer cursor — these
/// formulas are short (max ~120 chars) and we evaluate one per cast at most.
/// Returns <c>0</c> for parse failures rather than throwing; the caller
/// (<see cref="SpellTemplate"/>) treats a zero damage roll as "spell does
/// nothing" which is identical to how DS1 fails-soft on bad data.
/// </summary>
public static class SpellExpr
{
    /// <summary>Evaluate <paramref name="expr"/> with <c>#magic</c> bound to
    /// <paramref name="magicLevel"/>. Returns 0 on parse error.</summary>
    public static float Eval(string expr, float magicLevel)
    {
        if (string.IsNullOrWhiteSpace(expr)) return 0f;
        var p = new Parser(expr, magicLevel);
        try
        {
            float v = p.ParseAddSub();
            return p.AtEnd ? v : 0f;
        }
        catch
        {
            return 0f;
        }
    }

    private struct Parser
    {
        readonly string _s;
        readonly float _magic;
        int _i;

        public Parser(string s, float magic) { _s = s; _magic = magic; _i = 0; }

        public bool AtEnd { get { Skip(); return _i >= _s.Length; } }

        void Skip() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }

        public float ParseAddSub()
        {
            float left = ParseMulDiv();
            while (true)
            {
                Skip();
                if (_i >= _s.Length) break;
                char c = _s[_i];
                if (c == '+' || c == '-')
                {
                    _i++;
                    float right = ParseMulDiv();
                    left = c == '+' ? left + right : left - right;
                }
                else break;
            }
            return left;
        }

        float ParseMulDiv()
        {
            float left = ParseUnary();
            while (true)
            {
                Skip();
                if (_i >= _s.Length) break;
                char c = _s[_i];
                if (c == '*' || c == '/')
                {
                    _i++;
                    float right = ParseUnary();
                    if (c == '*') left = left * right;
                    else left = right == 0f ? 0f : left / right;
                }
                else break;
            }
            return left;
        }

        float ParseUnary()
        {
            Skip();
            if (_i < _s.Length && _s[_i] == '-')
            {
                _i++;
                return -ParseUnary();
            }
            if (_i < _s.Length && _s[_i] == '+')
            {
                _i++;
                return ParseUnary();
            }
            return ParsePrimary();
        }

        float ParsePrimary()
        {
            Skip();
            if (_i >= _s.Length) return 0f;
            char c = _s[_i];
            if (c == '(')
            {
                _i++;
                float v = ParseAddSub();
                Skip();
                if (_i < _s.Length && _s[_i] == ')') _i++;
                return v;
            }
            if (c == '#')
            {
                // Read the bare identifier after '#'. Only #magic is referenced
                // by shipped data we care about; unknown placeholders fold to 0.
                _i++;
                int start = _i;
                while (_i < _s.Length && char.IsLetterOrDigit(_s[_i])) _i++;
                var name = _s.AsSpan(start, _i - start);
                if (name.Equals("magic", StringComparison.OrdinalIgnoreCase)) return _magic;
                return 0f;
            }
            // Numeric literal — digits + dot, allow leading digit OR leading dot.
            int n0 = _i;
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
            if (_i == n0) return 0f;
            return float.TryParse(_s.AsSpan(n0, _i - n0), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
        }
    }
}
