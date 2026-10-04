using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Tests;

public class SpellExprTests
{
    [Theory]
    [InlineData("1+2*3", 7f)]
    [InlineData("(1+2)*3", 9f)]
    [InlineData("-4+10", 6f)]
    [InlineData("2**3**2", 512f)]       // right-associative
    [InlineData("2*3**2", 18f)]         // ** binds tighter than *
    [InlineData("10/4", 2.5f)]
    public void Arithmetic(string expr, float expected)
    {
        Assert.Equal(expected, SpellExpr.Eval(expr, magicLevel: 0f), precision: 4);
    }

    [Fact]
    public void SubstitutesMagicLevel()
    {
        Assert.Equal(25f, SpellExpr.Eval("(#magic+1)*5", magicLevel: 4f), precision: 4);
    }

    [Fact]
    public void SubstitutesTheFullPlaceholderSet()
    {
        var ctx = new SpellEvalContext(magic: 1f, maxLife: 100f, life: 40f, srcMana: 7f, srcLife: 3f);
        Assert.Equal(151f, SpellExpr.Eval("#magic+#maxlife+#life+#src_mana+#src_life", ctx), precision: 4);
    }

    [Theory]
    [InlineData(10f, 30f)]   // mana >= 20: the clamp's high branch
    [InlineData(5f, 5f)]     // otherwise: pass the mana through
    public void TernaryBlocksSelectABranch(float srcMana, float expected)
    {
        var ctx = new SpellEvalContext(magic: 0f, srcMana: srcMana);
        Assert.Equal(expected, SpellExpr.Eval("[[ #src_mana>=10 ?(30):(#src_mana) ]]", ctx), precision: 4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1 2")]          // trailing input the grammar can't consume
    [InlineData("#magic #magic")]
    public void ParseFailuresFoldToZero(string expr)
    {
        Assert.Equal(0f, SpellExpr.Eval(expr, magicLevel: 3f));
    }

    // Truncated formulas fail soft rather than to zero: a missing operand
    // reads as 0 and an unclosed '(' closes at end of input. Pinned so a
    // stricter parser is a deliberate choice, not an accident.
    [Theory]
    [InlineData("1+", 1f)]
    [InlineData("(1+2", 3f)]
    [InlineData("2*", 0f)]
    public void TruncatedFormulasAreLenient(string expr, float expected)
    {
        Assert.Equal(expected, SpellExpr.Eval(expr, magicLevel: 3f), precision: 4);
    }
}
