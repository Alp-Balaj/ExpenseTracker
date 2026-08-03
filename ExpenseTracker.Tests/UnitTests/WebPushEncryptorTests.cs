using System.Security.Cryptography;
using System.Text;
using ExpenseTracker.Infrastructure.Services.WebPush;
using Xunit;

namespace ExpenseTracker.Tests.UnitTests;

/// <summary>
/// Stream P pins the Web Push payload encryption against RFC 8291's own worked
/// example.
/// <para>
/// This is the test that justifies hand-writing the encryption instead of taking
/// a package. Getting the derivation wrong does not throw and does not return an
/// error: the push service accepts the request, the browser fails to decrypt, and
/// the notification simply never appears — on someone else's phone, days later.
/// Byte-for-byte agreement with the specification's vector is the only way to
/// know, and it costs one test.
/// </para>
/// </summary>
public sealed class WebPushEncryptorTests
{
    // RFC 8291 §5 and Appendix A. The §5 listing of the full body is wrapped for
    // presentation and drops the last octet, so the header and ciphertext are
    // taken from Appendix A, where they are given separately and in full.
    private const string Plaintext = "When I grow up, I want to be a watermelon";
    private const string AuthSecret = "BTBZMqHH6r4Tts7J_aSIgg";
    private const string ReceiverPublicKey =
        "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    private const string SenderPrivateKey = "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw";
    private const string SenderPublicKey =
        "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8";
    private const string Salt = "DGv6ra1nlYgDCS1FRnbzlw";
    private const string ExpectedHeader =
        "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27ml" +
        "mlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8";
    private const string ExpectedCiphertext =
        "8pfeW0KbunFT06SuDKoJH9Ql87S1QUrdirN6GcG7sFz1y1sqLgVi1VhjVkHsUoEs" +
        "bI_0LpXMuGvnzQ";

    [Fact]
    public void Encrypting_the_rfc_8291_example_reproduces_it_byte_for_byte()
    {
        using var senderKey = WebPushEncryptor.ImportPrivateKey(
            Base64Url.Decode(SenderPrivateKey),
            Base64Url.Decode(SenderPublicKey));

        var body = WebPushEncryptor.Encrypt(
            Encoding.ASCII.GetBytes(Plaintext),
            Base64Url.Decode(ReceiverPublicKey),
            Base64Url.Decode(AuthSecret),
            senderKey,
            Base64Url.Decode(Salt));

        var expected = Base64Url.Decode(ExpectedHeader)
            .Concat(Base64Url.Decode(ExpectedCiphertext))
            .ToArray();

        Assert.Equal(expected, body);
    }

    [Fact]
    public void The_header_carries_the_salt_record_size_and_sender_key()
    {
        using var senderKey = WebPushEncryptor.ImportPrivateKey(
            Base64Url.Decode(SenderPrivateKey),
            Base64Url.Decode(SenderPublicKey));

        var body = WebPushEncryptor.Encrypt(
            Encoding.ASCII.GetBytes(Plaintext),
            Base64Url.Decode(ReceiverPublicKey),
            Base64Url.Decode(AuthSecret),
            senderKey,
            Base64Url.Decode(Salt));

        Assert.Equal(Base64Url.Decode(Salt), body[..16]);
        // rs, big endian.
        Assert.Equal(new byte[] { 0x00, 0x00, 0x10, 0x00 }, body[16..20]);
        Assert.Equal(65, body[20]);
        Assert.Equal(Base64Url.Decode(SenderPublicKey), body[21..86]);
    }

    [Fact]
    public void A_fresh_ephemeral_key_makes_every_message_unique()
    {
        var receiver = Base64Url.Decode(ReceiverPublicKey);
        var auth = Base64Url.Decode(AuthSecret);
        var plaintext = Encoding.ASCII.GetBytes(Plaintext);

        var first = WebPushEncryptor.Encrypt(plaintext, receiver, auth);
        var second = WebPushEncryptor.Encrypt(plaintext, receiver, auth);

        // Same message, same subscriber, different bytes — a new salt and a new
        // sender key every time, which is what stops the push service from
        // recognising a repeated notification.
        Assert.NotEqual(first, second);
        Assert.Equal(first.Length, second.Length);
    }

    [Fact]
    public void The_ciphertext_decrypts_back_with_the_subscribers_private_key()
    {
        // The receiver's half of RFC 8291, written out independently: this is what
        // the browser does, and it is the end-to-end proof that a real device can
        // read what the encryptor produced with a randomly generated key.
        const string ReceiverPrivateKey = "q1dXpw3UpT5VOmu_cf_v6ih07Aems3njxI-JWgLcM94";

        var receiverPublic = Base64Url.Decode(ReceiverPublicKey);
        var auth = Base64Url.Decode(AuthSecret);

        var body = WebPushEncryptor.Encrypt(
            Encoding.ASCII.GetBytes(Plaintext), receiverPublic, auth);

        var salt = body[..16];
        var senderPublic = body[21..86];
        var ciphertext = body[86..];

        using var receiverKey = WebPushEncryptor.ImportPrivateKey(
            Base64Url.Decode(ReceiverPrivateKey), receiverPublic);
        using var senderKey = WebPushEncryptor.ImportPublicKey(senderPublic);

        var shared = receiverKey.DeriveRawSecretAgreement(senderKey.PublicKey);

        var keyInfo = Encoding.ASCII.GetBytes("WebPush: info\0")
            .Concat(receiverPublic)
            .Concat(senderPublic)
            .ToArray();

        var ikm = HKDF.Expand(
            HashAlgorithmName.SHA256,
            HKDF.Extract(HashAlgorithmName.SHA256, shared, auth),
            32,
            keyInfo);

        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        var decrypted = new byte[ciphertext.Length - 16];
        using (var aes = new AesGcm(cek, 16))
        {
            aes.Decrypt(nonce, ciphertext[..^16], ciphertext[^16..], decrypted);
        }

        // The trailing octet is the "last record" padding delimiter.
        Assert.Equal(0x02, decrypted[^1]);
        Assert.Equal(Plaintext, Encoding.ASCII.GetString(decrypted[..^1]));
    }

    [Fact]
    public void A_payload_too_large_for_one_record_is_refused_rather_than_truncated()
    {
        var oversized = new byte[WebPushEncryptor.MaxPlaintextLength + 1];

        Assert.Throws<ArgumentException>(() => WebPushEncryptor.Encrypt(
            oversized, Base64Url.Decode(ReceiverPublicKey), Base64Url.Decode(AuthSecret)));
    }

    [Theory]
    [InlineData("BTBZMqHH6r4Tts7J_aSIgg")]      // unpadded, as browsers usually send it
    [InlineData("BTBZMqHH6r4Tts7J_aSIgg==")]    // padded, as some send it
    public void Base64url_accepts_the_padding_browsers_disagree_about(string value)
    {
        Assert.Equal(16, Base64Url.Decode(value).Length);
        Assert.Equal("BTBZMqHH6r4Tts7J_aSIgg", Base64Url.Encode(Base64Url.Decode(value)));
    }

    [Fact]
    public void A_key_of_the_wrong_length_is_refused_at_the_boundary()
    {
        // 16 bytes where 65 are required: a malformed subscription must be caught
        // here, not produce a message no device can read.
        Assert.Throws<FormatException>(() => Base64Url.DecodeExact(AuthSecret, 65, "p256dh"));
    }
}
