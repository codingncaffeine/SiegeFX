using System.Globalization;

namespace SiegeFX.Core.Assets;

/// <summary>Caster/target snapshot consumed by <see cref="SpellExpr"/> when
/// resolving <c>#magic</c>, <c>#maxlife</c>, <c>#life</c>, <c>#src_mana</c>,
/// <c>#src_life</c>. Values default to 0 — any placeholder a caller doesn't
/// fill folds to 0 the same way the parser does for unknown identifiers, so
/// shipped formulas that only reference <c>#magic</c> still resolve cleanly
/// against a context built with just a magic level. <c>#src_*</c> always
/// reads from the caster; <c>#life</c> / <c>#maxlife</c> follow DS1
/// convention by reading from the *target* of the cast (which equals the
/// caster for self-target heals/buffs — Phase 17-SC-A2 ships with that
/// caster=target wiring on the heal path because the offensive path doesn't
/// need target life yet).</summary>
public readonly struct SpellEvalContext
{
    /// <summary>Caster's combat-magic skill level (<c>#magic</c>).</summary>
    public readonly float Magic;
    /// <summary>Target's max life (<c>#maxlife</c>) — caster's for self-target spells.</summary>
    public readonly float MaxLife;
    /// <summary>Target's current life (<c>#life</c>) — caster's for self-target spells.</summary>
    public readonly float Life;
    /// <summary>Caster's current mana (<c>#src_mana</c>).</summary>
    public readonly float SrcMana;
    /// <summary>Caster's current life (<c>#src_life</c>).</summary>
    public readonly float SrcLife;

    public SpellEvalContext(float magic, float maxLife = 0f, float life = 0f,
                            float srcMana = 0f, float srcLife = 0f)
    {
        Magic = magic;
        MaxLife = maxLife;
        Life = life;
        SrcMana = srcMana;
        SrcLife = srcLife;
    }
}

/// <summary>
/// Tiny arithmetic evaluator for the formula strings DS1 uses inside
/// <c>[magic]</c> blocks — things like
/// <c>(((#magic+5.51)-1/((#magic+1)/3))*1.23)*(1+((1/(#magic+3.3))+0.03))</c>
/// for spell damage scaling. Covers what shipped data actually needs:
/// numeric literals, the placeholder set
/// <c>#magic / #maxlife / #life / #src_mana / #src_life</c> (substituted from
/// a <see cref="SpellEvalContext"/>), parentheses, <c>+ - * /</c>, and the
/// right-associative <c>**</c> power operator (~19 offensive spells encode
/// level scaling as <c>(#magic+1)**1.15</c>; without this they read as 0).
/// Also handles the <c>[[ <i>cond</i> ?( <i>a</i> ):( <i>b</i> ) ]]</c>
/// ternary blocks DS1 uses for clamping logic — <c>spell_healing_hands</c>
/// uses a nested triple-ternary to clamp heal magnitude against caster
/// mana, and <c>spell_leech_life</c> uses one to clamp drain against the
/// target's remaining HP. The outer <c>[[ ]]</c> sentinels are stripped at
/// entry; comparison operators (<c>&lt; &gt; &lt;= &gt;= == !=</c>) yield
/// 1.0 / 0.0 and feed into ternary truthiness (<c>!= 0</c>).
///
/// Recursive-descent, no allocations beyond the Tokenizer cursor — these
/// formulas are short (max ~250 chars) and we evaluate one per cast at most.
/// Returns <c>0</c> for parse failures rather than throwing; the caller
/// (<see cref="SpellTemplate"/>) treats a zero damage roll as "spell does
/// nothing" which is identical to how DS1 fails-soft on bad data.
///
/// Precedence (low → high): ternary <c>?:</c> &lt; comparisons
/// <c>&lt; &gt; &lt;= &gt;= == !=</c> &lt; <c>+ -</c> &lt; <c>* /</c> &lt;
/// <c>**</c> &lt; unary <c>+ -</c> &lt; primary. Power binds tighter than
/// mul/div so <c>(#magic+1)**1.15*1.92</c> reads as
/// <c>((#magic+1)**1.15)*1.92</c>.
/// </summary>
public static class SpellExpr
{
    /// <summary>Evaluate <paramref name="expr"/> against <paramref name="ctx"/>.
    /// Returns 0 on parse error. <c>[[ ... ]]</c> outer brackets (DS1's
    /// "evaluate this expression block" wrapper, used to delimit ternary
    /// formulas in healing_hands / leech_life) are stripped before parsing.</summary>
    public static float Eval(string expr, in SpellEvalContext ctx)
    {
        if (string.IsNullOrWhiteSpace(expr)) return 0f;
        var trimmed = expr.AsSpan().Trim();
        if (trimmed.Length >= 4 && trimmed[0] == '[' && trimmed[1] == '['
                                && trimmed[^1] == ']' && trimmed[^2] == ']')
        {
            trimmed = trimmed[2..^2];
        }
        var p = new Parser(trimmed.ToString(), ctx);
        try
        {
            float v = p.ParseExpr();
            return p.AtEnd ? v : 0f;
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>Convenience overload for callers that only care about the
    /// caster's magic level — typical of survey/balance CLIs and back-compat
    /// call sites that predate the placeholder set. Anything beyond
    /// <c>#magic</c> folds to 0.</summary>
    public static float Eval(string expr, float magicLevel)
        => Eval(expr, new SpellEvalContext(magicLevel));

    private struct Parser
    {
        readonly string _s;
        readonly SpellEvalContext _ctx;
        int _i;

        public Parser(string s, in SpellEvalContext ctx) { _s = s; _ctx = ctx; _i = 0; }

        public bool AtEnd { get { Skip(); return _i >= _s.Length; } }

        void Skip() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }

        // Top-level entry. Ternary `cond ? a : b` (or DS1's parenthesized
        // `cond ?( a ):( b )` shape) sits here at the lowest precedence.
        // Truthiness is 0 = false, anything-else = true — comparisons emit
        // 1.0 / 0.0 from ParseCmp, but a bare arithmetic value works too.
        // Right-associative so `a ? b : c ? d : e` reads as `a ? b : (c ? d : e)`.
        public float ParseExpr()
        {
            float cond = ParseCmp();
            Skip();
            if (_i < _s.Length && _s[_i] == '?')
            {
                _i++;
                float t = ParseExpr();
                Skip();
                if (_i < _s.Length && _s[_i] == ':') _i++;
                float f = ParseExpr();
                return cond != 0f ? t : f;
            }
            return cond;
        }

        // Comparison tier. Six operators total; chains left-to-right but DS1
        // never chains them (always parenthesized into ternary conditions),
        // so the loop is just defensive. Yields 1.0 for true / 0.0 for false
        // so the result feeds straight into ternary truthiness or arithmetic.
        float ParseCmp()
        {
            float left = ParseAddSub();
            while (true)
            {
                Skip();
                if (_i >= _s.Length) break;
                char c = _s[_i];
                int op;     // 0 lt, 1 le, 2 gt, 3 ge, 4 eq, 5 ne
                int width;  //  characters consumed
                if (c == '<')
                {
                    if (_i + 1 < _s.Length && _s[_i + 1] == '=') { op = 1; width = 2; }
                    else                                          { op = 0; width = 1; }
                }
                else if (c == '>')
                {
                    if (_i + 1 < _s.Length && _s[_i + 1] == '=') { op = 3; width = 2; }
                    else                                          { op = 2; width = 1; }
                }
                else if (c == '=' && _i + 1 < _s.Length && _s[_i + 1] == '=') { op = 4; width = 2; }
                else if (c == '!' && _i + 1 < _s.Length && _s[_i + 1] == '=') { op = 5; width = 2; }
                else break;
                _i += width;
                float right = ParseAddSub();
                bool b = op switch
                {
                    0 => left <  right,
                    1 => left <= right,
                    2 => left >  right,
                    3 => left >= right,
                    4 => left == right,
                    _ => left != right,
                };
                left = b ? 1f : 0f;
            }
            return left;
        }

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
            float left = ParsePower();
            while (true)
            {
                Skip();
                if (_i >= _s.Length) break;
                // Don't consume the first '*' of a '**' here — ParsePower handles
                // the power operator on the LHS before we even reach this loop,
                // and the RHS path goes through ParsePower again below.
                if (_i + 1 < _s.Length && _s[_i] == '*' && _s[_i + 1] == '*') break;
                char c = _s[_i];
                if (c == '*' || c == '/')
                {
                    _i++;
                    float right = ParsePower();
                    if (c == '*') left = left * right;
                    else left = right == 0f ? 0f : left / right;
                }
                else break;
            }
            return left;
        }

        float ParsePower()
        {
            float left = ParseUnary();
            Skip();
            if (_i + 1 < _s.Length && _s[_i] == '*' && _s[_i + 1] == '*')
            {
                _i += 2;
                float right = ParsePower(); // right-associative: a**b**c = a**(b**c)
                return MathF.Pow(left, right);
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
                // Recurse through the full grammar (ternary down to primary)
                // so `(a > b ? c : d)` and DS1's `( cond ?( a ):( b ) )`
                // ternary blocks parse as one parenthesized sub-expression.
                float v = ParseExpr();
                Skip();
                if (_i < _s.Length && _s[_i] == ')') _i++;
                return v;
            }
            if (c == '#')
            {
                // Read the bare identifier after '#'. Identifiers may include
                // underscores (#src_mana, #src_life). Unknown placeholders
                // fold to 0 — DS1's cost/damage formulas reference a fixed
                // set and we'd rather miss a niche placeholder silently than
                // throw mid-cast.
                _i++;
                int start = _i;
                while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_')) _i++;
                var name = _s.AsSpan(start, _i - start);
                if (name.Equals("magic",    StringComparison.OrdinalIgnoreCase)) return _ctx.Magic;
                if (name.Equals("maxlife",  StringComparison.OrdinalIgnoreCase)) return _ctx.MaxLife;
                if (name.Equals("life",     StringComparison.OrdinalIgnoreCase)) return _ctx.Life;
                if (name.Equals("src_mana", StringComparison.OrdinalIgnoreCase)) return _ctx.SrcMana;
                if (name.Equals("src_life", StringComparison.OrdinalIgnoreCase)) return _ctx.SrcLife;
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
