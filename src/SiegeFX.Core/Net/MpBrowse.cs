using System.Diagnostics;

namespace SiegeFX.Core.Net;

/// <summary>What a typed host address names. The Internet screen's HOST
/// ADDRESS box takes either the id of a game listed on the internet game
/// service (an Epic product user id, 32 hex digits, handed out by the lobby
/// list) or an address reached directly over UDP (an IP, ip:port or a host
/// name). An IP sent through the internet service reaches nobody, so the
/// box routes by what was typed.</summary>
public static class MpAddress
{
    /// <summary>Length of an Epic product user id (EOS_PRODUCTUSERID_MAX_LENGTH).</summary>
    public const int EpicIdLength = 32;

    /// <summary>True for an internet-game host id: exactly 32 hex digits.</summary>
    public static bool IsInternetHostId(string address)
    {
        var s = address.Trim();
        if (s.Length != EpicIdLength) return false;
        foreach (char c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    /// <summary>The provider that reaches <paramref name="address"/>: "eos"
    /// for an internet-game host id, "lan" (direct UDP) for anything else.</summary>
    public static string ProviderFor(string address) => IsInternetHostId(address) ? "eos" : "lan";
}

/// <summary>A repeating deadline on the monotonic clock. The multiplayer
/// screens run while the world's play clock is stopped (there is no world on
/// a menu), so a game list that refreshed on the play clock searched once and
/// never again; anything that repeats on a menu screen keeps time with this.</summary>
public sealed class MpRefreshTimer
{
    readonly double _intervalSeconds;
    readonly Func<double> _now;
    double _next;

    /// <param name="intervalSeconds">time between refreshes.</param>
    /// <param name="nowSeconds">the clock; null = the monotonic clock
    /// (<see cref="MonotonicSeconds"/>). Tests pass their own.</param>
    public MpRefreshTimer(double intervalSeconds, Func<double>? nowSeconds = null)
    {
        _intervalSeconds = intervalSeconds;
        _now = nowSeconds ?? MonotonicSeconds;
        _next = double.NegativeInfinity;
    }

    /// <summary>Seconds on a clock that runs whatever the game is doing.</summary>
    public static double MonotonicSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>True when a refresh is due; the next one is then due one
    /// interval from now. Due at once after construction and after
    /// <see cref="DueNow"/>.</summary>
    public bool Due()
    {
        double now = _now();
        if (now < _next) return false;
        _next = now + _intervalSeconds;
        return true;
    }

    /// <summary>Make the next <see cref="Due"/> call true (a screen was just opened).</summary>
    public void DueNow() => _next = double.NegativeInfinity;
}
