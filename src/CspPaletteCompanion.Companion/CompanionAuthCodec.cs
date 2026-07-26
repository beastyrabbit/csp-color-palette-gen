using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CspPaletteCompanion.Companion;

public sealed record CompanionAuthResult(
    bool IsAuthenticated,
    string? ErrorReason,
    string? ServerSpecVersion,
    bool IsQuickAccessAvailable);

public static class CompanionAuthCodec
{
    private static readonly byte[] AuthenticationKey = [0xB6, 0xD5, 0x92, 0xC4, 0xA7, 0x83, 0xE1];

    public static string Encrypt(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Encoding.UTF8.GetBytes(value);
        CompanionPairingCodec.XorInPlace(bytes, AuthenticationKey);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Decrypt(string encryptedHex)
    {
        ArgumentNullException.ThrowIfNull(encryptedHex);
        var bytes = Convert.FromHexString(encryptedHex);
        CompanionPairingCodec.XorInPlace(bytes, AuthenticationKey);
        return Encoding.UTF8.GetString(bytes);
    }

    public static string CreateRotatedPassword()
    {
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=');
    }

    public static byte[] CreateAuthenticationDetail(string generation, string currentPassword, string newPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generation);
        ArgumentNullException.ThrowIfNull(currentPassword);
        ArgumentNullException.ThrowIfNull(newPassword);
        return JsonSerializer.SerializeToUtf8Bytes(
            new[] { generation, Encrypt(currentPassword), Encrypt(newPassword) });
    }

    public static CompanionAuthResult ParseResult(CompanionFrame frame)
    {
        if (frame.RawDetail.Length == 0)
        {
            return new CompanionAuthResult(
                frame.Type != CompanionFrameType.Error,
                frame.Type == CompanionFrameType.Error ? "EmptyResponse" : null,
                null,
                false);
        }

        if (frame.Detail is { ValueKind: JsonValueKind.Object } detail)
        {
            var reason = detail.TryGetProperty("AuthErrorReason", out var reasonValue)
                ? reasonValue.GetString()
                : null;
            var version = detail.TryGetProperty("RemoteCommandSpecVersionOfServer", out var versionValue)
                ? versionValue.GetString()
                : null;
            var quickAccess = detail.TryGetProperty("IsQuickAccessAvailable", out var quickAccessValue) &&
                              quickAccessValue.ValueKind is JsonValueKind.True;
            var failed = reason is "VersionMismatch" or "PasswordMismatch" or "ServerUnready";
            return new CompanionAuthResult(!failed, failed ? reason : null, version, quickAccess);
        }

        // CSP may return [true,false] for a successful authentication.
        return new CompanionAuthResult(true, null, null, false);
    }
}
