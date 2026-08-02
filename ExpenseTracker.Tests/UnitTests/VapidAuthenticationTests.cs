using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExpenseTracker.Infrastructure.Services.WebPush;
using Xunit;

namespace ExpenseTracker.Tests.UnitTests;

/// <summary>
/// The VAPID half (RFC 8292): the signed claim that authorises this server to
/// push to an endpoint at all.
/// <para>
/// A wrong token is a flat 401 from the push service with no other explanation,
/// so the things worth pinning are the ones that are silently wrong: the
/// signature format (JOSE wants raw r||s, .NET produces DER by default), the
/// audience (origin only, never the endpoint's secret path) and the expiry.
/// </para>
/// </summary>
public sealed class VapidAuthenticationTests
{
    private static readonly Uri Endpoint =
        new("https://fcm.googleapis.com/fcm/send/abcd1234-secret-path");

    private const string Subject = "mailto:admin@expensetracker.local";

    private static (byte[] Public, byte[] Private) NewKeyPair()
    {
        var (publicKey, privateKey) = VapidAuthentication.GenerateKeyPair();
        return (Base64Url.Decode(publicKey), Base64Url.Decode(privateKey));
    }

    private static (string Header, string Payload, string Signature, string Key) Parse(string authorization)
    {
        Assert.StartsWith("vapid t=", authorization);

        var parts = authorization["vapid ".Length..].Split(", ");
        var token = parts[0]["t=".Length..];
        var key = parts[1]["k=".Length..];

        var segments = token.Split('.');
        Assert.Equal(3, segments.Length);

        return (segments[0], segments[1], segments[2], key);
    }

    [Fact]
    public void A_generated_pair_is_the_shape_the_browser_expects()
    {
        var (publicKey, privateKey) = VapidAuthentication.GenerateKeyPair();

        // 65-byte uncompressed point and 32-byte scalar. The browser rejects an
        // applicationServerKey of any other length before it ever subscribes.
        var publicBytes = Base64Url.Decode(publicKey);
        Assert.Equal(65, publicBytes.Length);
        Assert.Equal(0x04, publicBytes[0]);
        Assert.Equal(32, Base64Url.Decode(privateKey).Length);
    }

    [Fact]
    public void The_token_is_signed_with_the_advertised_key_in_jose_format()
    {
        var (publicKey, privateKey) = NewKeyPair();

        var authorization = VapidAuthentication.CreateAuthorizationHeader(
            Endpoint, Subject, publicKey, privateKey, DateTimeOffset.UtcNow);

        var (header, payload, signature, advertisedKey) = Parse(authorization);

        Assert.Equal(Base64Url.Encode(publicKey), advertisedKey);

        using var verifier = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] }
        });

        var verified = verifier.VerifyData(
            Encoding.ASCII.GetBytes($"{header}.{payload}"),
            Base64Url.Decode(signature),
            HashAlgorithmName.SHA256,
            // The assertion that matters: a DER-encoded signature would verify
            // here too if this said DSASignatureFormat.Rfc3279..., and would be
            // rejected by every push service in the world.
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        Assert.True(verified);
    }

    [Fact]
    public void The_header_declares_es256()
    {
        var (publicKey, privateKey) = NewKeyPair();

        var (header, _, _, _) = Parse(VapidAuthentication.CreateAuthorizationHeader(
            Endpoint, Subject, publicKey, privateKey, DateTimeOffset.UtcNow));

        using var document = JsonDocument.Parse(Base64Url.Decode(header));
        Assert.Equal("JWT", document.RootElement.GetProperty("typ").GetString());
        Assert.Equal("ES256", document.RootElement.GetProperty("alg").GetString());
    }

    [Fact]
    public void The_audience_is_the_origin_and_never_the_endpoints_secret_path()
    {
        var (publicKey, privateKey) = NewKeyPair();
        var now = DateTimeOffset.UtcNow;

        var (_, payload, _, _) = Parse(VapidAuthentication.CreateAuthorizationHeader(
            Endpoint, Subject, publicKey, privateKey, now));

        using var document = JsonDocument.Parse(Base64Url.Decode(payload));

        // The path is a capability to push to one device. Push service operators
        // log the tokens they receive; the path has no business in one.
        Assert.Equal("https://fcm.googleapis.com", document.RootElement.GetProperty("aud").GetString());
        Assert.Equal(Subject, document.RootElement.GetProperty("sub").GetString());
        Assert.Equal(
            now.AddHours(12).ToUnixTimeSeconds(),
            document.RootElement.GetProperty("exp").GetInt64());
    }

    [Fact]
    public void An_expiry_beyond_the_rfc_ceiling_is_the_callers_to_choose_but_the_default_is_inside_it()
    {
        // RFC 8292 caps a VAPID token at 24 hours and services enforce it; the
        // default has to sit safely inside that with room for clock skew.
        Assert.True(VapidAuthentication.DefaultLifetime < TimeSpan.FromHours(24));
    }

    [Fact]
    public void Keys_of_the_wrong_size_are_refused_before_anything_is_signed()
    {
        var (publicKey, privateKey) = NewKeyPair();

        Assert.Throws<ArgumentException>(() => VapidAuthentication.CreateAuthorizationHeader(
            Endpoint, Subject, publicKey[..64], privateKey, DateTimeOffset.UtcNow));

        Assert.Throws<ArgumentException>(() => VapidAuthentication.CreateAuthorizationHeader(
            Endpoint, Subject, publicKey, privateKey[..31], DateTimeOffset.UtcNow));
    }
}
