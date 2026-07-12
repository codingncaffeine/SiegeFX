namespace SiegeFX.Core.Net;

/// <summary>SC-MP-EOS P7 — NAT reachability class, worst→best. In a star
/// topology only the HOST's reachability matters, so electing the most-
/// reachable player as host collapses relay demand to near zero — the
/// load-bearing rug-proofing (no vendor is critical).</summary>
public enum NatClass
{
    Symmetric = 0,   // hardest — needs relay to reach anyone
    PortRestricted = 1,
    AddressRestricted = 2,
    FullCone = 3,    // easiest — anyone can reach it
    OpenOrForwarded = 4, // public IP or forwarded port — ideal host
}

/// <summary>A candidate host's measured connectivity (from a STUN probe).</summary>
public readonly record struct HostCandidate(int Player, string Name, NatClass Nat, int PingMs);

public static class MpHostElection
{
    /// <summary>Elect the host: most-reachable NAT class wins; ties broken
    /// by lowest ping. When the winner is FullCone-or-better, every client
    /// connects directly and NO relay (EOS/TURN) is needed at all. Returns
    /// the elected candidate and whether a relay tier will be required for
    /// the worst-case peers.</summary>
    public static (HostCandidate Host, bool RelayLikely) Elect(IReadOnlyList<HostCandidate> candidates)
    {
        if (candidates.Count == 0) throw new ArgumentException("no candidates");
        HostCandidate best = candidates[0];
        foreach (var c in candidates)
            if ((int)c.Nat > (int)best.Nat || ((int)c.Nat == (int)best.Nat && c.PingMs < best.PingMs))
                best = c;
        // A host at FullCone+ is reachable by any client via punch; relay is
        // only likely when even the best available host is behind a
        // restricted/symmetric NAT (rare — the "everyone hostile-NAT" lobby).
        bool relayLikely = best.Nat < NatClass.FullCone;
        NetLog.Info($"host elected: player {best.Player} '{best.Name}' (NAT {best.Nat}, {best.PingMs}ms) — relay {(relayLikely ? "may be needed" : "not needed")}");
        return (best, relayLikely);
    }
}

/// <summary>SC-MP-EOS P7 — player-relay: when a client can't reach the host
/// directly, another player with clean connections to BOTH forwards the
/// traffic. Zero infrastructure, undiscontinuable. This planner picks the
/// best relay peer for a blocked client from the reachability matrix.</summary>
public static class MpPlayerRelay
{
    /// <summary>Pick a relay for <paramref name="blockedClient"/> → host:
    /// the reachable peer (canReach[peer] includes both host and the client)
    /// with the best combined NAT class. Returns -1 when no peer can bridge
    /// (falls through to the vendor relay tier, or a "no host reachable"
    /// notice).</summary>
    public static int PickRelay(int blockedClient, int host,
        IReadOnlyList<HostCandidate> peers,
        Func<int, int, bool> canReach)
    {
        int best = -1; int bestScore = -1;
        foreach (var p in peers)
        {
            if (p.Player == blockedClient || p.Player == host) continue;
            if (!canReach(p.Player, host) || !canReach(p.Player, blockedClient)) continue;
            int score = (int)p.Nat * 1000 - p.PingMs;
            if (score > bestScore) { bestScore = score; best = p.Player; }
        }
        if (best >= 0) NetLog.Info($"player-relay: routing client {blockedClient} → host {host} via peer {best}");
        else NetLog.Warn($"player-relay: no peer can bridge client {blockedClient} → host {host} — vendor relay or 'try another host'");
        return best;
    }
}
