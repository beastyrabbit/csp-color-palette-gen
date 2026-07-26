using System.Net;
using System.Text;

namespace CspPaletteCompanion.Companion.Tests;

public sealed class CompanionPairingCodecTests
{
    private static readonly byte[] Key = [0x74, 0xB2, 0x92, 0x5B, 0x4A, 0x21, 0xDA];

    [Fact]
    public void Decode_ValidPrivatePairingUrl_ReturnsAllFields()
    {
        var url = CreateUrl("192.168.1.50,fe80::1\t54312\tsecretpw\tG#1:2026");

        var result = CompanionPairingCodec.Decode(url);

        Assert.Equal((ushort)54312, result.Port);
        Assert.Equal("secretpw", result.Password);
        Assert.Equal("G#1:2026", result.Generation);
        Assert.Equal([IPAddress.Parse("192.168.1.50"), IPAddress.Parse("fe80::1")], result.Addresses);
    }

    [Theory]
    [InlineData("https://example.com/rc/en-us?s=00")]
    [InlineData("http://companion.clip-studio.com/rc/en-us?s=00")]
    [InlineData("https://companion.clip-studio.com/not-rc/en-us?s=00")]
    [InlineData("https://companion.clip-studio.com/rc/en-us/?s=00")]
    [InlineData("https://companion.clip-studio.com/rc/en-us?x=1&s=00")]
    [InlineData("https://companion.clip-studio.com:444/rc/en-us?s=00")]
    public void Decode_NonExactUrl_Rejects(string url) =>
        Assert.ThrowsAny<Exception>(() => CompanionPairingCodec.Decode(url));

    [Fact]
    public void Decode_PublicEndpoint_RequiresExplicitOptIn()
    {
        var url = CreateUrl("8.8.8.8\t12345\tpw\tgen");

        Assert.Throws<InvalidOperationException>(() => CompanionPairingCodec.Decode(url));
        Assert.Equal(IPAddress.Parse("8.8.8.8"), CompanionPairingCodec.Decode(url, true).Addresses[0]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("not-a-port")]
    public void Decode_InvalidPort_Rejects(string port)
    {
        var url = CreateUrl($"127.0.0.1\t{port}\tpw\tgen");
        Assert.Throws<FormatException>(() => CompanionPairingCodec.Decode(url));
    }

    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.31.255.254", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.4.2", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("2001:4860:4860::8888", false)]
    public void IsPrivateOrLocal_ClassifiesAddresses(string address, bool expected) =>
        Assert.Equal(expected, CompanionPairingCodec.IsPrivateOrLocal(IPAddress.Parse(address)));

    private static string CreateUrl(string raw)
    {
        var bytes = Encoding.UTF8.GetBytes(raw);
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] ^= Key[i % Key.Length];
        }

        return "https://companion.clip-studio.com/rc/en-us?s=" +
               Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
