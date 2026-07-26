using System.Text.Json;

namespace CspPaletteCompanion.Companion.Tests;

public sealed class CompanionAuthCodecTests
{
    [Fact]
    public void Encrypt_UsesKnownRepeatingXorVector()
    {
        Assert.Equal("c6b4e1b7d0ec93d2", CompanionAuthCodec.Encrypt("password"));
        Assert.Equal("password", CompanionAuthCodec.Decrypt("c6b4e1b7d0ec93d2"));
    }

    [Fact]
    public void CreateAuthenticationDetail_HasLoadBearingArrayOrder()
    {
        using var document = JsonDocument.Parse(
            CompanionAuthCodec.CreateAuthenticationDetail("G#1", "old", "new"));
        var values = document.RootElement.EnumerateArray().Select(item => item.GetString()).ToArray();

        Assert.Equal("G#1", values[0]);
        Assert.Equal(CompanionAuthCodec.Encrypt("old"), values[1]);
        Assert.Equal(CompanionAuthCodec.Encrypt("new"), values[2]);
    }

    [Fact]
    public void ParseResult_UsesReasonRatherThanPacketType()
    {
        var successFrame = DecodeFrame(
            CompanionFrameType.Error,
            """{"AuthErrorReason":"Unknown","RemoteCommandSpecVersionOfServer":"1.0","IsQuickAccessAvailable":true}""");
        var failureFrame = DecodeFrame(
            CompanionFrameType.Success,
            """{"AuthErrorReason":"PasswordMismatch"}""");

        var success = CompanionAuthCodec.ParseResult(successFrame);
        var failure = CompanionAuthCodec.ParseResult(failureFrame);

        Assert.True(success.IsAuthenticated);
        Assert.Equal("1.0", success.ServerSpecVersion);
        Assert.True(success.IsQuickAccessAvailable);
        Assert.False(failure.IsAuthenticated);
        Assert.Equal("PasswordMismatch", failure.ErrorReason);
    }

    [Fact]
    public void RotatedPassword_IsEightBase64Characters()
    {
        var password = CompanionAuthCodec.CreateRotatedPassword();
        Assert.Equal(8, password.Length);
        Assert.Equal(6, Convert.FromBase64String(password).Length);
    }

    private static CompanionFrame DecodeFrame(CompanionFrameType type, string json)
    {
        var bytes = CompanionFrameCodec.EncodeRaw(type, "Authenticate", 0, System.Text.Encoding.UTF8.GetBytes(json));
        Assert.True(CompanionFrameCodec.TryDecode(bytes, out var frame, out _));
        return Assert.IsType<CompanionFrame>(frame);
    }
}
