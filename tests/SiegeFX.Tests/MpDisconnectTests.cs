using SiegeFX.Core.Net;
using Xunit;

namespace SiegeFX.Tests;

public class MpDisconnectTests
{
    [Fact]
    public void ADisconnectFromATransportThreadIsHandledOnTick()
    {
        // The LAN keepalive drops a silent peer from a pool thread; the
        // render host's OnPlayerLeft edits its actor list, so the session
        // must hold the leave until the main thread ticks it.
        var (h, c) = LoopbackTransport.CreatePair();
        var host = new MpSession(h, isHost: true, "Host") { BuildSnapshot = () => [] };
        var client = new MpSession(c, isHost: false, "Client");
        client.SendJoinRequest();
        for (int i = 0; i < 4; i++) { host.Tick(0); client.Tick(0); }

        int left = -1, leftOnThread = -1, mainThread = Environment.CurrentManagedThreadId;
        host.OnPlayerLeft = p => { left = p; leftOnThread = Environment.CurrentManagedThreadId; };

        var t = new Thread(c.Dispose);
        t.Start(); t.Join();
        Assert.Equal(-1, left);

        host.Tick(0);
        Assert.Equal(1, left);
        Assert.Equal(mainThread, leftOnThread);
    }
}
