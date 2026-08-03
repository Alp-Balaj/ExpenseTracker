using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ExpenseTracker.Infrastructure.Services.WebPush;

/// <summary>
/// Voluntary Application Server Identification (RFC 8292): the signed claim that
/// says "the same server that owns this key subscribed you, and it is the one
/// pushing now".
///
/// <para>
/// It is what stops anyone who has merely seen an endpoint URL from pushing to
/// it — the browser pins the <c>applicationServerKey</c> it subscribed with, and
/// a push signed by any other key is rejected before it reaches the device.
/// Which is also why rotating the VAPID pair invalidates every existing
/// subscription: they were issued against the old public key.
/// </para>
/// </summary>
public static class VapidAuthentication
{
    /// <summary>
    /// Twelve hours. RFC 8292 caps the token at 24, and push services reject
    /// anything longer; half of that leaves room for clock skew in both directions.
    /// </summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(12);

    private static readonly byte[] Header =
        Encoding.ASCII.GetBytes("""{"typ":"JWT","alg":"ES256"}""");

    /// <summary>
    /// Builds the <c>Authorization</c> header value for one POST to a push service.
    /// </summary>
    /// <param name="endpoint">The subscription endpoint; only its origin is signed.</param>
    /// <param name="subject">
    /// A mailto: or https: URL identifying whoever operates this server. Push
    /// services require it so they can reach an operator before blocking one.
    /// </param>
    /// <param name="nowUtc">Injected rather than read from the clock so the token is testable.</param>
    public static string CreateAuthorizationHeader(
        Uri endpoint,
        string subject,
        byte[] publicKey,
        byte[] privateKey,
        DateTimeOffset nowUtc,
        TimeSpan? lifetime = null)
    {
        // The audience is the push service's origin, not the full endpoint: the
        // path is a secret capability and has no business inside a token that
        // service operators log.
        var audience = $"{endpoint.Scheme}://{endpoint.Authority}";

        var payload = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["aud"] = audience,
            ["exp"] = nowUtc.Add(lifetime ?? DefaultLifetime).ToUnixTimeSeconds(),
            ["sub"] = subject
        });

        var signingInput = $"{Base64Url.Encode(Header)}.{Base64Url.Encode(payload)}";

        using var key = ImportSigningKey(publicKey, privateKey);
        // JOSE wants the raw r||s pair, not the DER SEQUENCE that ECDsa produces
        // by default — a DER signature here is accepted by nothing and fails as a
        // flat 401 from the push service.
        var signature = key.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"vapid t={signingInput}.{Base64Url.Encode(signature)}, k={Base64Url.Encode(publicKey)}";
    }

    /// <summary>
    /// Loads the configured pair. Both halves are needed: .NET validates
    /// <see cref="ECParameters"/> on import and will not derive the public point
    /// from the private scalar.
    /// </summary>
    public static ECDsa ImportSigningKey(byte[] publicKey, byte[] privateKey)
    {
        if (publicKey.Length != 65 || publicKey[0] != 0x04)
            throw new ArgumentException("VAPID public key must be a 65-byte uncompressed P-256 point.", nameof(publicKey));
        if (privateKey.Length != 32)
            throw new ArgumentException("VAPID private key must be a 32-byte P-256 scalar.", nameof(privateKey));

        return ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = privateKey,
            Q = new ECPoint
            {
                X = publicKey[1..33],
                Y = publicKey[33..65]
            }
        });
    }

    /// <summary>
    /// Mints a fresh VAPID pair, base64url-encoded the way the configuration and
    /// the browser both want it. Not called by the app — it backs
    /// <c>scripts/generate-vapid-keys.sh</c> and the tests, so that generating
    /// keys never requires trusting a website to do it.
    /// </summary>
    public static (string PublicKey, string PrivateKey) GenerateKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);

        var d = parameters.D!;
        // Left-pad a short scalar to 32 bytes for the same reason the point
        // coordinates are padded — a leading zero byte is legitimate.
        var privateKey = new byte[32];
        d.CopyTo(privateKey, 32 - d.Length);

        return (
            Base64Url.Encode(WebPushEncryptor.ToUncompressedPoint(parameters.Q)),
            Base64Url.Encode(privateKey));
    }
}
