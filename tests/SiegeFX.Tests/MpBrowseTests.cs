using SiegeFX.Core.Net;

namespace SiegeFX.Tests;

/// <summary>The Internet screen's address routing and the game lists'
/// refresh clock.</summary>
public class MpBrowseTests
{
    [Theory]
    [InlineData("0002500600000000000000000000abcd")]   // the shape the lobby list hands out
    [InlineData("0002500600000000000000000000ABCD")]
    [InlineData("  0002500600000000000000000000abcd  ")]
    public void A_listed_games_id_goes_through_the_internet_service(string address)
    {
        Assert.True(MpAddress.IsInternetHostId(address));
        Assert.Equal("eos", MpAddress.ProviderFor(address));
    }

    [Theory]
    [InlineData("203.0.113.7")]                         // a public IPv4 address
    [InlineData("203.0.113.7:47778")]
    [InlineData("192.168.1.215")]
    [InlineData("friend.example.net")]
    [InlineData("friend.example.net:50000")]
    [InlineData("0002500600000000000000000000abc")]    // 31 digits
    [InlineData("0002500600000000000000000000abcde")]  // 33 digits
    [InlineData("0002500600000000000000000000abcg")]   // not hex
    public void Anything_else_is_dialed_directly(string address)
    {
        Assert.False(MpAddress.IsInternetHostId(address));
        Assert.Equal("lan", MpAddress.ProviderFor(address));
    }

    [Fact]
    public void The_refresh_timer_repeats_on_its_interval()
    {
        double now = 100;
        var timer = new MpRefreshTimer(2.5, () => now);
        Assert.True(timer.Due());          // a list searches as soon as it opens
        Assert.False(timer.Due());
        now += 2.4;
        Assert.False(timer.Due());
        now += 0.1;
        Assert.True(timer.Due());          // and again one interval later
        now += 1;
        timer.DueNow();
        Assert.True(timer.Due());          // reopening the screen searches at once
        Assert.False(timer.Due());
    }

    [Fact]
    public void The_refresh_timer_keeps_time_without_any_game_clock()
    {
        // The default clock must run on its own: a list timed by the play
        // clock (stopped on the menus) searched once and never again.
        var timer = new MpRefreshTimer(0.05);
        Assert.True(timer.Due());
        Assert.False(timer.Due());
        var waited = System.Diagnostics.Stopwatch.StartNew();
        bool dueAgain = false;
        while (!dueAgain && waited.Elapsed.TotalSeconds < 5)
        {
            Thread.Sleep(10);
            dueAgain = timer.Due();
        }
        Assert.True(dueAgain);
        Assert.InRange(waited.Elapsed.TotalSeconds, 0.04, 5);
    }

    [Fact]
    public void An_unregistered_provider_is_not_created_as_LAN()
    {
        Assert.False(MpProviderFactory.IsRegistered("not-a-provider"));
        Assert.Null(MpProviderFactory.TryCreate("not-a-provider"));
        Assert.True(MpProviderFactory.IsRegistered("lan"));
        var lan = MpProviderFactory.TryCreate("lan");
        Assert.NotNull(lan);
        lan!.Value.Lobby.Dispose();
        lan.Value.Transport.Dispose();
    }
}
