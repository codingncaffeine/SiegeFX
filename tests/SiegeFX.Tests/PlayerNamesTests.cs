using System.Text;
using SiegeFX.Core.Net;

namespace SiegeFX.Tests;

public class PlayerNamesTests
{
    [Theory]
    [InlineData(null, "Player")]
    [InlineData("", "Player")]
    [InlineData("   ", "Player")]
    [InlineData("  Farmhand  ", "Farmhand")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345", "abcdefghijklmnopqrstuvwxyz012345")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456", "abcdefghijklmnopqrstuvwxyz012345")]
    public void A_name_is_trimmed_and_cut_to_the_limit(string? name, string expected)
        => Assert.Equal(expected, PlayerNames.Fit(name, 32));

    [Fact]
    public void The_limit_counts_utf8_bytes_and_a_cut_keeps_whole_characters()
    {
        // é takes two bytes; the emoji takes four (two chars in .NET).
        Assert.Equal(new string('é', 16), PlayerNames.Fit(new string('é', 24), 32));
        string fit = PlayerNames.Fit(string.Concat(Enumerable.Repeat("😀", 9)), 32);
        Assert.Equal(string.Concat(Enumerable.Repeat("😀", 8)), fit);
        Assert.True(Encoding.UTF8.GetByteCount(fit) <= 32);
    }

    [Fact]
    public void A_cut_that_ends_on_a_space_drops_the_space()
        => Assert.Equal("abc", PlayerNames.Fit("abc " + new string('x', 40), 4));
}
