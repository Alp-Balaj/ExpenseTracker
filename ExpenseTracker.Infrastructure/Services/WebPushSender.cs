using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Options;
using ExpenseTracker.Infrastructure.Services.WebPush;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExpenseTracker.Infrastructure.Services;

/// <inheritdoc cref="IPushSender"/>
/// <remarks>
/// One POST per device, straight to whichever push service the browser named.
/// There is no fan-out API and no broker: Web Push is the browser vendors' own
/// infrastructure, and the endpoint the subscription carries is the whole
/// address.
/// </remarks>
public sealed class WebPushSender : IPushSender
{
    public const string HttpClientName = "webpush";

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PushOptions _options;
    private readonly ILogger<WebPushSender> _logger;

    public WebPushSender(
        IHttpClientFactory httpClientFactory,
        IOptions<PushOptions> options,
        ILogger<WebPushSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public string? PublicKey => _options.IsConfigured ? _options.PublicKey : null;

    public async Task<PushSendResult> SendAsync(
        PushTarget target, PushMessage message, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
            return new PushSendResult(PushSendOutcome.NotConfigured, 0, "No VAPID keys configured.");

        if (!Uri.TryCreate(target.Endpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
        {
            // A row whose endpoint no longer parses can never be delivered to;
            // treat it as gone so the caller prunes it rather than retrying for ever.
            return new PushSendResult(PushSendOutcome.Gone, 0, "Malformed endpoint.");
        }

        byte[] body;
        string authorization;
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(message, PayloadOptions);

            body = WebPushEncryptor.Encrypt(
                payload,
                Base64Url.DecodeExact(target.P256dh, 65, "p256dh"),
                Base64Url.DecodeExact(target.Auth, 16, "auth"));

            authorization = VapidAuthentication.CreateAuthorizationHeader(
                endpoint,
                _options.Subject,
                Base64Url.DecodeExact(_options.PublicKey, 65, "Push:PublicKey"),
                Base64Url.DecodeExact(_options.PrivateKey, 32, "Push:PrivateKey"),
                DateTimeOffset.UtcNow);
        }
        catch (FormatException ex)
        {
            // Malformed subscriber keys are the subscription's problem and will
            // never fix themselves; malformed *server* keys are a deployment
            // problem, and reporting them as gone would quietly delete every
            // subscription in the database.
            var serverKeyProblem = ex.Message.Contains("Push:", StringComparison.Ordinal);
            _logger.LogError(ex, "Web push encryption failed for {Endpoint}.", endpoint.Authority);
            return new PushSendResult(
                serverKeyProblem ? PushSendOutcome.NotConfigured : PushSendOutcome.Gone, 0, ex.Message);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Web push encryption failed for {Endpoint}.", endpoint.Authority);
            return new PushSendResult(PushSendOutcome.Gone, 0, ex.Message);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.TryAddWithoutValidation("TTL", _options.TimeToLive.ToString());
        // "normal" is the default, but Apple's service is happier when it is said
        // out loud, and it documents the intent: none of the three triggers is
        // urgent enough to wake a sleeping phone's radio.
        request.Headers.TryAddWithoutValidation("Urgency", "normal");

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
                return new PushSendResult(PushSendOutcome.Delivered, (int)response.StatusCode, null);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                // The browser was uninstalled, the permission revoked, or the
                // endpoint rotated. Definitive — the caller deletes the row.
                return new PushSendResult(PushSendOutcome.Gone, (int)response.StatusCode, "Subscription expired.");
            }

            var detail = await SafeReadAsync(response, cancellationToken);
            _logger.LogWarning(
                "Push service {Host} answered {Status} for a notification: {Detail}",
                endpoint.Authority, (int)response.StatusCode, detail);

            return new PushSendResult(PushSendOutcome.TransientFailure, (int)response.StatusCode, detail);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Unreachable or timed out. Never fatal: a push service having a bad
            // ten minutes must not cost anyone their subscription.
            _logger.LogWarning(ex, "Push service {Host} was unreachable.", endpoint.Authority);
            return new PushSendResult(PushSendOutcome.TransientFailure, 0, ex.Message);
        }
    }

    private static async Task<string?> SafeReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return text.Length <= 200 ? text : text[..200];
        }
        catch
        {
            return null;
        }
    }
}
