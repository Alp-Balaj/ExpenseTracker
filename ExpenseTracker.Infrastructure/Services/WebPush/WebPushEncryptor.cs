using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ExpenseTracker.Infrastructure.Services.WebPush;

/// <summary>
/// Message Encryption for Web Push (RFC 8291) over the aes128gcm content coding
/// (RFC 8188).
///
/// <para>
/// Why this is hand-written rather than a package: everything it needs is in the
/// base class library — <see cref="ECDiffieHellman"/>, <see cref="HKDF"/>,
/// <see cref="AesGcm"/> — the specification is a hundred lines long and fully
/// pinned by a published test vector, and the alternative was pulling a
/// years-stale NuGet package (and its BouncyCastle dependency) into an app that
/// holds real financial data. Sugg-11 is in flight to remove package leakage from
/// the lower layers; adding some here would be an odd direction to travel.
/// </para>
///
/// <para>
/// The property that makes this worth doing properly: the push service is an
/// untrusted intermediary. It sees the endpoint and the ciphertext and nothing
/// else — "Groceries is at 92% of its budget" is encrypted to a key that only the
/// subscriber's browser holds. Getting the derivation wrong does not fail loudly;
/// it produces a message the browser silently drops. Hence
/// <c>WebPushEncryptorTests</c>, which pins RFC 8291 §5 byte for byte.
/// </para>
/// </summary>
public static class WebPushEncryptor
{
    /// <summary>Advertised record size. One record is always enough here — the payloads are a sentence.</summary>
    public const int RecordSize = 4096;

    private const int SaltLength = 16;
    private const int KeyLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int UncompressedPointLength = 65;

    /// <summary>
    /// Largest plaintext that still fits one record, once the header, the
    /// delimiter and the GCM tag are accounted for.
    /// </summary>
    public const int MaxPlaintextLength =
        RecordSize - SaltLength - 4 - 1 - UncompressedPointLength - TagLength - 1;

    private static readonly byte[] KeyInfoPrefix = Encoding.ASCII.GetBytes("WebPush: info\0");
    private static readonly byte[] CekInfo = Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0");
    private static readonly byte[] NonceInfo = Encoding.ASCII.GetBytes("Content-Encoding: nonce\0");

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> to a subscriber, generating a fresh
    /// ephemeral key pair and salt. This is the overload production uses; every
    /// message gets its own sender key, which is the whole point of the scheme.
    /// </summary>
    /// <param name="uaPublicKey">The subscriber's <c>p256dh</c>, 65 raw bytes.</param>
    /// <param name="authSecret">The subscriber's <c>auth</c>, 16 raw bytes.</param>
    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, byte[] uaPublicKey, byte[] authSecret)
    {
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return Encrypt(plaintext, uaPublicKey, authSecret, ephemeral, RandomNumberGenerator.GetBytes(SaltLength));
    }

    /// <summary>
    /// The deterministic overload: same inputs, same bytes out. Exists so the RFC
    /// 8291 §5 vector can be reproduced exactly — the only way to know the
    /// derivation is right, since a wrong one is indistinguishable from a
    /// notification that simply never arrived.
    /// </summary>
    public static byte[] Encrypt(
        ReadOnlySpan<byte> plaintext,
        byte[] uaPublicKey,
        byte[] authSecret,
        ECDiffieHellman senderKey,
        byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(uaPublicKey);
        ArgumentNullException.ThrowIfNull(authSecret);
        ArgumentNullException.ThrowIfNull(senderKey);
        ArgumentNullException.ThrowIfNull(salt);

        if (uaPublicKey.Length != UncompressedPointLength || uaPublicKey[0] != 0x04)
            throw new ArgumentException("p256dh must be a 65-byte uncompressed P-256 point.", nameof(uaPublicKey));
        if (salt.Length != SaltLength)
            throw new ArgumentException($"Salt must be {SaltLength} bytes.", nameof(salt));
        if (plaintext.Length > MaxPlaintextLength)
            throw new ArgumentException($"Payload exceeds {MaxPlaintextLength} bytes.", nameof(plaintext));

        var senderPublicKey = ExportUncompressedPoint(senderKey);

        using var subscriberKey = ImportPublicKey(uaPublicKey);
        var sharedSecret = senderKey.DeriveRawSecretAgreement(subscriberKey.PublicKey);

        // RFC 8291 §3.3 — the auth secret salts the *first* extraction, which is
        // what binds the derived key to this subscriber rather than to anyone who
        // merely watched the ECDH exchange.
        var authPrk = HKDF.Extract(HashAlgorithmName.SHA256, sharedSecret, authSecret);

        // key_info = "WebPush: info" || 0x00 || ua_public || as_public. Both public
        // keys are in the info, so a message encrypted for one subscriber cannot be
        // replayed at another.
        var keyInfo = new byte[KeyInfoPrefix.Length + UncompressedPointLength * 2];
        KeyInfoPrefix.CopyTo(keyInfo, 0);
        uaPublicKey.CopyTo(keyInfo, KeyInfoPrefix.Length);
        senderPublicKey.CopyTo(keyInfo, KeyInfoPrefix.Length + UncompressedPointLength);

        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, authPrk, 32, keyInfo);

        // From here it is plain RFC 8188: the record salt drives the content key
        // and nonce.
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var contentKey = HKDF.Expand(HashAlgorithmName.SHA256, prk, KeyLength, CekInfo);
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, NonceLength, NonceInfo);

        // A single record, so the delimiter is 0x02 ("last record"), not 0x01.
        var padded = new byte[plaintext.Length + 1];
        plaintext.CopyTo(padded);
        padded[^1] = 0x02;

        var ciphertext = new byte[padded.Length];
        var tag = new byte[TagLength];
        using (var aes = new AesGcm(contentKey, TagLength))
        {
            aes.Encrypt(nonce, padded, ciphertext, tag);
        }

        // Body = salt(16) | rs(4, big endian) | idlen(1) | as_public(65) | ciphertext | tag
        var body = new byte[SaltLength + 4 + 1 + UncompressedPointLength + ciphertext.Length + TagLength];
        var offset = 0;

        salt.CopyTo(body, offset);
        offset += SaltLength;

        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(offset, 4), RecordSize);
        offset += 4;

        body[offset++] = UncompressedPointLength;
        senderPublicKey.CopyTo(body, offset);
        offset += UncompressedPointLength;

        ciphertext.CopyTo(body, offset);
        offset += ciphertext.Length;
        tag.CopyTo(body, offset);

        CryptographicOperations.ZeroMemory(sharedSecret);
        CryptographicOperations.ZeroMemory(contentKey);
        CryptographicOperations.ZeroMemory(ikm);

        return body;
    }

    /// <summary>Wraps 65 raw bytes back into a key object usable for ECDH.</summary>
    public static ECDiffieHellman ImportPublicKey(byte[] uncompressedPoint)
    {
        if (uncompressedPoint.Length != UncompressedPointLength || uncompressedPoint[0] != 0x04)
            throw new ArgumentException("Expected a 65-byte uncompressed P-256 point.", nameof(uncompressedPoint));

        return ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = uncompressedPoint[1..33],
                Y = uncompressedPoint[33..65]
            }
        });
    }

    /// <summary>
    /// Rebuilds a key pair from the raw private scalar. The public point has to be
    /// supplied too: .NET validates <see cref="ECParameters"/> on import and will
    /// not compute Q from D for us.
    /// </summary>
    public static ECDiffieHellman ImportPrivateKey(byte[] privateScalar, byte[] uncompressedPublicPoint)
    {
        if (uncompressedPublicPoint.Length != UncompressedPointLength || uncompressedPublicPoint[0] != 0x04)
            throw new ArgumentException("Expected a 65-byte uncompressed P-256 point.", nameof(uncompressedPublicPoint));

        return ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = privateScalar,
            Q = new ECPoint
            {
                X = uncompressedPublicPoint[1..33],
                Y = uncompressedPublicPoint[33..65]
            }
        });
    }

    public static byte[] ExportUncompressedPoint(ECDiffieHellman key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        return ToUncompressedPoint(parameters.Q);
    }

    internal static byte[] ToUncompressedPoint(ECPoint point)
    {
        var x = point.X ?? throw new ArgumentException("Public key has no X coordinate.", nameof(point));
        var y = point.Y ?? throw new ArgumentException("Public key has no Y coordinate.", nameof(point));

        var bytes = new byte[UncompressedPointLength];
        bytes[0] = 0x04;
        // Left-pad: a coordinate whose leading byte happens to be zero comes back
        // 31 bytes long from some providers, and silently shifting it by one is
        // exactly the kind of bug that only shows up on one machine in twenty.
        x.CopyTo(bytes, 1 + (32 - x.Length));
        y.CopyTo(bytes, 33 + (32 - y.Length));
        return bytes;
    }
}
