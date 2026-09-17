using System.Net;
using System.Net.Sockets;
using Inspection.Infrastructure.Plc;

namespace Inspection.Infrastructure.Tests;

public class ModbusTcpClientTests
{
    [Fact]
    public async Task Reads_known_registers_with_zero_based_address_and_fragmented_response()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync();
            var stream = peer.GetStream();
            var request = new byte[12];
            await stream.ReadExactlyAsync(request);
            Assert.Equal(Convert.FromHexString("000100000006010300020003"), request);
            byte[] response = Convert.FromHexString("000100000009010306000B00160021");
            foreach (var value in response) await stream.WriteAsync(new byte[] { value });
        });
        await using var client = new ModbusTcpClient("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, 1, TimeSpan.FromSeconds(2));
        Assert.Equal(new ushort[] { 11, 22, 33 }, await client.ReadRegistersAsync(2, 3));
        await server.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData("0002000000050103020001")] // wrong transaction
    [InlineData("0001000100050103020001")] // wrong protocol
    [InlineData("0001000000050203020001")] // wrong unit
    [InlineData("0001000000050104020001")] // wrong function
    [InlineData("0001000000050103010001")] // wrong byte count
    [InlineData("00010000010001")]         // oversized length
    [InlineData("000100000003018302")]     // exception
    [InlineData("00010000000501030200")]   // EOF in body
    public async Task Invalid_reply_closes_connection_and_does_not_retry(string responseHex)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync();
            var request = new byte[12];
            await peer.GetStream().ReadExactlyAsync(request);
            await peer.GetStream().WriteAsync(Convert.FromHexString(responseHex));
        });
        await using var client = new ModbusTcpClient("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, 1, TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<IOException>(() => client.ReadRegistersAsync(0, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ReadRegistersAsync(0, 1));
        await server.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Stalled_reply_is_bounded_and_connection_cannot_be_reused()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        await using var client = new ModbusTcpClient("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, 1, TimeSpan.FromMilliseconds(150));
        var read = client.ReadRegistersAsync(0, 1);
        using var peer = await listener.AcceptTcpClientAsync();
        await Assert.ThrowsAsync<TimeoutException>(() => read);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ReadRegistersAsync(0, 1));
    }

    [Fact]
    public async Task Writes_known_coordinates_and_checks_echo()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync();
            var request = new byte[19];
            await peer.GetStream().ReadExactlyAsync(request);
            Assert.Equal(Convert.FromHexString("00010000000D01100002000306007B01C8004E"), request);
            await peer.GetStream().WriteAsync(Convert.FromHexString("000100000006011000020003"));
        });
        await using var client = new ModbusTcpClient("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, 1, TimeSpan.FromSeconds(2));
        await client.WriteRegistersAsync(2, [123, 456, 78]);
        await server.WaitAsync(TimeSpan.FromSeconds(3));
    }
}
