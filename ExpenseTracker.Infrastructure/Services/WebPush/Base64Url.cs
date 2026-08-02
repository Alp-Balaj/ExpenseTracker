namespace ExpenseTracker.Infrastructure.Services.WebPush;

/// <summary>
/// Unpadded base64url (RFC 4648 §5) — the encoding every value in Web Push
/// arrives in: the browser's keys, the VAPID pair, and each segment of the JWT.
/// <para>
/// Hand-rolled because .NET 8 has no <c>Base64Url</c> type (it landed in .NET 9)
/// and because being tolerant of padding on the way in matters: browsers differ
/// on whether <c>p256dh</c> comes back padded.
/// </para>
/// </summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static byte[] Decode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new FormatException("Empty base64url value.");

        var normalized = value.Trim().Replace('-', '+').Replace('_', '/');

        // Re-pad to a multiple of four; anything else is genuinely malformed.
        var remainder = normalized.Length % 4;
        normalized = remainder switch
        {
            0 => normalized,
            2 => normalized + "==",
            3 => normalized + "=",
            _ => throw new FormatException("Malformed base64url value.")
        };

        return Convert.FromBase64String(normalized);
    }

    /// <summary>Decode, then insist on an exact length — key material is fixed-size or wrong.</summary>
    public static byte[] DecodeExact(string value, int expectedLength, string name)
    {
        var bytes = Decode(value);
        if (bytes.Length != expectedLength)
            throw new FormatException($"{name} must be {expectedLength} bytes, got {bytes.Length}.");
        return bytes;
    }
}
