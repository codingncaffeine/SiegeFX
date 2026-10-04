using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Tests;

public class GasDocumentTests
{
    [Fact]
    public void ParsesNestedBlocksAndAttributes()
    {
        var doc = GasDocument.Parse("""
            [t:template,n:farmboy]
            {
                doc = "Farm Boy";
                [aspect]
                {
                    f scale_base = 1.25;
                    model = m_c_gah_fb_pos_a1;
                }
            }
            """);

        var root = Assert.Single(doc.Roots);
        Assert.Equal("t:template,n:farmboy", root.Header);
        var doc0 = Assert.Single(root.Attributes);
        Assert.Equal(new GasAttribute("doc", null, "\"Farm Boy\""), doc0);

        var aspect = Assert.Single(root.Children);
        Assert.Equal("aspect", aspect.Header);
        Assert.Equal(new GasAttribute("scale_base", "f", "1.25"), aspect.Attributes[0]);
        Assert.Equal(new GasAttribute("model", null, "m_c_gah_fb_pos_a1"), aspect.Attributes[1]);
    }

    [Fact]
    public void SkipsLineAndBlockComments()
    {
        var doc = GasDocument.Parse("""
            // leading comment
            [a] { /* inline */ x = 1; // trailing
              /* multi
                 line */ y = 2; }
            """);

        var a = Assert.Single(doc.Roots);
        Assert.Equal(["x", "y"], a.Attributes.Select(at => at.Name));
    }

    [Fact]
    public void QuotedAndScriptLiteralValuesKeepTheirSemicolons()
    {
        var doc = GasDocument.Parse("""
            [fx]
            {
                name = "a;b";
                script = [[ sfx.run(); sfx.stop(); ]];
                excluded_chars = [["<>:/\|?*.%;]];
            }
            """);

        var attrs = Assert.Single(doc.Roots).Attributes;
        Assert.Equal("\"a;b\"", attrs[0].Value);
        Assert.Equal("[[ sfx.run(); sfx.stop(); ]]", attrs[1].Value);
        Assert.Equal("[[\"<>:/\\|?*.%;]]", attrs[2].Value);
    }

    [Fact]
    public void ToleratesStrayTextBetweenHeaderAndBrace()
    {
        // Retail ships `[anim_files]d {` and loads it; so must we.
        var doc = GasDocument.Parse("[anim_files]d { walk = a_walk; }");

        var block = Assert.Single(doc.Roots);
        Assert.Equal("anim_files", block.Header);
        Assert.Equal("walk", Assert.Single(block.Attributes).Name);
    }

    [Theory]
    [InlineData("[a] { x = 1; ")]          // unclosed block
    [InlineData("[a] { x = 1 }")]           // missing ';'
    [InlineData("[a { x = 1; }")]           // unterminated header
    [InlineData("[a] { /* never closed }")] // unterminated comment
    public void MalformedInputThrowsInvalidData(string text)
    {
        Assert.Throws<InvalidDataException>(() => GasDocument.Parse(text));
    }

    [Fact]
    public void LoadStripsUtf8BomAndRejectsUtf16()
    {
        var utf8 = new byte[] { 0xEF, 0xBB, 0xBF }.Concat("[a] { x = 1; }"u8.ToArray()).ToArray();
        Assert.Equal("a", Assert.Single(GasDocument.Load(utf8).Roots).Header);

        var utf16 = new byte[] { 0xFF, 0xFE, (byte)'[', 0 };
        Assert.Throws<InvalidDataException>(() => GasDocument.Load(utf16));
    }
}
