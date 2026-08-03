using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Options;
using ExpenseTracker.Infrastructure.Services;
using ExpenseTracker.Infrastructure.Services.WebPush;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExpenseTracker.Tests.UnitTests;

/// <summary>
/// The one HTTP request Stream P makes to something outside the app.
/// <para>
/// Every integration test swaps this class out for
/// <c>CapturingPushSender</c> — so without this file the real wire format would
/// be exercised by nothing. What is pinned here is exactly the set of things a
/// push service rejects silently: the content coding, the TTL header, the VAPID
/// scheme, and that the body a subscriber receives is the one they can decrypt.
/// </para>
/// </summary>
public sealed class WebPushSenderTests
{
    // The RFC 8291 subscriber, reused as a stand-in browser.
    private const string SubscriberPublicKey =
        "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    private const string SubscriberPrivateKey = "q1dXpw3UpT5VOmu_cf_v6ih07Aems3njxI-JWgLcM94";
    private const string SubscriberAuth = "BTBZMqHH6r4Tts7J_aSIgg";

    private static readonly PushTarget Target = new(
        "https://push.example.net/send/abc123", SubscriberPublicKey, SubscriberAuth);

    private static readonly PushMessage Message =
        new("Budget warning", "Groceries is at 85% of its budget.", "/budgets", "budget-1");

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public HttpRequestMessage? LastRequest { get; private set; }
        public byte[]? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);

            return _respond(request);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static (WebPushSender Sender, StubHandler Handler) Build(
        HttpStatusCode status = HttpStatusCode.Created, PushOptions? options = null)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status));

        options ??= NewConfiguredOptions();

        var sender = new WebPushSender(
            new StubHttpClientFactory(handler),
            Options.Create(options),
            NullLogger<WebPushSender>.Instance);

        return (sender, handler);
    }

    private static PushOptions NewConfiguredOptions()
    {
        var (publicKey, privateKey) = VapidAuthentication.GenerateKeyPair();
        return new PushOptions
        {
            Enabled = true,
            PublicKey = publicKey,
            PrivateKey = privateKey,
            Subject = "mailto:admin@expensetracker.local"
        };
    }

    [Fact]
    public async Task An_unconfigured_server_attempts_nothing()
    {
        var (sender, handler) = Build(options: new PushOptions { Enabled = true });

        var result = await sender.SendAsync(Target, Message);

        Assert.Equal(PushSendOutcome.NotConfigured, result.Outcome);
        Assert.False(sender.IsConfigured);
        Assert.Null(sender.PublicKey);
        // No request at all — an unconfigured deployment is silent, not broken.
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task The_request_carries_the_headers_a_push_service_requires()
    {
        var (sender, handler) = Build();

        var result = await sender.SendAsync(Target, Message);

        Assert.True(result.Delivered);

        var request = handler.LastRequest!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(Target.Endpoint, request.RequestUri!.ToString());

        // aes128gcm as a Content-Encoding, not a Content-Type: services route on it.
        Assert.Contains("aes128gcm", request.Content!.Headers.ContentEncoding);
        Assert.Equal("application/octet-stream", request.Content.Headers.ContentType!.MediaType);

        Assert.True(request.Headers.TryGetValues("TTL", out var ttl));
        Assert.Equal("43200", ttl!.Single());

        var authorization = request.Headers.GetValues("Authorization").Single();
        Assert.StartsWith("vapid t=", authorization);
        Assert.Contains(", k=", authorization);
    }

    [Fact]
    public async Task The_body_is_what_the_subscriber_can_decrypt()
    {
        var (sender, handler) = Build();

        await sender.SendAsync(Target, Message);

        var body = handler.LastBody!;
        var salt = body[..16];
        var senderPublic = body[21..86];
        var ciphertext = body[86..];

        using var subscriberKey = WebPushEncryptor.ImportPrivateKey(
            Base64Url.Decode(SubscriberPrivateKey), Base64Url.Decode(SubscriberPublicKey));
        using var ephemeral = WebPushEncryptor.ImportPublicKey(senderPublic);

        var shared = subscriberKey.DeriveRawSecretAgreement(ephemeral.PublicKey);
        var keyInfo = Encoding.ASCII.GetBytes("WebPush: info\0")
            .Concat(Base64Url.Decode(SubscriberPublicKey))
            .Concat(senderPublic)
            .ToArray();

        var ikm = HKDF.Expand(
            HashAlgorithmName.SHA256,
            HKDF.Extract(HashAlgorithmName.SHA256, shared, Base64Url.Decode(SubscriberAuth)),
            32,
            keyInfo);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        var plaintext = new byte[ciphertext.Length - 16];
        using (var aes = new AesGcm(cek, 16))
        {
            aes.Decrypt(nonce, ciphertext[..^16], ciphertext[^16..], plaintext);
        }

        using var payload = JsonDocument.Parse(plaintext[..^1]);

        // camelCase, because that is what push-sw.js reads off event.data.json().
        Assert.Equal("Budget warning", payload.RootElement.GetProperty("title").GetString());
        Assert.Equal("Groceries is at 85% of its budget.", payload.RootElement.GetProperty("body").GetString());
        Assert.Equal("/budgets", payload.RootElement.GetProperty("url").GetString());
        Assert.Equal("budget-1", payload.RootElement.GetProperty("tag").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, PushSendOutcome.Delivered)]
    [InlineData(HttpStatusCode.OK, PushSendOutcome.Delivered)]
    [InlineData(HttpStatusCode.NotFound, PushSendOutcome.Gone)]
    [InlineData(HttpStatusCode.Gone, PushSendOutcome.Gone)]
    [InlineData(HttpStatusCode.TooManyRequests, PushSendOutcome.TransientFailure)]
    [InlineData(HttpStatusCode.InternalServerError, PushSendOutcome.TransientFailure)]
    public async Task Push_service_responses_map_to_keep_or_drop(HttpStatusCode status, PushSendOutcome expected)
    {
        var (sender, _) = Build(status);

        var result = await sender.SendAsync(Target, Message);

        // The distinction that matters: 404/410 deletes the subscription, and a
        // push service having a bad ten minutes must not.
        Assert.Equal(expected, result.Outcome);
        Assert.Equal((int)status, result.StatusCode);
    }

    [Fact]
    public async Task An_unreachable_push_service_is_transient_never_fatal()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var sender = new WebPushSender(
            new StubHttpClientFactory(handler),
            Options.Create(NewConfiguredOptions()),
            NullLogger<WebPushSender>.Instance);

        var result = await sender.SendAsync(Target, Message);

        Assert.Equal(PushSendOutcome.TransientFailure, result.Outcome);
    }

    [Fact]
    public async Task A_subscription_with_unusable_keys_is_dropped_rather_than_retried()
    {
        var (sender, handler) = Build();

        var result = await sender.SendAsync(
            Target with { P256dh = "not-a-key" }, Message);

        // It will never encrypt, on any future attempt.
        Assert.Equal(PushSendOutcome.Gone, result.Outcome);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task A_malformed_endpoint_is_dropped_without_a_request()
    {
        var (sender, handler) = Build();

        var result = await sender.SendAsync(Target with { Endpoint = "not a url" }, Message);

        Assert.Equal(PushSendOutcome.Gone, result.Outcome);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task Misconfigured_server_keys_never_look_like_a_dead_subscription()
    {
        // The dangerous confusion: reporting a broken deployment as "Gone" would
        // delete every subscription in the database on the first sweep.
        var (sender, handler) = Build(options: new PushOptions
        {
            Enabled = true,
            PublicKey = "obviously-not-a-p256-point",
            PrivateKey = "obviously-not-a-scalar"
        });

        var result = await sender.SendAsync(Target, Message);

        Assert.Equal(PushSendOutcome.NotConfigured, result.Outcome);
        Assert.Null(handler.LastRequest);
    }
}
