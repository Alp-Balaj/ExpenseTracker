using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Infrastructure.OAuth;

/// <summary>
/// Resolves a client whose client_id is an https URL by fetching the JSON metadata
/// document at that URL (draft-ietf-oauth-client-id-metadata-document), the way MCP
/// clients identify themselves without registering first.
/// <para>
/// The URL comes from whoever starts an authorization request, so this is a server-side
/// request to an address an anonymous party picked. Hence: https only, no redirects, a
/// short timeout, a small body cap, and — enforced at connect time, after DNS, so a
/// rebinding hostname cannot slip past — no private, loopback or link-local addresses.
/// </para>
/// </summary>
public sealed class ClientMetadataDocumentFetcher
{
    public const string HttpClientName = "oauth-client-metadata";
    private const int MaxDocumentBytes = 16 * 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClientMetadataDocumentFetcher> _logger;

    public ClientMetadataDocumentFetcher(IHttpClientFactory httpClientFactory, ILogger<ClientMetadataDocumentFetcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ResolvedClient?> FetchAsync(string clientId, CancellationToken cancellationToken)
    {
        // The spec requires a path component and forbids fragments and credentials.
        if (!Uri.TryCreate(clientId, UriKind.Absolute, out var url) || url.Scheme != "https" ||
            url.AbsolutePath is "" or "/" || !string.IsNullOrEmpty(url.Fragment) || !string.IsNullOrEmpty(url.UserInfo))
            return null;

        try
        {
            var http = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[MaxDocumentBytes + 1];
            var read = 0;
            int n;
            while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken)) > 0) read += n;
            if (read > MaxDocumentBytes) return null;

            using var doc = JsonDocument.Parse(buffer.AsMemory(0, read));
            var root = doc.RootElement;

            // The document must claim to be the URL it was fetched from.
            if (!root.TryGetProperty("client_id", out var id) || id.GetString() != clientId) return null;

            // Only public clients: a metadata document cannot carry a shared secret.
            if (root.TryGetProperty("token_endpoint_auth_method", out var method) && method.GetString() is { } m && m != "none")
                return null;

            if (!root.TryGetProperty("redirect_uris", out var uris) || uris.ValueKind != JsonValueKind.Array) return null;
            var redirectUris = uris.EnumerateArray()
                .Select(u => u.GetString())
                .Where(u => u is not null && RedirectUriRules.IsAcceptable(u))
                .Select(u => u!)
                .ToList();
            if (redirectUris.Count == 0) return null;

            var name = root.TryGetProperty("client_name", out var cn) && cn.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(cn.GetString())
                ? cn.GetString()!.Trim()
                : url.Host;
            if (name.Length > 100) name = name[..100];

            return new ResolvedClient(clientId, name, redirectUris, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            _logger.LogInformation(e, "Could not read OAuth client metadata document {ClientId}", clientId);
            return null;
        }
    }

    /// <summary>The handler behind <see cref="HttpClientName"/>: refuses to connect anywhere internal.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var target = addresses.FirstOrDefault(IsPublic)
                ?? throw new HttpRequestException($"{context.DnsEndPoint.Host} does not resolve to a public address.");

            var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return false;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 10 ||
                     b[0] == 0 ||
                     b[0] == 127 ||
                     (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || // CGNAT, incl. Railway's private network
                     (b[0] == 169 && b[1] == 254) ||                // link-local, cloud metadata
                     (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
                     (b[0] == 192 && b[1] == 168) ||
                     (b[0] == 192 && b[1] == 0 && b[2] == 0) ||
                     (b[0] == 198 && (b[1] == 18 || b[1] == 19)) ||
                     b[0] >= 224);                                  // multicast and reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast ||
                     (b[0] & 0xFE) == 0xFC); // unique local fc00::/7, Railway's private IPv6
        }

        return false;
    }
}
