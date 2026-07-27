using System.Net;
using System.Net.Sockets;
using CspPaletteCompanion.App;
using CspPaletteCompanion.Companion;

namespace CspPaletteCompanion.App.Tests;

/// <summary>
/// The sink-side loopback rule is tested directly rather than through its one caller,
/// because the point of putting it here is that a future second caller inherits it.
/// </summary>
public sealed class ConnectThroughMuxGuardTests
{
    [Fact]
    public async Task RefusesANonLoopbackAddressWithoutOpeningASocket()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
            var pairing = new CompanionPairingInfo(
                [IPAddress.Parse("192.168.1.5"), IPAddress.Loopback],
                port,
                "not-a-real-password",
                "1");

            await using var service = new CompanionCanvasService();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ConnectThroughMuxAsync(pairing, CancellationToken.None));

            // The listener is the one endpoint in the list that would have accepted.
            Assert.False(listener.Pending());
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task RefusesAnEmptyAddressList()
    {
        var pairing = new CompanionPairingInfo([], 1, "not-a-real-password", "1");

        await using var service = new CompanionCanvasService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ConnectThroughMuxAsync(pairing, CancellationToken.None));
    }

    [Fact]
    public void ReportsNoRouteBeforeAnythingIsAdopted()
    {
        var service = new CompanionCanvasService();
        Assert.Equal(ConnectionRoute.None, service.Route);
    }
}
